using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Services;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace X360GameHack.UI
{
    /// <summary>
    /// What the drop target tells the data it is dropping (shell's IDataObjectAsyncCapability, also called IAsyncOperation): with
    /// it, Explorer copies on a thread of its own after the drop, so the program the files came from keeps working meanwhile.
    /// </summary>
    [ComImport, Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDataObjectAsyncCapability
    {
        void SetAsyncMode(int fDoOpAsync);
        void GetAsyncMode(out int pfIsOpAsync);
        void StartOperation(IntPtr pbcReserved);
        void InOperation(out int pfInAsyncOp);
        void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects);
    }

    /// <summary>
    /// Files and folders dragged off an Xbox drive into Windows Explorer, the desktop or any program that takes dropped files.
    /// Windows is handed the list of what is coming (FileGroupDescriptorW: names, folders, sizes, times) and reads each file
    /// from the drive only when the drop lands (FileContents - one stream per file, read cluster by cluster): nothing is copied
    /// anywhere first, and Explorer shows its own progress. The copy runs on Windows' side in the background
    /// (<see cref="IDataObjectAsyncCapability"/>), so this window keeps working; <see cref="Started"/> / <see cref="Ended"/> say
    /// when it is under way, so the drive is not closed under it. Every name is made safe for Windows (PathSafety.SafeName) -
    /// a crafted drive cannot point the drop outside the folder it lands in. Only reads the drive.
    /// </summary>
    [ComVisible(true)]
    public sealed class DragOutData : ComTypes.IDataObject, IDataObjectAsyncCapability
    {
        public sealed class Item
        {
            public string Path;             // relative, made safe: "Folder\Sub\file.bin"
            public bool Folder;
            public long Size;
            public DateTime Modified;
            public FatxEntry Entry;
        }

        public const int MaxItems = 50000, MaxPath = 259;
        private const int S_OK = 0, S_FALSE = 1;
        private const int DV_E_FORMATETC = unchecked((int)0x80040064), DV_E_LINDEX = unchecked((int)0x80040068), DV_E_TYMED = unchecked((int)0x80040069),
                          DV_E_DVASPECT = unchecked((int)0x8004006B), OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003),
                          STG_E_READFAULT = unchecked((int)0x8003001E), DATA_S_SAMEFORMATETC = 0x00040130;
        private static readonly short CfDescriptor = unchecked((short)DataFormats.GetFormat("FileGroupDescriptorW").Id),
                                      CfContents = unchecked((short)DataFormats.GetFormat("FileContents").Id),
                                      CfPreferredEffect = unchecked((short)DataFormats.GetFormat("Preferred DropEffect").Id);

        private readonly FatxVolume _vol;
        private readonly List<Item> _items;
        private readonly Dictionary<short, byte[]> _set = new Dictionary<short, byte[]>();
        private readonly SynchronizationContext _ui;
        private bool _async = true;
        private int _inOperation;

        public IReadOnlyList<Item> Items => _items;
        public int FileCount => _items.Count(i => !i.Folder);
        public long Bytes => _items.Where(i => !i.Folder).Sum(i => i.Size);

        /// <summary>Windows started copying in the background (on this window's thread).</summary>
        public event Action Started;
        /// <summary>Windows finished (true) or stopped / failed (false), on this window's thread.</summary>
        public event Action<bool> Ended;

        private DragOutData(FatxVolume vol, List<Item> items)
        {
            _vol = vol; _items = items;
            _ui = SynchronizationContext.Current;
        }

        /// <summary>
        /// The selected files and folders (folders with everything in them), each name made safe for Windows and unique in its
        /// folder. Throws InvalidOperationException (with what to do instead) when there are more than <see cref="MaxItems"/> or a
        /// path is longer than Windows takes in a drop. Reads the folders.
        /// </summary>
        public static DragOutData Build(FatxVolume vol, IEnumerable<FatxEntry> selected)
        {
            var items = new List<Item>();
            var seen = new HashSet<uint>();
            void Add(FatxEntry e, string parent, HashSet<string> used, int depth)
            {
                if (items.Count >= MaxItems)
                    throw new InvalidOperationException("That is more than " + MaxItems.ToString("N0") + " files and folders - too many to drag. Use Extract selected... instead.");
                string path = (parent.Length > 0 ? parent + "\\" : "") + Unique(PathSafety.SafeName(e.Name), used);
                if (path.Length > MaxPath)
                    throw new InvalidOperationException("\"" + path + "\" is nested too deeply for Windows to take in a drop. Use Extract selected... instead.");
                items.Add(new Item { Path = path, Folder = e.IsDirectory, Size = e.IsDirectory ? 0 : e.Size, Modified = e.Modified, Entry = e });
                if (!e.IsDirectory) return;
                if (depth > 64 || !seen.Add(e.FirstCluster)) throw new IOException("Folder loop at " + e.Name + " (damaged file system).");
                var inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in vol.ListDirectory(e.FirstCluster)) Add(c, path, inside, depth + 1);
            }
            var top = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in selected) Add(e, "", top, 0);
            return new DragOutData(vol, items);
        }

        /// <summary>"name", or "name (2).ext" and on when the folder has it already (two names can become one once made safe).</summary>
        private static string Unique(string name, HashSet<string> used)
        {
            if (used.Add(name)) return name;
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 2; ; i++)
            {
                string n = stem + " (" + i + ")" + ext;
                if (used.Add(n)) return n;
            }
        }

        /// <summary>FILEGROUPDESCRIPTORW: the count, then a 592-byte FILEDESCRIPTORW per item (folders before what is in them).</summary>
        internal byte[] Descriptor()
        {
            const int one = 592;
            var b = new byte[4 + _items.Count * one];
            BitConverter.GetBytes(_items.Count).CopyTo(b, 0);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                int o = 4 + i * one;
                bool time = it.Modified != DateTime.MinValue;
                uint flags = 0x04 /*FD_ATTRIBUTES*/ | 0x40 /*FD_FILESIZE*/ | 0x4000 /*FD_PROGRESSUI*/ | 0x80000000 /*FD_UNICODE*/ | (time ? 0x20u /*FD_WRITESTIME*/ : 0);
                BitConverter.GetBytes(flags).CopyTo(b, o);
                // clsid (16), sizel (8), pointl (8) stay zero
                BitConverter.GetBytes(it.Folder ? 0x10 /*FILE_ATTRIBUTE_DIRECTORY*/ : 0x80 /*FILE_ATTRIBUTE_NORMAL*/).CopyTo(b, o + 36);
                if (time)
                {
                    long ft;
                    try { ft = it.Modified.ToFileTime(); } catch (ArgumentOutOfRangeException) { ft = 0; }
                    BitConverter.GetBytes(ft).CopyTo(b, o + 56);                  // ftLastWriteTime
                }
                BitConverter.GetBytes((uint)(it.Size >> 32)).CopyTo(b, o + 64);   // nFileSizeHigh
                BitConverter.GetBytes((uint)it.Size).CopyTo(b, o + 68);          // nFileSizeLow
                var name = Encoding.Unicode.GetBytes(it.Path);
                Buffer.BlockCopy(name, 0, b, o + 72, Math.Min(name.Length, 259 * 2));
            }
            return b;
        }

        // ------------------------------------------------------------------ IDataObject

        public void GetData(ref ComTypes.FORMATETC format, out ComTypes.STGMEDIUM medium)
        {
            medium = new ComTypes.STGMEDIUM();
            int hr = QueryGetData(ref format);
            if (hr != S_OK) throw new COMException("That format is not offered.", hr);
            if (format.cfFormat == CfDescriptor) { medium.tymed = ComTypes.TYMED.TYMED_HGLOBAL; medium.unionmember = Global(Descriptor()); return; }
            if (format.cfFormat == CfPreferredEffect) { medium.tymed = ComTypes.TYMED.TYMED_HGLOBAL; medium.unionmember = Global(BitConverter.GetBytes((int)DragDropEffects.Copy)); return; }
            if (format.cfFormat == CfContents)
            {
                var it = _items[format.lindex];
                if ((format.tymed & ComTypes.TYMED.TYMED_ISTREAM) != 0)
                {
                    var s = new ReadStream(_vol.OpenRead(it.Entry), Path.GetFileName(it.Path), it.Modified);
                    medium.tymed = ComTypes.TYMED.TYMED_ISTREAM;
                    medium.unionmember = Marshal.GetComInterfaceForObject(s, typeof(ComTypes.IStream));
                    return;
                }
                // memory only for small files (a target that asks for no stream)
                if (it.Size > 64L << 20) throw new COMException("Too big to hand over in memory.", DV_E_TYMED);
                var all = new byte[it.Size];
                using (var r = _vol.OpenRead(it.Entry))
                {
                    int n = 0;
                    while (n < all.Length) { int got = r.Read(all, n, all.Length - n); if (got <= 0) break; n += got; }
                    if (n != all.Length) throw new COMException(it.Path + " could not be read whole.", STG_E_READFAULT);
                }
                medium.tymed = ComTypes.TYMED.TYMED_HGLOBAL;
                medium.unionmember = Global(all);
                return;
            }
            lock (_set)
            {
                medium.tymed = ComTypes.TYMED.TYMED_HGLOBAL;
                medium.unionmember = Global(_set[format.cfFormat]);
            }
        }

        public int QueryGetData(ref ComTypes.FORMATETC format)
        {
            if (format.dwAspect != ComTypes.DVASPECT.DVASPECT_CONTENT) return DV_E_DVASPECT;
            bool hglobal = (format.tymed & ComTypes.TYMED.TYMED_HGLOBAL) != 0;
            if (format.cfFormat == CfDescriptor || format.cfFormat == CfPreferredEffect) return hglobal ? S_OK : DV_E_TYMED;
            if (format.cfFormat == CfContents)
            {
                if (format.lindex < 0 || format.lindex >= _items.Count || _items[format.lindex].Folder) return DV_E_LINDEX;
                return hglobal || (format.tymed & ComTypes.TYMED.TYMED_ISTREAM) != 0 ? S_OK : DV_E_TYMED;
            }
            lock (_set) if (_set.ContainsKey(format.cfFormat)) return hglobal ? S_OK : DV_E_TYMED;
            return DV_E_FORMATETC;
        }

        public ComTypes.IEnumFORMATETC EnumFormatEtc(ComTypes.DATADIR direction)
        {
            if (direction != ComTypes.DATADIR.DATADIR_GET) throw new NotImplementedException();
            ComTypes.FORMATETC F(short cf, ComTypes.TYMED t) => new ComTypes.FORMATETC { cfFormat = cf, dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = t };
            var list = new List<ComTypes.FORMATETC>
            {
                F(CfDescriptor, ComTypes.TYMED.TYMED_HGLOBAL),
                F(CfContents, ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL),
                F(CfPreferredEffect, ComTypes.TYMED.TYMED_HGLOBAL)
            };
            lock (_set) list.AddRange(_set.Keys.Select(k => F(k, ComTypes.TYMED.TYMED_HGLOBAL)));
            return new FormatList(list.ToArray(), 0);
        }

        /// <summary>What the drop target hands back (the effect it performed, "Paste Succeeded", drag-image state) - kept as bytes.</summary>
        public void SetData(ref ComTypes.FORMATETC formatIn, ref ComTypes.STGMEDIUM medium, bool release)
        {
            if (medium.tymed != ComTypes.TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero) throw new COMException("Only memory is kept.", DV_E_TYMED);
            IntPtr p = GlobalLock(medium.unionmember);
            try
            {
                long size = (long)GlobalSize(medium.unionmember).ToUInt64();
                if (p == IntPtr.Zero || size > 1 << 20) throw new COMException("Not kept.", DV_E_TYMED);
                var bytes = new byte[size];
                Marshal.Copy(p, bytes, 0, bytes.Length);
                lock (_set) _set[formatIn.cfFormat] = bytes;
            }
            finally { if (p != IntPtr.Zero) GlobalUnlock(medium.unionmember); }
            if (release) ReleaseStgMedium(ref medium);
        }

        public void GetDataHere(ref ComTypes.FORMATETC format, ref ComTypes.STGMEDIUM medium) => throw new NotImplementedException();
        public int GetCanonicalFormatEtc(ref ComTypes.FORMATETC formatIn, out ComTypes.FORMATETC formatOut) { formatOut = formatIn; formatOut.ptd = IntPtr.Zero; return DATA_S_SAMEFORMATETC; }
        public int DAdvise(ref ComTypes.FORMATETC pFormatetc, ComTypes.ADVF advf, ComTypes.IAdviseSink adviseSink, out int connection) { connection = 0; return OLE_E_ADVISENOTSUPPORTED; }
        public void DUnadvise(int connection) => throw new COMException("No advise sinks.", OLE_E_ADVISENOTSUPPORTED);
        public int EnumDAdvise(out ComTypes.IEnumSTATDATA enumAdvise) { enumAdvise = null; return OLE_E_ADVISENOTSUPPORTED; }

        // ------------------------------------------------------------------ IDataObjectAsyncCapability

        public void SetAsyncMode(int fDoOpAsync) => _async = fDoOpAsync != 0;
        public void GetAsyncMode(out int pfIsOpAsync) => pfIsOpAsync = _async ? 1 : 0;
        public void InOperation(out int pfInAsyncOp) => pfInAsyncOp = Volatile.Read(ref _inOperation) > 0 ? 1 : 0;

        public void StartOperation(IntPtr pbcReserved)
        {
            _startedAsync = true;
            if (Interlocked.Exchange(ref _inOperation, 1) == 0) OnUi(() => Started?.Invoke());
        }

        private volatile bool _startedAsync;
        /// <summary>The drop target took the copy to a thread of its own (it reports the end through <see cref="Ended"/>).</summary>
        public bool StartedAsync => _startedAsync;

        public void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects)
        {
            if (Interlocked.Exchange(ref _inOperation, 0) == 1) OnUi(() => Ended?.Invoke(hResult >= 0 && dwEffects != 0));
        }

        public bool Running => Volatile.Read(ref _inOperation) > 0;

        private void OnUi(Action a)
        {
            if (_ui == null) { a(); return; }
            _ui.Post(_ => { try { a(); } catch (Exception ex) { Log.Warn("Drag to Windows: " + ex.Message); } }, null);
        }

        // ------------------------------------------------------------------ helpers

        private static IntPtr Global(byte[] bytes)
        {
            IntPtr h = GlobalAlloc(0x0042 /*GHND: moveable, zeroed*/, (UIntPtr)Math.Max(1, bytes.Length));
            if (h == IntPtr.Zero) throw new OutOfMemoryException();
            IntPtr p = GlobalLock(h);
            try { Marshal.Copy(bytes, 0, p, bytes.Length); }
            finally { GlobalUnlock(h); }
            return h;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr h);
        [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);

        [ComVisible(true)]
        private sealed class FormatList : ComTypes.IEnumFORMATETC
        {
            private readonly ComTypes.FORMATETC[] _f;
            private int _i;
            public FormatList(ComTypes.FORMATETC[] f, int i) { _f = f; _i = i; }
            public int Next(int celt, ComTypes.FORMATETC[] rgelt, int[] pceltFetched)
            {
                int n = 0;
                while (n < celt && _i < _f.Length && rgelt != null && n < rgelt.Length) rgelt[n++] = _f[_i++];
                if (pceltFetched != null && pceltFetched.Length > 0) pceltFetched[0] = n;
                return n == celt ? S_OK : S_FALSE;
            }
            public int Skip(int celt) { _i = Math.Min(_f.Length, _i + Math.Max(0, celt)); return _i < _f.Length ? S_OK : S_FALSE; }
            public int Reset() { _i = 0; return S_OK; }
            public void Clone(out ComTypes.IEnumFORMATETC newEnum) => newEnum = new FormatList(_f, _i);
        }

        /// <summary>One file's bytes for the drop target, read from the drive as it asks (read-only, seekable).</summary>
        [ComVisible(true)]
        private sealed class ReadStream : ComTypes.IStream
        {
            private readonly Stream _s;
            private readonly string _name;
            private readonly DateTime _modified;
            public ReadStream(Stream s, string name, DateTime modified) { _s = s; _name = name; _modified = modified; }

            public void Read(byte[] pv, int cb, IntPtr pcbRead)
            {
                int n = 0;
                try { lock (_s) while (n < cb) { int r = _s.Read(pv, n, cb - n); if (r <= 0) break; n += r; } }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is UnauthorizedAccessException)
                {
                    throw new COMException(_name + ": " + ex.Message, STG_E_READFAULT);
                }
                if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n);
            }

            public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
            {
                long p;
                lock (_s)
                {
                    p = dwOrigin == 0 ? dlibMove : dwOrigin == 1 ? _s.Position + dlibMove : dwOrigin == 2 ? _s.Length + dlibMove : -1;
                    if (p < 0) throw new COMException("Bad seek.", unchecked((int)0x80030019) /*STG_E_INVALIDFUNCTION*/);
                    _s.Position = p;
                }
                if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, p);
            }

            public void Stat(out ComTypes.STATSTG pstatstg, int grfStatFlag)
            {
                long ft = 0;
                try { if (_modified != DateTime.MinValue) ft = _modified.ToFileTime(); } catch (ArgumentOutOfRangeException) { }
                var t = new ComTypes.FILETIME { dwLowDateTime = (int)ft, dwHighDateTime = (int)(ft >> 32) };
                pstatstg = new ComTypes.STATSTG
                {
                    pwcsName = (grfStatFlag & 1 /*STATFLAG_NONAME*/) != 0 ? null : _name,
                    type = 2 /*STGTY_STREAM*/, cbSize = _s.Length, mtime = t, ctime = t, atime = t, grfMode = 0 /*STGM_READ*/
                };
            }

            public void CopyTo(ComTypes.IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
            {
                var buf = new byte[1 << 20];
                long done = 0;
                while (done < cb)
                {
                    int want = (int)Math.Min(buf.Length, cb - done), n = 0;
                    lock (_s) while (n < want) { int r = _s.Read(buf, n, want - n); if (r <= 0) break; n += r; }
                    if (n == 0) break;
                    pstm.Write(buf, n, IntPtr.Zero);
                    done += n;
                    if (n < want) break;
                }
                if (pcbRead != IntPtr.Zero) Marshal.WriteInt64(pcbRead, done);
                if (pcbWritten != IntPtr.Zero) Marshal.WriteInt64(pcbWritten, done);
            }

            public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new COMException("Read-only.", unchecked((int)0x80030005) /*STG_E_ACCESSDENIED*/);
            public void SetSize(long libNewSize) => throw new COMException("Read-only.", unchecked((int)0x80030005));
            public void Commit(int grfCommitFlags) { }
            public void Revert() { }
            public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("No locks.", unchecked((int)0x80030019));
            public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("No locks.", unchecked((int)0x80030019));
            public void Clone(out ComTypes.IStream ppstm) => throw new NotImplementedException();
        }
    }
}
