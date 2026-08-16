// =============================================================================
// XDCKIT.XboxClient.Screenshot.cs - DmScreenShot wire helper
// =============================================================================
// xbdm's screenshot command returns one contiguous binary blob (NOT the
// chunked 0x400 framing that getmemex uses).  This partial drives the full
// status-line -> metadata-line -> binary-read sequence under the I/O lock so
// no other text command interleaves with the binary read.
// =============================================================================
using System;

public sealed partial class XboxClient
{
    /// <summary>Maximum framebuffer size we will read from <c>screenshot</c> (abuse guard).</summary>
    public const int ScreenshotMaxBytes = 48 * 1024 * 1024;

    /// <summary>
    /// xbdm <c>screenshot</c> end-to-end: send command, read status + metadata line(s),
    /// then read one contiguous binary blob (Microsoft <c>DmReceiveBinary</c> — not
    /// the <c>getmemex</c> 0x400 chunk framing). Must run under the I/O lock so
    /// no other command interleaves before the binary read completes.
    /// </summary>
    /// <param name="command">Usually <c>screenshot</c> or <c>screenshot</c> plus xbdm args.</param>
    /// <param name="binaryReadTimeoutMs">Temporary receive timeout while reading pixels.</param>
    public (XboxResponseType status, string metadataLine, byte[] pixels) TakeScreenshotCommand(
        string command = "screenshot",
        int binaryReadTimeoutMs = 120_000)
    {
        if (!Connected) throw new InvalidOperationException("Not connected to console.");
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command required.", nameof(command));

        lock (_ioLock)
        {
            SendRawAscii(command.Trim() + "\r\n");
            string line1 = ReadLine();
            var status = ParseStatusCode(line1);
            string msg1 = StripStatusPrefix(line1);
            int code = (int)status;

            if (code != 203 && code != 200)
                throw new InvalidOperationException($"screenshot failed: {code} {msg1}");

            // Official xbdm: DmReceiveSocketLine reads ONE line of key=value metadata, then DmReceiveBinary.
            // Some builds merge metadata into the 203- line; others send a generic 203 then a second line.
            string meta = msg1;
            if (code == 203 && !ScreenshotMetadataLineHasKeys(meta))
                meta = ReadLine();

            uint len = ParseScreenshotByteLength(meta);
            if (len == 0)
            {
                if (code == 200)
                    return (status, meta, Array.Empty<byte>());
                throw new InvalidOperationException($"screenshot: could not determine size (metadata: {meta})");
            }
            if (len > ScreenshotMaxBytes)
                throw new InvalidOperationException($"screenshot: size {len} exceeds cap {ScreenshotMaxBytes}.");

            int oldRecv = ReceiveTimeout;
            int oldTcpRecv = _tcp != null ? _tcp.ReceiveTimeout : 0;
            try
            {
                ReceiveTimeout = binaryReadTimeoutMs;
                if (_tcp != null) _tcp.ReceiveTimeout = binaryReadTimeoutMs;
                byte[] pixels = ReadExact((int)len);
                return (status, meta, pixels);
            }
            finally
            {
                ReceiveTimeout = oldRecv;
                if (_tcp != null) _tcp.ReceiveTimeout = oldTcpRecv;
            }
        }
    }

    /// <summary>True if this line looks like the screenshot key=value metadata.</summary>
    private static bool ScreenshotMetadataLineHasKeys(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        // Must look like xbdm token=value pairs, not a bare human message.
        if (line.IndexOf("=", StringComparison.Ordinal) < 0) return false;
        return line.IndexOf("pitch", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("width", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("height", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("framebuffersize", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("format", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static uint ParseScreenshotByteLength(string meta)
    {
        if (string.IsNullOrEmpty(meta)) return 0;

        uint fbs = ParseKvUIntHex(meta, "framebuffersize");
        if (fbs != 0) return fbs;

        uint pitch = ParseKvUIntHex(meta, "pitch");
        if (pitch == 0)
        {
            uint phi = ParseKvUIntHex(meta, "pitchhi");
            uint plo = ParseKvUIntHex(meta, "pitchlo");
            if (phi != 0 || plo != 0)
            {
                ulong p64 = ((ulong)phi << 32) | plo;
                if (p64 <= uint.MaxValue) pitch = (uint)p64;
            }
        }

        uint height = ParseKvUIntHex(meta, "height");
        if (pitch > 0 && height > 0) return pitch * height;

        uint w = ParseKvUIntHex(meta, "width");
        uint h = ParseKvUIntHex(meta, "height");
        uint bpp = GuessBytesPerPixelFromFormat(ParseKvUIntHex(meta, "format"));
        if (w > 0 && h > 0 && bpp > 0) return w * h * bpp;

        return 0;
    }

    private static uint GuessBytesPerPixelFromFormat(uint format)
    {
        // D3DFORMAT-ish values commonly seen on Xenon (not exhaustive).
        switch (format)
        {
            case 22:  // D3DFMT_A8R8G8G8
            case 21:  // D3DFMT_X8R8G8B8
                return 4;
            case 50:  // D3DFMT_LIN_A8R8G8B8
            case 51:  // D3DFMT_LIN_X8R8G8B8
                return 4;
            case 12:  // D3DFMT_R5G6B5
            case 13:  // D3DFMT_A1R5G5B5
            case 24:  // D3DFMT_X1R5G5B5
                return 2;
            default:
                return 0;
        }
    }

    private static uint ParseKvUIntHex(string line, string key) => XboxExtensions.ParseKvUIntHex(line, key);
}
