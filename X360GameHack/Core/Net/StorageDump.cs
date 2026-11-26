using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace X360GameHack.Core.Net
{
    /// <summary>
    /// A raw copy of one of the console's storage devices - the internal 4 GB memory of a Slim / E, a memory unit - into
    /// an image file on this PC, to open in Drive Explorer and recover deleted files from. READ ONLY: the console's own
    /// kernel opens the device for reading (NtOpenFile) and reads it a piece at a time (NtReadFile); nothing is written to
    /// any storage. Plain XBDM cannot read a device, so the kernel is called:
    ///  - on a development kit through its own XDRPC (no plugin);
    ///  - on an RGH / JTAG console through the JRPC2 plugin (never next to a plugin known to hang it) - proven on a Trinity
    ///    RGH3 (xbdm 2.0.21076.11) on 2026-11-24: \Device\BuiltInMuUsb\Storage, NtOpenFile with SIX arguments (handle,
    ///    access, attributes, status block, share, options - Xenia lists five; with five the console answered "not a
    ///    directory"), a physical-memory buffer (MmAllocatePhysicalMemory) that getmemex reads.
    /// </summary>
    public static class StorageDump
    {
        public enum Route { Xdrpc, Jrpc2 }

        public sealed class Device
        {
            public string Name, Path, Drive;
            public long KnownSize;
            public override string ToString() => Name + (KnownSize > 0 ? "  (" + Formats.FileSniffer.FormatSize(KnownSize) + ")" : "");
        }

        private const uint Access = 0x80100000;            // GENERIC_READ | SYNCHRONIZE
        private const uint Share = 3, Options = 0x20;      // FILE_SHARE_READ | FILE_SHARE_WRITE, FILE_SYNCHRONOUS_IO_NONALERT
        private const uint EndOfFile = 0xC0000011;
        private const int JrpcChunk = 4 << 20, XdrpcChunk = 256 << 10;

        /// <summary>The kernel names of storage the console's drive list shows (drive name -> device).</summary>
        private static readonly (string Drive, string Name, string Path)[] Known =
        {
            ("INTUSB", "Internal storage (Slim / E, 4 GB)", @"\Device\BuiltInMuUsb\Storage"),
            ("INTMMC", "Internal storage (Corona, 4 GB)", @"\Device\BuiltInMuMmc\Storage"),
            ("MUINT", "Internal storage (Corona, 4 GB)", @"\Device\BuiltInMuMmc\Storage"),
        };

        /// <summary>The storage devices this console can be asked for, from its drive list (with their file-system size).</summary>
        public static List<Device> Offer(XboxConsole c)
        {
            var list = new List<Device>();
            var r = c.SendTextCommand("drivelist");
            var drives = Regex.Matches(r.Body ?? "", "drivename=\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            foreach (var k in Known)
            {
                if (!drives.Any(d => d.Equals(k.Drive, StringComparison.OrdinalIgnoreCase)) || list.Any(x => x.Path == k.Path)) continue;
                long size = 0;
                var f = c.SendTextCommand("drivefreespace name=\"" + k.Drive + ":\\\"");
                var hi = Regex.Match(f.Body ?? "", "totalbyteshi=0x([0-9a-fA-F]+)"); var lo = Regex.Match(f.Body ?? "", "totalbyteslo=0x([0-9a-fA-F]+)");
                if (hi.Success && lo.Success) size = (long)Convert.ToUInt32(hi.Groups[1].Value, 16) << 32 | Convert.ToUInt32(lo.Groups[1].Value, 16);
                list.Add(new Device { Name = k.Name + " - " + k.Drive + ":", Path = k.Path, Drive = k.Drive, KnownSize = size });
            }
            return list;
        }

        /// <summary>How this console's kernel can be called; null (with why in <paramref name="why"/>) when it cannot.</summary>
        public static Route? RouteFor(XboxConsole c, out string why)
        {
            why = null;
            string type = (c.SendTextCommand("consoletype").StatusMessage ?? "").Trim().ToLowerInvariant();
            var mods = (c.GetModules() ?? new List<ModuleInfo>()).Select(m => m.Name ?? "").ToList();
            bool jrpc = mods.Any(n => n.Equals(Jrpc.PluginName, StringComparison.OrdinalIgnoreCase));
            string clash = Jrpc.Conflicting.FirstOrDefault(p => mods.Any(n => n.Equals(p, StringComparison.OrdinalIgnoreCase)));
            // a development kit's own RPC first; elsewhere the JRPC2 plugin, then XDRPC if a plugin answers it
            if (type == "devkit" && Xdrpc.IsAvailable(c)) return Route.Xdrpc;
            if (jrpc && clash == null) return Route.Jrpc2;
            if (Xdrpc.IsAvailable(c)) return Route.Xdrpc;
            why = jrpc ? clash + " is loaded next to JRPC2.xex, and together they hang the console - take it out of launch.ini and restart the console."
                       : "This console's xbdm cannot call its kernel: load the JRPC2 plugin (RGH / JTAG; Console Setup > launch.ini), or use a development kit.";
            return null;
        }

        /// <summary>
        /// Copies the device into <paramref name="image"/> (must not exist) - all of it, or the first
        /// <paramref name="maxBytes"/> bytes when given. Returns the number of bytes; the image's SHA-256 is written next to
        /// it (.sha256). Must run inside ConsoleService.RunAsync (one connection, nothing else in between).
        /// </summary>
        public static long Dump(XboxConsole c, Route route, string devicePath, string image, long maxBytes, IProgress<long> progress, CancellationToken ct, out string sha256)
        {
            if (File.Exists(image)) throw new IOException("Already there: " + image);
            if (!Regex.IsMatch(devicePath ?? "", @"^\\Device\\[A-Za-z0-9\\]{1,60}$")) throw new ArgumentException("Not a device name: " + devicePath);
            using (var sha = SHA256.Create())
            using (var o = new FileStream(image, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20))
            {
                long done = route == Route.Jrpc2
                    ? DumpJrpc(c, devicePath, o, sha, maxBytes, progress, ct)
                    : DumpXdrpc(c, devicePath, o, sha, maxBytes, progress, ct);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                sha256 = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                File.WriteAllText(image + ".sha256", sha256 + " *" + Path.GetFileName(image) + Environment.NewLine);
                return done;
            }
        }

        private static byte[] OpenBlock(uint at, string dev)
        {
            // at+0x000 name, +0x100 ANSI_STRING {len, max, ptr}, +0x110 OBJECT_ATTRIBUTES {root, name*, OBJ_CASE_INSENSITIVE}
            var b = new byte[0x150];
            Encoding.ASCII.GetBytes(dev).CopyTo(b, 0);
            Xdrpc.PutBE32(b, 0x100, (uint)(dev.Length << 16 | (dev.Length + 1)));
            Xdrpc.PutBE32(b, 0x104, at);
            Xdrpc.PutBE32(b, 0x110, 0); Xdrpc.PutBE32(b, 0x114, at + 0x100); Xdrpc.PutBE32(b, 0x118, 0x40);
            return b;
        }

        private static long DumpJrpc(XboxConsole c, string dev, Stream o, HashAlgorithm sha, long maxBytes, IProgress<long> progress, CancellationToken ct)
        {
            uint pAlloc = Jrpc.Resolve(c, "xboxkrnl.exe", 185), pFree = Jrpc.Resolve(c, "xboxkrnl.exe", 189);
            uint pOpen = Jrpc.Resolve(c, "xboxkrnl.exe", 223), pRead = Jrpc.Resolve(c, "xboxkrnl.exe", 240), pClose = Jrpc.Resolve(c, "xboxkrnl.exe", 207);
            if (new[] { pAlloc, pFree, pOpen, pRead, pClose }.Any(p => p < 0x80000000)) throw new InvalidOperationException("A kernel function did not resolve on the console.");
            uint a = Jrpc.Call(c, pAlloc, false, 0u, (uint)JrpcChunk + 0x1000, 0x04u);          // physical memory, read-write
            if (a == 0) throw new InvalidOperationException("The console gave no buffer.");
            uint handle = 0;
            try
            {
                c.SetMemory(a, OpenBlock(a, dev));
                uint st = Jrpc.Call(c, pOpen, false, a + 0x130, Access, a + 0x110, a + 0x120, Share, Options);
                handle = Xdrpc.BE32(c.GetMemory(a + 0x130, 4), 0);
                if (st != 0 || handle == 0) throw new IOException(dev + " did not open on the console (status 0x" + st.ToString("X8") + ").");
                long offset = 0;
                var piece = new byte[1 << 20];
                while (maxBytes <= 0 || offset < maxBytes)
                {
                    ct.ThrowIfCancellationRequested();
                    uint want = (uint)(maxBytes > 0 ? Math.Min(JrpcChunk, maxBytes - offset) : JrpcChunk);
                    var off = new byte[8];
                    Xdrpc.PutBE64(off, 0, (ulong)offset);
                    c.SetMemory(a + 0x140, off);
                    uint rs = Jrpc.Call(c, pRead, false, handle, 0u, 0u, 0u, a + 0x120, a + 0x1000, want, a + 0x140);
                    if (rs == EndOfFile) break;
                    if (rs != 0) throw new IOException("Reading stopped at offset 0x" + offset.ToString("X") + " (status 0x" + rs.ToString("X8") + ").");
                    uint got = Xdrpc.BE32(c.GetMemory(a + 0x124, 4), 0);
                    for (uint p = 0; p < got; p += (uint)piece.Length)
                    {
                        uint n = Math.Min((uint)piece.Length, got - p);
                        c.GetMemory(a + 0x1000 + p, n, piece, out uint back);
                        if (back != n) throw new IOException("The console buffer could not be read back.");
                        o.Write(piece, 0, (int)n);
                        sha.TransformBlock(piece, 0, (int)n, null, 0);
                    }
                    offset += got;
                    progress?.Report(offset);
                    if (got < want) break;
                }
                return offset;
            }
            finally
            {
                try { if (handle != 0) Jrpc.Call(c, pClose, false, handle); } catch { }
                try { Jrpc.Call(c, pFree, false, 0u, a); } catch { }
            }
        }

        private static long DumpXdrpc(XboxConsole c, string dev, Stream o, HashAlgorithm sha, long maxBytes, IProgress<long> progress, CancellationToken ct)
        {
            // the structures and the data travel inside each call's own buffer - nothing is placed in console memory
            var open = new Xdrpc.Request { Module = "xboxkrnl.exe", Ordinal = 223 };
            var hOut = open.Add(8); var name = open.Add(Encoding.ASCII.GetBytes(dev + "\0")); var ansi = open.Add(8); var oa = open.Add(12); var iosb = open.Add(8);
            open.Fill = r =>
            {
                Xdrpc.PutBE32(ansi.Data, 0, (uint)(dev.Length << 16 | (dev.Length + 1))); Xdrpc.PutBE32(ansi.Data, 4, name.Address);
                Xdrpc.PutBE32(oa.Data, 0, 0); Xdrpc.PutBE32(oa.Data, 4, ansi.Address); Xdrpc.PutBE32(oa.Data, 8, 0x40);
            };
            open.Args.AddRange(new object[] { hOut, Access, oa, iosb, Share, Options });
            uint st = Xdrpc.Call(c, open).Value32;
            uint handle = Xdrpc.BE32(hOut.After, 0);
            if (st != 0 || handle == 0) throw new IOException(dev + " did not open on the console (status 0x" + st.ToString("X8") + ").");
            try
            {
                long offset = 0;
                while (maxBytes <= 0 || offset < maxBytes)
                {
                    ct.ThrowIfCancellationRequested();
                    int want = (int)(maxBytes > 0 ? Math.Min(XdrpcChunk, maxBytes - offset) : XdrpcChunk);
                    var read = new Xdrpc.Request { Module = "xboxkrnl.exe", Ordinal = 240 };
                    var io = read.Add(8); var data = read.Add(want); var offs = read.Add(8);
                    Xdrpc.PutBE64(offs.Data, 0, (ulong)offset);
                    read.Args.AddRange(new object[] { handle, 0u, 0u, 0u, io, data, (uint)want, offs });
                    uint rs = Xdrpc.Call(c, read).Value32;
                    if (rs == EndOfFile) break;
                    if (rs != 0) throw new IOException("Reading stopped at offset 0x" + offset.ToString("X") + " (status 0x" + rs.ToString("X8") + ").");
                    int got = (int)Math.Min(Xdrpc.BE32(io.After, 4), (uint)want);
                    o.Write(data.After, 0, got);
                    sha.TransformBlock(data.After, 0, got, null, 0);
                    offset += got;
                    progress?.Report(offset);
                    if (got < want) break;
                }
                return offset;
            }
            finally { try { Xdrpc.Call(c, "xboxkrnl.exe", 207, handle); } catch { } }
        }
    }
}
