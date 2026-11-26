// =============================================================================
// X360GameHack Disk Reader
//
// The ONLY component of X360GameHack that runs with administrator rights. It exists because Windows
// requires administrator rights to read (or change) a raw disk. Windows elevates the small native launcher next to it
// (X360GameHack.DiskReader.exe, DiskReader\Launcher), which starts this program with a clean environment - so no .NET
// profiler or runtime option from the user's environment is ever loaded into an administrator process. This program
// refuses to run when anything else started it. Its whole job, deliberately small:
//
//   * READ one physical disk that carries an Xbox FATX / XTAF file system,
//   * refuse the Windows system disk, every disk with a Windows partition table and every disk without
//     an Xbox file system, and every disk that holds a Windows volume with a file system (NTFS, FAT, exFAT, ...),
//   * accept requests only from the X360GameHack.exe that lives in the same (protected) folder,
//     over a private named pipe whose server process id is verified,
//   * exit as soon as that process closes the pipe or exits.
//
// WRITE MODE (only when started with --write, which the program does only after the user allowed changes):
//   * writes are accepted only inside the Xbox partitions found on the disk when it was opened - never in
//     the first 512 KB (security sector, partition data) and never outside a partition,
//   * before any sector is changed, what it held is saved to an undo journal in a folder only
//     administrators can write (%ProgramData%\X360GameHack\DiskUndo), and that is flushed to disk first,
//   * every write is read back and compared,
//   * "undo" puts every changed sector of a session back, newest first, and stops if a sector was changed
//     by something else since. It is refused outright when anything else changed an allocation table since
//     the session started (a console used the drive, a later session was not put back first): the journal
//     keeps a hash of every 4 KB block of every table as it was before the session's first change.
//
// FORMAT MODE (only with --write --format og|x360, which the program does only when the user chose to make a drive):
//   * a blank drive is accepted (no Xbox file system needed), everything else is refused as in write mode - the
//     Windows disk, any disk with a Windows volume or partition table, development kit drives,
//   * writes may go only where that console's drive layout has something to write, worked out here - never from the
//     program: original Xbox the first 4 KB (partition table, refurb sector) and everything from 0x80000; Xbox 360
//     only from SysExt (0x10C080000) on - never the security sector or the console's two cache partitions,
//   * every write is journaled as in write mode, so a format can be undone like any other change.
//
// It takes no paths from anywhere, starts no processes and loads no plug-ins.
//
// ELSEWHERE: "--eject" safely removes an Xbox drive (flush, spin down, Windows lets go of it); it writes nothing.
// "--remount" starts again disks that were safely removed but are still connected; it reads and writes no disk.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

[assembly: System.Reflection.AssemblyTitle("X360GameHack Disk Reader")]
[assembly: System.Reflection.AssemblyDescription("Read-only access to Xbox-formatted disks for X360GameHack")]
[assembly: System.Reflection.AssemblyProduct("X360GameHack 2026")]
[assembly: System.Reflection.AssemblyVersion("4.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("4.0.0.0")]
[assembly: ComVisible(false)]

namespace X360GameHack.DiskReader
{
    /// <summary>
    /// The undo journal of one write session: for every write, where it was, the SHA-256 of what was there and of what
    /// was written, and the old bytes themselves - on disk (flushed) before the disk is changed. Tied to one disk by its
    /// size and identity. Undo puts the sectors back newest first and stops at a sector something else changed since.
    /// </summary>
    internal sealed class Journal : IDisposable
    {
        // XGHUNDO2 adds the allocation-table snapshot after the header; XGHUNDO1 journals (no snapshot) are never undone.
        // XGHUNDO3 is a format session: one byte after the magic says which console's layout (1 = og, 2 = x360), and its
        // records may lie anywhere that format may write (see Program.FormatRanges).
        private const string Magic = "XGHUNDO2", FormatMagic = "XGHUNDO3", OldMagic = "XGHUNDO1";
        private const byte RecordMark = 0xA5;
        private const int MaxRegions = 64, MaxRegionBlocks = 1 << 22;      // 4M blocks of 4 KB = a 16 GB table, more than any drive
        private readonly string _folder, _identity, _legacyIdentity;
        private readonly long _size;
        private readonly Stream _disk;
        private readonly List<Program.Range> _allowed, _snapshot;
        private FileStream _f;
        public string SessionName { get; }

        /// <summary>"og" / "x360" for a format session, null for an ordinary write session.</summary>
        public string Mode { get; }

        public Journal(string folder, long size, string identity, Stream disk, List<Program.Range> allowed)
            : this(folder, size, identity, identity, disk, allowed, allowed, null) { }

        /// <summary>
        /// <paramref name="identity"/> ties new sessions to the disk; sessions recorded with <paramref name="legacyIdentity"/>
        /// (the identity older versions used) are still listed and can still be undone. <paramref name="snapshot"/>: the
        /// partitions whose allocation tables are hashed when the session starts (in a format session the ones the drive
        /// has now, which can differ from where the format may write).
        /// </summary>
        public Journal(string folder, long size, string identity, string legacyIdentity, Stream disk, List<Program.Range> allowed, List<Program.Range> snapshot, string mode)
        {
            _folder = folder; _size = size; _identity = identity; _legacyIdentity = legacyIdentity ?? identity; _disk = disk; _allowed = allowed; _snapshot = snapshot ?? allowed;
            Mode = mode == "og" || mode == "x360" ? mode : null;
            var rnd = new byte[2];
            using (var g = RandomNumberGenerator.Create()) g.GetBytes(rnd);
            // the time to the millisecond comes first, so the names sort in the order the sessions happened
            SessionName = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + identity.Substring(0, 12) + "-" +
                          BitConverter.ToString(rnd).Replace("-", "").ToLowerInvariant() + ".undo";
        }

        private static readonly Regex NameRule = new Regex(@"^\d{8}-\d{6}-\d{3}-(?<id>[0-9a-f]{12})-[0-9a-f]{4}\.undo$");

        public static bool ValidName(string n) => NameRule.IsMatch(n ?? "");

        private static byte[] Sha(byte[] b, int len) { using (var h = SHA256.Create()) return h.ComputeHash(b, 0, len); }

        /// <summary>
        /// Starts the journal at the session's first change: the header, then every allocation table of the writable
        /// partitions as SHA-256 per 4 KB block, read before anything is changed (see <see cref="Undo"/>).
        /// </summary>
        private void Open()
        {
            if (_f != null) return;
            var regions = Program.TableRegions(_disk, _snapshot);
            var hashes = regions.Select(r => BlockHashes(_disk, r.Start, r.Blocks)).ToList();
            _f = new FileStream(Path.Combine(_folder, SessionName), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var w = new BinaryWriter(_f, Encoding.UTF8, true);
            w.Write(Encoding.ASCII.GetBytes(Mode == null ? Magic : FormatMagic));
            if (Mode != null) w.Write((byte)(Mode == "og" ? 1 : 2));
            w.Write(_size); w.Write(_identity);
            w.Write(regions.Count);
            for (int i = 0; i < regions.Count; i++)
            {
                w.Write(regions[i].Start); w.Write(regions[i].Blocks);
                foreach (var h in hashes[i]) w.Write(h);
            }
            w.Flush();
            _f.Flush(true);
        }

        /// <summary>SHA-256 of each 4 KB block from <paramref name="start"/>.</summary>
        internal static byte[][] BlockHashes(Stream disk, long start, int blocks)
        {
            var result = new byte[blocks][];
            var buf = new byte[256 * Program.Align];
            using (var sha = SHA256.Create())
                for (int i = 0; i < blocks;)
                {
                    int n = Math.Min(256, blocks - i);
                    if (Program.ReadAt(disk, start + (long)i * Program.Align, buf, n * Program.Align) != n * Program.Align) throw new IOException("An allocation table could not be read.");
                    for (int k = 0; k < n; k++) result[i + k] = sha.ComputeHash(buf, k * Program.Align, Program.Align);
                    i += n;
                }
            return result;
        }

        /// <summary>Saves what <paramref name="len"/> bytes at <paramref name="off"/> held, before they are changed to <paramref name="now"/>.</summary>
        public void Record(long off, byte[] old, byte[] now, int len)
        {
            Open();
            var w = new BinaryWriter(_f, Encoding.UTF8, true);
            w.Write(RecordMark);
            w.Write(off); w.Write(len);
            w.Write(Sha(old, len)); w.Write(Sha(now, len));
            w.Write(old, 0, len);
            w.Flush();
            _f.Flush(true);     // on the disk before the Xbox drive is changed
        }

        /// <summary>The undo sessions recorded for this disk, newest first.</summary>
        public List<string> Sessions() =>
            Directory.GetFiles(_folder, "*.undo").Select(Path.GetFileName)
                     .Where(n => ValidName(n) && (NameRule.Match(n).Groups["id"].Value == _identity.Substring(0, 12) || NameRule.Match(n).Groups["id"].Value == _legacyIdentity.Substring(0, 12)))
                     .OrderByDescending(n => n, StringComparer.Ordinal).ToList();

        internal struct UndoResult { public int Status, Restored, Total; }

        /// <summary>
        /// Puts every sector of a session back, newest first. 0 = done; 2 = read back different; 3 = disk error;
        /// 4 = no such session; 5 = the journal is damaged; 6 = the journal is for another disk; 7 = a record points
        /// outside the Xbox partitions; 8 = a sector was changed by something else since (stopped there);
        /// 9 = recorded without the table snapshot (an older version) - never undone; 10 = something else changed an
        /// allocation table since the session started - nothing is put back.
        /// Status 10 is checked before anything is written: a block of a table that this session did not write must
        /// still be as it was when the session started. A console that used the drive since (or a later session not put
        /// back first) allocates or frees clusters there; putting this session back over that could hide what it added.
        /// </summary>
        public UndoResult Undo(string name, Stream disk, List<Program.Range> allowed)
        {
            string path = Path.Combine(_folder, name);
            if (!File.Exists(path)) return new UndoResult { Status = 4 };
            var recs = new List<(long Off, int Len, byte[] OldSha, byte[] NewSha, byte[] Old)>();
            var regions = new List<(long Start, int Blocks, byte[][] Hashes)>();
            bool headerRead = false;
            List<Program.Range> bounds = null;          // a format session's records may lie where that format may write
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var r = new BinaryReader(fs))
            {
                try
                {
                    string magic = Encoding.ASCII.GetString(r.ReadBytes(8));
                    if (magic == OldMagic) return new UndoResult { Status = 9 };
                    if (magic != Magic && magic != FormatMagic) return new UndoResult { Status = 5 };
                    if (magic == FormatMagic)
                    {
                        byte mode = r.ReadByte();
                        if (mode != 1 && mode != 2) return new UndoResult { Status = 5 };
                        bounds = Program.FormatRanges(mode == 1 ? "og" : "x360", _size);
                    }
                    long size = r.ReadInt64(); string id = r.ReadString();
                    if (size != _size || (id != _identity && id != _legacyIdentity)) return new UndoResult { Status = 6 };
                    int count = r.ReadInt32();
                    if (count < 0 || count > MaxRegions) return new UndoResult { Status = 5 };
                    for (int i = 0; i < count; i++)
                    {
                        long start = r.ReadInt64(); int blocks = r.ReadInt32();
                        if (start < Program.ProtectedStart || start % Program.Align != 0 || blocks < 0 || blocks > MaxRegionBlocks || start + (long)blocks * Program.Align > _size)
                            return new UndoResult { Status = 5 };
                        var hs = new byte[blocks][];
                        for (int k = 0; k < blocks; k++) { hs[k] = r.ReadBytes(32); if (hs[k].Length != 32) throw new EndOfStreamException(); }
                        regions.Add((start, blocks, hs));
                    }
                    headerRead = true;
                    while (fs.Position < fs.Length)
                    {
                        if (r.ReadByte() != RecordMark) return new UndoResult { Status = 5 };
                        long off = r.ReadInt64(); int len = r.ReadInt32();
                        if (len <= 0 || len > Program.MaxRequest || off % Program.Align != 0 || len % Program.Align != 0) return new UndoResult { Status = 5 };
                        byte[] oldSha = r.ReadBytes(32), newSha = r.ReadBytes(32), old = r.ReadBytes(len);
                        if (oldSha.Length != 32 || newSha.Length != 32 || old.Length != len) break;     // a last record cut short never reached the disk
                        if (!Sha(old, len).SequenceEqual(oldSha)) return new UndoResult { Status = 5 };
                        recs.Add((off, len, oldSha, newSha, old));
                    }
                }
                catch (EndOfStreamException) { /* a last record cut short: everything before it is complete */ }
            }
            // a header cut short: the session stopped before its first change was recorded, so it changed nothing
            if (!headerRead) return new UndoResult { Status = 0 };

            // nothing else may have changed an allocation table since the session started (checked before any write)
            var covered = new HashSet<long>();
            foreach (var x in recs)
                foreach (var g in regions)
                {
                    long from = Math.Max(x.Off, g.Start), to = Math.Min(x.Off + x.Len, g.Start + (long)g.Blocks * Program.Align);
                    for (long o = from; o < to; o += Program.Align) covered.Add(o);
                }
            var tbl = new byte[256 * Program.Align];
            using (var sha = SHA256.Create())
                foreach (var g in regions)
                    for (int i = 0; i < g.Blocks;)
                    {
                        int n = Math.Min(256, g.Blocks - i);
                        if (Program.ReadAt(disk, g.Start + (long)i * Program.Align, tbl, n * Program.Align) != n * Program.Align) return new UndoResult { Status = 3, Total = recs.Count };
                        for (int k = 0; k < n; k++)
                        {
                            if (covered.Contains(g.Start + (long)(i + k) * Program.Align)) continue;      // this session's own change: checked per record below
                            if (!sha.ComputeHash(tbl, k * Program.Align, Program.Align).SequenceEqual(g.Hashes[i + k])) return new UndoResult { Status = 10, Total = recs.Count };
                        }
                        i += n;
                    }

            int restored = 0;
            var cur = new byte[Program.MaxRequest];
            for (int i = recs.Count - 1; i >= 0; i--)
            {
                var x = recs[i];
                bool inside = bounds != null ? bounds.Any(a => x.Off >= a.Start && x.Off + x.Len <= a.End)
                                             : x.Off >= Program.ProtectedStart && allowed.Any(a => x.Off >= a.Start && x.Off + x.Len <= a.End);
                if (!inside) return new UndoResult { Status = 7, Restored = restored, Total = recs.Count };
                if (Program.ReadAt(disk, x.Off, cur, x.Len) != x.Len) return new UndoResult { Status = 3, Restored = restored, Total = recs.Count };
                byte[] curSha = Sha(cur, x.Len);
                if (curSha.SequenceEqual(x.OldSha)) { restored++; continue; }                  // already as it was
                if (!curSha.SequenceEqual(x.NewSha)) return new UndoResult { Status = 8, Restored = restored, Total = recs.Count };
                try
                {
                    disk.Position = x.Off;
                    disk.Write(x.Old, 0, x.Len);
                    if (disk is FileStream f) f.Flush(true); else disk.Flush();
                }
                catch (IOException) { return new UndoResult { Status = 3, Restored = restored, Total = recs.Count }; }
                if (Program.ReadAt(disk, x.Off, cur, x.Len) != x.Len || !Sha(cur, x.Len).SequenceEqual(x.OldSha)) return new UndoResult { Status = 2, Restored = restored, Total = recs.Count };
                restored++;
            }
            return new UndoResult { Status = 0, Restored = restored, Total = recs.Count };
        }

        public void Dispose() { try { _f?.Dispose(); } catch { } }
    }

    /// <summary>
    /// "Safely remove" for an Xbox drive on a port Windows does not treat as removable (an internal SATA port, for example):
    /// the drive's write cache is flushed, it is told to spin down and park its heads (ATA STANDBY IMMEDIATE, or SCSI
    /// STOP UNIT behind a USB adapter), and Windows removes the disk device so nothing can use it until it is plugged in
    /// again. Nothing is written to the drive. Only a non-Windows disk with an Xbox file system is ever ejected.
    ///   --eject --disk n --parent pid       exit 0 = removed; 60+ = why not
    /// </summary>
    internal static class Eject
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ATA_PASS_THROUGH_EX
        {
            public ushort Length, AtaFlags; public byte PathId, TargetId, Lun, ReservedAsUchar;
            public uint DataTransferLength, TimeOutValue, ReservedAsUlong; public UIntPtr DataBufferOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PreviousTaskFile;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] CurrentTaskFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCSI_PASS_THROUGH
        {
            public ushort Length; public byte ScsiStatus, PathId, TargetId, Lun, CdbLength, SenseInfoLength, DataIn;
            public uint DataTransferLength, TimeOutValue; public UIntPtr DataBufferOffset; public uint SenseInfoOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Cdb;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushFileBuffers(SafeFileHandle h);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref ATA_PASS_THROUGH_EX inb, int ins, ref ATA_PASS_THROUGH_EX outb, int outs, out int ret, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref SCSI_PASS_THROUGH inb, int ins, ref SCSI_PASS_THROUGH outb, int outs, out int ret, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inb, int ins, byte[] outb, int outs, out int ret, IntPtr ov);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid cls, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid cls, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int needed, ref SP_DEVINFO_DATA devInfo);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Request_Device_Eject(int devInst, out int vetoType, System.Text.StringBuilder vetoName, int nameLength, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Query_And_Remove_SubTree(int devInst, out int vetoType, System.Text.StringBuilder vetoName, int nameLength, int flags);

        private static readonly Guid DiskInterface = new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

        internal static int Run(string[] args)
        {
            // Exactly: --eject --disk <n> --parent <pid>
            if (args.Length != 5 || args[1] != "--disk" || args[3] != "--parent") return 10;
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int disk) || disk < 0 || disk > 255) return 12;
            if (!int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid) || parentPid <= 0) return 13;
            string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "X360GameHack.exe");
            string parent = Program.ProcessImagePath(parentPid);
            if (parent == null || !string.Equals(Path.GetFullPath(parent), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) return 14;
            if (Program.IsSystemDisk(disk)) return 21;
            if (Program.WindowsFileSystemOn(disk) != null) return 27;
            using (var h = CreateFile(@"\\.\PhysicalDrive" + disk.ToString(CultureInfo.InvariantCulture), 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return 20;
                using (var fs = new FileStream(h, FileAccess.Read, 1, false))
                {
                    long size = Program.DiskLength(h);
                    if (size < Program.Align || !Program.HasXboxFileSystem(fs, size - size % Program.Align)) return 23;
                    if (!FlushFileBuffers(h)) return 61;                 // the drive's own write cache onto the platters
                    SpinDown(h);                                          // best effort: some USB adapters pass neither command on
                }
            }
            int devInst = DevInstOf(disk);
            if (devInst == 0) return 62;
            var veto = new System.Text.StringBuilder(512);
            if (CM_Request_Device_Eject(devInst, out int vetoType, veto, veto.Capacity, 0) == 0) return 0;
            veto.Clear();
            // a port Windows does not call removable: take the disk device away until it is plugged in again
            if (CM_Query_And_Remove_SubTree(devInst, out vetoType, veto, veto.Capacity, 2 /* CM_REMOVE_NO_RESTART */) == 0) return 0;
            return vetoType != 0 ? 63 : 64;                           // 63 = something has it open
        }

        private static void SpinDown(SafeFileHandle h)
        {
            var a = new ATA_PASS_THROUGH_EX { PreviousTaskFile = new byte[8], CurrentTaskFile = new byte[8] };
            a.Length = (ushort)Marshal.SizeOf(typeof(ATA_PASS_THROUGH_EX));
            a.AtaFlags = 1;            // ATA_FLAGS_DRDY_REQUIRED, no data
            a.TimeOutValue = 30;
            a.CurrentTaskFile[6] = 0xE0;                               // STANDBY IMMEDIATE
            if (DeviceIoControl(h, 0x0004D02C, ref a, a.Length, ref a, a.Length, out _, IntPtr.Zero)) return;
            var s = new SCSI_PASS_THROUGH { Cdb = new byte[16] };
            s.Length = (ushort)Marshal.SizeOf(typeof(SCSI_PASS_THROUGH));
            s.CdbLength = 6; s.DataIn = 2 /* SCSI_IOCTL_DATA_UNSPECIFIED */; s.TimeOutValue = 30;
            s.Cdb[0] = 0x1B; s.Cdb[4] = 0x00;                         // START STOP UNIT: stop
            DeviceIoControl(h, 0x0004D004, ref s, s.Length, ref s, s.Length, out _, IntPtr.Zero);
        }

        /// <summary>The device node of PhysicalDrive<paramref name="disk"/>, or 0.</summary>
        private static int DevInstOf(int disk)
        {
            var cls = DiskInterface;
            IntPtr set = SetupDiGetClassDevs(ref cls, IntPtr.Zero, IntPtr.Zero, 0x2 | 0x10);   // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
            if (set == new IntPtr(-1)) return 0;
            try
            {
                for (int i = 0; ; i++)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref cls, i, ref data)) return 0;
                    var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int needed, ref info);
                    if (needed < 8 || needed > 4096) continue;
                    IntPtr detail = Marshal.AllocHGlobal(needed);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);          // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, needed, out _, ref info)) continue;
                        string path = Marshal.PtrToStringUni(detail + 4);
                        using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (h.IsInvalid) continue;
                            var o = new byte[12];
                            if (DeviceIoControl(h, 0x002D1080, IntPtr.Zero, 0, o, 12, out _, IntPtr.Zero) && BitConverter.ToInt32(o, 4) == disk) return info.DevInst;
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }

    /// <summary>
    /// "Bring back drive": a disk that was safely removed but is still connected stays parked by Windows (device problem
    /// 21 "being removed" or 47 "held for eject") - a hardware scan does not start it again (checked on a real drive:
    /// only "ready" does). Every such disk is started again, then Windows looks for disks connected since (a port that
    /// does not report new drives by itself). Nothing is read from or written to any disk.
    ///   --remount --parent pid       exit 0 = done (brought back, or nothing was parked); 71 = a parked disk did not start
    /// </summary>
    internal static class Remount
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid cls, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out int devInst, string id, int flags);
        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Reenumerate_DevNode(int devInst, int flags);
        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Setup_DevNode(int devInst, int flags);
        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_DevNode_PropertyW(int devInst, ref DEVPROPKEY key, out uint type, byte[] buffer, ref uint size, uint flags);

        private static readonly Guid DiskDrives = new Guid("4D36E967-E325-11CE-BFC1-08002BE10318");          // GUID_DEVCLASS_DISKDRIVE
        private static DEVPROPKEY ProblemCode = new DEVPROPKEY { fmtid = new Guid("4340A6C5-93FA-4706-972C-7B648008A5A7"), pid = 3 };

        internal static int Run(string[] args)
        {
            // Exactly: --remount --parent <pid>
            if (args.Length != 3 || args[1] != "--parent") return 10;
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid) || parentPid <= 0) return 13;
            string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "X360GameHack.exe");
            string parent = Program.ProcessImagePath(parentPid);
            if (parent == null || !string.Equals(Path.GetFullPath(parent), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) return 14;

            int failed = 0;
            var cls = DiskDrives;
            IntPtr set = SetupDiGetClassDevs(ref cls, IntPtr.Zero, IntPtr.Zero, 0x2 /* DIGCF_PRESENT */);
            if (set != new IntPtr(-1))
            {
                try
                {
                    for (int i = 0; i < 256; i++)
                    {
                        var d = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                        if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                        int problem = Problem(d.DevInst);
                        if (problem != 21 && problem != 47) continue;               // only disks parked by a safe removal
                        if (CM_Setup_DevNode(d.DevInst, 0 /* CM_SETUP_DEVNODE_READY */) != 0) failed++;
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(set); }
            }
            if (CM_Locate_DevNodeW(out int root, null, 0) == 0) CM_Reenumerate_DevNode(root, 1 /* CM_REENUMERATE_SYNCHRONOUS */);
            return failed == 0 ? 0 : 71;
        }

        /// <summary>The device's problem code (DEVPKEY_Device_ProblemCode), 0 when it has none or it cannot be read.</summary>
        private static int Problem(int devInst)
        {
            var buf = new byte[4];
            uint size = 4;
            var key = ProblemCode;
            if (CM_Get_DevNode_PropertyW(devInst, ref key, out uint type, buf, ref size, 0) != 0 || type != 7 /* DEVPROP_TYPE_UINT32 */ || size != 4) return 0;
            return BitConverter.ToInt32(buf, 0);
        }
    }

    internal static class Program
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
        private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        internal const int Align = 4096;
        internal const int MaxRequest = 4 * 1024 * 1024;
        internal const uint HelloMagic = 0x44484758;   // "XGHD"

        // Known XTAF (Xbox 360) and FATX (original Xbox) partition starts.
        private static readonly long[] SignatureOffsets =
        {
            0x80000, 0x80080000, 0x10C030000, 0x10C080000, 0x118EB0000, 0x120EB0000, 0x130EB0000,
            0x2EE80000, 0x5DC80000, 0x8CA80000, 0xABE80000, 0x1DD156000
        };

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        private static int Main(string[] args)
        {
            try { return Run(args); }
            catch { return 1; }
        }

        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
        internal const long ProtectedStart = 0x80000;          // never written: security sector, Josh, partition data
        internal const byte OpRead = 1, OpWrite = 2, OpUndo = 3, OpSessions = 4;

        private static int Run(string[] args)
        {
            // only ever started by the native launcher next to it, which gives it a clean environment
            if (!StartedByLauncher()) return 15;
            if (args.Length > 0 && args[0] == "--eject") return Eject.Run(args);
            if (args.Length > 0 && args[0] == "--remount") return Remount.Run(args);
            // Exactly: --pipe <name> --disk <n> --parent <pid> [--write [--format og|x360]]. Anything else is rejected.
            bool write = (args.Length == 7 || args.Length == 9) && args[6] == "--write";
            string format = args.Length == 9 && write && args[7] == "--format" && (args[8] == "og" || args[8] == "x360") ? args[8] : null;
            if ((args.Length != 6 && !(args.Length == 7 && write) && format == null) || args[0] != "--pipe" || args[2] != "--disk" || args[4] != "--parent") return 10;
            string pipe = args[1];
            if (!Regex.IsMatch(pipe, "^X360GameHack-disk-[0-9a-f]{32}$")) return 11;
            if (!int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int disk) || disk < 0 || disk > 255) return 12;
            if (!int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid) || parentPid <= 0) return 13;

            // Only the X360GameHack.exe installed next to this helper may use it.
            string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "X360GameHack.exe");
            string parent = ProcessImagePath(parentPid);
            if (parent == null || !string.Equals(Path.GetFullPath(parent), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) return 14;

            if (IsSystemDisk(disk)) return 21;
            if (WindowsFileSystemOn(disk) != null) return 27;
            using (var h = CreateFile(@"\\.\PhysicalDrive" + disk.ToString(CultureInfo.InvariantCulture), write ? GENERIC_READ | GENERIC_WRITE : GENERIC_READ,
                       FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, write ? FILE_FLAG_WRITE_THROUGH : 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return 20;
                long size = DiskLength(h);
                if (size < Align) return 22;
                size -= size % Align;
                using (var fs = new FileStream(h, write ? FileAccess.ReadWrite : FileAccess.Read, 1, false))
                {
                    bool xbox = HasXboxFileSystem(fs, size);
                    if (!xbox && format == null) return 23;                          // only a format may start from a blank drive
                    Journal journal = null;
                    List<Range> allowed = null;
                    if (write)
                    {
                        if (HasWindowsPartitionTable(fs)) return 24;
                        if (DevkitVolumes(fs, size).Count > 0) return 26;          // development kit drives stay read-only (not tested yet)
                        var existing = xbox ? WritableRanges(fs, size) : new List<Range>();
                        allowed = format != null ? FormatRanges(format, size) : existing;
                        if (allowed.Count == 0) return 23;
                        string folder = UndoFolder();
                        if (folder == null) return 25;
                        journal = new Journal(folder, size, StableIdentity(fs, size), DiskIdentity(fs, size), fs, allowed, existing, format);
                    }
                    using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None))
                    {
                        client.Connect(15000);
                        if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out uint serverPid) || serverPid != (uint)parentPid) return 30;
                        WatchParent(parentPid);
                        try { return Serve(fs, size, client, allowed, journal); }
                        finally { journal?.Dispose(); }
                    }
                }
            }
        }

        internal struct Range { public long Start, End; public override string ToString() => Start.ToString("X") + "-" + End.ToString("X"); }

        /// <summary>
        /// Request loop. Every request starts with one op byte:
        ///   1 read  [int64 offset][int32 length]            -> [int32 count][bytes]
        ///   2 write [int64 offset][int32 length][bytes]     -> [int32 status]    (write mode only)
        ///   3 undo  [int32 n][n bytes: session file name]   -> [int32 status][int32 restored][int32 total]
        ///   4 list undo sessions                            -> [int32 n] n x [string]
        /// Offsets and lengths are 4 KB aligned and bounded; anything malformed ends the session.
        /// </summary>
        internal static int Serve(Stream disk, long size, Stream pipe, List<Range> allowed = null, Journal journal = null)
        {
            var r = new BinaryReader(pipe);
            var w = new BinaryWriter(pipe);
            w.Write(HelloMagic);
            w.Write(size);
            w.Write(journal != null);
            w.Write(journal?.SessionName ?? "");
            w.Flush();
            var buf = new byte[MaxRequest];
            var old = new byte[MaxRequest];
            while (true)
            {
                byte op;
                try { op = r.ReadByte(); }
                catch (EndOfStreamException) { return 0; }        // X360GameHack closed the drive
                catch (IOException) { return 0; }
                if (op == OpRead)
                {
                    long off = r.ReadInt64(); int len = r.ReadInt32();
                    if (!Aligned(off, len, size)) return 40;
                    int n = ReadAt(disk, off, buf, len);
                    w.Write(n);
                    w.Write(buf, 0, n);
                    w.Flush();
                }
                else if (op == OpWrite)
                {
                    long off = r.ReadInt64(); int len = r.ReadInt32();
                    if (journal == null || !Aligned(off, len, size)) return 41;
                    int got = 0;
                    while (got < len) { int k = r.Read(buf, got, len - got); if (k <= 0) return 0; got += k; }
                    // below 0x80000 only a format session writes, and only inside its allowed ranges (the first 4 KB): an original
                    // Xbox one its partition table and refurb sector; an Xbox 360 one nothing but zeros (an old original Xbox
                    // table taken off - the security sector, sector 16, is not in that block)
                    bool low = off < ProtectedStart && !(journal.Mode == "og" || (journal.Mode == "x360" && AllZero(buf, len)));
                    if (low || !allowed.Any(a => off >= a.Start && off + len <= a.End)) { w.Write(1); w.Flush(); continue; }
                    w.Write(WriteChecked(disk, off, buf, old, len, journal));
                    w.Flush();
                }
                else if (op == OpUndo)
                {
                    int n = r.ReadInt32();
                    if (n <= 0 || n > 200) return 42;
                    string name = Encoding.UTF8.GetString(r.ReadBytes(n));
                    if (journal == null || !Journal.ValidName(name)) { w.Write(1); w.Write(0); w.Write(0); w.Flush(); continue; }
                    var res = journal.Undo(name, disk, allowed);
                    w.Write(res.Status); w.Write(res.Restored); w.Write(res.Total);
                    w.Flush();
                }
                else if (op == OpSessions)
                {
                    var names = journal == null ? new List<string>() : journal.Sessions();
                    w.Write(names.Count);
                    foreach (var s in names) w.Write(s);
                    w.Flush();
                }
                else return 43;
            }
        }

        private static bool AllZero(byte[] b, int len)
        {
            for (int i = 0; i < len; i++) if (b[i] != 0) return false;
            return true;
        }

        private static bool Aligned(long off, int len, long size) =>
            off >= 0 && len > 0 && len <= MaxRequest && off % Align == 0 && len % Align == 0 && off <= size - len;

        internal static int ReadAt(Stream disk, long off, byte[] buf, int len)
        {
            disk.Position = off;
            int n = 0;
            while (n < len)
            {
                int k = disk.Read(buf, n, len - n);
                if (k <= 0) break;
                n += k;
            }
            return n;
        }

        /// <summary>
        /// One write: what the sectors held goes into the journal (flushed first), then the new bytes are written,
        /// flushed and read back. 0 = written and the same when read back, 2 = read back different, 3 = could not be done.
        /// </summary>
        internal static int WriteChecked(Stream disk, long off, byte[] data, byte[] scratch, int len, Journal journal)
        {
            try
            {
                if (ReadAt(disk, off, scratch, len) != len) return 3;
                if (scratch.Take(len).SequenceEqual(data.Take(len))) return 0;          // already so: nothing to change
                journal.Record(off, scratch, data, len);
                disk.Position = off;
                disk.Write(data, 0, len);
                if (disk is FileStream f) f.Flush(true); else disk.Flush();
                if (ReadAt(disk, off, scratch, len) != len) return 3;
                for (int i = 0; i < len; i++) if (scratch[i] != data[i]) return 2;
                return 0;
            }
            catch (IOException) { return 3; }
            catch (UnauthorizedAccessException) { return 3; }
        }

        /// <summary>A disk that holds a Windows (MBR or GPT) partition table is never changed.</summary>
        internal static bool HasWindowsPartitionTable(Stream fs)
        {
            var b = new byte[1024];
            if (ReadAt(fs, 0, b, 1024) < 1024) return true;                       // cannot tell: fail closed
            if (Encoding.ASCII.GetString(b, 512, 8) == "EFI PART") return true;
            if (b[510] == 0x55 && b[511] == 0xAA)
                for (int i = 0; i < 4; i++) if (b[446 + i * 16 + 4] != 0) return true;   // a partition entry with a type
            return false;
        }

        // Xbox 360 retail layout: the system partitions that hold files, up to the Data partition at 0x130EB0000. The two
        // cache partitions (0x80000, 0x80080000) hold the console's own cache data and are never written.
        private static readonly long[] X360Fixed = { 0x10C080000, 0x118EB0000, 0x120EB0000, 0x130EB0000 };
        private static readonly long[] X360Caches = { 0x80000, 0x80080000 };

        /// <summary>
        /// An Xbox 360 (XTAF) partition holds files only when its table starts with the media marker (FF F8 or FF FF FF F8);
        /// the console's caches carry an XTAF header over their own data. Original Xbox (FATX) partitions are taken as found.
        /// </summary>
        private static bool HoldsFiles(Stream fs, long off, byte[] b)
        {
            if (ReadAt(fs, off, b, Align) < 4) return false;
            string m = Encoding.ASCII.GetString(b, 0, 4);
            if (m == "FATX") return true;
            if (m != "XTAF") return false;
            if (ReadAt(fs, off + 0x1000, b, Align) < 4) return false;
            return (b[0] == 0xFF && b[1] == 0xF8) || (b[0] == 0xFF && b[1] == 0xFF && b[2] == 0xFF && b[3] == 0xF8);
        }

        /// <summary>
        /// Where writes may go: each Xbox partition found on the disk, from its start to the next one found (or the end).
        /// On an Xbox 360 retail layout (Data partition present), also the empty fixed slots (for example the
        /// compatibility partition at 0x120EB0000) so a missing one can be created - nothing below 0x80000, ever.
        /// </summary>
        internal static List<Range> WritableRanges(Stream fs, long size)
        {
            var b = new byte[Align];
            var starts = new SortedSet<long>();
            foreach (long off in SignatureOffsets)
            {
                if (off + Align > size || off < ProtectedStart) continue;
                if (HoldsFiles(fs, off, b)) starts.Add(off);
            }
            bool x360 = starts.Contains(0x130EB0000) && ReadAt(fs, 0x130EB0000, b, Align) >= 4 && Encoding.ASCII.GetString(b, 0, 4) == "XTAF";
            if (x360)
            {
                foreach (long off in X360Caches) starts.Remove(off);
                foreach (long off in X360Fixed) if (off + Align <= size) starts.Add(off);
            }
            var list = starts.ToList();
            var ranges = new List<Range>();
            for (int i = 0; i < list.Count; i++) ranges.Add(new Range { Start = list[i], End = i + 1 < list.Count ? list[i + 1] : size });
            // on an Xbox 360 drive nothing in front of SysExt (the caches) is ever written
            if (x360) ranges.RemoveAll(r => r.Start < 0x10C080000);
            return ranges;
        }

        /// <summary>
        /// The allocation table of every partition writes may go to, as (start, 4 KB blocks): from the partition start +
        /// 0x1000, (clusters + 1) entries of 2 or 4 bytes (Xbox 360: from the media marker; else 4 bytes from 0xFFF0
        /// entries on), rounded up to 4 KB - the reader's own sizing, at most one block more. A slot without a file
        /// system (an empty compatibility slot) has no table.
        /// </summary>
        internal static List<(long Start, int Blocks)> TableRegions(Stream fs, List<Range> allowed)
        {
            var list = new List<(long, int)>();
            if (allowed == null) return list;
            var b = new byte[Align];
            foreach (var r in allowed)
            {
                if (ReadAt(fs, r.Start, b, Align) < 16) continue;
                string m = Encoding.ASCII.GetString(b, 0, 4);
                bool be = m == "XTAF";
                if (!be && m != "FATX") continue;
                uint spc = be ? (uint)(b[8] << 24 | b[9] << 16 | b[10] << 8 | b[11]) : BitConverter.ToUInt32(b, 8);
                if (spc == 0 || spc > 1024 || (spc & (spc - 1)) != 0) continue;
                long clusters = (r.End - r.Start) / (spc * 512L), entries = clusters + 1;
                int entry = entries < 0xFFF0 ? 2 : 4;
                if (be && ReadAt(fs, r.Start + 0x1000, b, Align) >= 4)
                {
                    if (b[0] == 0xFF && b[1] == 0xFF && b[2] == 0xFF && b[3] == 0xF8) entry = 4;
                    else if (b[0] == 0xFF && b[1] == 0xF8) entry = 2;
                }
                long bytes = (entries * entry + 0xFFF) & ~0xFFFL;
                if (r.Start + 0x1000 + bytes > r.End || bytes / Align > int.MaxValue) continue;
                list.Add((r.Start + 0x1000, (int)(bytes / Align)));
            }
            return list;
        }

        /// <summary>
        /// Where a format session may write: original Xbox ("og") the first 4 KB (sector 0's partition table, sector 3's
        /// refurb info) and everything from 0x80000; Xbox 360 ("x360") from SysExt (0x10C080000) on, plus zeros (only zeros,
        /// see Serve) in the first 4 KB to take an old original Xbox partition table off - never the drive's security sector
        /// (0x2000) or the console's two cache partitions. Worked out here, never taken from the program.
        /// </summary>
        internal static List<Range> FormatRanges(string kind, long size)
        {
            var list = new List<Range>();
            if (kind == "og")
            {
                if (size >= Align) list.Add(new Range { Start = 0, End = Align });
                if (size > ProtectedStart) list.Add(new Range { Start = ProtectedStart, End = size });
            }
            else if (kind == "x360" && size > 0x10C080000)
            {
                list.Add(new Range { Start = 0, End = Align });              // zeros only (see Serve): an old original Xbox table taken off
                list.Add(new Range { Start = 0x10C080000, End = size });
            }
            return list;
        }

        /// <summary>
        /// What ties an undo journal to this disk from version 4.0.0 (2026-11-25) on: its size and the bytes from 4 KB to
        /// 0x80000 - written by nothing, in any mode (an Xbox 360 drive's security sector is there). Unlike
        /// <see cref="DiskIdentity"/> it stays the same when the drive is formatted, so a format - even one that stopped
        /// half way - can be undone.
        /// </summary>
        internal static string StableIdentity(Stream fs, long size)
        {
            using (var h = SHA256.Create())
            {
                var tag = Encoding.ASCII.GetBytes("x360gh-disk-2");
                h.TransformBlock(tag, 0, tag.Length, null, 0);
                var sz = BitConverter.GetBytes(size); h.TransformBlock(sz, 0, 8, null, 0);
                // exactly 4 KB up to 0x80000 - not a byte further: the original Xbox's X: partition starts there
                var b = new byte[64 * 1024];
                for (long at = Align; at < ProtectedStart;)
                {
                    int n = (int)Math.Min(b.Length, ProtectedStart - at);
                    if (at + n > size || ReadAt(fs, at, b, n) != n) break;
                    h.TransformBlock(b, 0, n, null, 0);
                    at += n;
                }
                h.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>What tied an undo journal to this disk before 2026-11-25: its size and the first sector of every Xbox partition start found on it.</summary>
        internal static string DiskIdentity(Stream fs, long size)
        {
            using (var h = SHA256.Create())
            {
                var b = new byte[Align];
                var sz = BitConverter.GetBytes(size); h.TransformBlock(sz, 0, 8, null, 0);
                foreach (long off in new long[] { 0x80000, 0x130EB0000, 0xABE80000 })
                    if (off + Align <= size && ReadAt(fs, off, b, Align) == Align) h.TransformBlock(b, 0, 16, null, 0);   // magic + volume id + cluster size + root
                h.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// %ProgramData%\X360GameHack\DiskUndo, made so that only administrators and SYSTEM can change it (users may read).
        /// BOTH folders are checked and locked down - %ProgramData% lets every user create folders, so a user could make
        /// X360GameHack there first and later swap DiskUndo for a link to anywhere. A folder that belongs to someone else,
        /// or that is a link (junction, symbolic link), is not trusted: write mode is then refused.
        /// </summary>
        internal static string UndoFolder() => UndoFolderIn(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        /// <summary><see cref="UndoFolder"/> under <paramref name="programData"/> (a parameter so the tests can point it at a test folder).</summary>
        internal static string UndoFolderIn(string programData)
        {
            try
            {
                string root = Path.Combine(programData, "X360GameHack");
                string dir = Path.Combine(root, "DiskUndo");
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                var sec = new DirectorySecurity();
                sec.SetOwner(admins);
                sec.SetAccessRuleProtection(true, false);
                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                foreach (string d in new[] { root, dir })
                {
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d, sec);
                    if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) return null;          // a junction or link: never followed
                    var owner = Directory.GetAccessControl(d).GetOwner(typeof(SecurityIdentifier));
                    if (!owner.Equals(admins) && !owner.Equals(system)) return null;
                    Directory.SetAccessControl(d, sec);
                }
                return dir;
            }
            catch { return null; }
        }

        internal static bool HasXboxFileSystem(Stream fs, long size)
        {
            var b = new byte[Align];
            foreach (long off in SignatureOffsets)
            {
                if (off + Align > size) continue;
                fs.Position = off;
                if (fs.Read(b, 0, Align) < 4) continue;
                string m = Encoding.ASCII.GetString(b, 0, 4);
                if (m == "XTAF" || m == "FATX") return true;
            }
            return DevkitVolumes(fs, size).Count > 0;
        }

        /// <summary>
        /// The XTAF volumes a development kit drive's partition table (sector 0: offset and length in 512-byte sectors,
        /// big-endian, entries at 0x08 0x10 0x20 0x28 0x40 0x48 0x50) points at. Empty for any other drive.
        /// </summary>
        internal static List<long> DevkitVolumes(Stream fs, long size)
        {
            var found = new List<long>();
            var b = new byte[Align];
            if (size < Align || ReadAt(fs, 0, b, Align) < 0x200) return found;
            var spans = new List<(long Off, long Len)>();
            foreach (int at in new[] { 0x08, 0x10, 0x20, 0x28, 0x40, 0x48, 0x50 })
            {
                long off = (long)((uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3])) * 512;
                long len = (long)((uint)(b[at + 4] << 24 | b[at + 5] << 16 | b[at + 6] << 8 | b[at + 7])) * 512;
                if (off < 0x1000 || len < 0x10000 || off + len > size || off % Align != 0) continue;
                if (spans.Any(p => off < p.Off + p.Len && p.Off < off + len)) return new List<long>();      // overlapping: not a table
                spans.Add((off, len));
            }
            var m = new byte[Align];
            foreach (var p in spans)
                if (ReadAt(fs, p.Off, m, Align) >= 4 && Encoding.ASCII.GetString(m, 0, 4) == "XTAF") found.Add(p.Off);
            return found;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION { public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId; }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);

        /// <summary>
        /// True when the process that started this one is the native launcher in this folder (X360GameHack.DiskReader.exe).
        /// That launcher is what Windows elevates; it starts this program with a clean environment. Started any other way
        /// (directly elevated, by another program), this program does nothing.
        /// </summary>
        internal static bool StartedByLauncher()
        {
            var info = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, ref info, Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)), out _) != 0) return false;
            long parent = info.InheritedFromUniqueProcessId.ToInt64();
            if (parent <= 0 || parent > int.MaxValue) return false;
            string image = ProcessImagePath((int)parent);
            string launcher = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "X360GameHack.DiskReader.exe");
            return image != null && string.Equals(Path.GetFullPath(image), Path.GetFullPath(launcher), StringComparison.OrdinalIgnoreCase);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindFirstVolumeW(StringBuilder name, int length);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool FindNextVolumeW(IntPtr find, StringBuilder name, int length);
        [DllImport("kernel32.dll")]
        private static extern bool FindVolumeClose(IntPtr find);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetVolumeInformationW(string root, StringBuilder volumeName, int volumeNameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, int fileSystemSize);

        /// <summary>
        /// The file system of a Windows volume on disk <paramref name="disk"/> (for example "NTFS"), or null when the disk
        /// holds none. An Xbox drive never carries one: Windows sees at most a RAW volume on it (left over from an old
        /// partition table), which is not counted. A disk with a real Windows volume is never opened - not even to read:
        /// that keeps this administrator helper from ever reading a Windows disk sector by sector. "?" = the volumes could
        /// not be listed (fails closed).
        /// </summary>
        internal static string WindowsFileSystemOn(int disk)
        {
            var name = new StringBuilder(1024);
            IntPtr find = FindFirstVolumeW(name, name.Capacity);
            if (find == new IntPtr(-1)) return "?";
            try
            {
                do
                {
                    string volume = name.ToString();                                 // \\?\Volume{guid}\
                    if (!VolumeOnDisk(volume, disk)) continue;
                    var fs = new StringBuilder(64);
                    if (GetVolumeInformationW(volume, null, 0, out _, out _, out _, fs, fs.Capacity) && fs.Length > 0 &&
                        !string.Equals(fs.ToString(), "RAW", StringComparison.OrdinalIgnoreCase)) return fs.ToString();
                }
                while (FindNextVolumeW(find, name, name.Capacity));
            }
            finally { FindVolumeClose(find); }
            return null;
        }

        /// <summary>True when any part of the volume lies on disk <paramref name="disk"/>.</summary>
        private static bool VolumeOnDisk(string volume, int disk)
        {
            using (var v = CreateFile(volume.TrimEnd('\\'), 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (v.IsInvalid) return false;
                var o = new byte[8 + 24 * 32];                                       // VOLUME_DISK_EXTENTS, up to 32 extents
                if (!DeviceIoControl(v, 0x00560000 /* IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS */, IntPtr.Zero, 0, o, o.Length, out int got, IntPtr.Zero)) return false;
                int n = BitConverter.ToInt32(o, 0);
                for (int i = 0; i < n && 8 + i * 24 + 4 <= got; i++)
                    if (BitConverter.ToInt32(o, 8 + i * 24) == disk) return true;
            }
            return false;
        }

        internal static long DiskLength(SafeFileHandle h)
        {
            var o = new byte[8];
            return DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, o, 8, out _, IntPtr.Zero) ? BitConverter.ToInt64(o, 0) : -1;
        }

        /// <summary>True when the disk holds the Windows volume (or when that cannot be determined - fail closed).</summary>
        internal static bool IsSystemDisk(int disk)
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory);      // "C:\"
            if (string.IsNullOrEmpty(root) || root.Length < 2) return true;
            using (var v = CreateFile(@"\\.\" + root.Substring(0, 2), 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (v.IsInvalid) return true;
                var o = new byte[12];
                if (!DeviceIoControl(v, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, o, 12, out _, IntPtr.Zero)) return true;
                return BitConverter.ToInt32(o, 4) == disk;
            }
        }

        internal static string ProcessImagePath(int pid)
        {
            IntPtr p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (p == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(p, 0, sb, ref n) ? sb.ToString(0, n) : null;
            }
            finally { CloseHandle(p); }
        }

        private static void WatchParent(int pid)
        {
            var t = new Thread(() =>
            {
                try { using (var p = Process.GetProcessById(pid)) p.WaitForExit(); }
                catch { }
                Environment.Exit(0);
            }) { IsBackground = true };
            t.Start();
        }
    }
}
