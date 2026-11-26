using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Something whose Xbox 360 memory can be read and written: a console over XBDM, or a game running in Xenia.</summary>
public interface IMemoryTarget
{
    byte[] GetMemory(uint address, uint length);
    /// <summary>Like GetMemory, but a range with gaps is not an error: unreadable bytes are zero and listed in holes (start, length).</summary>
    byte[] GetMemoryWithGaps(uint address, uint length, List<KeyValuePair<uint, uint>> holes = null);
    void SetMemory(uint address, byte[] data);
    List<XboxMemoryRegion> WalkCommittedMemory();
}

public partial class XboxConsole : IMemoryTarget { }

namespace X360GameHack.Core.Net
{
    /// <summary>
    /// The memory of a game running in Xenia on this PC, addressed exactly like the console's (0x82000000 is
    /// 0x82000000). Xenia keeps the emulated console's whole 4 GB address space as one block inside its own
    /// process, at the first free power of two from 4 GB up; this finds that block and reads / writes inside it
    /// with the ordinary Windows calls a same-user program may use on another of the user's programs. No
    /// administrator rights, no driver, nothing injected into Xenia.
    ///
    /// Good for values, tables, text and anything the game reads as data. Code is different: Xenia keeps a game's
    /// code and read-only data read-only while it runs (and translates code to PC code the first time it runs), so
    /// code patches belong in a Xenia patch file, applied when the game starts.
    /// </summary>
    public sealed class XeniaProcess : IMemoryTarget, IDisposable
    {
        private const uint VmOperation = 0x0008, VmRead = 0x0010, VmWrite = 0x0020, QueryInformation = 0x0400;
        private const uint MemCommit = 0x1000, MemFree = 0x10000, MemMapped = 0x40000;
        private const uint PageNoAccess = 0x01, PageGuard = 0x100;
        private const uint PageReadWrite = 0x04, PageWriteCopy = 0x08, PageExecuteReadWrite = 0x40, PageExecuteWriteCopy = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress, AllocationBase;
            public uint AllocationProtect; public uint Alignment1;
            public IntPtr RegionSize;
            public uint State, Protect, Type; public uint Alignment2;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation info, IntPtr length);

        private IntPtr _handle;
        public int ProcessId { get; }
        public string ProcessName { get; }
        /// <summary>Where the emulated console's address 0 sits inside Xenia.</summary>
        public long MemoryBase { get; }
        public bool IsRunning { get { try { using (var p = Process.GetProcessById(ProcessId)) return !p.HasExited; } catch (Exception) { return false; } } }

        /// <summary>The Title ID of the game Xenia is running, from its window title ("... | [5841128F v0.0.0.4] Terraria"); null when it shows none.</summary>
        public string RunningTitleId
        {
            get
            {
                try
                {
                    using (var p = Process.GetProcessById(ProcessId))
                    {
                        var m = Regex.Match(p.MainWindowTitle ?? "", @"\[([0-9A-Fa-f]{8}) v");
                        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
                    }
                }
                catch (Exception) { return null; }
            }
        }

        private XeniaProcess(IntPtr handle, int id, string name, long memoryBase) { _handle = handle; ProcessId = id; ProcessName = name; MemoryBase = memoryBase; }

        /// <summary>Running Xenia programs (any build whose file name starts with "xenia").</summary>
        public static List<Process> Running() =>
            Process.GetProcesses().Where(p => { try { return p.ProcessName.StartsWith("xenia", StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } }).ToList();

        /// <summary>Attaches to the running Xenia. Throws with a plain reason when there is none or no game has been loaded yet.</summary>
        public static XeniaProcess Attach()
        {
            if (!Environment.Is64BitProcess) throw new InvalidOperationException("Reading Xenia's memory needs the 64-bit build of X360GameHack.");
            var running = Running();
            if (running.Count == 0) throw new InvalidOperationException("Xenia is not running. Start a game in Xenia first (Xenia page > Games).");
            Exception last = null;
            foreach (var p in running)
            {
                IntPtr h = OpenProcess(VmOperation | VmRead | VmWrite | QueryInformation, false, p.Id);
                if (h == IntPtr.Zero) { last = new InvalidOperationException("Windows did not allow access to " + p.ProcessName + " (is it running as administrator?)."); continue; }
                long found = FindMemoryBase(h);
                if (found != 0) return new XeniaProcess(h, p.Id, p.ProcessName, found);
                CloseHandle(h);
                last = new InvalidOperationException("Xenia is running but has not set up a game's memory yet. Start a game, then try again.");
            }
            throw last ?? new InvalidOperationException("Xenia is not running.");
        }

        // The console's memory is a set of views of one section, placed at the first of 4 GB, 8 GB, 16 GB ... that was free.
        // A candidate is right when both the low and the high half of the 4 GB block are such views.
        private static long FindMemoryBase(IntPtr process)
        {
            for (int n = 32; n <= 46; n++)
            {
                long candidate = 1L << n;
                if (IsMappedView(process, candidate + 0x00010000) && IsMappedView(process, candidate + 0x80000000L) && IsMappedView(process, candidate + 0xC0000000L)) return candidate;
            }
            return 0;
        }

        private static bool IsMappedView(IntPtr process, long address)
        {
            if (VirtualQueryEx(process, new IntPtr(address), out MemoryBasicInformation info, new IntPtr(Marshal.SizeOf(typeof(MemoryBasicInformation)))) == IntPtr.Zero) return false;
            return info.State != MemFree && info.Type == MemMapped;
        }

        private IntPtr Handle()
        {
            if (_handle == IntPtr.Zero) throw new ObjectDisposedException("Xenia");
            return _handle;
        }

        public byte[] GetMemory(uint address, uint length)
        {
            if (length == 0) return new byte[0];
            if (length > 256u * 1024 * 1024 || (ulong)address + length > 0x100000000UL) throw new InvalidOperationException("That range is outside the console's memory.");
            var buffer = new byte[length];
            if (!ReadProcessMemory(Handle(), new IntPtr(MemoryBase + address), buffer, new IntPtr(length), out IntPtr read) || read.ToInt64() != length)
                throw new InvalidOperationException(IsRunning ? "0x" + address.ToString("X8") + " is not allocated in the emulated console." : "Xenia has closed.");
            return buffer;
        }

        public byte[] GetMemoryWithGaps(uint address, uint length, List<KeyValuePair<uint, uint>> holes = null)
        {
            try { return GetMemory(address, length); }
            catch (InvalidOperationException) { if (!IsRunning) throw; }
            var buffer = new byte[length];
            uint done = 0;
            while (done < length)
            {
                uint at = address + done;
                uint n = Math.Min(0x1000 - (at & 0xFFF), length - done);
                try { Buffer.BlockCopy(GetMemory(at, n), 0, buffer, (int)done, (int)n); }
                catch (InvalidOperationException) { if (!IsRunning) throw; holes?.Add(new KeyValuePair<uint, uint>(at, n)); }
                done += n;
            }
            return buffer;
        }

        public void SetMemory(uint address, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            if ((ulong)address + (ulong)data.Length > 0x100000000UL) throw new InvalidOperationException("That range is outside the console's memory.");
            if (!WriteProcessMemory(Handle(), new IntPtr(MemoryBase + address), data, new IntPtr(data.Length), out IntPtr written) || written.ToInt64() != data.Length)
                throw new InvalidOperationException(IsRunning ? "0x" + address.ToString("X8") + " cannot be written (not allocated, or read-only in the emulator)." : "Xenia has closed.");
        }

        /// <summary>
        /// True when every byte of the range is allocated and writable right now. Xenia keeps a game's code and
        /// read-only data read-only while it runs, as the console does; values and data are writable.
        /// </summary>
        public bool IsWritable(uint address, int length)
        {
            if (length <= 0) return true;
            if ((ulong)address + (ulong)length > 0x100000000UL) return false;
            long at = MemoryBase + address, end = at + length;
            int size = Marshal.SizeOf(typeof(MemoryBasicInformation));
            while (at < end)
            {
                if (VirtualQueryEx(Handle(), new IntPtr(at), out MemoryBasicInformation info, new IntPtr(size)) == IntPtr.Zero) return false;
                if (info.State != MemCommit || (info.Protect & PageGuard) != 0) return false;
                if ((info.Protect & (PageReadWrite | PageWriteCopy | PageExecuteReadWrite | PageExecuteWriteCopy)) == 0) return false;
                long next = info.BaseAddress.ToInt64() + info.RegionSize.ToInt64();
                if (next <= at) return false;
                at = next;
            }
            return true;
        }

        /// <summary>The allocated parts of the console's memory, as Xenia has them right now.</summary>
        public List<XboxMemoryRegion> WalkCommittedMemory()
        {
            var list = new List<XboxMemoryRegion>();
            long at = MemoryBase, end = MemoryBase + 0x100000000L;
            int size = Marshal.SizeOf(typeof(MemoryBasicInformation));
            while (at < end)
            {
                if (VirtualQueryEx(Handle(), new IntPtr(at), out MemoryBasicInformation info, new IntPtr(size)) == IntPtr.Zero) break;
                long regionSize = info.RegionSize.ToInt64();
                if (regionSize <= 0) break;
                long start = Math.Max(at, info.BaseAddress.ToInt64()), stop = Math.Min(end, info.BaseAddress.ToInt64() + regionSize);
                if (info.State == MemCommit && (info.Protect & (PageNoAccess | PageGuard)) == 0 && stop > start)
                {
                    uint guest = (uint)(start - MemoryBase);
                    var flags = (XboxMemoryRegionFlags)(int)(info.Protect & 0xFF);
                    var previous = list.Count > 0 ? list[list.Count - 1] : null;
                    if (previous != null && previous.BaseAddress + previous.Size == guest && previous.Flags == flags) previous.Size += (uint)(stop - start);
                    else list.Add(new XboxMemoryRegion { BaseAddress = guest, Size = (uint)(stop - start), Flags = flags });
                }
                at = info.BaseAddress.ToInt64() + regionSize;
            }
            return list;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
        }
    }

    /// <summary>Xenia patch files (.patch.toml) beyond a console: into a running Xenia, and into Xenia's own patches folder.</summary>
    public static class XeniaPatchTools
    {
        /// <summary>What happened to each enabled patch written into a game running in Xenia (patch names).</summary>
        public sealed class WriteReport
        {
            public int Writes;
            public readonly List<string> Written = new List<string>();
            /// <summary>Already in memory: Xenia applied them as the game started, or they were written before.</summary>
            public readonly List<string> AlreadyThere = new List<string>();
            /// <summary>Left out: they change code or read-only data (read-only while the game runs), or memory the game has not set up.</summary>
            public readonly List<string> NotWritable = new List<string>();
        }

        /// <summary>
        /// Writes every enabled patch into the game running in Xenia. A patch is written whole or not at all: one
        /// that touches memory Xenia keeps read-only is left out and named in the report, so a game is never left
        /// with half a patch. A patch whose bytes are already in memory is not written again.
        /// </summary>
        public static WriteReport WriteEnabled(XeniaPatchFile file, XeniaProcess xenia)
        {
            var report = new WriteReport();
            foreach (var patch in file.Patches)
            {
                if (!patch.IsEnabled) continue;
                var ops = new List<XeniaPatchOperation>();
                foreach (var op in patch.Operations)
                {
                    if (op.Bytes == null) op.EncodeFromRawValue();
                    if (op.Bytes != null && op.Bytes.Length > 0) ops.Add(op);
                }
                if (ops.Count == 0) continue;
                string name = string.IsNullOrWhiteSpace(patch.Name) ? "(unnamed patch)" : patch.Name.Trim();
                if (ops.All(op => Holds(xenia, op))) { report.AlreadyThere.Add(name); continue; }
                if (!ops.All(op => xenia.IsWritable(op.Address, op.Bytes.Length))) { report.NotWritable.Add(name); continue; }
                int done = 0;
                try
                {
                    foreach (var op in ops) { xenia.SetMemory(op.Address, op.Bytes); done++; report.Writes++; }
                    report.Written.Add(name);
                }
                catch (InvalidOperationException) when (xenia.IsRunning) { report.NotWritable.Add(done == 0 ? name : name + " (only partly written)"); }
            }
            return report;
        }

        private static bool Holds(XeniaProcess xenia, XeniaPatchOperation op)
        {
            try { return xenia.GetMemory(op.Address, (uint)op.Bytes.Length).SequenceEqual(op.Bytes); }
            catch (InvalidOperationException) when (xenia.IsRunning) { return false; }
        }

        private static string Code(string line)
        {
            // the part before a comment (a # outside quotes)
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') quoted = !quoted;
                else if (line[i] == '#' && !quoted) return line.Substring(0, i);
            }
            return line;
        }

        /// <summary>
        /// The same patch file text with each [[patch]]'s "is_enabled" set as given (one flag per patch, in file
        /// order). Everything else - comments, spacing, order - is kept as it is. A patch without the line gets
        /// one only when it is to be enabled (Xenia treats a missing line as disabled).
        /// </summary>
        public static string SetEnabled(string text, IList<bool> enabled)
        {
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            int patch = -1, header = -1; bool seen = true;
            for (int i = 0; i <= lines.Count; i++)
            {
                string code = i < lines.Count ? Code(lines[i]).Trim() : "[[patch]]";
                bool startsPatch = code.StartsWith("[[patch]]", StringComparison.OrdinalIgnoreCase), startsWrite = code.StartsWith("[[patch.", StringComparison.OrdinalIgnoreCase);
                if ((startsPatch || startsWrite) && !seen && patch >= 0 && patch < enabled.Count && enabled[patch])
                {
                    // this patch had no is_enabled line: add one right under its header
                    lines.Insert(header + 1, "    is_enabled = true");
                    i++;
                    seen = true;
                }
                if (i >= lines.Count) break;
                if (startsPatch) { patch++; header = i; seen = false; continue; }
                if (startsWrite) { seen = true; continue; }       // past the patch's own keys
                if (patch < 0 || patch >= enabled.Count || seen) continue;
                int eq = code.IndexOf('=');
                if (eq <= 0 || !code.Substring(0, eq).Trim().Equals("is_enabled", StringComparison.OrdinalIgnoreCase)) continue;
                string line = lines[i];
                string indent = line.Substring(0, line.Length - line.TrimStart().Length);
                string comment = line.Substring(Code(line).Length);
                lines[i] = indent + "is_enabled = " + (enabled[patch] ? "true" : "false") + (comment.Length > 0 ? " " + comment : "");
                seen = true;
            }
            return string.Join(newline, lines);
        }

        /// <summary>Where the Xenia at <paramref name="xeniaExe"/> looks for patch files.</summary>
        public static string PatchesFolder(string xeniaExe)
        {
            // a Xenia in a folder that cannot be written is started with its own storage folder - its patches live there
            if (X360GameHack.Core.Services.XeniaHost.NeedsOwnStorage(xeniaExe))
                return System.IO.Path.Combine(X360GameHack.Core.Services.XeniaHost.OwnStorage, "patches");
            string beside = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(xeniaExe));
            string documents = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Xenia");
            // Xenia keeps its files next to its program when its settings file is there (or portable.txt); otherwise in Documents\Xenia
            bool besideInUse = System.IO.File.Exists(System.IO.Path.Combine(beside, "xenia-canary.config.toml")) || System.IO.File.Exists(System.IO.Path.Combine(beside, "portable.txt"))
                               || System.IO.Directory.Exists(System.IO.Path.Combine(beside, "patches"));
            bool documentsInUse = System.IO.File.Exists(System.IO.Path.Combine(documents, "xenia-canary.config.toml")) || System.IO.Directory.Exists(System.IO.Path.Combine(documents, "patches"));
            return System.IO.Path.Combine(besideInUse || !documentsInUse ? beside : documents, "patches");
        }

        /// <summary>
        /// Copies the loaded patch file, with the chosen patches switched on, into Xenia's patches folder under its
        /// own file name (Xenia finds patch files by the Title ID at the start of the name). Returns the new path.
        /// </summary>
        public static string Install(XeniaPatchFile file, string xeniaExe)
        {
            if (string.IsNullOrEmpty(file.SourcePath) || !System.IO.File.Exists(file.SourcePath)) throw new InvalidOperationException("Load a patch file first.");
            string name = System.IO.Path.GetFileName(file.SourcePath);
            if (!name.EndsWith(".patch.toml", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Xenia only reads files named like \"415608C3 - Game.patch.toml\".");
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("That file name cannot be used.");
            string folder = PatchesFolder(xeniaExe);
            System.IO.Directory.CreateDirectory(folder);
            string target = System.IO.Path.Combine(folder, name);
            string text = SetEnabled(System.IO.File.ReadAllText(file.SourcePath), file.Patches.Select(p => p.IsEnabled).ToList());
            if (System.IO.File.Exists(target) && !string.Equals(System.IO.Path.GetFullPath(target), System.IO.Path.GetFullPath(file.SourcePath), StringComparison.OrdinalIgnoreCase))
            {
                string backup = target + ".bak";
                if (!System.IO.File.Exists(backup)) System.IO.File.Copy(target, backup);
            }
            System.IO.File.WriteAllText(target, text, new System.Text.UTF8Encoding(false));
            return target;
        }
    }

    /// <summary>Which memory the memory pages work on - the console over XBDM, or the game running in Xenia - and one way to run work against it.</summary>
    public static class MemoryTargets
    {
        private static readonly object Gate = new object();
        private static XeniaProcess _xenia;
        private static bool _useXenia;

        public static event Action Changed;

        /// <summary>True = Xenia on this PC, false = the console connected over XBDM.</summary>
        public static bool UseXenia
        {
            get { return _useXenia; }
            set { if (_useXenia == value) return; _useXenia = value; if (!value) DropXenia(); Changed?.Invoke(); }
        }

        public static string Name => _useXenia ? "Xenia" : "the console";

        public static bool IsReady
        {
            get
            {
                if (!_useXenia) return ConsoleService.IsConnected;
                lock (Gate) return (_xenia != null && _xenia.IsRunning) || XeniaProcess.Running().Count > 0;
            }
        }

        public static string NotReadyMessage => _useXenia ? "Start a game in Xenia first (Xenia page > Games)." : "Connect to a console first.";

        private static void DropXenia()
        {
            lock (Gate) { _xenia?.Dispose(); _xenia = null; }
        }

        private static XeniaProcess Xenia()
        {
            lock (Gate)
            {
                if (_xenia != null && !_xenia.IsRunning) { _xenia.Dispose(); _xenia = null; }
                return _xenia ?? (_xenia = XeniaProcess.Attach());
            }
        }

        public static Task<T> RunAsync<T>(Func<IMemoryTarget, T> work, CancellationToken ct = default(CancellationToken))
        {
            if (!_useXenia) return ConsoleService.RunAsync(c => work(c), ct);
            return Task.Run(() => { lock (Gate) return work(Xenia()); }, ct);
        }

        public static Task RunAsync(Action<IMemoryTarget> work, CancellationToken ct = default(CancellationToken))
            => RunAsync<bool>(t => { work(t); return true; }, ct);
    }
}
