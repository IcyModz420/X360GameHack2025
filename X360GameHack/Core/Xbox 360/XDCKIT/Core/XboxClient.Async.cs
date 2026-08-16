// =============================================================================
// XDCKIT.XboxClient.Async.cs - Lightweight async wrappers
// =============================================================================
// XDCKIT's transport is fundamentally synchronous (built on TcpClient +
// NetworkStream), so these helpers route through Task.Run.  They give
// callers an awaitable surface and CancellationToken support without
// requiring a major refactor of the wire framing.
//
// True async I/O (using ReadAsync / WriteAsync end-to-end) is a future
// improvement; the current shape is sufficient for almost every UI tool
// since the actual blocking operations (banner read, getmemex page) are
// short or already overlap-friendly via SetConnectionTimeout.
// =============================================================================
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class XboxClient
{
    /// <summary>
    /// Connect on a thread-pool thread.  The <see cref="CancellationToken"/>
    /// is honoured between the connect attempt and the banner read; the
    /// raw <see cref="System.Net.Sockets.TcpClient.Connect(string,int)"/>
    /// call itself doesn't accept tokens.
    /// </summary>
    public Task ConnectAsync(string ip, int port = DefaultPort, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ip)) throw new ArgumentNullException(nameof(ip));
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Connect(ip, port);
            ct.ThrowIfCancellationRequested();
        }, ct);
    }

    /// <summary>
    /// Send a text command on a thread-pool thread.  The cancellation token
    /// is checked before and after the underlying I/O.  In-flight reads
    /// will not be torn down — see <see cref="SetConnectionTimeout"/> if
    /// you need short, predictable timeouts.
    /// </summary>
    public Task<XbdmResponse> SendTextCommandAsync(string command, CancellationToken ct = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var resp = SendTextCommand(command);
            ct.ThrowIfCancellationRequested();
            return resp;
        }, ct);
    }

    /// <summary>Ping the console asynchronously.</summary>
    public Task<bool> PingAsync(int waitTimeMs = 250, CancellationToken ct = default)
        => Task.Run(() => Ping(waitTimeMs), ct);
}

public partial class XboxConsole
{
    /// <summary>Async wrapper around <see cref="Connect(string)"/>.</summary>
    public Task<bool> ConnectAsync(string ipOrName, CancellationToken ct = default)
        => Task.Run(() => Connect(ipOrName), ct);

    /// <summary>Async wrapper around <see cref="Connect(string,int)"/>.</summary>
    public Task<bool> ConnectAsync(string ipOrName, int port, CancellationToken ct = default)
        => Task.Run(() => Connect(ipOrName, port), ct);

    /// <summary>
    /// Async wrapper around <see cref="GetMemory(uint, uint)"/>.  Reads
    /// run on a thread-pool thread so the caller's UI thread isn't
    /// blocked during large transfers.
    /// </summary>
    public Task<byte[]> GetMemoryAsync(uint address, uint length, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return GetMemory(address, length);
        }, ct);

    /// <summary>Async wrapper around <see cref="SetMemory(uint, byte[])"/>.</summary>
    public Task SetMemoryAsync(uint address, byte[] data, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            SetMemory(address, data);
        }, ct);
}
