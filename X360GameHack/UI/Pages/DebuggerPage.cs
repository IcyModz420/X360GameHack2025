using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Formats;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// Source-less debugger for a running title over plain XBDM: break / continue, step, run-to, breakpoints (code and
    /// data), registers, PowerPC disassembly, threads, modules and instruction tracing. Uses stock XBDM commands only.
    /// </summary>
    public sealed class DebuggerPage : PageBase
    {
        public override string Title => "Debugger";
        public override string Subtitle => "Break, step and trace a running title over XBDM, with live PowerPC disassembly and registers.";
        public override string PageGlyph => Glyph.Bug;

        // execution
        private readonly TextBlock _state = new TextBlock("Not connected.", Theme.BodyBold, Theme.TextDim);
        private readonly XCombo _thread = new XCombo { Width = Theme.S(260) };
        private readonly XTextBox _goto = new XTextBox("Address, e.g. 82000000", false, true) { Width = Theme.S(190) };
        private readonly ListView _code = ListStyle.Create(("", 30), ("Address", 96), ("Bytes", 90), ("Instruction", 330), ("Goes to", 140));
        private readonly ListView _regs = ListStyle.Create(("Register", 80), ("Value", 170), ("", 120));
        // threads / modules / breakpoints / trace
        private readonly ListView _threads = ListStyle.Create(("Thread", 100), ("State", 150), ("PC", 96), ("Start", 96), ("Priority", 70), ("Suspend", 70), ("Stack", 190));
        private readonly ListView _modules = ListStyle.Create(("Module", 220), ("Base", 96), ("Size", 96), ("Built", 150));
        private readonly ListView _sections = ListStyle.Create(("Section", 120), ("Base", 96), ("Size", 96), ("Flags", 90));
        private readonly ListView _breaks = ListStyle.Create(("Kind", 150), ("Address", 96), ("Size", 70), ("Instruction", 320));
        private readonly XTextBox _bpAddr = new XTextBox("Address", false, true) { Width = Theme.S(170) };
        private readonly Segmented _bpKind = new Segmented("Execute", "Write", "Read", "Read / write");
        private readonly XTextBox _bpSize = new XTextBox("Size (data)", false, true) { Width = Theme.S(110) };
        private readonly XCheck _onModule = new XCheck("Module load"), _onThread = new XCheck("Thread create"),
                                _onException = new XCheck("First-chance exception"), _onDebugString = new XCheck("Debug string");
        private readonly XTextBox _traceCount = new XTextBox("Instructions", false, true) { Width = Theme.S(130) };
        private readonly Segmented _traceKind = new Segmented("Into calls", "Over calls");
        private readonly ListView _trace = ListStyle.Create(("#", 56), ("Address", 96), ("Instruction", 300), ("Changed", 420));
        private readonly VStack _execTab;

        private XboxConsole _console;                       // the console the debugger below belongs to
        private XbdmDebugger _dbg;
        private readonly SortedSet<uint> _userBreaks = new SortedSet<uint>();
        private readonly List<(XboxDataBreakpointType Kind, uint Address, uint Size)> _dataBreaks = new List<(XboxDataBreakpointType, uint, uint)>();
        private readonly List<ModuleInfo> _moduleList = new List<ModuleInfo>();
        private DebugStop _stop;                            // what the views show
        private uint _viewAddress;
        private bool _armed;                                // watch for a break while the title runs
        private readonly System.Windows.Forms.Timer _watch = new System.Windows.Forms.Timer { Interval = 400 };
        private CancellationTokenSource _traceCancel;
        private readonly List<TraceLine> _traceLines = new List<TraceLine>();

        public DebuggerPage()
        {
            Body.Add(new ConnectBar());

            // ---------------------------------------------------------------- execution
            _execTab = AddSection("Execution", Glyph.Play);
            var run = AddCard("Execution", null, Glyph.Bug, _execTab);
            run.Add(_state);
            var bar = new WrapPanel();
            bar.Add(Btn("Break", Glyph.Cancel, Break, BtnKind.Primary, "Freeze the console (XBDM stop)"));
            bar.Add(Btn("Continue", Glyph.Play, Continue, BtnKind.Secondary, "Let it run again (XBDM go)"));
            bar.Add(Btn("Step into", Glyph.ChevronDown, () => Step(StepKind.Into), tip: "Run one instruction; follow calls"));
            bar.Add(Btn("Step over", Glyph.ChevronRight, () => Step(StepKind.Over), tip: "Run one instruction; run calls to completion"));
            bar.Add(Btn("Run to return", Glyph.Up, RunToReturn, tip: "Run until the address in LR is reached (use at the start of a function)"));
            bar.Add(Btn("Run to selected", Glyph.Send, RunToSelected, tip: "Run until the selected line is reached"));
            run.Add(bar);
            PageBase.Field(run, "Thread", _thread, Btn("", Glyph.Refresh, () => RefreshAll(), tip: "Read threads and registers again")).FillChild = null;
            _thread.SelectedIndexChanged += (s, e) => { if (!_filling) ShowThread(); };

            var cols = AddColumns(2, 520, _execTab);
            var code = AddCard("Disassembly", "PowerPC code read from the console. Double-click a line to toggle a breakpoint.", Glyph.Code, cols);
            var nav = new WrapPanel();
            nav.Add(_goto);
            nav.Add(Btn("Go", Glyph.Search, GoToAddress, BtnKind.Secondary, "Show this address (Enter)"));
            nav.Add(Btn("PC", Glyph.Back, () => { if (_stop != null) ShowCode(_stop.Address); }, tip: "Back to where the thread is"));
            nav.Add(Btn("Follow", Glyph.Link, FollowBranch, tip: "Go to where the selected branch leads"));
            nav.Add(Btn("Breakpoint", Glyph.Add, ToggleSelectedBreakpoint, tip: "Toggle a breakpoint on the selected line"));
            code.Add(nav);
            _code.Font = Theme.Mono;
            _code.Height = Theme.S(470);
            _code.MultiSelect = false;
            _code.DoubleClick += (s, e) => ToggleSelectedBreakpoint();
            code.Add(_code);
            _goto.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GoToAddress(); } };

            var regs = AddCard("Registers", "Of the selected thread. Double-click a value to change it (only while it is stopped).", Glyph.Memory, cols);
            _regs.Font = Theme.Mono;
            _regs.Height = Theme.S(520);
            _regs.MultiSelect = false;
            _regs.DoubleClick += (s, e) => EditRegister();
            regs.Add(_regs);

            // ---------------------------------------------------------------- threads
            var threadsTab = AddSection("Threads", Glyph.List);
            var tc = AddCard("Threads", "Every thread of the running title.", Glyph.List, threadsTab);
            tc.AddAction("Refresh", Glyph.Refresh, (s, e) => LoadThreads());
            _threads.Height = Theme.S(360);
            _threads.MultiSelect = false;
            _threads.DoubleClick += (s, e) => UseSelectedThread();
            tc.Add(_threads);
            Buttons(tc,
                Btn("Debug this thread", Glyph.Bug, UseSelectedThread, BtnKind.Primary),
                Btn("Suspend", Glyph.Cancel, () => ThreadCommand("Suspending thread", (c, id) => c.SuspendThread(id))),
                Btn("Resume", Glyph.Play, () => ThreadCommand("Resuming thread", (c, id) => c.ResumeThread(id))),
                Btn("Halt", Glyph.Lock, () => ThreadCommand("Halting thread", (c, id) => { c.HaltThread(id); return true; }), tip: "Break into this thread only"));

            // ---------------------------------------------------------------- modules
            var modulesTab = AddSection("Modules", Glyph.Package);
            var mcols = AddColumns(2, 460, modulesTab);
            var mc = AddCard("Loaded modules", "Double-click a module to disassemble from its base.", Glyph.Package, mcols);
            mc.AddAction("Refresh", Glyph.Refresh, (s, e) => LoadModules());
            _modules.Height = Theme.S(360);
            _modules.MultiSelect = false;
            _modules.SelectedIndexChanged += (s, e) => LoadSections();
            _modules.DoubleClick += (s, e) => { var m = SelectedModule(); if (m != null) { ShowSection(_execTab); ShowCode(m.BaseAddress); } };
            mc.Add(_modules);
            Buttons(mc, Btn("Dump module to file...", Glyph.Save, DumpModule));
            var sc = AddCard("Sections", "Of the selected module. Double-click to disassemble.", Glyph.List, mcols);
            _sections.Height = Theme.S(360);
            _sections.MultiSelect = false;
            _sections.DoubleClick += (s, e) =>
            {
                if (_sections.SelectedItems.Count == 0) return;
                ShowSection(_execTab);
                ShowCode((uint)_sections.SelectedItems[0].Tag);
            };
            sc.Add(_sections);

            // ---------------------------------------------------------------- breakpoints
            var breaksTab = AddSection("Breakpoints", Glyph.Shield);
            var bc = AddCard("Breakpoints", "Execute breakpoints stop a thread before the instruction runs. Data breakpoints stop on a memory read or write.", Glyph.Shield, breaksTab);
            var add = new WrapPanel();
            add.Add(_bpAddr); add.Add(_bpKind); add.Add(_bpSize);
            add.Add(Btn("Add", Glyph.Add, AddBreakpoint, BtnKind.Primary));
            bc.Add(add);
            _bpSize.Text = "4";
            _breaks.Height = Theme.S(240);
            bc.Add(_breaks);
            Buttons(bc,
                Btn("Show", Glyph.Search, () => { if (_breaks.SelectedItems.Count > 0) { ShowSection(_execTab); ShowCode(((uint Address, int Data))_breaks.SelectedItems[0].Tag is var t ? t.Address : 0); } }),
                Btn("Remove", Glyph.Delete, RemoveSelectedBreakpoints, BtnKind.Danger),
                Btn("Remove all", Glyph.Clear, RemoveAllBreakpoints));
            var so = AddCard("Also stop when", "The console holds the title when one of these happens.", Glyph.Warning, breaksTab);
            var checks = new WrapPanel();
            checks.Add(_onModule); checks.Add(_onThread); checks.Add(_onException); checks.Add(_onDebugString);
            so.Add(checks);
            Buttons(so, Btn("Apply", Glyph.Check, ApplyStopOn, BtnKind.Secondary));

            // ---------------------------------------------------------------- trace
            var traceTab = AddSection("Trace", Glyph.List);
            var tr = AddCard("Instruction trace", "Steps the stopped thread and records every instruction with the registers it changed.", Glyph.List, traceTab);
            _traceCount.Text = "200";
            var tb = new WrapPanel();
            tb.Add(_traceCount); tb.Add(_traceKind);
            tb.Add(Btn("Trace", Glyph.Play, StartTrace, BtnKind.Primary));
            tb.Add(Btn("Stop", Glyph.Cancel, () => _traceCancel?.Cancel()));
            tb.Add(Btn("Save...", Glyph.Save, SaveTrace));
            tr.Add(tb);
            _trace.Font = Theme.Mono;
            _trace.Height = Theme.S(440);
            tr.Add(_trace);

            _watch.Tick += async (s, e) => await WatchTick();
            Hooks.ConsoleChanged(this, Sync);
            Sync();
        }

        // ---------------------------------------------------------------- plumbing

        private bool _filling;

        private void Sync()
        {
            bool on = ConsoleService.IsConnected;
            if (!on)
            {
                _watch.Stop(); _armed = false; _stop = null; _console = null; _dbg = null;
                SetState("Not connected. Connect to a console above, then press Break.", Theme.TextDim);
            }
            else if (_stop == null && !_armed) SetState("Connected. The title is running - press Break to freeze it, or add a breakpoint.", Theme.TextDim);
        }

        public override void OnActivated() { Sync(); if (_armed) _watch.Start(); }
        public override void OnDeactivated() => _watch.Stop();

        private void SetState(string text, System.Drawing.Color color)
        {
            _state.Text = text;
            _state.ForeColor = color;
            _state.Invalidate();
        }

        private static bool _riskAccepted;
        private const string RiskText = "Breakpoints and stepping can hang the console until it is switched off and on again (seen on an RGH console). Save your game first and stay near the power button.";

        private bool Ready()
        {
            if (!ConsoleService.IsConnected)
            {
                Dlg.Info("Not connected", "Connect to a console first.");
                return false;
            }
            if (_riskAccepted) return true;
            // Measured on a Trinity RGH3 (xbdm 2.0.21076): the first breakpoint planted in a running title
            // froze the whole console, network included, until it was switched off and on.
            Dlg.Info("Before you use the debugger", RiskText); _riskAccepted = true;
            return _riskAccepted;
        }

        /// <summary>Runs debugger work on the console thread. A new console connection gets a new debugger with the user's breakpoints set again.</summary>
        private Task<T> Dbg<T>(Func<XbdmDebugger, XboxConsole, T> work) => ConsoleService.RunAsync(c =>
        {
            if (!ReferenceEquals(c, _console))
            {
                _console = c;
                _dbg = new XbdmDebugger(c);
                foreach (uint a in _userBreaks) _dbg.AddBreakpoint(a);
                if (_dataBreaks.Count > 0) _dbg.Attach();
                foreach (var d in _dataBreaks) c.SetDataBreakpoint(d.Kind, d.Address, d.Size);
            }
            return work(_dbg, c);
        });

        private static uint ParseAddr(string text)
        {
            try { return BE.ParseHex32(text); }
            catch { throw new FormatException("Addresses are hexadecimal, e.g. 82000000."); }
        }

        private uint CurrentThread => _thread.SelectedItem is ThreadItem t ? t.Id : 0;

        private sealed class ThreadItem
        {
            public uint Id; public string Label;
            public override string ToString() => Label;
        }

        // ---------------------------------------------------------------- execution

        /// <summary>The running game's own module (from "xbeinfo running"), or null - e.g. when only the dashboard runs.</summary>
        private static ModuleInfo TitleModule(XboxConsole c)
        {
            string info = c.GetXbeInfo() ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(info, "name=\"([^\"]*)\"");
            if (!m.Success) return null;
            string file = m.Groups[1].Value.Substring(m.Groups[1].Value.LastIndexOf('\\') + 1);
            return (c.GetModules() ?? new List<ModuleInfo>()).FirstOrDefault(x => x.Name.Equals(file, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Inside(ModuleInfo m, uint address) => m != null && address >= m.BaseAddress && address - m.BaseAddress < m.Size;

        /// <summary>
        /// Steps, Run to... and code breakpoints plant breakpoints and let the console run to them. One planted in system code
        /// (the kernel, XAM, XBDM or a plugin) is met by system code as well and can freeze the whole console until it is
        /// switched off and on - measured on a Trinity RGH3 on 2026-11-18: one Step into at xboxkrnl.exe+0x35360, where Break
        /// parks every thread. Asks first then; addresses in the game's own code go ahead without a question.
        /// </summary>
        private async Task<bool> SystemCodeOk(params uint[] addresses)
        {
            string where = null;
            try
            {
                var found = await Dbg((d, c) =>
                {
                    var title = TitleModule(c);
                    var mods = c.GetModules() ?? new List<ModuleInfo>();
                    foreach (uint a in addresses)
                    {
                        if (Inside(title, a)) continue;
                        var m = mods.FirstOrDefault(x => Inside(x, a));
                        return m != null ? m.Name + "+" + (a - m.BaseAddress).ToString("X") : "0x" + a.ToString("X8");
                    }
                    return null;
                });
                where = found;
            }
            catch (Exception) { return true; }           // cannot tell: the step itself reports any problem
            if (where == null) return true;
            return Dlg.Confirm("System code", where + " is system code, not the game's own. Stepping there, or running to a line there, can freeze the " +
                                              "whole console until it is switched off and on again (it happened on a test console).\n\nDo it anyway?", "Do it anyway", true);
        }

        private void Break()
        {
            if (!Ready()) return;
            Run(null, "Breaking", async () =>
            {
                var result = await Dbg((d, c) =>
                {
                    d.Break();
                    var ids = c.GetThreadIds() ?? new uint[0];
                    // A thread a breakpoint or an exception stopped comes first, then the chosen one. Measured on a Trinity RGH3
                    // (2026-11-18): Break parks EVERY thread, the game's own too, at the same spot inside the kernel - none of
                    // them can be stepped safely from there (SystemCodeOk asks). Stepping starts at a breakpoint in the game.
                    var stop = d.FindStop(CurrentThread);
                    uint pick = stop?.ThreadId ?? (ids.Contains(CurrentThread) ? CurrentThread : ids.FirstOrDefault());
                    return (Ids: ids, Stop: stop ?? (pick != 0 ? d.Snapshot(pick) : null));
                });
                _armed = false; _watch.Stop();
                FillThreads(result.Ids, result.Stop?.ThreadId ?? 0);
                Show(result.Stop, "Frozen");
            });
        }

        private void Continue()
        {
            if (!Ready()) return;
            Run(null, "Continuing", async () =>
            {
                await Dbg((d, c) => { d.Continue(); return true; });
                _stop = null;
                _armed = true;
                _watch.Start();
                SetState("Running. Waiting for a breakpoint...", Theme.Success);
                MarkCode();
            });
        }

        private bool RequireStop()
        {
            if (!Ready()) return false;
            if (_stop != null && CurrentThread != 0) return true;
            Dlg.Info("Debugger", "Press Break first (or wait for a breakpoint) - a thread can only be stepped while it is held.");
            return false;
        }

        private async void Step(StepKind kind)
        {
            if (!RequireStop()) return;
            uint id = CurrentThread;
            // a step plants breakpoints where the instruction can lead and lets the console run to them: check those too
            uint[] targets;
            try { targets = (await Dbg((d, c) => d.StepTargets(id, kind))).ToArray(); }
            catch (Exception) { targets = new uint[0]; }             // the step itself reports the problem
            if (!await SystemCodeOk(new[] { _stop.Address }.Concat(targets).ToArray())) return;
            Run(null, kind == StepKind.Into ? "Stepping into" : "Stepping over", async () => Show(await Dbg((d, c) => d.Step(id, kind)), "Stepped"));
        }

        private async void RunToReturn()
        {
            if (!RequireStop()) return;
            uint id = CurrentThread;
            uint lr = (_stop.Context?.Lr ?? 0) & ~3u;
            bool known = lr != 0 && lr != 0xFFFFFFFC;              // otherwise the run itself says the console did not give LR
            if (!await SystemCodeOk(known ? new[] { _stop.Address, lr } : new[] { _stop.Address })) return;
            Run(null, "Running to the return address", async () => Show(await Dbg((d, c) => d.RunToReturnAddress(id)), "Returned"));
        }

        private async void RunToSelected()
        {
            if (!RequireStop()) return;
            if (_code.SelectedItems.Count == 0) { Dlg.Info("Run to selected", "Select a line in the disassembly first."); return; }
            uint id = CurrentThread, address = ((PpcInstruction)_code.SelectedItems[0].Tag).Address;
            if (!await SystemCodeOk(_stop.Address, address)) return;
            Run(null, "Running to 0x" + address.ToString("X8"), async () => Show(await Dbg((d, c) => d.RunTo(id, address)), "Reached"));
        }

        private async Task WatchTick()
        {
            if (!_armed || !ConsoleService.IsConnected || Status.IsBusy) return;
            try
            {
                var result = await Dbg((d, c) =>
                {
                    var stop = d.FindStop(CurrentThread);
                    return (Ids: stop != null ? c.GetThreadIds() ?? new uint[0] : null, Stop: stop);
                });
                if (result.Stop == null) return;
                _armed = false; _watch.Stop();
                FillThreads(result.Ids, result.Stop.ThreadId);
                Show(result.Stop, "Stopped");
                Log.Info("Debugger: thread 0x" + result.Stop.ThreadId.ToString("X8") + " stopped at 0x" + result.Stop.Address.ToString("X8") + " (" + result.Stop.Reason + ").");
            }
            catch { _armed = false; _watch.Stop(); }
        }

        private void FillThreads(uint[] ids, uint select)
        {
            _filling = true;
            try
            {
                _thread.Items.Clear();
                foreach (uint id in ids ?? new uint[0]) _thread.Items.Add(new ThreadItem { Id = id, Label = "0x" + id.ToString("X8") });
                var pick = _thread.Items.Cast<ThreadItem>().FirstOrDefault(t => t.Id == select) ?? _thread.Items.Cast<ThreadItem>().FirstOrDefault();
                if (pick != null) _thread.SelectedItem = pick;
            }
            finally { _filling = false; }
        }

        private void Show(DebugStop stop, string verb)
        {
            if (stop == null) { SetState("The console did not report any thread.", Theme.Warning); return; }
            _stop = stop;
            if (_thread.Items.Cast<ThreadItem>().All(t => t.Id != stop.ThreadId)) FillThreads(_thread.Items.Cast<ThreadItem>().Select(t => t.Id).Concat(new[] { stop.ThreadId }).ToArray(), stop.ThreadId);
            else { _filling = true; _thread.SelectedItem = _thread.Items.Cast<ThreadItem>().First(t => t.Id == stop.ThreadId); _filling = false; }
            bool readable = stop.Context != null && stop.Context.Iar != 0;
            SetState(readable
                ? verb + ": thread 0x" + stop.ThreadId.ToString("X8") + " at 0x" + stop.Address.ToString("X8") + "  (" + stop.Reason + ")"
                : verb + ": thread 0x" + stop.ThreadId.ToString("X8") + " - the console did not return its registers", readable ? Theme.Warning : Theme.Danger);
            ShowRegisters(stop.Context);
            if (readable) ShowCode(stop.Address);
        }

        private void ShowThread()
        {
            uint id = CurrentThread;
            if (id == 0 || !ConsoleService.IsConnected || _stop == null) return;
            Run(null, "Reading thread", async () => Show(await Dbg((d, c) => d.Snapshot(id)), "Showing"));
        }

        private void RefreshAll()
        {
            if (!Ready()) return;
            uint id = CurrentThread;
            Run(null, "Reading threads", async () =>
            {
                var result = await Dbg((d, c) =>
                {
                    var ids = c.GetThreadIds() ?? new uint[0];
                    uint pick = ids.Contains(id) ? id : ids.FirstOrDefault();
                    return (Ids: ids, Stop: (d.IsFrozen || d.FindStop(pick) != null) && pick != 0 ? d.Snapshot(pick) : null);
                });
                FillThreads(result.Ids, result.Stop?.ThreadId ?? id);
                if (result.Stop != null) Show(result.Stop, "Showing");
            });
        }

        // ---------------------------------------------------------------- registers

        private void ShowRegisters(XBOX_CONTEXT_EX x)
        {
            _regs.BeginUpdate();
            _regs.Items.Clear();
            if (x != null)
            {
                void Row(string name, string value, string note = "") { var it = new ListViewItem(name); it.SubItems.Add(value); it.SubItems.Add(note); _regs.Items.Add(it); }
                Row("pc", x.Iar.ToString("X8"));
                Row("lr", x.Lr.ToString("X8"));
                Row("ctr", x.Ctr.ToString("X8"));
                Row("cr", x.Cr.ToString("X8"), CrText(x.Cr));
                Row("xer", x.Xer.ToString("X8"));
                Row("msr", x.Msr.ToString("X8"));
                for (int i = 0; i < 32; i++) Row("r" + i, x.Gpr[i].ToString("X16"), i == 1 ? "stack" : i == 13 ? "thread data" : "");
                for (int i = 0; i < 32; i++) Row("f" + i, x.Fpr[i].ToString("R", CultureInfo.InvariantCulture));
            }
            _regs.EndUpdate();
        }

        private static string CrText(uint cr)
        {
            var sb = new StringBuilder();
            foreach (int field in new[] { 0, 1, 6, 7 })
            {
                uint bits = cr >> (28 - field * 4) & 0xF;
                sb.Append("cr").Append(field).Append(':').Append((bits & 8) != 0 ? "lt" : (bits & 4) != 0 ? "gt" : (bits & 2) != 0 ? "eq" : "-").Append(' ');
            }
            return sb.ToString().TrimEnd();
        }

        private void EditRegister()
        {
            if (!RequireStop() || _regs.SelectedItems.Count == 0) return;
            string name = _regs.SelectedItems[0].Text, current = _regs.SelectedItems[0].SubItems[1].Text;
            if (name.StartsWith("f")) { Dlg.Info("Registers", "Floating-point registers are shown only."); return; }
            string text = Dlg.Input("Set " + name, "New value (hexadecimal):", current, true);
            if (text == null) return;
            ulong value;
            try { value = Convert.ToUInt64(text.Trim().Replace("0x", "").Replace("0X", ""), 16); }
            catch { Dlg.Warn("Set " + name, "That is not a hexadecimal number."); return; }
            if (!name.StartsWith("r") && value > uint.MaxValue) { Dlg.Warn("Set " + name, name + " is a 32-bit register."); return; }
            if (name == "pc" && (value & 3) != 0) { Dlg.Warn("Set pc", "The program counter must be a multiple of 4."); return; }
            uint id = CurrentThread;
            Run(null, "Writing " + name, async () =>
            {
                var stop = await Dbg((d, c) =>
                {
                    var x = c.GetThreadContext(id, XboxContextFlags.Control | XboxContextFlags.Integer);
                    if (x.Iar == 0) throw new InvalidOperationException("The console did not return the thread's registers, so nothing was written.");
                    switch (name)
                    {
                        case "pc": x.Iar = (uint)value; break;
                        case "lr": x.Lr = (uint)value; break;
                        case "ctr": x.Ctr = (uint)value; break;
                        case "cr": x.Cr = (uint)value; break;
                        case "xer": x.Xer = (uint)value; break;
                        case "msr": x.Msr = (uint)value; break;
                        default: x.Gpr[int.Parse(name.Substring(1))] = value; break;
                    }
                    c.SetThreadContext(id, x);
                    return d.Snapshot(id);
                });
                Log.Success("Debugger: " + name + " of thread 0x" + id.ToString("X8") + " set to " + value.ToString("X") + ".");
                Show(stop, "Showing");
            });
        }

        // ---------------------------------------------------------------- disassembly

        private const int CodeLines = 96;

        private void GoToAddress()
        {
            if (!Ready()) return;
            try { ShowCode(ParseAddr(_goto.Text) & ~3u); }
            catch (Exception ex) { Dlg.Warn("Go to address", ex.Message); }
        }

        private void ShowCode(uint address)
        {
            if (!ConsoleService.IsConnected) return;
            // keep the current window when the address is already comfortably inside it
            if (_code.Items.Count > 0 && address >= _viewAddress && address < _viewAddress + (CodeLines - 12) * 4) { MarkCode(); Reveal(address); return; }
            uint start = address >= 0x20 ? address - 0x20 : 0;
            Run(null, "Reading code at 0x" + address.ToString("X8"), async () =>
            {
                var list = await Dbg((d, c) =>
                {
                    // the window starts a few lines before the address, which at the start of a module is unmapped
                    var holes = new List<KeyValuePair<uint, uint>>();
                    byte[] data = c.GetMemoryWithGaps(start, CodeLines * 4, holes);
                    if (holes.Any(h => address >= h.Key && address - h.Key < h.Value))
                        throw new InvalidOperationException("Memory at 0x" + address.ToString("X8") + " can't be read (nothing is mapped there).");
                    var ins = PowerPc.Disassemble(data, 0, data.Length, start).Where(x => !holes.Any(h => x.Address >= h.Key && x.Address - h.Key < h.Value)).ToList();
                    // XBDM normally hides its breakpoints in memory reads; look through any that show
                    for (int i = 0; i < ins.Count; i++)
                        if (ins[i].Word == XbdmDebugger.BreakpointTrap && d.Breakpoints.Contains(ins[i].Address)) ins[i] = d.ReadInstruction(ins[i].Address);
                    return ins;
                });
                _viewAddress = start;
                _code.BeginUpdate();
                _code.Items.Clear();
                foreach (var ins in list)
                {
                    var it = new ListViewItem("") { Tag = ins };
                    it.SubItems.Add(ins.Address.ToString("X8"));
                    it.SubItems.Add(ins.Word.ToString("X8"));
                    it.SubItems.Add(ins.Text);
                    it.SubItems.Add(ins.Target.HasValue ? Where(ins.Target.Value) : ins.ToLinkRegister ? "lr" : ins.ToCountRegister ? "ctr" : "");
                    _code.Items.Add(it);
                }
                _code.EndUpdate();
                MarkCode();
                Reveal(address);
            });
        }

        /// <summary>"module+offset" for an address inside a loaded module (when the module list has been read).</summary>
        private string Where(uint address)
        {
            var m = _moduleList.FirstOrDefault(x => address >= x.BaseAddress && address - x.BaseAddress < x.Size);
            return m == null ? address.ToString("X8") : Path.GetFileNameWithoutExtension(m.Name) + "+" + (address - m.BaseAddress).ToString("X");
        }

        private void MarkCode()
        {
            uint pc = _stop?.Address ?? 0;
            foreach (ListViewItem it in _code.Items)
            {
                uint a = ((PpcInstruction)it.Tag).Address;
                bool here = _stop != null && a == pc, bp = _userBreaks.Contains(a);
                it.Text = here && bp ? "●►" : here ? "►" : bp ? "●" : "";
                it.ForeColor = here ? Theme.Accent : bp ? Theme.Danger : Theme.Text;
            }
        }

        private void Reveal(uint address)
        {
            foreach (ListViewItem it in _code.Items)
                if (((PpcInstruction)it.Tag).Address == address)
                {
                    it.Selected = true;
                    int last = Math.Min(_code.Items.Count - 1, it.Index + 14);
                    _code.EnsureVisible(last);
                    _code.EnsureVisible(Math.Max(0, it.Index - 4));
                    it.EnsureVisible();
                    return;
                }
        }

        private void FollowBranch()
        {
            if (_code.SelectedItems.Count == 0) return;
            var ins = (PpcInstruction)_code.SelectedItems[0].Tag;
            uint? to = ins.Target ?? (_stop?.Context == null ? (uint?)null : ins.ToLinkRegister ? _stop.Context.Lr & ~3u : ins.ToCountRegister ? _stop.Context.Ctr & ~3u : (uint?)null);
            if (to == null) { Dlg.Info("Follow", "The selected line is not a branch with a known destination."); return; }
            ShowCode(to.Value);
        }

        // ---------------------------------------------------------------- breakpoints

        private async void ToggleSelectedBreakpoint()
        {
            if (!Ready() || _code.SelectedItems.Count == 0) return;
            uint a = ((PpcInstruction)_code.SelectedItems[0].Tag).Address;
            bool on = !_userBreaks.Contains(a);
            if (on && !await SystemCodeOk(a)) return;
            SetExecuteBreakpoint(a, on);
        }

        private void SetExecuteBreakpoint(uint address, bool on)
        {
            Run(null, (on ? "Setting" : "Removing") + " breakpoint at 0x" + address.ToString("X8"), async () =>
            {
                // remember it first, so a debugger created inside this very call (new connection) does not set it twice
                var text = await Dbg((d, c) =>
                {
                    if (on) d.AddBreakpoint(address); else d.RemoveBreakpoint(address);
                    return on ? d.ReadInstruction(address).Text : "";
                });
                if (on) _userBreaks.Add(address); else _userBreaks.Remove(address);
                Log.Info("Debugger: breakpoint " + (on ? "set at" : "removed from") + " 0x" + address.ToString("X8") + (on ? "  " + text : "") + ".");
                ListBreakpoints();
                MarkCode();
                if (on && _stop == null && !_armed) { _armed = true; _watch.Start(); SetState("Running. Waiting for a breakpoint...", Theme.Success); }
            });
        }

        private async void AddBreakpoint()
        {
            if (!Ready()) return;
            uint address, size = 0;
            try
            {
                address = ParseAddr(_bpAddr.Text);
                if (_bpKind.SelectedIndex != 0)
                {
                    string t = _bpSize.Text.Trim();
                    size = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(t.Substring(2), 16) : uint.Parse(t, CultureInfo.InvariantCulture);
                    if (size != 1 && size != 2 && size != 4 && size != 8) throw new FormatException("A data breakpoint covers 1, 2, 4 or 8 bytes.");
                }
                else if ((address & 3) != 0) throw new FormatException("Instructions sit on 4-byte boundaries.");
            }
            catch (Exception ex) { Dlg.Warn("Add breakpoint", ex.Message); return; }
            if (_bpKind.SelectedIndex == 0) { if (await SystemCodeOk(address)) SetExecuteBreakpoint(address, true); return; }
            var kind = _bpKind.SelectedIndex == 1 ? XboxDataBreakpointType.OnWrite : _bpKind.SelectedIndex == 2 ? XboxDataBreakpointType.OnRead : XboxDataBreakpointType.OnReadWrite;
            if (_dataBreaks.Any(b => b.Address == address && b.Kind == kind)) return;
            Run(null, "Setting data breakpoint", async () =>
            {
                await Dbg((d, c) => { d.Attach(); c.SetDataBreakpoint(kind, address, size); return true; });
                _dataBreaks.Add((kind, address, size));
                Log.Info("Debugger: data breakpoint (" + kind + ") set on 0x" + address.ToString("X8") + ", " + size + " byte(s).");
                ListBreakpoints();
                if (_stop == null && !_armed) { _armed = true; _watch.Start(); SetState("Running. Waiting for a breakpoint...", Theme.Success); }
            });
        }

        private void ListBreakpoints()
        {
            _breaks.BeginUpdate();
            _breaks.Items.Clear();
            foreach (uint a in _userBreaks)
            {
                var it = new ListViewItem("Execute") { Tag = (Address: a, Data: -1) };
                it.SubItems.Add(a.ToString("X8")); it.SubItems.Add("");
                var line = _code.Items.Cast<ListViewItem>().Select(i => (PpcInstruction)i.Tag).FirstOrDefault(i => i.Address == a);
                it.SubItems.Add(line?.Text ?? "");
                _breaks.Items.Add(it);
            }
            for (int i = 0; i < _dataBreaks.Count; i++)
            {
                var b = _dataBreaks[i];
                var it = new ListViewItem(b.Kind == XboxDataBreakpointType.OnWrite ? "Data: write" : b.Kind == XboxDataBreakpointType.OnRead ? "Data: read" : "Data: read / write") { Tag = (Address: b.Address, Data: i) };
                it.SubItems.Add(b.Address.ToString("X8")); it.SubItems.Add(b.Size.ToString()); it.SubItems.Add("");
                _breaks.Items.Add(it);
            }
            _breaks.EndUpdate();
        }

        private void RemoveSelectedBreakpoints()
        {
            if (!Ready() || _breaks.SelectedItems.Count == 0) return;
            var picked = _breaks.SelectedItems.Cast<ListViewItem>().Select(i => ((uint Address, int Data))i.Tag).ToList();
            var data = picked.Where(p => p.Data >= 0).Select(p => _dataBreaks[p.Data]).ToList();
            var code = picked.Where(p => p.Data < 0).Select(p => p.Address).ToList();
            Run(null, "Removing breakpoints", async () =>
            {
                await Dbg((d, c) =>
                {
                    foreach (uint a in code) d.RemoveBreakpoint(a);
                    foreach (var b in data) c.ClearDataBreakpoint(b.Kind, b.Address, b.Size);
                    return true;
                });
                foreach (uint a in code) _userBreaks.Remove(a);
                foreach (var b in data) _dataBreaks.Remove(b);
                ListBreakpoints();
                MarkCode();
            });
        }

        private void RemoveAllBreakpoints()
        {
            if (!Ready() || (_userBreaks.Count == 0 && _dataBreaks.Count == 0)) return;
            var data = _dataBreaks.ToList();
            Run(null, "Removing every breakpoint", async () =>
            {
                await Dbg((d, c) =>
                {
                    d.ClearBreakpoints();
                    foreach (var b in data) c.ClearDataBreakpoint(b.Kind, b.Address, b.Size);
                    return true;
                });
                _userBreaks.Clear(); _dataBreaks.Clear();
                ListBreakpoints();
                MarkCode();
            });
        }

        private void ApplyStopOn()
        {
            if (!Ready()) return;
            var on = (_onModule.Checked ? XboxStopOnFlags.OnModuleLoad : 0) | (_onThread.Checked ? XboxStopOnFlags.OnThreadCreate : 0) |
                     (_onException.Checked ? XboxStopOnFlags.OnFirstChanceException : 0) | (_onDebugString.Checked ? XboxStopOnFlags.OnDebugString : 0);
            var off = (XboxStopOnFlags.OnModuleLoad | XboxStopOnFlags.OnThreadCreate | XboxStopOnFlags.OnFirstChanceException | XboxStopOnFlags.OnDebugString) & ~on;
            Run(null, "Applying stop conditions", async () =>
            {
                await Dbg((d, c) =>
                {
                    if (off != 0) c.NoStopOn(off);
                    if (on != 0) { d.Attach(); c.StopOn(on); }      // a stop needs a registered debugger to report to
                    return true;
                });
                Log.Info("Debugger: the console now also stops on: " + (on == 0 ? "nothing extra" : on.ToString()) + ".");
                if (on != 0 && _stop == null && !_armed) { _armed = true; _watch.Start(); SetState("Running. Waiting for a stop...", Theme.Success); }
            });
        }

        // ---------------------------------------------------------------- threads

        private void LoadThreads()
        {
            if (!Ready()) return;
            Run(null, "Reading threads", async () =>
            {
                var rows = await Dbg((d, c) =>
                {
                    var list = new List<(XboxThreadInfo Info, string Reason, uint Pc)>();
                    foreach (uint id in c.GetThreadIds() ?? new uint[0])
                    {
                        var info = c.GetThreadInfo(id) ?? new XboxThreadInfo { ThreadId = id };
                        info.ThreadId = id;
                        string reason = c.GetThreadStopReason(id);
                        uint pc = reason != null || d.IsFrozen ? c.GetThreadContext(id, XboxContextFlags.Control).Iar : 0;
                        list.Add((info, reason, pc));
                    }
                    return (List: list, Frozen: d.IsFrozen);
                });
                _threads.BeginUpdate();
                _threads.Items.Clear();
                foreach (var r in rows.List)
                {
                    var it = new ListViewItem("0x" + r.Info.ThreadId.ToString("X8")) { Tag = r.Info.ThreadId };
                    it.SubItems.Add(r.Reason ?? (rows.Frozen ? "frozen" : "running"));
                    it.SubItems.Add(r.Pc != 0 ? r.Pc.ToString("X8") : "");
                    it.SubItems.Add(r.Info.StartAddress.ToString("X8"));
                    it.SubItems.Add(r.Info.Priority.ToString());
                    it.SubItems.Add(r.Info.Suspend.ToString());
                    it.SubItems.Add(r.Info.Limit.ToString("X8") + " - " + r.Info.Base.ToString("X8"));
                    _threads.Items.Add(it);
                }
                _threads.EndUpdate();
                FillThreads(rows.List.Select(r => r.Info.ThreadId).ToArray(), CurrentThread);
            });
        }

        private void UseSelectedThread()
        {
            if (_threads.SelectedItems.Count == 0) return;
            uint id = (uint)_threads.SelectedItems[0].Tag;
            ShowSection(_execTab);
            Run(null, "Reading thread", async () =>
            {
                var result = await Dbg((d, c) => (Held: d.IsFrozen || c.GetThreadStopReason(id) != null, Snap: d.Snapshot(id)));
                if (_thread.Items.Cast<ThreadItem>().All(t => t.Id != id)) _thread.Items.Add(new ThreadItem { Id = id, Label = "0x" + id.ToString("X8") });
                if (result.Held) Show(result.Snap, "Showing");
                else { _filling = true; _thread.SelectedItem = _thread.Items.Cast<ThreadItem>().First(t => t.Id == id); _filling = false; SetState("Thread 0x" + id.ToString("X8") + " is running - press Break to inspect it.", Theme.TextDim); }
            });
        }

        private void ThreadCommand(string what, Func<XboxConsole, uint, bool> command)
        {
            if (!Ready() || _threads.SelectedItems.Count == 0) return;
            uint id = (uint)_threads.SelectedItems[0].Tag;
            Run(null, what, async () =>
            {
                bool ok = await Dbg((d, c) => command(c, id));
                if (!ok) throw new InvalidOperationException("The console refused (the thread may have ended).");
                LoadThreads();
            });
        }

        // ---------------------------------------------------------------- modules

        private ModuleInfo SelectedModule() => _modules.SelectedItems.Count == 0 ? null : (ModuleInfo)_modules.SelectedItems[0].Tag;

        private void LoadModules()
        {
            if (!Ready()) return;
            Run(null, "Reading modules", async () =>
            {
                var list = await Dbg((d, c) => c.GetModules() ?? new List<ModuleInfo>());
                _moduleList.Clear(); _moduleList.AddRange(list);
                _modules.BeginUpdate();
                _modules.Items.Clear();
                foreach (var m in list.OrderBy(m => m.BaseAddress))
                {
                    var it = new ListViewItem(m.Name) { Tag = m };
                    it.SubItems.Add(m.BaseAddress.ToString("X8"));
                    it.SubItems.Add(m.Size.ToString("X"));
                    it.SubItems.Add(m.TimeStamp.Year > 1990 ? m.TimeStamp.ToString("yyyy-MM-dd HH:mm") : "");
                    _modules.Items.Add(it);
                }
                _modules.EndUpdate();
                _sections.Items.Clear();
            });
        }

        private void LoadSections()
        {
            var m = SelectedModule();
            if (m == null || !ConsoleService.IsConnected || Status.IsBusy) return;
            Run(null, "Reading sections of " + m.Name, async () =>
            {
                var list = await Dbg((d, c) => c.GetModuleSections(m.Name) ?? new List<ModuleSection>());
                _sections.BeginUpdate();
                _sections.Items.Clear();
                foreach (var s in list)
                {
                    var it = new ListViewItem(s.Name) { Tag = s.Base };
                    it.SubItems.Add(s.Base.ToString("X8"));
                    it.SubItems.Add(s.Size.ToString("X"));
                    it.SubItems.Add(s.Flags.ToString("X"));
                    _sections.Items.Add(it);
                }
                _sections.EndUpdate();
            });
        }

        private void DumpModule()
        {
            var m = SelectedModule();
            if (!Ready() || m == null) { if (m == null) Dlg.Info("Dump module", "Select a module first."); return; }
            string path = PickSave("Dump " + m.Name, Path.GetFileNameWithoutExtension(m.Name) + "_" + m.BaseAddress.ToString("X8") + ".bin", "Memory dump (*.bin)|*.bin|All files (*.*)|*.*");
            if (path == null) return;
            Run(null, "Dumping " + m.Name, async () =>
            {
                // read in pieces: a module can be tens of megabytes, and unmapped pages inside it must not lose the rest
                int bad = await Dbg((d, c) =>
                {
                    int failed = 0;
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        for (uint off = 0; off < m.Size; off += 0x10000)
                        {
                            uint n = Math.Min(0x10000, m.Size - off);
                            var holes = new List<KeyValuePair<uint, uint>>();
                            byte[] part = c.GetMemoryWithGaps(m.BaseAddress + off, n, holes);
                            failed += (int)holes.Sum(h => (long)h.Value);
                            fs.Write(part, 0, part.Length);
                        }
                    return failed;
                });
                if (bad == 0) Log.Success("Dumped " + m.Name + " (" + m.Size.ToString("N0") + " bytes of memory from 0x" + m.BaseAddress.ToString("X8") + ") to " + path);
                else Log.Warn("Dumped " + m.Name + " to " + path + " - " + bad.ToString("N0") + " byte(s) are not mapped on the console and are zero in the file.");
            });
        }

        // ---------------------------------------------------------------- trace

        private async void StartTrace()
        {
            if (!RequireStop()) return;
            int count;
            if (!int.TryParse(_traceCount.Text.Trim(), out count) || count < 1 || count > 100000) { Dlg.Warn("Trace", "Trace between 1 and 100000 instructions."); return; }
            if (!await SystemCodeOk(_stop.Address)) return;
            uint id = CurrentThread, start = _stop.Address;
            var kind = _traceKind.SelectedIndex == 0 ? StepKind.Into : StepKind.Over;
            _traceCancel?.Dispose();
            _traceCancel = new CancellationTokenSource();
            var ct = _traceCancel.Token;
            _traceLines.Clear();
            _trace.Items.Clear();
            Run(null, "Tracing " + count + " instructions", async () =>
            {
                var pending = new List<TraceLine>();
                DebugStop end = null;
                try
                {
                    end = await Dbg((d, c) =>
                    {
                        // a trace that starts in the game's code ends where a step would leave it (system code can freeze
                        // the console); one started in system code was asked about above
                        var title = TitleModule(c);
                        Func<uint, bool> may = Inside(title, start) ? (a => Inside(title, a)) : (Func<uint, bool>)null;
                        try { d.Trace(id, count, kind, line => { lock (pending) pending.Add(line); }, ct, may); }
                        catch (OperationCanceledException) { }
                        return d.Snapshot(id);
                    });
                }
                finally
                {
                    lock (pending) _traceLines.AddRange(pending);
                    _trace.BeginUpdate();
                    for (int i = 0; i < _traceLines.Count; i++)
                    {
                        var l = _traceLines[i];
                        var it = new ListViewItem((i + 1).ToString());
                        it.SubItems.Add(l.Instruction.Address.ToString("X8"));
                        it.SubItems.Add(l.Instruction.Text);
                        it.SubItems.Add(l.Changes);
                        _trace.Items.Add(it);
                    }
                    _trace.EndUpdate();
                }
                Log.Info("Debugger: traced " + _traceLines.Count + " instruction(s) of thread 0x" + id.ToString("X8") + ".");
                if (end != null) Show(end, "Traced");
            });
        }

        private void SaveTrace()
        {
            if (_traceLines.Count == 0) { Dlg.Info("Trace", "There is no trace yet."); return; }
            string path = PickSave("Save trace", "trace.txt", "Text (*.txt)|*.txt");
            if (path == null) return;
            var sb = new StringBuilder();
            foreach (var l in _traceLines)
                sb.Append(l.Instruction.Address.ToString("X8")).Append("  ").Append(l.Instruction.Word.ToString("X8")).Append("  ").Append(l.Instruction.Text.PadRight(40)).Append(' ').AppendLine(l.Changes);
            try { File.WriteAllText(path, sb.ToString()); Log.Success("Trace saved to " + path); }
            catch (Exception ex) { Dlg.Error("Save trace", ex.Message); }
        }
    }
}
