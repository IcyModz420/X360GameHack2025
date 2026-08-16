// =============================================================================
// XDCKIT.XboxClient.SharedConn.cs - shared / secure connection helpers
// =============================================================================
// Implements the DmMakeSharedConnection / DmUseSharedConnection refcount and
// the DmOpenSecureConnection NONCE+KEYXCHG handshake.  Pure additions to
// XboxClient; the underlying socket and reference counter live in the main
// partial.
// =============================================================================
using System;

public sealed partial class XboxClient
{
    /// <summary>
    /// DmMakeSharedConnection — flag this client as shareable.  In
    /// genuine xbdm.dll a shared connection lets multiple PC tools
    /// reuse the same socket; we model that by exposing a ref count
    /// and letting <see cref="UseSharedConnection"/> increment it.
    /// </summary>
    public XboxClient MakeSharedConnection()
    {
        _sharedRefCount = Math.Max(_sharedRefCount, 1);
        return this;
    }

    /// <summary>
    /// DmUseSharedConnection — bump the share count so the underlying
    /// socket isn't disposed until every consumer calls
    /// <see cref="Disconnect"/>.
    /// </summary>
    public XboxClient UseSharedConnection()
    {
        _sharedRefCount = Math.Max(_sharedRefCount, 1) + 1;
        return this;
    }

    /// <summary>
    /// DmOpenSecureConnection — connect to <paramref name="ip"/>:<paramref name="port"/>
    /// and immediately run the xbdm <c>NONCE</c> + <c>KEYXCHG</c>
    /// handshake.  On consoles where security is off, the NONCE call
    /// returns 420 (Box is not locked) and we fall back to a normal
    /// connection so callers can keep using SendTextCommand.
    /// </summary>
    public void OpenSecureConnection(string ip, int port = DefaultPort)
    {
        Connect(ip, port);
        try
        {
            var nonceResp = SendTextCommand("nonce");
            if ((int)nonceResp.Status == 421 || (int)nonceResp.Status == 200)
                SendTextCommand("keyxchg");
        }
        catch { /* leave the socket open as a regular connection */ }
    }
}
