// =============================================================================
// XDCKIT.XboxClient.cs - Per-console TCP/xbdm transport
// =============================================================================
// Each XboxConsole owns its own XboxClient. The client speaks the xbdm
// "300- ", "200- ", "202- multiline response follows" framing protocol that
// the debug monitor on every devkit/JTAG/RGH console uses on TCP/730.
//
// This file holds the core transport: connect/disconnect, SendTextCommand,
// raw line/byte I/O, and the Wait helpers. The class is split into partials:
//
//   XboxClient.cs            - transport, framing, raw I/O, Wait
//   XboxClient.SharedConn.cs - shared / secure connection helpers
//   XboxClient.Discovery.cs  - UDP name service + LAN-wide xbdm scan
//   XboxClient.Screenshot.cs - DmScreenShot wire helper
//   XboxClient.Async.cs      - async (TPL) shims over the sync surface
// =============================================================================
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>Result of a SendTextCommand.</summary>
public sealed class XbdmResponse
{
    public XboxResponseType Status;     // 200, 202, 203 ...
    public string StatusMessage;        // text after the "200- "
    public string Body;                 // multi-line body (without the "."), null if single-line
    public bool IsSuccess => (int)Status >= 200 && (int)Status < 300;

    public override string ToString()
        => Body == null ? $"{(int)Status}: {StatusMessage}" : $"{(int)Status}: {StatusMessage}\n{Body}";
}

/// <summary>
/// Low-level xbdm protocol client. Public so callers can drop down a layer
/// when they need raw socket access (similar to XDevkit's underlying
/// connection object), but most users will go through <see cref="XboxConsole"/>.
/// </summary>
[Browsable(false), EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed partial class XboxClient : IDisposable
{
    public const int DefaultPort = 730;
    private const int InitialBannerTimeoutMs = 1500;

    /// <summary>
    /// Hard cap on a single <see cref="ReadLine"/> result.  An xbdm peer that
    /// never sends CRLF will be killed at this size instead of OOM-ing the
    /// process.  Tunable for callers that legitimately need huge replies.
    /// </summary>
    public static int MaxLineLengthBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>Default buffer size used by file-transfer helpers (SendFile / GetFile).</summary>
    public static int FileTransferBufferSize { get; set; } = 64 * 1024;

    /// <summary>Default chunk size for <c>DumpMemory</c>.</summary>
    public static int MemoryDumpChunkSize { get; set; } = 0x10000;

    private readonly object _ioLock = new object();
    private TcpClient _tcp;
    private NetworkStream _stream;
    private bool _disposed;
    private int _sharedRefCount;

    /// <summary>Console host name / IPv4 address.  Renamed from IPAddress to avoid clashing with <see cref="System.Net.IPAddress"/>.</summary>
    public string HostName { get; private set; } = "0.0.0.0";

    /// <summary>Deprecated alias for <see cref="HostName"/>; kept for source compatibility.</summary>
    [Obsolete("Use HostName instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string IPAddress => HostName;

    public int Port { get; private set; } = DefaultPort;
    public bool Connected => !_disposed && _tcp != null && _tcp.Connected;
    public int SendTimeout { get; set; } = 5000;
    public int ReceiveTimeout { get; set; } = 10000;

    /// <summary>Fires immediately before a command line is sent (debug/trace hook).</summary>
    public event Action<string> CommandSent;

    /// <summary>Fires after a response (status + optional body) is fully read.</summary>
    public event Action<XbdmResponse> ResponseReceived;

    public TcpClient UnderlyingClient => _tcp;
    public NetworkStream UnderlyingStream => _stream;

    /// <summary>
    /// DmSetConnectionTimeout — set send/receive socket timeouts (milliseconds).
    /// <c>0</c> means infinite (same as <see cref="TcpClient.SendTimeout"/>).
    /// Updates the live <see cref="TcpClient"/> when already connected; assigning
    /// <see cref="SendTimeout"/> / <see cref="ReceiveTimeout"/> alone only affects
    /// the next <see cref="Connect"/>.
    /// </summary>
    public void SetConnectionTimeout(int sendMs, int receiveMs)
    {
        if (sendMs < 0) throw new ArgumentOutOfRangeException(nameof(sendMs), "Send timeout must be non-negative (0 = infinite).");
        if (receiveMs < 0) throw new ArgumentOutOfRangeException(nameof(receiveMs), "Receive timeout must be non-negative (0 = infinite).");
        SendTimeout = sendMs;
        ReceiveTimeout = receiveMs;
        if (_tcp == null) return;
        try
        {
            if (_tcp.Connected)
            {
                _tcp.SendTimeout = sendMs;
                _tcp.ReceiveTimeout = receiveMs;
            }
        }
        catch { /* ignore */ }
    }

    #region Connect / Disconnect

    public void Connect(string ip, int port = DefaultPort)
    {
        if (string.IsNullOrEmpty(ip)) throw new ArgumentNullException(nameof(ip));
        Disconnect();

        var tcp = new TcpClient();
        try
        {
            tcp.SendTimeout = SendTimeout;
            tcp.ReceiveTimeout = ReceiveTimeout;
            tcp.NoDelay = true;
            tcp.Connect(ip, port);
        }
        catch
        {
            tcp.Close();
            throw;
        }

        HostName = ip;
        Port = port;
        _tcp = tcp;
        _stream = tcp.GetStream();

        // Drain the "201- connected" banner so the next SendTextCommand sees the response cleanly.
        try { ReadInitialBanner(); }
        catch { /* ignore - some homebrew xbdm servers skip the banner */ }
    }

    private void ReadInitialBanner()
    {
        var sw = Stopwatch.StartNew();
        while (_tcp.Available == 0 && sw.ElapsedMilliseconds < InitialBannerTimeoutMs)
            Thread.Sleep(20);

        // Try to read a single line ("201- connected") if any.
        if (_tcp.Available > 0) ReadLine();
    }

    public void Disconnect()
    {
        // Take the I/O lock so we don't tear the socket out from under a
        // concurrent SendTextCommand / ReadExact.
        lock (_ioLock)
        {
            if (_disposed || _tcp == null) return;
            // Honor DmMakeSharedConnection / DmUseSharedConnection refcounts.
            if (_sharedRefCount > 1)
            {
                _sharedRefCount--;
                return;
            }
            _sharedRefCount = 0;
            try
            {
                if (_tcp.Connected)
                {
                    try { SendRawAscii("bye\r\n"); } catch { /* swallow */ }
                }
            }
            finally
            {
                try { _stream?.Dispose(); } catch { /* ignore */ }
                try { _tcp?.Close(); } catch { /* ignore */ }
                _stream = null;
                _tcp = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer: close the socket if the user forgot to call <see cref="Dispose"/>.
    /// Best-effort; we never throw from here.
    /// </summary>
    ~XboxClient()
    {
        try { Disconnect(); } catch { /* never throw from finalizer */ }
    }

    #endregion

    #region SendTextCommand (the heart of xbdm)

    /// <summary>
    /// Send a text command to xbdm and parse the reply.
    /// Handles 200 (single line), 202 (multi-line ending in "."), and 203
    /// (binary follows - body returned empty, caller does the binary read).
    /// </summary>
    public XbdmResponse SendTextCommand(string command)
    {
        if (!Connected) throw new InvalidOperationException("Not connected to console.");

        lock (_ioLock)
        {
            CommandSent?.Invoke(command);
            SendRawAscii(command + "\r\n");
            var resp = ReadResponse();
            ResponseReceived?.Invoke(resp);
            return resp;
        }
    }

    private XbdmResponse ReadResponse()
    {
        string firstLine = ReadLine();
        var resp = new XbdmResponse { Status = ParseStatusCode(firstLine), StatusMessage = StripStatusPrefix(firstLine) };

        switch ((int)resp.Status)
        {
            case 202: // multi-line, terminated by a single "."
                var sb = new StringBuilder();
                while (true)
                {
                    string line = ReadLine();
                    if (line == ".") break;
                    sb.AppendLine(line);
                }
                resp.Body = sb.ToString().TrimEnd('\r', '\n');
                break;

            case 203: // binary follows - caller reads from UnderlyingStream
                resp.Body = string.Empty;
                break;

            default:
                // single-line; body stays null
                break;
        }

        return resp;
    }

    private static XboxResponseType ParseStatusCode(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length < 3) return XboxResponseType.UndefinedError;
        if (!int.TryParse(line.Substring(0, 3), out var code)) return XboxResponseType.UndefinedError;
        return (XboxResponseType)code;
    }

    private static string StripStatusPrefix(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length < 5) return line ?? string.Empty;
        // Format: "200- text...." or "200 text..."
        int sep = line.IndexOf("- ", StringComparison.Ordinal);
        if (sep == 3) return line.Substring(5);
        if (line.Length > 4 && (line[3] == ' ' || line[3] == '-')) return line.Substring(4).TrimStart();
        return line.Substring(3).TrimStart();
    }

    #endregion

    #region Raw I/O (used by SendTextCommand and binary R/W ops)

    public void SendRawAscii(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
        _stream.Flush();
    }

    public void SendRaw(byte[] data, int offset, int count)
    {
        _stream.Write(data, offset, count);
        _stream.Flush();
    }

    public void SendRaw(byte[] data) => SendRaw(data, 0, data.Length);

    /// <summary>
    /// DmSendBinary — send a contiguous binary payload across the wire.
    /// Used after a custom command sequence that expects a binary follow-up
    /// (e.g. after a 203 prompt or as part of <c>WRITEFILE</c> / <c>setmem</c>
    /// streaming forms). Takes the I/O lock so the binary block isn't
    /// interleaved with another thread's <see cref="SendTextCommand"/>.
    /// </summary>
    public void SendBinary(byte[] data, int offset, int count)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (offset < 0 || count < 0 || offset + count > data.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (!Connected) throw new InvalidOperationException("Not connected to console.");
        lock (_ioLock) { SendRaw(data, offset, count); }
    }

    /// <summary>DmSendBinary convenience overload — send the full buffer.</summary>
    public void SendBinary(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        SendBinary(data, 0, data.Length);
    }

    /// <summary>
    /// Atomic send-then-receive used by bulk upload helpers.  Holds the I/O
    /// lock across BOTH the binary write and the following status line so a
    /// concurrent thread can't interleave a command between them.
    /// </summary>
    public XbdmResponse SendBinaryAndReceiveStatus(byte[] data, int offset, int count)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (offset < 0 || count < 0 || offset + count > data.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (!Connected) throw new InvalidOperationException("Not connected to console.");
        lock (_ioLock)
        {
            SendRaw(data, offset, count);
            var resp = ReadResponse();
            ResponseReceived?.Invoke(resp);
            return resp;
        }
    }

    /// <summary>Convenience overload that sends the entire buffer.</summary>
    public XbdmResponse SendBinaryAndReceiveStatus(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        return SendBinaryAndReceiveStatus(data, 0, data.Length);
    }

    /// <summary>
    /// DmReceiveSocketLine — read one CRLF-terminated text line from the
    /// xbdm socket.  Public surface around the internal line reader so
    /// callers driving a custom request/response can read replies one line
    /// at a time (e.g. when reading a 202 multi-line body manually).
    /// </summary>
    public string ReceiveSocketLine()
    {
        if (!Connected) throw new InvalidOperationException("Not connected to console.");
        lock (_ioLock) { return ReadLine(); }
    }

    /// <summary>
    /// DmReceiveStatusResponse — read the next NNN status line plus its
    /// body (single-line, 202 multi-line ending in ".", or 203 binary
    /// follows).  Equivalent to the body half of <see cref="SendTextCommand"/>;
    /// use this after a manual <see cref="SendRawAscii"/> or <see cref="SendBinary(byte[], int, int)"/>
    /// sequence to consume the reply.
    /// </summary>
    public XbdmResponse ReceiveStatusResponse()
    {
        if (!Connected) throw new InvalidOperationException("Not connected to console.");
        lock (_ioLock) { return ReadResponse(); }
    }

    /// <summary>Read exactly <paramref name="count"/> bytes from the wire.</summary>
    public void ReadExact(byte[] buffer, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = _stream.Read(buffer, offset + read, count - read);
            if (n == 0) throw new EndOfStreamException("Connection closed while reading.");
            read += n;
        }
    }

    public byte[] ReadExact(int count)
    {
        var buf = new byte[count];
        ReadExact(buf, 0, count);
        return buf;
    }

    /// <summary>
    /// Read up to and including the next CRLF, return the line without the CRLF.
    /// Capped at <see cref="MaxLineLengthBytes"/> to defend against
    /// peers that never send a terminator.
    /// </summary>
    public string ReadLine()
    {
        var sb = new StringBuilder(64);
        byte prev = 0;
        var sw = Stopwatch.StartNew();
        int cap = MaxLineLengthBytes;
        while (true)
        {
            int b = _stream.ReadByte();
            if (b < 0) throw new EndOfStreamException("Connection closed while reading line.");
            if (prev == '\r' && b == '\n') { sb.Length -= 1; return sb.ToString(); }
            sb.Append((char)b);
            prev = (byte)b;
            if (sb.Length > cap)
                throw new InvalidDataException($"xbdm reply exceeded MaxLineLengthBytes ({cap}).");
            if (sw.ElapsedMilliseconds > ReceiveTimeout) throw new TimeoutException("xbdm read timed out.");
        }
    }

    #endregion

    #region Wait helpers (kept compatible with old code)

    public void Wait(int targetLength, int timeoutMs = 5000)
    {
        if (_tcp == null) throw new InvalidOperationException("Not connected.");
        if (_tcp.Available >= targetLength) return;

        var sw = Stopwatch.StartNew();
        while (_tcp.Available < targetLength)
        {
            Thread.Sleep(1);
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException();
        }
    }

    public void Wait(WaitType type, int timeoutMs = 5000)
    {
        if (_tcp == null) throw new InvalidOperationException("Not connected.");
        var sw = Stopwatch.StartNew();
        switch (type)
        {
            case WaitType.None:
                return;

            case WaitType.Partial:
                while (_tcp.Available == 0)
                { Thread.Sleep(1); if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(); }
                return;

            case WaitType.Full:
                while (_tcp.Available == 0)
                { Thread.Sleep(1); if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(); }
                int avail = _tcp.Available;
                Thread.Sleep(1);
                while (_tcp.Available != avail)
                {
                    avail = _tcp.Available;
                    Thread.Sleep(1);
                    if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException();
                }
                return;

            case WaitType.Idle:
                int prev = _tcp.Available;
                Thread.Sleep(1);
                while (_tcp.Available != prev)
                { prev = _tcp.Available; Thread.Sleep(1); if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(); }
                return;
        }
    }

    public bool Ping(int waitTimeMs = 250)
    {
        if (_tcp == null) return false;
        lock (_ioLock)
        {
            int oldTimeout = _tcp.SendTimeout;
            try
            {
                while (_tcp.Available > 0) ReadByteOrZero();
                _tcp.SendTimeout = waitTimeMs;
                SendRawAscii("\r\n");

                // Eat the "400- Unknown Command\r\n" reply.
                try
                {
                    var sw = Stopwatch.StartNew();
                    while (_tcp.Available < 5 && sw.ElapsedMilliseconds < waitTimeMs * 4) Thread.Sleep(10);
                    if (_tcp.Available > 0) ReadLine();
                }
                catch { /* ignore */ }
                return true;
            }
            catch { return false; }
            finally { try { if (_tcp != null) _tcp.SendTimeout = oldTimeout; } catch { /* ignore */ } }
        }
    }

    private int ReadByteOrZero()
    {
        try { return _stream.ReadByte(); } catch { return 0; }
    }

    #endregion
}
