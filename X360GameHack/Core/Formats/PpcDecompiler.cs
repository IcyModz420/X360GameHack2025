using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>A set of register names as bits (there are more than 64 of them).</summary>
    internal struct RegSet
    {
        public ulong A, B;
        public static RegSet Bit(int i) => i < 64 ? new RegSet { A = 1UL << i } : new RegSet { B = 1UL << (i - 64) };
        public static RegSet operator |(RegSet x, RegSet y) => new RegSet { A = x.A | y.A, B = x.B | y.B };
        public static RegSet operator &(RegSet x, RegSet y) => new RegSet { A = x.A & y.A, B = x.B & y.B };
        public static RegSet operator ~(RegSet x) => new RegSet { A = ~x.A, B = ~x.B };
        public bool Any => A != 0 || B != 0;
        public bool Same(RegSet o) => A == o.A && B == o.B;
    }

    public sealed class PpcStatement
    {
        public PS S;
        public uint Address;
        public PpcStatement(PS s, uint address) { S = s; Address = address; }
    }

    /// <summary>One step of a printed listing: "statement", "if" (taken when Cond, negated when Negate), "else", "end", "do", "while" (repeats when Cond), "whiledo" (enters while Cond) / "forever" ... "endwhile", "breakunless" (leaves the loop unless Cond) or "label".</summary>
    public sealed class PpcOp
    {
        public string Kind;
        public PX Cond;
        public bool Negate;
        public PpcStatement Statement;
        public uint Address;
    }

    public sealed class PpcBlock
    {
        public uint Address, End;
        /// <summary>What the instructions do, one after another (the checked translation).</summary>
        public List<PpcStatement> Original = new List<PpcStatement>();
        /// <summary>The same after folding values into the places that use them.</summary>
        public List<PpcStatement> Statements = new List<PpcStatement>();
        /// <summary>Registers whose values are still needed after this block.</summary>
        public HashSet<string> LiveOut = new HashSet<string>();
        public bool FallsThrough;
        internal List<int> Preds = new List<int>();
        internal int[] Succ = new int[0];
        internal RegSet LiveMask;
    }

    /// <summary>
    /// Turns one PowerPC function into C-like code. Instructions are translated by <see cref="PpcLifter"/> (checked
    /// against Ghidra's emulator), then values are folded forward into the statements that use them when that
    /// cannot change the result: the value's inputs and memory are untouched in between and the register is not
    /// needed afterwards. "Needed afterwards" follows the Xbox 360 calling convention: a call reads r1-r10, r12, r13
    /// and overwrites r0, r3-r12, ctr, lr, carry and cr0/1/5/6/7; a return keeps r1-r4, r13-r31 and lr.
    /// Control flow becomes if / else / do-while where the jumps form exactly that shape, goto otherwise.
    /// </summary>
    public sealed class PpcFunction
    {
        public uint Start, End;
        public List<PpcBlock> Blocks { get; } = new List<PpcBlock>();
        public int Instructions { get; private set; }

        private static string[] Range(string prefix, int from, int count) => Enumerable.Range(from, count).Select(i => prefix + i).ToArray();
        private static readonly string[] All = Range("r", 0, 32).Concat(Range("cr", 0, 8)).Concat(new[] { "ctr", "lr", "ca" }).Concat(Range("f", 0, 32)).ToArray();   // "tmp" / "tmp2" live inside one instruction only
        // the Xbox 360 calling convention: integer arguments r3-r10, floating-point arguments f1-f13, results in r3 / f1;
        // r14-r31 and f14-f31 belong to the caller and are restored before returning
        private static readonly string[] ExitUses = new[] { "r1", "r2", "r3", "r4", "r13", "lr", "f1" }.Concat(Range("r", 14, 18)).Concat(Range("f", 14, 18)).ToArray();
        private static readonly string[] TailUses = ExitUses.Concat(new[] { "r5", "r6", "r7", "r8", "r9", "r10", "r12" }).Concat(Range("f", 1, 13)).Distinct().ToArray();
        private static readonly string[] CallUses = new[] { "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r12", "r13" }.Concat(Range("f", 1, 13)).ToArray();
        private static readonly string[] CallDefs = new[] { "r0", "r3", "r4", "r5", "r6", "r7", "r8", "r9", "r10", "r11", "r12", "ctr", "lr", "ca", "cr0", "cr1", "cr5", "cr6", "cr7" }.Concat(Range("f", 0, 14)).ToArray();

        /// <summary>Decompiles the code in [start, end). <paramref name="readWord"/> returns null outside the image.</summary>
        public static PpcFunction Build(Func<uint, uint?> readWord, uint start, uint end)
        {
            var f = new PpcFunction { Start = start, End = end };
            var lifted = new List<KeyValuePair<uint, List<PS>>>();
            for (uint a = start; a < end; a += 4)
            {
                uint? w = readWord(a);
                if (w == null) { f.End = end = a; break; }
                lifted.Add(new KeyValuePair<uint, List<PS>>(a, PpcLifter.Lift(PowerPc.Decode(w.Value, a))));
            }
            f.Instructions = lifted.Count;
            f.FindJumpTables(lifted, readWord);

            var leaders = new HashSet<uint> { start };
            foreach (var kv in lifted)
            {
                var last = kv.Value.Count > 0 ? kv.Value[kv.Value.Count - 1] : null;
                if (last is PJump j) { if (j.Target >= start && j.Target < end) leaders.Add(j.Target); leaders.Add(kv.Key + 4); }
                else if (last is PReturn || last is PIndirectJump)
                {
                    leaders.Add(kv.Key + 4);
                    if (last is PIndirectJump table && table.Targets != null)
                    {
                        foreach (uint target in table.Targets) leaders.Add(target);
                        leaders.Add(table.Table); leaders.Add(table.Table + (uint)table.Targets.Length * 4);
                    }
                }
            }
            PpcBlock block = null;
            foreach (var kv in lifted)
            {
                if (block == null || leaders.Contains(kv.Key)) { block = new PpcBlock { Address = kv.Key }; f.Blocks.Add(block); }
                foreach (var s in kv.Value) block.Original.Add(new PpcStatement(s, kv.Key));
                block.End = kv.Key + 4;
            }
            foreach (var b in f.Blocks)
            {
                var last = b.Original.Count > 0 ? b.Original[b.Original.Count - 1].S : null;
                b.FallsThrough = !((last is PJump j && j.Cond == null) || (last is PReturn r && r.Cond == null) || (last is PIndirectJump ij && ij.Cond == null));
                b.Statements = b.Original.Select(s => new PpcStatement(s.S, s.Address)).ToList();
            }
            f.LinkBlocks();
            for (int pass = 0; pass < 12; pass++)
            {
                f.Liveness();
                bool changed = false;
                foreach (var b in f.Blocks) changed |= f.Fold(b);
                if (!changed) break;
            }
            f.Liveness();
            foreach (var b in f.Blocks) { f.AnnotateCalls(b); f.AnnotateSwitch(b); }
            f.Liveness();
            return f;
        }

        private static readonly string[] IntArgs = Range("r", 3, 8), FloatArgs = Range("f", 1, 13);

        /// <summary>
        /// Gives each call its argument list: the run of "argument register = value" statements right before it moves
        /// into the call (a call destroys those registers anyway, so nothing else can depend on them). Lower
        /// registers that were not set there are passed as they are. This follows the calling convention; how many
        /// arguments the function really takes is not known, so it is a best reading. The result register is named
        /// when something reads it afterwards.
        /// </summary>
        private void AnnotateCalls(PpcBlock block)
        {
            var L = block.Statements;
            var after = new RegSet[L.Count];
            RegSet live = block.LiveMask;
            for (int k = L.Count - 1; k >= 0; k--)
            {
                after[k] = live;
                var m = Masks(L[k].S);
                live = (live & ~m[1]) | m[0];
            }
            for (int j = L.Count - 1; j >= 0; j--)
            {
                var call = L[j].S as PCall;
                if (call == null || call.Args != null) continue;
                var values = new Dictionary<string, PX>();
                var remove = new List<int>();
                var readLater = new List<string>();                 // registers the values collected so far read
                for (int k = j - 1; k >= 0; k--)
                {
                    var a = L[k].S as PAssign;
                    if (a == null) break;
                    var inputs = new List<string>();
                    Vars(a.Value, inputs);
                    if (a.Dest == "ctr" && call.Indirect != null && !remove.Contains(k))
                    {
                        // the target of an indirect call, set between the arguments and the call: stays where it is
                        if (inputs.Any(x => IntArgs.Contains(x) || FloatArgs.Contains(x))) break;
                        continue;
                    }
                    if (!(IntArgs.Contains(a.Dest) || FloatArgs.Contains(a.Dest)) || values.ContainsKey(a.Dest)) break;
                    if (readLater.Contains(a.Dest)) break;                           // a later value reads this register
                    if (inputs.Any(x => values.ContainsKey(x))) break;               // this value reads a register that is set again later
                    values[a.Dest] = a.Value;
                    remove.Add(k);
                    readLater.AddRange(inputs);
                }
                // the result: f1 when the code that follows reads f1 before anything else sets it, else r3 when r3 is still needed
                string result = null;
                for (int k = j + 1; k < L.Count && result == null; k++)
                {
                    if (Uses(L[k].S, true).Contains("f1")) result = "f1";
                    else if (Uses(L[k].S, true).Contains("r3")) result = "r3";
                    else if ((Masks(L[k].S)[1] & (Bit("f1") | Bit("r3"))).Any || L[k].S is PAsm) break;
                }
                if (result == null && (after[j] & Bit("r3")).Any) result = "r3";
                var args = new List<PX>();
                int ints = 0, floats = 0;
                if (values.Count > 0)
                {
                    ints = IntArgs.Select((r, i) => values.ContainsKey(r) ? i + 1 : 0).Max(); floats = FloatArgs.Select((r, i) => values.ContainsKey(r) ? i + 1 : 0).Max();
                    for (int i = 0; i < ints; i++) args.Add(values.TryGetValue(IntArgs[i], out PX v) ? v : new PReg(IntArgs[i]));
                    for (int i = 0; i < floats; i++) args.Add(values.TryGetValue(FloatArgs[i], out PX v) ? v : new PReg(FloatArgs[i]));
                }
                L[j] = new PpcStatement(new PCall { Target = call.Target, Indirect = call.Indirect, Args = args.ToArray(), Result = result, IntArgs = ints, FloatArgs = floats }, L[j].Address);
                foreach (int k in remove.OrderByDescending(x => x)) L.RemoveAt(k);
                j -= remove.Count;
            }
        }

        private int IndexOf(uint address)
        {
            int lo = 0, hi = Blocks.Count - 1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (Blocks[mid].Address == address) return mid; if (Blocks[mid].Address < address) lo = mid + 1; else hi = mid - 1; }
            return -1;
        }

        private IEnumerable<int> Successors(int i)
        {
            var b = Blocks[i];
            if (b.FallsThrough && i + 1 < Blocks.Count) yield return i + 1;
            var last = b.Original.Count > 0 ? b.Original[b.Original.Count - 1].S as PJump : null;
            if (last != null) { int t = IndexOf(last.Target); if (t >= 0) yield return t; }
            var table = b.Original.Count > 0 ? b.Original[b.Original.Count - 1].S as PIndirectJump : null;
            if (table != null && table.Targets != null)
                foreach (uint target in table.Targets.Distinct()) { int t = IndexOf(target); if (t >= 0) yield return t; }
        }

        // ---------------------------------------------------------------- jump tables

        private static PX Substitute(PX e, Dictionary<string, PX> known)
        {
            if (e is PReg r) return known.TryGetValue(r.Name, out PX v) ? v : e;
            if (e is PBin b) return new PBin(b.Op, Substitute(b.A, known), Substitute(b.B, known));
            if (e is PUn u) return new PUn(u.Op, Substitute(u.A, known));
            if (e is PLoad l) return new PLoad(l.Size, Substitute(l.Addr, known));
            if (e is PFn f) return new PFn(f.Name, f.Args.Select(x => Substitute(x, known)).ToArray());
            return e;
        }

        private static void Terms(PX e, List<PX> into)
        {
            if (e is PBin b && b.Op == "+") { Terms(b.A, into); Terms(b.B, into); } else into.Add(e);
        }

        /// <summary>The constant in "table + something", when the expression is a 4-byte read from such an address.</summary>
        private static bool TableRead(PX value, out uint table, out PX rest)
        {
            table = 0; rest = null;
            var load = value as PLoad;
            if (load == null || load.Size != 4) return false;
            var terms = new List<PX>();
            Terms(load.Addr, terms);
            var constants = terms.OfType<PConst>().ToList();
            if (constants.Count != 1 || terms.Count != 2) return false;
            table = (uint)constants[0].V;
            rest = terms.First(x => !(x is PConst));
            return true;
        }

        /// <summary>
        /// A compiler "switch": the count register is loaded from table[index * 4] and jumped through, and the table
        /// of code addresses usually sits in the function itself. Finding it gives the jump its real targets and
        /// keeps the table's bytes from being read as instructions.
        /// </summary>
        private void FindJumpTables(List<KeyValuePair<uint, List<PS>>> lifted, Func<uint, uint?> readWord)
        {
            for (int at = 0; at < lifted.Count; at++)
            {
                var statements = lifted[at].Value;
                var jump = statements.Count > 0 ? statements[statements.Count - 1] as PIndirectJump : null;
                if (jump == null || jump.Cond != null) continue;
                // what the count register holds, followed back through the last few instructions
                var known = new Dictionary<string, PX>();
                for (int k = Math.Max(0, at - 12); k < at; k++)
                    foreach (var st in lifted[k].Value)
                    {
                        if (st is PAssign a) known[a.Dest] = Simplify(Substitute(a.Value, known));
                        else if (!(st is PStore || st is PCompare)) known.Clear();
                    }
                if (!known.TryGetValue("ctr", out PX value) || !TableRead(value, out uint table, out PX rest) || (table & 3) != 0) continue;
                var targets = new List<uint>();
                for (int i = 0; i < 2048; i++)
                {
                    uint entryAt = table + (uint)i * 4;
                    if (targets.Contains(entryAt)) break;                       // a table inside the function ends where one of its own targets begins
                    uint? word = readWord(entryAt);
                    if (word == null || (word.Value & 3) != 0 || word.Value < Start || word.Value >= End) break;
                    if (word.Value >= table && word.Value < entryAt + 4) break; // an address inside the table itself is not code
                    targets.Add(word.Value);
                }
                if (targets.Count < 2) continue;
                statements[statements.Count - 1] = new PIndirectJump { Table = table, Targets = targets.ToArray() };
                // the table's own words are data
                for (int k = 0; k < lifted.Count; k++)
                    if (lifted[k].Key >= table && lifted[k].Key < table + (uint)targets.Count * 4) lifted[k].Value.Clear();
            }
        }

        /// <summary>Names the value a jump table is indexed with, when the table read sits right before the jump.</summary>
        private void AnnotateSwitch(PpcBlock block)
        {
            var L = block.Statements;
            if (L.Count < 2) return;
            var jump = L[L.Count - 1].S as PIndirectJump;
            var load = L[L.Count - 2].S as PAssign;
            if (jump == null || jump.Targets == null || jump.Index != null || load == null || load.Dest != "ctr") return;
            // the table's address is often still in a register that was loaded with it earlier in the block
            var constants = new Dictionary<string, PX>();
            for (int k = 0; k < L.Count - 2; k++)
            {
                if (L[k].S is PAssign a) { if (a.Value is PConst) constants[a.Dest] = a.Value; else constants.Remove(a.Dest); }
                else if (L[k].S is PCall || L[k].S is PAsm) constants.Clear();
            }
            if (!TableRead(Simplify(Substitute(load.Value, constants)), out uint table, out PX rest) || table != jump.Table) return;
            // index * 4, in the forms compilers write it
            PX index;
            if (rest is PUn cast && cast.Op == "u32") rest = cast.A;
            if (rest is PBin shift && shift.Op == "<<" && shift.B is PConst two && two.V == 2) index = shift.A;
            else if (rest is PBin mask && mask.Op == "&" && mask.B is PConst m && m.V == 0xFFFFFFFC && mask.A is PBin rot && rot.Op == "rotl32" && rot.B is PConst by && by.V == 2) index = rot.A;
            else index = new PBin("/u", rest, new PConst(4));
            L[L.Count - 1] = new PpcStatement(new PIndirectJump { Table = jump.Table, Targets = jump.Targets, Index = index }, L[L.Count - 1].Address);
        }

        private void LinkBlocks()
        {
            for (int i = 0; i < Blocks.Count; i++)
            {
                Blocks[i].Succ = Successors(i).ToArray();
                foreach (int s in Blocks[i].Succ) Blocks[s].Preds.Add(i);
            }
        }

        // ---------------------------------------------------------------- uses and definitions

        private static string VarOf(string reg) => reg.StartsWith("cr") && reg.Length > 3 && reg[3] == '.' ? reg.Substring(0, 3) : reg;

        private static void Vars(PX e, List<string> into)
        {
            if (e is PReg r) into.Add(VarOf(r.Name));
            else if (e is PBin b) { Vars(b.A, into); Vars(b.B, into); }
            else if (e is PUn u) Vars(u.A, into);
            else if (e is PLoad l) Vars(l.Addr, into);
            else if (e is PFn f) foreach (var x in f.Args) Vars(x, into);
        }

        private bool Inside(uint target) => target >= Start && target < End;

        /// <summary>Registers a statement reads; <paramref name="explicitOnly"/> leaves out what calls and exits read by convention.</summary>
        private List<string> Uses(PS s, bool explicitOnly = false)
        {
            var u = new List<string>();
            if (s is PAssign a) Vars(a.Value, u);
            else if (s is PStore st) { Vars(st.Addr, u); Vars(st.Value, u); }
            else if (s is PCompare c) { Vars(c.A, u); Vars(c.B, u); }
            else if (s is PJump j) { if (j.Cond != null) Vars(j.Cond, u); if (!explicitOnly && !Inside(j.Target)) u.AddRange(TailUses); }
            else if (s is PReturn r) { if (r.Cond != null) Vars(r.Cond, u); if (!explicitOnly) u.AddRange(ExitUses); }
            else if (s is PIndirectJump ij)
            {
                if (ij.Cond != null) Vars(ij.Cond, u);
                if (ij.Index != null) Vars(ij.Index, u);
                // a jump with known targets only needs the count register; an unknown one may be a tail call that needs everything
                if (!explicitOnly) { if (ij.Targets != null) u.Add("ctr"); else u.AddRange(All); }
            }
            else if (s is PCall call)
            {
                if (call.Args != null) foreach (var x in call.Args) Vars(x, u);
                if (!explicitOnly)
                {
                    // by convention a call may read every argument register; the ones its argument list already covers are not read again
                    foreach (string reg in CallUses)
                    {
                        int ai = Array.IndexOf(IntArgs, reg), fi = Array.IndexOf(FloatArgs, reg);
                        if ((ai >= 0 && ai < call.IntArgs) || (fi >= 0 && fi < call.FloatArgs)) continue;
                        u.Add(reg);
                    }
                    if (call.Indirect != null) u.Add("ctr");
                }
            }
            else if (!explicitOnly) u.AddRange(All);                 // assembly: may read anything
            return u;
        }

        private static IEnumerable<string> Defs(PS s)
        {
            if (s is PAssign a) return new[] { a.Dest };
            if (s is PCompare c) return new[] { "cr" + c.Field };
            if (s is PCall) return CallDefs;
            return new string[0];
        }

        // register sets as bit masks
        private static readonly string[] Names = All.Concat(new[] { "tmp", "tmp2" }).ToArray();
        private static readonly Dictionary<string, int> VarIndex = Names.Select((name, i) => new { name, i }).ToDictionary(x => x.name, x => x.i);
        private static RegSet Bit(string v) => RegSet.Bit(VarIndex[v]);
        private static RegSet MaskOf(IEnumerable<string> vars) { var m = new RegSet(); foreach (string v in vars) m |= Bit(v); return m; }
        private static readonly RegSet AllMask = MaskOf(All);
        private readonly Dictionary<PS, RegSet[]> _masks = new Dictionary<PS, RegSet[]>();

        /// <summary>[0] what the statement reads, [1] what it certainly overwrites.</summary>
        private RegSet[] Masks(PS s)
        {
            if (!_masks.TryGetValue(s, out var m)) _masks[s] = m = new[] { MaskOf(Uses(s)), s is PAsm ? new RegSet() : MaskOf(Defs(s)) };
            return m;
        }

        private void Liveness()
        {
            int n = Blocks.Count;
            var use = new RegSet[n]; var def = new RegSet[n]; var liveIn = new RegSet[n]; var liveOut = new RegSet[n];
            for (int i = 0; i < n; i++)
            {
                foreach (var st in Blocks[i].Statements)
                {
                    var m = Masks(st.S);
                    use[i] |= m[0] & ~def[i];
                    def[i] |= m[1];
                }
                // running off the end of the listed range (no return seen): everything may still be needed
                if (Blocks[i].FallsThrough && i + 1 >= n) liveOut[i] = AllMask;
            }
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = n - 1; i >= 0; i--)
                {
                    RegSet o = liveOut[i];
                    foreach (int s in Blocks[i].Succ) o |= liveIn[s];
                    RegSet into = use[i] | (o & ~def[i]);
                    if (!o.Same(liveOut[i]) || !into.Same(liveIn[i])) { liveOut[i] = o; liveIn[i] = into; changed = true; }
                }
            }
            for (int i = 0; i < n; i++)
            {
                Blocks[i].LiveMask = liveOut[i];
                Blocks[i].LiveOut = new HashSet<string>(Names.Where(name => (liveOut[i] & Bit(name)).Any));
            }
        }

        // ---------------------------------------------------------------- folding values forward

        private static bool HasLoad(PX e) => e is PLoad || (e is PBin b && (HasLoad(b.A) || HasLoad(b.B))) || (e is PUn u && HasLoad(u.A)) || (e is PFn f && f.Args.Any(HasLoad));
        private static int Size(PX e) => e is PBin b ? 1 + Size(b.A) + Size(b.B) : e is PUn u ? 1 + Size(u.A) : e is PLoad l ? 1 + Size(l.Addr) : e is PFn f ? 1 + f.Args.Sum(Size) : 1;
        private static int Size(PS s) => s is PAssign a ? Size(a.Value) : s is PStore st ? Size(st.Addr) + Size(st.Value) : s is PCompare c ? Size(c.A) + Size(c.B)
                                       : s is PJump j && j.Cond != null ? Size(j.Cond) : s is PReturn r && r.Cond != null ? Size(r.Cond) : s is PIndirectJump ij && ij.Cond != null ? Size(ij.Cond) : 0;

        private static int Count(PX e, string v) =>
            e is PReg r ? (VarOf(r.Name) == v ? 1 : 0) : e is PBin b ? Count(b.A, v) + Count(b.B, v) : e is PUn u ? Count(u.A, v) : e is PLoad l ? Count(l.Addr, v) : e is PFn f ? f.Args.Sum(x => Count(x, v)) : 0;

        private static PX Replace(PX e, string v, Func<PReg, PX> with)
        {
            if (e is PReg r) return VarOf(r.Name) == v ? with(r) : e;
            if (e is PBin b) return new PBin(b.Op, Replace(b.A, v, with), Replace(b.B, v, with));
            if (e is PUn u) return new PUn(u.Op, Replace(u.A, v, with));
            if (e is PLoad l) return new PLoad(l.Size, Replace(l.Addr, v, with));
            if (e is PFn f) return new PFn(f.Name, f.Args.Select(x => Replace(x, v, with)).ToArray());
            return e;
        }

        private static PS Replace(PS s, string v, Func<PReg, PX> with)
        {
            Func<PX, PX> x = e => e == null ? null : Simplify(Replace(e, v, with));
            if (s is PAssign a) return new PAssign { Dest = a.Dest, Value = x(a.Value) };
            if (s is PStore st) return new PStore { Size = st.Size, Addr = x(st.Addr), Value = x(st.Value) };
            if (s is PCompare c) return new PCompare { Field = c.Field, A = x(c.A), B = x(c.B), Signed = c.Signed, Float = c.Float };
            if (s is PJump j) return new PJump { Cond = x(j.Cond), Target = j.Target };
            if (s is PReturn r) return new PReturn { Cond = x(r.Cond) };
            if (s is PIndirectJump ij) return new PIndirectJump { Cond = x(ij.Cond) };
            return s;
        }

        private static readonly PpcState Folder = new PpcState();

        /// <summary>Rewrites that keep the 64-bit value exactly: constant folding and removing operations that do nothing.</summary>
        public static PX Simplify(PX e)
        {
            if (e is PUn u)
            {
                PX a = Simplify(u.A);
                if (a is PConst && !u.Op.StartsWith("f") && u.Op != "d2f") lock (Folder) return new PConst(Folder.Eval(new PUn(u.Op, a)));
                if (a is PUn inner && inner.Op == u.Op && (u.Op == "u32" || u.Op == "s32" || u.Op == "u16" || u.Op == "s16" || u.Op == "u8" || u.Op == "s8")) return a;
                if (u.Op == "!" && a is PUn not && not.Op == "!" && IsBool(not.A)) return not.A;
                if (u.Op == "u32" && a is PLoad l && l.Size <= 4) return a;
                if (u.Op == "u32" && a is PBin m && m.Op == "&" && m.B is PConst mc && mc.V <= 0xFFFFFFFF) return a;
                return new PUn(u.Op, a);
            }
            if (e is PBin b)
            {
                PX x = Simplify(b.A), y = Simplify(b.B);
                bool pure = b.Op != "/u" && b.Op != "/s" && !b.Op.StartsWith("f");
                if (x is PConst && y is PConst && pure) lock (Folder) return new PConst(Folder.Eval(new PBin(b.Op, x, y)));
                if (y is PConst c)
                {
                    if (c.V == 0 && (b.Op == "+" || b.Op == "-" || b.Op == "|" || b.Op == "^" || b.Op == "<<" || b.Op == ">>" || b.Op == ">>s")) return x;
                    if (c.V == ulong.MaxValue && b.Op == "&") return x;
                    if (b.Op == "+" && x is PBin inner && inner.Op == "+" && inner.B is PConst ic) return Simplify(new PBin("+", inner.A, new PConst(ic.V + c.V)));
                }
                return new PBin(b.Op, x, y);
            }
            if (e is PLoad ld) return new PLoad(ld.Size, Simplify(ld.Addr));
            if (e is PFn fn) return new PFn(fn.Name, fn.Args.Select(Simplify).ToArray());
            return e;
        }

        private static bool IsBool(PX e) =>
            (e is PBin b && (b.Op == "==" || b.Op == "!=" || b.Op == "<s" || b.Op == ">s" || b.Op == "<u" || b.Op == ">u" || b.Op == "<=u" || b.Op == "f<" || b.Op == "f>" || b.Op == "f==")) ||
            (e is PUn u && u.Op == "!") || (e is PReg r && r.Name.StartsWith("cr") && r.Name.Length > 3);

        private bool Fold(PpcBlock block)
        {
            bool any = false;
            var L = block.Statements;
            int resume = 0;
            restart:
            // what is still needed after each statement
            var after = new RegSet[L.Count];
            RegSet live = block.LiveMask;
            for (int k = L.Count - 1; k >= 0; k--)
            {
                after[k] = live;
                var m = Masks(L[k].S);
                live = (live & ~m[1]) | m[0];
            }
            for (int i = resume; i < L.Count; i++)
            {
                var s = L[i].S;
                string v; PX value = null; PCompare compare = s as PCompare;
                if (s is PAssign asg) { v = asg.Dest; value = asg.Value; }
                else if (compare != null) v = "cr" + compare.Field;
                else continue;

                RegSet bit = Bit(v);
                if (!(after[i] & bit).Any) { L.RemoveAt(i); any = true; resume = Math.Max(0, i - 1); goto restart; }        // never read: a value nobody needs

                var inputs = new List<string>();
                if (value != null) Vars(value, inputs); else { Vars(compare.A, inputs); Vars(compare.B, inputs); }
                bool loads = value != null ? HasLoad(value) : HasLoad(compare.A) || HasLoad(compare.B);
                RegSet inputMask = MaskOf(inputs);
                int j = -1;
                for (int k = i + 1; k < L.Count; k++)
                {
                    var t = L[k].S;
                    var m = Masks(t);
                    if ((m[0] & bit).Any) { j = k; break; }
                    if (t is PAsm || (loads && (t is PStore || t is PCall))) break;
                    if ((m[1] & (bit | inputMask)).Any) break;
                }
                if (j < 0) continue;
                var target = L[j].S;
                if (!Uses(target, true).Contains(v)) continue;                                  // read by convention (a call's argument), not by an expression
                if (Uses(target).Count(x => x == v) != Uses(target, true).Count(x => x == v)) continue;
                if (!((Masks(target)[1] & bit).Any || !(after[j] & bit).Any)) continue;        // needed again later: keep the register
                int occurrences = Uses(target, true).Count(x => x == v);
                if (compare != null)
                {
                    // a condition bit becomes the comparison itself
                    bool usable = true;
                    Func<PReg, PX> asComparison = r =>
                    {
                        string name = r.Name.Substring(4);
                        if (compare.Float)
                        {
                            if (name == "lt") return new PBin("f<", compare.A, compare.B);
                            if (name == "gt") return new PBin("f>", compare.A, compare.B);
                            if (name == "eq") return new PBin("f==", compare.A, compare.B);
                            usable = false; return r;
                        }
                        if (name == "lt") return new PBin(compare.Signed ? "<s" : "<u", compare.A, compare.B);
                        if (name == "gt") return new PBin(compare.Signed ? ">s" : ">u", compare.A, compare.B);
                        if (name == "eq") return new PBin("==", compare.A, compare.B);
                        usable = false; return r;
                    };
                    if (occurrences > 1 && Size(compare.A) + Size(compare.B) > 4) continue;
                    var replaced = Replace(target, v, asComparison);
                    if (!usable || Size(replaced) > 48) continue;
                    L[j] = new PpcStatement(replaced, L[j].Address);
                }
                else
                {
                    if (occurrences > 1 && !(value is PConst || value is PReg)) continue;
                    if (Size(value) + Size(target) > 48) continue;
                    L[j] = new PpcStatement(Replace(target, v, r => value), L[j].Address);
                }
                L.RemoveAt(i);
                any = true;
                resume = Math.Max(0, i - 1);
                goto restart;
            }
            return any;
        }

        // ---------------------------------------------------------------- printing

        private sealed class Printer
        {
            public PpcFunction F;
            public Func<uint, string> Name;
            public StringBuilder Out;
            public HashSet<uint> Labels = new HashSet<uint>();      // blocks some goto names
            public bool Emit;
            public List<KeyValuePair<int, uint>> Lines = new List<KeyValuePair<int, uint>>();   // output line -> address
            private int _line;

            public List<PpcOp> Ops = new List<PpcOp>();

            public void Line(int indent, string text, uint address = 0, PpcOp op = null)
            {
                if (!Emit) return;
                if (op != null) Ops.Add(op);
                Out.Append(' ', indent * 4).Append(text).Append('\n');
                if (address != 0) Lines.Add(new KeyValuePair<int, uint>(_line, address));
                _line++;
            }

            public string Label(uint address) => "loc_" + address.ToString("X8");

            private bool EnteredFromOutside(int lo, int hi, int allowedSource, int allowedTarget)
            {
                for (int k = lo; k <= hi; k++)
                    foreach (int p in F.Blocks[k].Preds)
                        if ((p < lo || p > hi) && !(p == allowedSource && k == allowedTarget)) return true;
                return false;
            }

            private static PJump LastJump(PpcBlock b) => b.Statements.Count > 0 ? b.Statements[b.Statements.Count - 1].S as PJump : null;

            public void Region(int from, int to, int indent, int noLabel = -1)
            {
                int i = from;
                while (i < to)
                {
                    var b = F.Blocks[i];
                    bool label = i != noLabel && Labels.Contains(b.Address);

                    // do { ... } while (c): a later block of this region jumps back here on a condition, and nothing else enters the middle
                    int tail = -1;
                    foreach (int x in b.Preds)                                   // only a block that jumps here can close a loop
                    {
                        if (x < i || x >= to || x <= tail) continue;
                        var back = LastJump(F.Blocks[x]);
                        if (back != null && back.Cond != null && back.Target == b.Address && (x == i || !EnteredFromOutside(i + 1, x, -1, -1))) tail = x;
                    }
                    if (tail >= 0)
                    {
                        if (label) Line(Math.Max(0, indent - 1), Label(b.Address) + ":", 0, new PpcOp { Kind = "label", Address = b.Address });
                        Line(indent, "do {", 0, new PpcOp { Kind = "do" });
                        Region(i, tail, indent + 1, i);                     // everything before the block that closes the loop
                        var closing = F.Blocks[tail];
                        if (tail != i && Labels.Contains(closing.Address)) Line(indent, Label(closing.Address) + ":", 0, new PpcOp { Kind = "label", Address = closing.Address });
                        for (int k = 0; k < closing.Statements.Count - 1; k++) Statement(closing.Statements[k], indent + 1);
                        var jump = closing.Statements[closing.Statements.Count - 1];
                        Line(indent, "} while (" + Cond(((PJump)jump.S).Cond, false) + ");", jump.Address, new PpcOp { Kind = "while", Cond = ((PJump)jump.S).Cond });
                        i = tail + 1;
                        continue;
                    }
                    if (label) Line(Math.Max(0, indent - 1), Label(b.Address) + ":", 0, new PpcOp { Kind = "label", Address = b.Address });

                    var last = LastJump(b);

                    // while (c) { ... }: this block jumps ahead to the test, and the test jumps back to the block right after this one
                    int test = last != null && last.Cond == null ? F.IndexOf(last.Target) : -1;
                    if (test > i + 1 && test < to)
                    {
                        var back = LastJump(F.Blocks[test]);
                        if (back != null && back.Cond != null && back.Target == F.Blocks[i + 1].Address && !EnteredFromOutside(i + 1, test, i, test))
                        {
                            for (int k = 0; k < b.Statements.Count - 1; k++) Statement(b.Statements[k], indent);
                            var testBlock = F.Blocks[test];
                            var jump = testBlock.Statements[testBlock.Statements.Count - 1];
                            bool testLabel = Labels.Contains(testBlock.Address);
                            if (testBlock.Statements.Count == 1)
                            {
                                if (testLabel) Line(Math.Max(0, indent - 1), Label(testBlock.Address) + ":", 0, new PpcOp { Kind = "label", Address = testBlock.Address });
                                Line(indent, "while (" + Cond(back.Cond, false) + ") {", jump.Address, new PpcOp { Kind = "whiledo", Cond = back.Cond });
                            }
                            else
                            {
                                // the test needs statements of its own first: they run at the top of every round
                                Line(indent, "while (1) {", 0, new PpcOp { Kind = "forever" });
                                if (testLabel) Line(indent, Label(testBlock.Address) + ":", 0, new PpcOp { Kind = "label", Address = testBlock.Address });
                                for (int k = 0; k < testBlock.Statements.Count - 1; k++) Statement(testBlock.Statements[k], indent + 1);
                                Line(indent + 1, "if (" + Cond(back.Cond, true) + ") break;", jump.Address, new PpcOp { Kind = "breakunless", Cond = back.Cond });
                            }
                            Region(i + 1, test, indent + 1);
                            Line(indent, "}", 0, new PpcOp { Kind = "endwhile" });
                            i = test + 1;
                            continue;
                        }
                    }

                    int t = last != null && last.Cond != null ? F.IndexOf(last.Target) : -1;
                    bool isIf = t > i && t <= to && (t == i + 1 || !EnteredFromOutside(i + 1, t - 1, i, i + 1));
                    for (int k = 0; k < b.Statements.Count - (isIf ? 1 : 0); k++) Statement(b.Statements[k], indent);
                    if (!isIf) { i++; continue; }
                    uint at = b.Statements[b.Statements.Count - 1].Address;
                    if (t == i + 1) { i++; continue; }                    // a conditional jump to the next instruction does nothing

                    // if / else: the first part ends by jumping over the second
                    var skip = LastJump(F.Blocks[t - 1]);
                    int e = skip != null && skip.Cond == null ? F.IndexOf(skip.Target) : -1;
                    if (e > t && e <= to && !EnteredFromOutside(t, e - 1, i, t))
                    {
                        Line(indent, "if (" + Cond(last.Cond, true) + ") {", at, new PpcOp { Kind = "if", Cond = last.Cond, Negate = true });
                        RegionWithoutLastJump(i + 1, t, indent + 1);
                        Line(indent, "} else {", 0, new PpcOp { Kind = "else" });
                        Region(t, e, indent + 1);
                        Line(indent, "}", 0, new PpcOp { Kind = "end" });
                        i = e;
                    }
                    else
                    {
                        Line(indent, "if (" + Cond(last.Cond, true) + ") {", at, new PpcOp { Kind = "if", Cond = last.Cond, Negate = true });
                        Region(i + 1, t, indent + 1);
                        Line(indent, "}", 0, new PpcOp { Kind = "end" });
                        i = t;
                    }
                }
            }
            // the "then" part of an if / else: its final "goto end" is what the else replaces
            private void RegionWithoutLastJump(int from, int to, int indent)
            {
                var b = F.Blocks[to - 1];
                var saved = b.Statements;
                b.Statements = saved.Take(saved.Count - 1).ToList();
                bool falls = b.FallsThrough;
                try { Region(from, to, indent); }
                finally { b.Statements = saved; b.FallsThrough = falls; }
            }

            private void Statement(PpcStatement st, int indent)
            {
                var s = st.S;
                string text;
                if (s is PAssign a) text = a.Dest + " = " + (a.Dest[0] == 'f' && a.Value is PLoad dl && dl.Size == 8 ? "*(double*)" + Paren(dl.Addr) : Expr(a.Value)) + ";";
                else if (s is PStore store)
                {
                    if (store.Value is PUn single && single.Op == "d2f") text = "*(float*)" + Paren(store.Addr) + " = (float)" + Operand(single.A) + ";";
                    else if (store.Value is PReg fr && fr.Name[0] == 'f') text = store.Size == 8 ? "*(double*)" + Paren(store.Addr) + " = " + fr.Name + ";" : "*(uint32_t*)" + Paren(store.Addr) + " = low32(" + fr.Name + ");";
                    else text = "*(" + TypeOf(store.Size) + "*)" + Paren(store.Addr) + " = " + Expr(store.Value) + ";";
                }
                else if (s is PCompare fc && fc.Float) text = "cr" + fc.Field + " = compare(" + Expr(fc.A) + ", " + Expr(fc.B) + ");";
                else if (s is PCompare c) text = "cr" + c.Field + " = compare(" + (c.Signed ? Signed(c.A) : Expr(c.A)) + ", " + (c.Signed ? Signed(c.B) : Expr(c.B)) + ");";
                else if (s is PJump j)
                {
                    string go;
                    if (F.Inside(j.Target)) { Labels.Add(j.Target); go = "goto " + Label(j.Target) + ";"; }
                    else go = "return " + Name(j.Target) + "();   // tail call";
                    text = j.Cond == null ? go : "if (" + Cond(j.Cond, false) + ") " + go;
                }
                else if (s is PCall call)
                {
                    string list = call.Args == null ? "" : string.Join(", ", call.Args.Select(Expr));
                    // the compiler's register save / restore helpers take no arguments and return nothing
                    bool helper = call.Target.HasValue && Name(call.Target.Value).StartsWith("__");
                    if (helper && (call.Args == null || call.Args.Length == 0)) text = Name(call.Target.Value) + "();";
                    else text = (call.Result != null ? call.Result + " = " : "") + (call.Target.HasValue ? Name(call.Target.Value) + "(" + list + ");" : "(*ctr)(" + list + ");");
                }
                else if (s is PReturn r) text = r.Cond == null ? "return;" : "if (" + Cond(r.Cond, false) + ") return;";
                else if (s is PIndirectJump sw && sw.Targets != null)
                {
                    // a compiler switch: one line per case, all part of this one statement
                    Line(indent, "switch (" + (sw.Index != null ? Expr(sw.Index) : "/* the index used to read ctr from the table */") + ") {   // jump table at 0x" + sw.Table.ToString("X8"), st.Address, new PpcOp { Kind = "statement", Statement = st });
                    for (int k = 0; k < sw.Targets.Length; k++)
                    {
                        int same = k;
                        while (same + 1 < sw.Targets.Length && sw.Targets[same + 1] == sw.Targets[k]) same++;
                        Labels.Add(sw.Targets[k]);
                        Line(indent + 1, (same > k ? "case " + k + " ... " + same : "case " + k) + ": goto " + Label(sw.Targets[k]) + ";");
                        k = same;
                    }
                    Line(indent, "}");
                    return;
                }
                else if (s is PIndirectJump ij) text = (ij.Cond == null ? "" : "if (" + Cond(ij.Cond, false) + ") ") + "goto *ctr;   // jump table or tail call";
                else text = "__asm { " + System.Text.RegularExpressions.Regex.Replace(((PAsm)s).Text, " +", " ") + " }";
                Line(indent, text, st.Address, new PpcOp { Kind = "statement", Statement = st });
            }

            private static string TypeOf(int size) => size == 1 ? "uint8_t" : size == 2 ? "uint16_t" : size == 4 ? "uint32_t" : "uint64_t";

            private string Const(ulong v)
            {
                if (v < 10) return v.ToString(CultureInfo.InvariantCulture);
                long s = (long)v;
                if (s < 0 && s >= -0x100000) return "-" + (s > -10 ? (-s).ToString(CultureInfo.InvariantCulture) : "0x" + (-s).ToString("X"));
                if ((v >> 31) == 0x1FFFFFFFF) return "(int32_t)0x" + ((uint)v).ToString("X8");      // a sign-extended 32-bit value (addresses loaded with lis)
                return "0x" + v.ToString("X");
            }

            private string Paren(PX e) => e is PConst || e is PReg ? Expr(e) : "(" + Expr(e) + ")";

            private string Signed(PX e)
            {
                if (e is PConst c) { long s = (long)c.V; return s < 0 ? "-" + (s > -10 || s < -0x100000 ? ((ulong)(-s)).ToString(CultureInfo.InvariantCulture) : "0x" + (-s).ToString("X")) : Const(c.V); }
                if (e is PUn u && (u.Op == "s8" || u.Op == "s16" || u.Op == "s32")) return Expr(e);
                return "(int64_t)" + Paren(e);
            }

            /// <summary>A condition, optionally negated, written the way it reads best.</summary>
            public string Cond(PX e, bool negate)
            {
                if (e is PUn u && u.Op == "!") return Cond(u.A, !negate);
                if (e is PBin b)
                {
                    string op = null; bool signed = false;
                    if (b.Op == "f<" || b.Op == "f>" || b.Op == "f==")
                    {
                        // a negated floating-point comparison is not the opposite comparison: it is also true when a value is not a number
                        string plain = Operand(b.A) + (b.Op == "f<" ? " < " : b.Op == "f>" ? " > " : " == ") + Operand(b.B);
                        return negate ? "!(" + plain + ")" : plain;
                    }
                    switch (b.Op)
                    {
                        case "==": op = negate ? "!=" : "=="; break;
                        case "!=": op = negate ? "==" : "!="; break;
                        case "<s": op = negate ? ">=" : "<"; signed = true; break;
                        case ">s": op = negate ? "<=" : ">"; signed = true; break;
                        case "<u": op = negate ? ">=" : "<"; break;
                        case ">u": op = negate ? "<=" : ">"; break;
                        case "<=u": op = negate ? ">" : "<="; break;
                    }
                    if (op != null)
                    {
                        // equality of values that came from a signed comparison reads better with the same casts
                        bool cast = signed || ((b.Op == "==" || b.Op == "!=") && (b.A is PUn ua && ua.Op.StartsWith("s")));
                        return (cast ? Signed(b.A) : Operand(b.A)) + " " + op + " " + (cast ? Signed(b.B) : Operand(b.B));
                    }
                    if (b.Op == "&" && IsBool(b.A) && IsBool(b.B))
                        return negate ? "!(" + Cond(b.A, false) + " && " + Cond(b.B, false) + ")" : Cond(b.A, false) + " && " + Cond(b.B, false);
                }
                return negate ? "!" + Paren(e) : Expr(e);
            }

            private string Operand(PX e) => e is PBin || e is PFn ? "(" + Expr(e) + ")" : Expr(e);

            public string Expr(PX e)
            {
                if (e is PConst c) return Const(c.V);
                if (e is PReg r) return r.Name;
                if (e is PLoad l) return "*(" + TypeOf(l.Size) + "*)" + Paren(l.Addr);
                if (e is PFn fn)
                {
                    if (fn.Name == "fmadd") return Operand(fn.Args[0]) + " * " + Operand(fn.Args[1]) + " + " + Operand(fn.Args[2]);
                    if (fn.Name == "fmsub") return Operand(fn.Args[0]) + " * " + Operand(fn.Args[1]) + " - " + Operand(fn.Args[2]);
                    return Operand(fn.Args[0]) + " >= 0.0 ? " + Operand(fn.Args[1]) + " : " + Operand(fn.Args[2]);
                }
                if (e is PUn u)
                {
                    switch (u.Op)
                    {
                        case "fneg": return "-" + Operand(u.A);
                        case "fabs": return "fabs(" + Expr(u.A) + ")";
                        case "fnabs": return "-fabs(" + Expr(u.A) + ")";
                        case "fsqrt": return "sqrt(" + Expr(u.A) + ")";
                        case "f32": return "(float)" + Operand(u.A);
                        case "f2d": return u.A is PLoad fl && fl.Size == 4 ? "*(float*)" + Paren(fl.Addr) : "as_float(" + Expr(u.A) + ")";
                        case "d2f": return "(float)" + Operand(u.A);
                        case "fctiwz": return "to_int32(" + Expr(u.A) + ")";
                        case "fctidz": return "to_int64(" + Expr(u.A) + ")";
                        case "fcfid": return "(double)as_int64(" + Expr(u.A) + ")";
                        case "~": return "~" + Operand(u.A);
                        case "-": return "-" + Operand(u.A);
                        case "!": return Cond(e, false);
                        case "s8": return "(int8_t)" + Operand(u.A);
                        case "s16": return u.A is PLoad l16 && l16.Size == 2 ? "*(int16_t*)" + Paren(l16.Addr) : "(int16_t)" + Operand(u.A);
                        case "s32": return u.A is PLoad l32 && l32.Size == 4 ? "*(int32_t*)" + Paren(l32.Addr) : "(int32_t)" + Operand(u.A);
                        case "u8": return "(uint8_t)" + Operand(u.A);
                        case "u16": return "(uint16_t)" + Operand(u.A);
                        case "u32": return "(uint32_t)" + Operand(u.A);
                        default: return u.Op + "(" + Expr(u.A) + ")";
                    }
                }
                var b = (PBin)e;
                switch (b.Op)
                {
                    case "+":
                        if (b.B is PConst k && (long)k.V < 0 && (long)k.V >= -0x100000) return Operand(b.A) + " - " + Const((ulong)(-(long)k.V));
                        return Operand(b.A) + " + " + Operand(b.B);
                    case "-": case "*": case "&": case "|": case "^": case "<<": case ">>":
                        return Operand(b.A) + " " + b.Op + " " + Operand(b.B);
                    case "f+": case "f-": case "f*": case "f/":
                        return Operand(b.A) + " " + b.Op.Substring(1) + " " + Operand(b.B);
                    case ">>s": return Signed(b.A) + " >> " + Operand(b.B);
                    case "/u": return Operand(b.A) + " / " + Operand(b.B);
                    case "/s": return Signed(b.A) + " / " + Signed(b.B);
                    case "rotl32": case "rotl64": case "mulhs": case "mulhu": return b.Op + "(" + Expr(b.A) + ", " + Expr(b.B) + ")";
                    default: return Cond(e, false);
                }
            }
        }

        public sealed class Listing
        {
            public string Text;
            /// <summary>Output line number (0-based) -> the address of the instruction it came from.</summary>
            public List<KeyValuePair<int, uint>> Lines;
            /// <summary>The same listing as a sequence of steps (what the self-test executes).</summary>
            public List<PpcOp> Ops;
        }

        /// <summary>The function as C-like text. <paramref name="name"/> gives the name of a called address.</summary>
        public Listing Print(Func<uint, string> name)
        {
            Func<uint, string> safe = a => { string n = name?.Invoke(a); return string.IsNullOrEmpty(n) ? "sub_" + a.ToString("X8") : n; };
            var p = new Printer { F = this, Name = safe, Out = new StringBuilder() };
            p.Region(0, Blocks.Count, 1);                          // first pass: finds which blocks need a label
            p.Emit = true;
            p.Line(0, "// " + safe(Start) + "   0x" + Start.ToString("X8") + " - 0x" + End.ToString("X8") + "   (" + Instructions + " instructions)");
            p.Line(0, "// r0-r31: 64-bit registers (arguments r3-r10, result r3, stack r1).  f0-f31: doubles (arguments f1-f13, result f1).");
            p.Line(0, "// Argument lists are read from the values set up right before each call; a function may take more or fewer.");
            p.Line(0, "void " + safe(Start) + "()");
            p.Line(0, "{");
            p.Region(0, Blocks.Count, 1);
            p.Line(0, "}");
            return new Listing { Text = p.Out.ToString(), Lines = p.Lines, Ops = p.Ops };
        }
    }
}
