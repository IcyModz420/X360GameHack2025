using System;
using Be.Windows.Forms;

namespace X360GameHack.UI
{
    /// <summary>
    /// Wraps a HexBox byte provider so bytes can only be overwritten - never inserted or deleted.
    /// Inserting or deleting would shift every following byte and silently corrupt an XEX or live memory.
    /// </summary>
    public sealed class OverwriteOnlyProvider : IByteProvider, IDisposable
    {
        public IByteProvider Inner { get; }

        public OverwriteOnlyProvider(IByteProvider inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            inner.Changed += (s, e) => Changed?.Invoke(this, e);
            inner.LengthChanged += (s, e) => LengthChanged?.Invoke(this, e);
        }

        public byte ReadByte(long index) => Inner.ReadByte(index);
        public void WriteByte(long index, byte value) => Inner.WriteByte(index, value);
        public void InsertBytes(long index, byte[] bs) { /* overwrite-only: ignored */ }
        public void DeleteBytes(long index, long length) { /* overwrite-only: ignored */ }
        public long Length => Inner.Length;
        public event EventHandler LengthChanged;
        public bool HasChanges() => Inner.HasChanges();
        public void ApplyChanges() => Inner.ApplyChanges();
        public event EventHandler Changed;
        public bool SupportsWriteByte() => Inner.SupportsWriteByte();
        public bool SupportsInsertBytes() => false;
        public bool SupportsDeleteBytes() => false;
        public void Dispose() => (Inner as IDisposable)?.Dispose();
    }
}
