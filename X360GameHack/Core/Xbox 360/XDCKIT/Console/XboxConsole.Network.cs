// =============================================================================
// XDCKIT.XboxConsole.Network.cs - All network-related xbdm wrappers
// =============================================================================
// Consolidates everything XBDM exposes about the console's networking stack:
//
//   * XNet introspection
//       DmGetSockInfo            -> GETSOCKINFO
//       DmGetXnSecAssocInfo      -> GETXNSECASSOCINFO
//       DmGetXnKeyInfo           -> GETXNKEYINFO
//       DmGetXnQosLookupInfo     -> GETXNQOSLOOKUPINFO
//       (helper)                  -> xnaddr
//
//   * Network simulator (NETSIM ...)
//       DmNetSimSetLinkStatusHidden, DmNetSimInsert/Remove/Modify/GetQueue,
//       DmNetSimRemoveAllQueues, DmNetSimGetNumQueues,
//       DmNetSim*Ipv4Redirect
//
//   * Network emulation / capture (NETEMU* / NETCAP)
//       DmStartNetEmulation, DmStopNetEmulation, DmSetNetEmulationData,
//       DmNetCaptureStart, DmNetCaptureStop
//
// Result classes / enums for every reply live in Common\Types.cs and
// Common\Enums.cs to keep this file focused on commands.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;

    public partial class XboxConsole
    {
        #region GETSOCKINFO (DmGetSockInfo)

        /// <summary>
        /// DmGetSockInfo - wraps SDK <c>GETSOCKINFO</c>.  Returns one row per
        /// open socket on the title (handle/owner/type/proto/local+remote
        /// address/port/tcp state etc).
        /// </summary>
        public List<XboxSocketInfo> GetSockInfo()
        {
            var list = new List<XboxSocketInfo>();
            var resp = SendTextCommand("GETSOCKINFO");
            if ((int)resp.Status != 202) return list;
            foreach (var line in SplitMultilineBody(resp.Body))
            {
                list.Add(new XboxSocketInfo
                {
                    Handle        = ParseUIntKvHex(line, "handle"),
                    OwnerType     = ParseKvLine(line, "ownertype"),
                    AddrFamily    = ParseKvLine(line, "addrfamily"),
                    SockType      = ParseKvLine(line, "socktype"),
                    Protocol      = ParseKvLine(line, "protocol"),
                    LocalAddress  = ParseKvLine(line, "localaddr"),
                    LocalPort     = ParseUIntKvHex(line, "localport"),
                    RemoteAddress = ParseKvLine(line, "remoteaddr"),
                    RemotePort    = ParseUIntKvHex(line, "remoteport"),
                    TcpState      = ParseKvLine(line, "tcpstate"),
                    RawLine       = line
                });
            }
            return list;
        }

        #endregion

        #region GETXNSECASSOCINFO (DmGetXnSecAssocInfo)

        /// <summary>
        /// DmGetXnSecAssocInfo - wraps SDK <c>GETXNSECASSOCINFO</c>.  Returns
        /// the active XNet security associations (one row per peer).
        /// </summary>
        public List<XboxXnSecAssocInfo> GetXnSecAssocInfo()
        {
            var list = new List<XboxXnSecAssocInfo>();
            var resp = SendTextCommand("GETXNSECASSOCINFO");
            if ((int)resp.Status != 202) return list;
            foreach (var line in SplitMultilineBody(resp.Body))
            {
                list.Add(new XboxXnSecAssocInfo
                {
                    OwnerType = ParseKvLine(line, "ownertype"),
                    ServiceId = ParseKvLine(line, "serviceid"),
                    XnAddr    = ParseKvLine(line, "xnaddr"),
                    XnKid     = ParseKvLine(line, "xnkid"),
                    SecAddr   = ParseKvLine(line, "secaddr"),
                    NatAddr   = ParseKvLine(line, "nataddr"),
                    NatPort   = ParseUIntKvHex(line, "natport"),
                    RawLine   = line
                });
            }
            return list;
        }

        #endregion

        #region GETXNKEYINFO (DmGetXnKeyInfo)

        /// <summary>
        /// DmGetXnKeyInfo - wraps SDK <c>GETXNKEYINFO</c>.  Returns the
        /// XNet session-key registrations (xnkid/qosbps/qosdatasize).
        /// </summary>
        public List<XboxXnKeyInfo> GetXnKeyInfo()
        {
            var list = new List<XboxXnKeyInfo>();
            var resp = SendTextCommand("GETXNKEYINFO");
            if ((int)resp.Status != 202) return list;
            foreach (var line in SplitMultilineBody(resp.Body))
            {
                list.Add(new XboxXnKeyInfo
                {
                    XnKid       = ParseKvLine(line, "xnkid"),
                    QosBps      = ParseUIntKvHex(line, "qosbps"),
                    QosDataSize = ParseUIntKvHex(line, "qosdatasize"),
                    RawLine     = line
                });
            }
            return list;
        }

        #endregion

        #region GETXNQOSLOOKUPINFO (DmGetXnQosLookupInfo)

        /// <summary>
        /// DmGetXnQosLookupInfo - wraps SDK <c>GETXNQOSLOOKUPINFO</c>.
        /// Returns active XNet QoS lookup descriptors.
        /// </summary>
        public List<XboxXnQosLookupInfo> GetXnQosLookupInfo()
        {
            var list = new List<XboxXnQosLookupInfo>();
            var resp = SendTextCommand("GETXNQOSLOOKUPINFO");
            if ((int)resp.Status != 202) return list;
            foreach (var line in SplitMultilineBody(resp.Body))
            {
                list.Add(new XboxXnQosLookupInfo
                {
                    PReq          = ParseUIntKvHex(line, "preq"),
                    PTx           = ParseUIntKvHex(line, "ptx"),
                    PRx           = ParseUIntKvHex(line, "prx"),
                    DataRx        = ParseUIntKvHex(line, "datarx"),
                    Bps           = ParseUIntKvHex(line, "bps"),
                    TargetAddress = ParseKvLine(line, "targetaddr"),
                    TargetPort    = ParseUIntKvHex(line, "targetport"),
                    Retry         = ParseUIntKvHex(line, "retry"),
                    RawLine       = line
                });
            }
            return list;
        }

        #endregion

        #region xnaddr (helper used by GetXnSecAssocInfo internally)

        /// <summary>
        /// Wraps the SDK helper <c>xnaddr</c> (no Dm* export, but the SDK
        /// uses it to retrieve the local title XNADDR for the rest of the
        /// XNet helpers).  Returns the raw status text.
        /// </summary>
        public string GetXnAddr()
        {
            var resp = SendTextCommand("xnaddr");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        #endregion

        #region Network simulator (DmNetSim*)

        /// <summary>
        /// DmNetSimSetLinkStatusHidden - wraps SDK
        /// <c>NETSIM linkstate disconnect</c> / <c>NETSIM linkstate restore</c>.
        /// Pass <c>true</c> to hide the cable from the title (simulate
        /// network unplugged), <c>false</c> to restore.
        /// </summary>
        public void NetSimSetLinkStatusHidden(bool hidden)
            => SendTextCommand(hidden ? "NETSIM linkstate disconnect" : "NETSIM linkstate restore");

        /// <summary>
        /// DmNetSimInsertQueue - wraps SDK
        /// <c>NETSIM insertq [in|out] index=N cfg=...</c>.
        /// </summary>
        public void NetSimInsertQueue(NetSimDirection direction, uint index, string cfgString)
            => SendTextCommand(BuildNetSimQueueCmd("insertq", direction, index, cfgString));

        /// <summary>DmNetSimRemoveQueue - wraps SDK <c>NETSIM removeq [in|out] index=N</c>.</summary>
        public void NetSimRemoveQueue(NetSimDirection direction, uint index)
            => SendTextCommand(BuildNetSimQueueCmd("removeq", direction, index, null));

        /// <summary>
        /// DmNetSimRemoveAllQueues - remove every queue on the given direction.
        /// xbdm renumbers queues after each <c>removeq</c>, so we cannot
        /// iterate by index — instead repeatedly delete index 0 until the
        /// server reports zero remaining (or until a small safety cap fires
        /// to avoid an infinite loop if the device misreports its count).
        /// </summary>
        public void NetSimRemoveAllQueues(NetSimDirection direction)
        {
            const int safetyCap = 4096;
            for (int i = 0; i < safetyCap; i++)
            {
                int n = NetSimGetNumQueues(direction);
                if (n <= 0) return;
                NetSimRemoveQueue(direction, 0u);
            }
        }

        /// <summary>DmNetSimModifyQueueSettings - wraps SDK <c>NETSIM modifyq [in|out] index=N cfg=...</c>.</summary>
        public void NetSimModifyQueueSettings(NetSimDirection direction, uint index, string cfgString)
            => SendTextCommand(BuildNetSimQueueCmd("modifyq", direction, index, cfgString));

        /// <summary>
        /// DmNetSimGetQueueSettings - wraps SDK <c>NETSIM getq [in|out] index=N</c>.
        /// Returns null if the console doesn't have such a queue configured.
        /// </summary>
        public NetSimQueueSettings NetSimGetQueueSettings(NetSimDirection direction, uint index)
        {
            var resp = SendTextCommand(BuildNetSimQueueCmd("getq", direction, index, null));
            if (!resp.IsSuccess) return null;
            return new NetSimQueueSettings
            {
                Direction = direction,
                Index     = index,
                Config    = ParseKvLine(resp.StatusMessage, "cfg"),
                RawLine   = resp.StatusMessage
            };
        }

        /// <summary>DmNetSimGetQueueStats - wraps SDK <c>NETSIM getqstats [in|out] index=N</c>.</summary>
        public NetSimQueueStats NetSimGetQueueStats(NetSimDirection direction, uint index)
        {
            var resp = SendTextCommand(BuildNetSimQueueCmd("getqstats", direction, index, null));
            if (!resp.IsSuccess) return null;
            string s = resp.StatusMessage ?? string.Empty;
            return new NetSimQueueStats
            {
                TotalPackets   = ParseUIntKvHex(s, "totalpkts"),
                TotalBytes     = ParseUIntKvHex(s, "totalbytes"),
                DroppedPackets = ParseUIntKvHex(s, "droppkts"),
                DroppedBytes   = ParseUIntKvHex(s, "dropbytes"),
                RawLine        = s
            };
        }

        /// <summary>DmNetSimGetNumQueues - wraps SDK <c>NETSIM getnumqs [in|out]</c>.</summary>
        public int NetSimGetNumQueues(NetSimDirection direction)
        {
            string cmd = "NETSIM getnumqs " + DirToken(direction);
            var resp = SendTextCommand(cmd);
            if (!resp.IsSuccess) return 0;
            string s = resp.StatusMessage ?? string.Empty;
            uint n = ParseUIntKvHex(s, "num");
            if (n == 0 && uint.TryParse(s.Trim(), out uint plain)) return (int)plain;
            return (int)n;
        }

        /// <summary>
        /// DmNetSimInsertIpv4Redirect - wraps SDK <c>NETSIM insertipv4redir ...</c>.
        /// The 8-tuple matches the SDK signature (proto, srcIP, srcPort,
        /// destIP, destPort, newDestIP, newDestPort, flags).
        /// </summary>
        public void NetSimInsertIpv4Redirect(uint protocol,
                                             uint sourceIp, uint sourcePort,
                                             uint destIp, uint destPort,
                                             uint newDestIp, uint newDestPort,
                                             uint flags)
        {
            var sb = new StringBuilder("NETSIM insertipv4redir");
            sb.Append(" proto=0x").Append(protocol.ToString("X8"));
            sb.Append(" srcaddr=0x").Append(sourceIp.ToString("X8"));
            sb.Append(" srcport=0x").Append(sourcePort.ToString("X8"));
            sb.Append(" dstaddr=0x").Append(destIp.ToString("X8"));
            sb.Append(" dstport=0x").Append(destPort.ToString("X8"));
            sb.Append(" newdstaddr=0x").Append(newDestIp.ToString("X8"));
            sb.Append(" newdstport=0x").Append(newDestPort.ToString("X8"));
            sb.Append(" flags=0x").Append(flags.ToString("X8"));
            SendTextCommand(sb.ToString());
        }

        /// <summary>DmNetSimRemoveIpv4Redirect - wraps SDK <c>NETSIM removeipv4redir index=N</c>.</summary>
        public void NetSimRemoveIpv4Redirect(uint index)
            => SendTextCommand($"NETSIM removeipv4redir index={index}");

        /// <summary>DmNetSimGetNumIpv4Redirects - wraps SDK <c>NETSIM getnumipv4redirs</c>.</summary>
        public int NetSimGetNumIpv4Redirects()
        {
            var resp = SendTextCommand("NETSIM getnumipv4redirs");
            if (!resp.IsSuccess) return 0;
            string s = resp.StatusMessage ?? string.Empty;
            uint n = ParseUIntKvHex(s, "num");
            if (n == 0 && uint.TryParse(s.Trim(), out uint plain)) return (int)plain;
            return (int)n;
        }

        private static string DirToken(NetSimDirection d) => d == NetSimDirection.In ? "in" : "out";

        private static string BuildNetSimQueueCmd(string verb, NetSimDirection direction, uint index, string cfgString)
        {
            var sb = new StringBuilder("NETSIM ");
            sb.Append(verb).Append(' ').Append(DirToken(direction)).Append(" index=").Append(index);
            if (!string.IsNullOrEmpty(cfgString)) sb.Append(" cfg=").Append(cfgString);
            return sb.ToString();
        }

        #endregion

        #region NetEmu (DmStartNetEmulation / DmStopNetEmulation / DmSetNetEmulationData)

        /// <summary>DmStartNetEmulation - wraps SDK <c>NETEMUSTART PORT=%d</c>.</summary>
        public void NetEmuStart(int port) => SendTextCommand($"netemustart port={port}");

        /// <summary>DmStopNetEmulation - wraps SDK <c>NETEMUSTOP</c>.</summary>
        public void NetEmuStop() => SendTextCommand("netemustop");

        /// <summary>DmSetNetEmulationData - wraps SDK <c>NETEMUDATA PORT=%d MAXREBOOTS=%d</c>.</summary>
        public void NetEmuData(int port, int maxReboots)
            => SendTextCommand($"netemudata port={port} maxreboots={maxReboots}");

        #endregion

        #region NetCap (DmNetCaptureStart / DmNetCaptureStop)

        /// <summary>DmNetCaptureStop - wraps SDK <c>NETCAP stop</c>.</summary>
        public void NetCaptureStop() => SendTextCommand("netcap stop");

        /// <summary>
        /// DmNetCaptureStart - wraps SDK
        /// <c>NETCAP start maxframedata=%u flags=0x%08x buffersize=%u file="%hs"</c>.
        /// The remote file path is where the .pcap will be written on the
        /// console's filesystem.
        /// </summary>
        public void NetCaptureStart(uint maxFrameData, uint flags, uint bufferSize, string remotePcapPath)
            => SendTextCommand(
                "netcap start maxframedata=" + maxFrameData +
                " flags=0x" + flags.ToString("X8") +
                " buffersize=" + bufferSize +
                " file=" + XboxExtensions.QuoteXbdm(remotePcapPath));

        #endregion
    }
