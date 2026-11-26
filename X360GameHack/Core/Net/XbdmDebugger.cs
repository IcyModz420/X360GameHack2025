using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;

/// <summary>The console is a debug target as it stands: every member below is one of its plain XBDM commands.</summary>
public partial class XboxConsole : IDebugTarget
{
    private bool _debuggerRegistered;

    public void DebuggerAttach()
    {
        AttachDebugger("X360GameHack", "X360GameHack", 0, true);
        // measured on a console: "isdebugger" answers 410 with the registered name once this has worked
        if (!IsDebuggerPresent())
            throw new System.InvalidOperationException("The console did not accept this program as its debugger, so no breakpoint was set (a breakpoint without a debugger hangs the console).");
        _debuggerRegistered = true;
    }

    public void DebuggerDetach()
    {
        _debuggerRegistered = false;
        SendTextCommand("break clearall");
        DetachDebugger();
    }

    /// <summary>Before the connection is closed on purpose: never leave breakpoints behind with nobody listening.</summary>
    internal void ReleaseDebuggerBeforeDisconnect()
    {
        if (!_debuggerRegistered || !Connected) return;
        try
        {
            // take the breakpoints off, let anything that is stopped at one run on, then let go
            SendTextCommand("break clearall");
            foreach (uint id in GetThreadIds() ?? new uint[0])
                if (GetThreadStopReason(id) != null) ContinueThread(id, false);
            Go();
            DebuggerDetach();
        }
        catch (System.Exception) { _debuggerRegistered = false; }
    }
}

namespace X360GameHack.Core.Net
{
    /// <summary>
    /// What the debugger needs from a console. Everything here is a stock XBDM text command (getmem, getcontext,
    /// setcontext, break, isstopped, continue, stop, go) - no remote-call plugin of any kind is involved.
    /// </summary>
    public interface IDebugTarget
    {
        uint[] GetThreadIds();
        string GetThreadStopReason(uint threadId);
        XBOX_CONTEXT_EX GetThreadContext(uint threadId, XboxContextFlags flags);
        void SetThreadContext(uint threadId, XBOX_CONTEXT_EX context);
        byte[] GetMemory(uint address, uint length);
        void SetBreakpoint(uint address);
        void ClearBreakpoint(uint address);
        void ContinueThread(uint threadId, bool exception);
        void Stop();
        void Go();
        /// <summary>Registers this program as the console's debugger. Must be done before any breakpoint is planted:
        /// a breakpoint that is hit while no debugger is registered has nobody to report to, and the console hangs.</summary>
        void DebuggerAttach();
        /// <summary>Removes every breakpoint and gives up the debugger registration.</summary>
        void DebuggerDetach();
    }

    public enum StepKind
    {
        /// <summary>One instruction; a call is followed into the function.</summary>
        Into,
        /// <summary>One instruction; a call runs to completion and stops after it.</summary>
        Over
    }

    /// <summary>Where and why a thread is stopped.</summary>
    public sealed class DebugStop
    {
        public uint ThreadId;
        public string Reason;
        public XBOX_CONTEXT_EX Context;
        public uint Address => Context?.Iar ?? 0;
    }

    /// <summary>One line of an instruction trace.</summary>
    public sealed class TraceLine
    {
        public PpcInstruction Instruction;
        /// <summary>Registers the instruction changed, e.g. "r3=0000000000000010 cr=20000000".</summary>
        public string Changes = "";
    }

    /// <summary>
    /// Source-less debugging over XBDM: breakpoints, break / continue, stepping, run-to and tracing. XBDM has no
    /// "single step" command, so a step plants temporary breakpoints on every place the current instruction can go
    /// next (worked out from the decoded instruction and the thread's registers), lets the thread run, and removes
    /// them again. Not thread-safe: call it from one thread at a time (ConsoleService.RunAsync does that).
    /// </summary>
    public sealed class XbdmDebugger
    {
        private readonly IDebugTarget _target;
        private readonly SortedSet<uint> _breakpoints = new SortedSet<uint>();
        private bool _frozen;

        /// <summary>True after <see cref="Break"/> (or a stop) until execution is resumed: every thread is held, so any of them can be inspected and stepped.</summary>
        public bool IsFrozen => _frozen;

        /// <summary>How long a step may take before it is reported as "did not come back".</summary>
        public int StepTimeoutMs = 5000;
        /// <summary>Pause between "is it stopped yet" questions.</summary>
        public int PollMs = 10;

        public XbdmDebugger(IDebugTarget target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        // ---------------------------------------------------------------- breakpoints

        public IReadOnlyCollection<uint> Breakpoints => _breakpoints;

        private static void RequireCodeAddress(uint address)
        {
            if ((address & 3) != 0) throw new ArgumentException("Instructions sit on 4-byte boundaries: 0x" + address.ToString("X8") + " is not a code address.");
        }

        private bool _attached;

        /// <summary>True once this program is registered as the console's debugger.</summary>
        public bool IsAttached => _attached;

        /// <summary>Registers as the console's debugger (once). Every path that plants a breakpoint goes through here first.</summary>
        public void Attach()
        {
            if (_attached) return;
            _target.DebuggerAttach();
            _attached = true;
        }

        /// <summary>Takes every breakpoint off the console, lets it run and gives up the debugger registration.</summary>
        public void Detach()
        {
            try { ClearBreakpoints(); }
            finally
            {
                if (_attached) { _attached = false; _target.DebuggerDetach(); }
            }
        }

        public void AddBreakpoint(uint address)
        {
            RequireCodeAddress(address);
            if (_breakpoints.Contains(address)) return;
            Attach();
            _target.SetBreakpoint(address);
            _breakpoints.Add(address);
        }

        public void RemoveBreakpoint(uint address)
        {
            if (!_breakpoints.Remove(address)) return;
            _target.ClearBreakpoint(address);
        }

        public void ClearBreakpoints()
        {
            foreach (uint a in _breakpoints.ToList()) RemoveBreakpoint(a);
        }

        // ---------------------------------------------------------------- state

        /// <summary>The first stopped thread (preferring <paramref name="preferThread"/>), or null when everything runs.</summary>
        public DebugStop FindStop(uint preferThread = 0)
        {
            var ids = _target.GetThreadIds() ?? new uint[0];
            foreach (uint id in ids.OrderBy(i => i == preferThread ? 0 : 1))
            {
                string reason = _target.GetThreadStopReason(id);
                if (reason != null) return new DebugStop { ThreadId = id, Reason = reason, Context = _target.GetThreadContext(id, XboxContextFlags.Full) };
            }
            return null;
        }

        /// <summary>Freezes the whole console (XBDM "stop").</summary>
        public void Break()
        {
            _target.Stop();
            _frozen = true;
        }

        /// <summary>The registers of a thread right now (meaningful while the console is frozen or the thread is stopped).</summary>
        public DebugStop Snapshot(uint threadId)
            => new DebugStop { ThreadId = threadId, Reason = _target.GetThreadStopReason(threadId) ?? (_frozen ? "frozen" : "running"), Context = _target.GetThreadContext(threadId, XboxContextFlags.Full) };

        /// <summary>Lets every stopped thread go on and unfreezes the console.</summary>
        public void Continue()
        {
            foreach (uint id in _target.GetThreadIds() ?? new uint[0])
                if (_target.GetThreadStopReason(id) != null) _target.ContinueThread(id, false);
            _target.Go();
            _frozen = false;
        }

        /// <summary>The instruction at <paramref name="address"/>, as the title sees it (a breakpoint planted there is looked through).</summary>
        public PpcInstruction ReadInstruction(uint address)
        {
            uint word = ReadWord(address);
            // XBDM hides its own breakpoints in getmem; should one show through anyway, lift it for a moment.
            if (word == BreakpointTrap && _breakpoints.Contains(address))
            {
                _target.ClearBreakpoint(address);
                try { word = ReadWord(address); }
                finally { _target.SetBreakpoint(address); }
            }
            return PowerPc.Decode(word, address);
        }

        /// <summary>The instruction XBDM writes over code for a breakpoint (twui r0, 0x16).</summary>
        public const uint BreakpointTrap = 0x0FE00016;

        private uint ReadWord(uint address)
        {
            byte[] b = _target.GetMemory(address, 4);
            if (b == null || b.Length < 4) throw new InvalidOperationException("Could not read the instruction at 0x" + address.ToString("X8") + ".");
            return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        }

        // ---------------------------------------------------------------- stepping

        /// <summary>
        /// Every address execution can be at after <paramref name="ins"/> has run on a thread with these registers.
        /// A conditional branch gives both ways so the step holds even if a register changes underneath us.
        /// </summary>
        public static List<uint> Successors(PpcInstruction ins, XBOX_CONTEXT_EX context, StepKind kind)
        {
            if (ins == null) throw new ArgumentNullException(nameof(ins));
            if (context == null) throw new ArgumentNullException(nameof(context));
            uint next = ins.Address + 4;
            var list = new List<uint>();
            if (!ins.Valid || !ins.IsBranch) { list.Add(next); return list; }
            if (ins.IsCall && kind == StepKind.Over) { list.Add(next); return list; }
            uint target;
            if (ins.Target.HasValue) target = ins.Target.Value;
            else if (ins.ToLinkRegister || ins.ToCountRegister)
            {
                uint reg = (ins.ToLinkRegister ? context.Lr : context.Ctr) & ~3u;
                // 0 / all ones = the console did not give that register (seen on a Trinity RGH3: a thread Break parks reports its
                // volatile registers as all ones, and CTR came back 0 at a breakpoint) - never plant a breakpoint there
                if (reg == 0 || reg == 0xFFFFFFFC)
                {
                    if (ins.IsCall) { list.Add(next); return list; }          // a call: run it as a whole
                    throw new InvalidOperationException("The console did not say where this " + (ins.ToLinkRegister ? "return" : "jump") + " goes (its " +
                        (ins.ToLinkRegister ? "LR" : "CTR") + " register), so it cannot be stepped. Put a breakpoint where it leads and press Continue.");
                }
                target = reg;
            }
            else target = next;
            list.Add(target);
            if (ins.IsConditional && target != next) list.Add(next);
            return list;
        }

        /// <summary>
        /// Where a step of a stopped thread would plant its temporary breakpoints (read only). A step lets the console run
        /// until the thread reaches one of them, so a breakpoint in system code is met by system code too - one planted
        /// in the kernel's thread switching froze a test console (2026-11-18).
        /// </summary>
        public List<uint> StepTargets(uint threadId, StepKind kind)
        {
            var context = RequireStopped(threadId);
            return Successors(ReadInstruction(context.Iar), context, kind);
        }

        /// <summary>Runs one instruction of a stopped thread.</summary>
        public DebugStop Step(uint threadId, StepKind kind, CancellationToken ct = default(CancellationToken))
        {
            var context = RequireStopped(threadId);
            var ins = ReadInstruction(context.Iar);
            return RunUntil(threadId, Successors(ins, context, kind), ct);
        }

        /// <summary>Lets a stopped thread run until it reaches <paramref name="address"/> (or stops for another reason first).</summary>
        public DebugStop RunTo(uint threadId, uint address, CancellationToken ct = default(CancellationToken))
        {
            RequireCodeAddress(address);
            RequireStopped(threadId);
            return RunUntil(threadId, new List<uint> { address }, ct);
        }

        /// <summary>Lets a stopped thread run until the function it is in returns to its caller (the address in LR right now).</summary>
        public DebugStop RunToReturnAddress(uint threadId, CancellationToken ct = default(CancellationToken))
        {
            var context = RequireStopped(threadId);
            uint lr = context.Lr & ~3u;
            if (lr == 0 || lr == 0xFFFFFFFC)
                throw new InvalidOperationException("The console did not say where this function returns to (its LR register). Put a breakpoint there and press Continue.");
            return RunUntil(threadId, new List<uint> { lr }, ct);
        }

        private XBOX_CONTEXT_EX RequireStopped(uint threadId)
        {
            // a thread can be stepped when it is stopped at a break / exception, or when the whole console is frozen
            if (!_frozen && _target.GetThreadStopReason(threadId) == null)
                throw new InvalidOperationException("Thread 0x" + threadId.ToString("X8") + " is running. Break first, then step.");
            return _target.GetThreadContext(threadId, XboxContextFlags.Full);
        }

        private DebugStop RunUntil(uint threadId, List<uint> addresses, CancellationToken ct)
        {
            var planted = new List<uint>();
            Attach();
            try
            {
                foreach (uint a in addresses.Distinct())
                {
                    if (_breakpoints.Contains(a)) continue;      // the user's own breakpoint will stop it there
                    _target.SetBreakpoint(a);
                    planted.Add(a);
                }
                // "continue" only applies to a thread that is stopped at a break or exception; a frozen one just needs "go"
                if (_target.GetThreadStopReason(threadId) != null) _target.ContinueThread(threadId, false);
                _target.Go();
                _frozen = false;

                var clock = System.Diagnostics.Stopwatch.StartNew();
                int polls = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    string reason = _target.GetThreadStopReason(threadId);
                    if (reason != null)
                    {
                        _frozen = true;     // a break holds the whole title until it is continued
                        return new DebugStop { ThreadId = threadId, Reason = reason, Context = _target.GetThreadContext(threadId, XboxContextFlags.Full) };
                    }
                    if (++polls % 16 == 0)
                    {
                        // another thread may have run into one of our temporary breakpoints or one of the user's
                        var other = FindStop(threadId);
                        if (other != null) { _frozen = true; return other; }
                    }
                    if (clock.ElapsedMilliseconds > StepTimeoutMs)
                        throw new TimeoutException("The thread did not stop again within " + (StepTimeoutMs / 1000.0).ToString("0.#") +
                                                   " s (it may be waiting, or it left through an exception). It is still running; use Break to stop the console.");
                    if (PollMs > 0) Thread.Sleep(PollMs);
                }
            }
            finally
            {
                foreach (uint a in planted)
                {
                    try { _target.ClearBreakpoint(a); } catch { /* connection lost: nothing more can be done from here */ }
                }
            }
        }

        // ---------------------------------------------------------------- trace

        /// <summary>
        /// Steps a stopped thread <paramref name="count"/> instructions and records each one with the registers it
        /// changed. Stops early when the thread stops somewhere a step cannot explain (a breakpoint in another thread).
        /// </summary>
        /// <param name="mayStopAt">null = anywhere; otherwise the trace ends before a step that would plant a breakpoint
        /// where this says no (system code - see <see cref="StepTargets"/>).</param>
        public List<TraceLine> Trace(uint threadId, int count, StepKind kind, Action<TraceLine> onLine = null, CancellationToken ct = default(CancellationToken),
                                     Func<uint, bool> mayStopAt = null)
        {
            if (count < 1 || count > 100000) throw new ArgumentOutOfRangeException(nameof(count), "Trace between 1 and 100000 instructions.");
            var lines = new List<TraceLine>();
            var before = RequireStopped(threadId);
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var ins = ReadInstruction(before.Iar);
                List<uint> targets;
                try { targets = Successors(ins, before, kind); }
                catch (InvalidOperationException ex)
                {
                    var stuck = new TraceLine { Instruction = ins, Changes = "(" + ex.Message + " - trace ended)" };
                    lines.Add(stuck); onLine?.Invoke(stuck);
                    break;
                }
                uint outside = mayStopAt == null ? 0 : targets.FirstOrDefault(a => !mayStopAt(a));
                if (mayStopAt != null && targets.Any(a => !mayStopAt(a)))
                {
                    var end = new TraceLine { Instruction = ins, Changes = "(the next step would stop in system code at 0x" + outside.ToString("X8") + " - trace ended; stepping system code can freeze the console)" };
                    lines.Add(end); onLine?.Invoke(end);
                    break;
                }
                var stop = RunUntil(threadId, targets, ct);
                var line = new TraceLine { Instruction = ins };
                if (stop.ThreadId != threadId)
                {
                    line.Changes = "(thread 0x" + stop.ThreadId.ToString("X8") + " stopped at 0x" + stop.Address.ToString("X8") + " - trace ended)";
                    lines.Add(line); onLine?.Invoke(line);
                    break;
                }
                line.Changes = Describe(before, stop.Context);
                lines.Add(line); onLine?.Invoke(line);
                before = stop.Context;
            }
            return lines;
        }

        /// <summary>The registers that differ between two contexts of the same thread (the program counter is left out).</summary>
        public static string Describe(XBOX_CONTEXT_EX before, XBOX_CONTEXT_EX after)
        {
            var parts = new List<string>();
            for (int r = 0; r < 32; r++)
                if (before.Gpr[r] != after.Gpr[r]) parts.Add("r" + r + "=" + after.Gpr[r].ToString("X16"));
            if (before.Lr != after.Lr) parts.Add("lr=" + after.Lr.ToString("X8"));
            if (before.Ctr != after.Ctr) parts.Add("ctr=" + after.Ctr.ToString("X8"));
            if (before.Cr != after.Cr) parts.Add("cr=" + after.Cr.ToString("X8"));
            if (before.Xer != after.Xer) parts.Add("xer=" + after.Xer.ToString("X8"));
            for (int r = 0; r < 32; r++)
                if (BitConverter.DoubleToInt64Bits(before.Fpr[r]) != BitConverter.DoubleToInt64Bits(after.Fpr[r]))
                    parts.Add("f" + r + "=" + after.Fpr[r].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" ", parts);
        }
    }
}
