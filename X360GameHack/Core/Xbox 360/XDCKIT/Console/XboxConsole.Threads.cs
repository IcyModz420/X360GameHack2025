// =============================================================================
// XDCKIT.XboxConsole.Threads.cs - Per-thread control and register R/W
// =============================================================================
// Wraps the xbdm thread-management exports.  Exact wire formats and key names
// are taken straight from the public xbdm.dll source (dmserv.c):
//
//   continue     thread=0xID [exception]
//   suspend      thread=0xID                -> XBDM_NOERR (no body)
//   resume       thread=0xID                -> XBDM_NOERR (no body)
//   isstopped    thread=0xID                -> "stopped" / "<reason>" / "" (running)
//   getcontext   thread=0xID [control] [int] [fp] [vr] [full]
//   setcontext   thread=0xID Reg=value ...
//   stopon       [all] [fce] [debugstr] [createthread] [stacktrace] [modload]
//   nostopon     same flags
//   getpid                                  -> "pid=0xXXXXXXXX"
//   isdebugger                              -> 200 if not attached, 410 + name/user line if attached
//   altaddr                                 -> "addr=0xXX"
//   threadinfo   thread=0xID                -> 202 multiline body
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

    public partial class XboxConsole
    {
        #region Thread halt / continue / suspend / resume / isstopped

        /// <summary>
        /// DmHaltThread — halt a single thread (debugger break on it).
        /// xbdm SDK wire format: <c>HALT THREAD=0x%0.8x</c>.
        /// </summary>
        public void HaltThread(uint threadId)
            => SendTextCommand($"HALT THREAD=0x{threadId:X8}");

        /// <summary>
        /// DmContinueThread — resume a halted thread.  xbdm SDK wire format:
        /// <c>CONTINUE THREAD=0x%0.8x [EXCEPTION]</c>.
        /// </summary>
        public void ContinueThread(uint threadId, bool exception = false)
            => SendTextCommand($"continue thread=0x{threadId:X}" + (exception ? " exception" : string.Empty));

        /// <summary>
        /// DmSuspendThread — bump the OS suspend count by 1.  xbdm answers with
        /// just <c>200- OK</c> (no body), so we return <c>true</c> on success and
        /// <c>false</c> on any error (no such thread, etc.).
        /// </summary>
        public bool SuspendThread(uint threadId)
        {
            var resp = SendTextCommand($"suspend thread=0x{threadId:X}");
            return resp.IsSuccess;
        }

        /// <summary>DmResumeThread — decrement the OS suspend count by 1.</summary>
        public bool ResumeThread(uint threadId)
        {
            var resp = SendTextCommand($"resume thread=0x{threadId:X}");
            return resp.IsSuccess;
        }

        /// <summary>
        /// DmIsThreadStopped — returns <c>true</c> iff the thread is halted at a
        /// breakpoint / debugstr / assert / etc.  xbdm replies with one of:
        /// <list type="bullet">
        /// <item><description>empty body — thread is running</description></item>
        /// <item><description>"stopped" — generic stop</description></item>
        /// <item><description>"assert thread=0xID straddr=0xX strlen=0xX"</description></item>
        /// <item><description>"debugstr thread=0xID stop"</description></item>
        /// <item><description>"&lt;notify-reason&gt;" — execution break, single step, ...</description></item>
        /// </list>
        /// (See dmserv.c HrIsThreadStopped.)
        /// </summary>
        public bool IsThreadStopped(uint threadId)
        {
            var resp = SendTextCommand($"isstopped thread=0x{threadId:X}");
            if (!resp.IsSuccess) return false;
            string body = resp.StatusMessage ?? string.Empty;
            if (string.IsNullOrWhiteSpace(body)) return false; // empty == running
            // "running" or "executing" never appear; anything non-empty is a stop reason.
            return true;
        }

        /// <summary>
        /// Read the stop reason for <paramref name="threadId"/> as raw text
        /// (e.g. <c>"stopped"</c>, <c>"singlestep address=0xX"</c>,
        /// <c>"assert thread=0xX straddr=0xX strlen=0xX"</c>, ...).  Returns
        /// <c>null</c> when the thread is running or no longer exists.
        /// </summary>
        public string GetThreadStopReason(uint threadId)
        {
            var resp = SendTextCommand($"isstopped thread=0x{threadId:X}");
            if (!resp.IsSuccess) return null;
            string s = resp.StatusMessage?.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        #endregion

        #region Thread context (registers)

        /// <summary>
        /// DmGetThreadContext - read the PowerPC register file for a thread.
        /// Pass <see cref="XboxContextFlags.Full"/> to get the integer + float
        /// + control banks; add <see cref="XboxContextFlags.Vector"/> for VMX.
        /// </summary>
        public XBOX_CONTEXT_EX GetThreadContext(uint threadId,
                                                XboxContextFlags flags = XboxContextFlags.Full)
        {
            var ctx = new XBOX_CONTEXT_EX { ContextFlags = flags };

            var sb = new StringBuilder("getcontext thread=0x").Append(threadId.ToString("X"));
            // dmserv.c HrGetContext recognises: control, int, fp, vr, full
            if ((flags & XboxContextFlags.Control) != 0)  sb.Append(" control");
            if ((flags & XboxContextFlags.Integer) != 0)  sb.Append(" int");
            if ((flags & XboxContextFlags.Floating) != 0) sb.Append(" fp");
            if ((flags & XboxContextFlags.Vector) != 0)   sb.Append(" vr");

            var resp = SendTextCommand(sb.ToString());
            if (!resp.IsSuccess) return ctx;

            string body = (int)resp.Status == 202 ? resp.Body : resp.StatusMessage;
            if (string.IsNullOrEmpty(body)) return ctx;

            // The body is a sequence of "Reg=value" tokens.  Parse them all generically.
            string flat = body.Replace("\r", " ").Replace("\n", " ");

            ctx.Msr = ParseUIntKvHex(flat, "Msr");
            ctx.Iar = ParseUIntKvHex(flat, "Iar");
            ctx.Lr = ParseUIntKvHex(flat, "Lr");
            ctx.Ctr = ParseUIntKvHex(flat, "Ctr");
            ctx.Cr = ParseUIntKvHex(flat, "Cr");
            ctx.Xer = ParseUIntKvHex(flat, "Xer");
            ctx.Fpscr = ParseUIntKvHex(flat, "Fpscr");

            for (int i = 0; i < 32; i++)
            {
                string raw = ParseKvLine(flat, "Gpr" + i);
                if (!string.IsNullOrEmpty(raw))
                    ctx.Gpr[i] = XboxExtensions.ParseHexULong(raw);

                string fpr = ParseKvLine(flat, "Fpr" + i);
                if (!string.IsNullOrEmpty(fpr))
                {
                    if (double.TryParse(fpr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        ctx.Fpr[i] = d;
                    else
                    {
                        ulong bits = XboxExtensions.ParseHexULong(fpr);
                        ctx.Fpr[i] = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(BitConverter.GetBytes(bits), 0));
                    }
                }
            }

            // Vector registers (V0..V127) only if requested.
            if ((flags & XboxContextFlags.Vector) != 0)
            {
                ctx.Vr = new byte[128][];
                for (int i = 0; i < 128; i++)
                {
                    string raw = ParseKvLine(flat, "Vr" + i);
                    ctx.Vr[i] = string.IsNullOrEmpty(raw) ? new byte[16] : XboxExtensions.HexToBytes(raw);
                    if (ctx.Vr[i].Length != 16) Array.Resize(ref ctx.Vr[i], 16);
                }
                ctx.Vscr = ParseUIntKvHex(flat, "Vscr");
            }

            return ctx;
        }

        /// <summary>
        /// DmSetThreadContext - write a (subset of a) PowerPC register file back
        /// to a thread.  Only the fields whose flags are set in
        /// <see cref="XBOX_CONTEXT_EX.ContextFlags"/> are pushed.
        /// </summary>
        public void SetThreadContext(uint threadId, XBOX_CONTEXT_EX ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var sb = new StringBuilder("setcontext thread=0x").Append(threadId.ToString("X"));

            if ((ctx.ContextFlags & XboxContextFlags.Control) != 0)
            {
                sb.Append(" Msr=0x").Append(ctx.Msr.ToString("X"))
                  .Append(" Iar=0x").Append(ctx.Iar.ToString("X"))
                  .Append(" Lr=0x").Append(ctx.Lr.ToString("X"))
                  .Append(" Ctr=0x").Append(ctx.Ctr.ToString("X"))
                  .Append(" Cr=0x").Append(ctx.Cr.ToString("X"))
                  .Append(" Xer=0x").Append(ctx.Xer.ToString("X"));
            }
            if ((ctx.ContextFlags & XboxContextFlags.Integer) != 0)
            {
                for (int i = 0; i < 32; i++)
                    sb.Append(" Gpr").Append(i).Append("=0x").Append(ctx.Gpr[i].ToString("X"));
            }
            if ((ctx.ContextFlags & XboxContextFlags.Floating) != 0)
            {
                for (int i = 0; i < 32; i++)
                {
                    long bits = BitConverter.DoubleToInt64Bits(ctx.Fpr[i]);
                    sb.Append(" Fpr").Append(i).Append("=0x").Append(bits.ToString("X"));
                }
                sb.Append(" Fpscr=0x").Append(ctx.Fpscr.ToString("X"));
            }
            if ((ctx.ContextFlags & XboxContextFlags.Vector) != 0 && ctx.Vr != null)
            {
                for (int i = 0; i < 128 && i < ctx.Vr.Length; i++)
                    sb.Append(" Vr").Append(i).Append("=0x").Append((ctx.Vr[i] ?? new byte[16]).ToHexString());
                sb.Append(" Vscr=0x").Append(ctx.Vscr.ToString("X"));
            }

            SendTextCommand(sb.ToString());
        }

        #endregion

        #region StopOn / NoStopOn (what to break on)

        /// <summary>DmStopOn - tell xbdm which events should halt execution for the debugger.</summary>
        public void StopOn(XboxStopOnFlags flags)
        {
            var sb = new StringBuilder("stopon");
            AppendStopOnTokens(sb, flags);
            SendTextCommand(sb.ToString());
        }

        /// <summary>Inverse of <see cref="StopOn"/>: clear specific stop-on flags.</summary>
        public void NoStopOn(XboxStopOnFlags flags)
        {
            var sb = new StringBuilder("nostopon");
            AppendStopOnTokens(sb, flags);
            SendTextCommand(sb.ToString());
        }

        private static void AppendStopOnTokens(StringBuilder sb, XboxStopOnFlags f)
        {
            // Real tokens accepted by xbdm dmserv.c HrDoStopOn.
            if ((f & XboxStopOnFlags.OnFirstChanceException) != 0) sb.Append(" fce");
            if ((f & XboxStopOnFlags.OnDebugString) != 0)          sb.Append(" debugstr");
            if ((f & XboxStopOnFlags.OnThreadCreate) != 0)         sb.Append(" createthread");
            if ((f & XboxStopOnFlags.OnStackTrace) != 0)           sb.Append(" stacktrace");
            if ((f & XboxStopOnFlags.OnModuleLoad) != 0)           sb.Append(" modload");
        }

        /// <summary>Set every stop-on flag at once (xbdm <c>stopon all</c>).</summary>
        public void StopOnAll() => SendTextCommand("stopon all");

        /// <summary>Clear every stop-on flag at once (xbdm <c>nostopon all</c>).</summary>
        public void NoStopOnAll() => SendTextCommand("nostopon all");

        #endregion

        #region Process state (DmGetPid / DmGetAltAddress)

        /// <summary>DmGetPid — get the process id of the currently running title.</summary>
        public uint GetPid()
        {
            var resp = SendTextCommand("getpid");
            if (!resp.IsSuccess) return 0;
            string s = ParseKvLine(resp.StatusMessage, "pid") ?? resp.StatusMessage?.Trim();
            return XboxExtensions.ParseHexUInt(s);
        }

        /// <summary>
        /// DmGetAltAddress — the title's alternate (game-side) IP address as a
        /// dotted quad.  xbdm replies with <c>addr=0xAABBCCDD</c>.
        /// </summary>
        public string GetAltAddress()
        {
            var resp = SendTextCommand("altaddr");
            if (!resp.IsSuccess) return null;
            string addrHex = ParseKvLine(resp.StatusMessage, "addr");
            if (string.IsNullOrEmpty(addrHex)) return resp.StatusMessage?.Trim();

            uint addr = XboxExtensions.ParseHexUInt(addrHex);
            return $"{(addr >> 24) & 0xFF}.{(addr >> 16) & 0xFF}.{(addr >> 8) & 0xFF}.{addr & 0xFF}";
        }

        #endregion
    }