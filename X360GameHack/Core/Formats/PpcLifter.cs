using System;
using System.Collections.Generic;

namespace X360GameHack.Core.Formats
{
    // ------------------------------------------------------------------ expressions (all values are 64-bit)

    public abstract class PX { }
    public sealed class PConst : PX { public ulong V; public PConst(ulong v) { V = v; } }
    /// <summary>r0-r31, lr, ctr, ca (carry) or a condition bit such as "cr6.eq".</summary>
    public sealed class PReg : PX { public string Name; public PReg(string name) { Name = name; } }
    /// <summary>
    /// Op: + - * &amp; | ^ &lt;&lt; &gt;&gt; (logical) &gt;&gt;s (arithmetic) /u /s == != &lt;s &lt;u &gt;s &gt;u &lt;=u rotl32 rotl64 mulhs mulhu.
    /// Shifts by 64 or more give 0 (or the sign for &gt;&gt;s); comparisons give 1 or 0.
    /// </summary>
    public sealed class PBin : PX { public string Op; public PX A, B; public PBin(string op, PX a, PX b) { Op = op; A = a; B = b; } }
    /// <summary>Op: ~ - ! s8 s16 s32 u8 u16 u32 (truncate then sign / zero extend) clz32 clz64 bswap16 bswap32.</summary>
    public sealed class PUn : PX { public string Op; public PX A; public PUn(string op, PX a) { Op = op; A = a; } }
    /// <summary>Big-endian read of Size bytes, zero-extended.</summary>
    public sealed class PLoad : PX { public int Size; public PX Addr; public PLoad(int size, PX addr) { Size = size; Addr = addr; } }

    /// <summary>
    /// A three-operand floating-point operation: fmadd(a, c, b) = a * c + b, fmsub(a, c, b) = a * c - b,
    /// fsel(a, c, b) = a &gt;= 0 ? c : b. Floating-point registers f0-f31 hold the bits of a double.
    /// </summary>
    public sealed class PFn : PX { public string Name; public PX[] Args; public PFn(string name, params PX[] args) { Name = name; Args = args; } }

    // ------------------------------------------------------------------ statements

    public abstract class PS { }
    public sealed class PAssign : PS { public string Dest; public PX Value; }
    public sealed class PStore : PS { public int Size; public PX Addr, Value; }
    /// <summary>Sets a condition field: lt / gt / eq from comparing A with B (both already extended to 64 bits).
    /// With Float the operands are doubles and the "so" bit means "unordered" (a NaN was involved).</summary>
    public sealed class PCompare : PS { public int Field; public PX A, B; public bool Signed, Float; }
    public sealed class PJump : PS { public PX Cond; public uint Target; }
    /// <summary>A call. Args / Result are filled in by the decompiler: the values set up just before the call, and the register its result is read from afterwards.</summary>
    public sealed class PCall : PS { public uint? Target; public PX Indirect; public PX[] Args; public string Result; public int IntArgs, FloatArgs; }
    public sealed class PReturn : PS { public PX Cond; }
    /// <summary>A jump through the count register. When it goes through a compiler jump table the decompiler fills in the table address, its targets and the value that selects one.</summary>
    public sealed class PIndirectJump : PS { public PX Cond; public uint Table; public uint[] Targets; public PX Index; }
    /// <summary>An instruction that is shown as it is (floating point, vector, system).</summary>
    public sealed class PAsm : PS { public string Text; }

    /// <summary>
    /// What each PowerPC instruction does, as statements over 64-bit registers. Integer, load / store and branch
    /// instructions are translated; everything else is passed through as assembly. Each translation is checked
    /// against Ghidra's emulator on instructions taken from real games.
    /// </summary>
    public static class PpcLifter
    {
        private static readonly string[] BitNames = { "lt", "gt", "eq", "so" };

        private static PX R(int n) => new PReg("r" + n);
        private static PX C(ulong v) => new PConst(v);
        private static PX B(string op, PX a, PX b) => new PBin(op, a, b);
        private static PX U(string op, PX a) => new PUn(op, a);
        private static PAssign Set(int reg, PX v) => new PAssign { Dest = "r" + reg, Value = v };
        private static PAssign Set(string reg, PX v) => new PAssign { Dest = reg, Value = v };
        private static PCompare Cr0(int reg) => new PCompare { Field = 0, A = R(reg), B = C(0), Signed = true };

        public static ulong Mask64(int mb, int me)
        {
            ulong from = ulong.MaxValue >> mb, to = me >= 63 ? ulong.MaxValue : ~(ulong.MaxValue >> (me + 1));
            return mb <= me ? from & to : from | to;
        }

        /// <summary>The statements for one instruction (never empty: unknown instructions give one <see cref="PAsm"/>).</summary>
        public static List<PS> Lift(PpcInstruction ins)
        {
            var s = new List<PS>();
            if (!ins.Valid || !Lift(ins, s))
            {
                s.Clear();
                s.Add(new PAsm { Text = ins.Valid ? ins.Text : ".long 0x" + ins.Word.ToString("X8") });
            }
            return s;
        }

        private static bool Lift(PpcInstruction ins, List<PS> s)
        {
            uint w = ins.Word;
            int op = (int)(w >> 26), d = (int)(w >> 21) & 31, a = (int)(w >> 16) & 31, b = (int)(w >> 11) & 31;
            long simm = (short)w;
            ulong uimm = w & 0xFFFF;
            bool rc = (w & 1) != 0;
            PX a0 = a == 0 ? C(0) : R(a);                 // (rA|0)
            PX ea = a == 0 ? C((ulong)simm) : simm == 0 ? R(a) : B("+", R(a), C((ulong)simm));

            switch (op)
            {
                case 14: s.Add(Set(d, a == 0 ? C((ulong)simm) : B("+", R(a), C((ulong)simm)))); return true;                       // addi
                case 15: s.Add(Set(d, a == 0 ? C((ulong)(simm << 16)) : B("+", R(a), C((ulong)(simm << 16))))); return true;      // addis
                case 12:
                case 13:                                                                                                            // addic[.]
                    s.Add(Set("ca", B("<u", B("+", R(a), C((ulong)simm)), C((ulong)simm))));
                    s.Add(Set(d, B("+", R(a), C((ulong)simm))));
                    if (op == 13) s.Add(Cr0(d));
                    return true;
                case 8:                                                                                                             // subfic
                    s.Add(Set("ca", B("<=u", R(a), C((ulong)simm))));
                    s.Add(Set(d, B("-", C((ulong)simm), R(a))));
                    return true;
                case 7: s.Add(Set(d, B("*", R(a), C((ulong)simm)))); return true;                                                   // mulli
                case 10:                                                                                                            // cmpli
                    s.Add(new PCompare { Field = d >> 2, A = (d & 1) != 0 ? R(a) : U("u32", R(a)), B = C(uimm), Signed = false });
                    return true;
                case 11:                                                                                                            // cmpi
                    s.Add(new PCompare { Field = d >> 2, A = (d & 1) != 0 ? R(a) : U("s32", R(a)), B = C((ulong)simm), Signed = true });
                    return true;
                case 24: if (uimm == 0 && a == d) return true; s.Add(Set(a, uimm == 0 ? R(d) : B("|", R(d), C(uimm)))); return true;    // ori (nop)
                case 25: s.Add(Set(a, B("|", R(d), C(uimm << 16)))); return true;
                case 26: s.Add(Set(a, B("^", R(d), C(uimm)))); return true;
                case 27: s.Add(Set(a, B("^", R(d), C(uimm << 16)))); return true;
                case 28: s.Add(Set(a, B("&", R(d), C(uimm)))); s.Add(Cr0(a)); return true;
                case 29: s.Add(Set(a, B("&", R(d), C(uimm << 16)))); s.Add(Cr0(a)); return true;

                case 32: case 34: case 40: case 42:                                                                                // lwz lbz lhz lha
                    s.Add(Set(d, LoadValue(op == 32 ? 4 : op == 34 ? 1 : 2, op == 42, ea))); return true;
                case 33: case 35: case 41: case 43:                                                                                // with update
                    if (a == 0 || a == d) return false;
                    s.Add(Set(a, B("+", R(a), C((ulong)simm))));
                    s.Add(Set(d, LoadValue(op == 33 ? 4 : op == 35 ? 1 : 2, op == 43, R(a)))); return true;
                case 36: case 38: case 44:                                                                                         // stw stb sth
                    s.Add(new PStore { Size = op == 36 ? 4 : op == 38 ? 1 : 2, Addr = ea, Value = R(d) }); return true;
                case 37: case 39: case 45:
                    if (a == 0) return false;
                    s.Add(new PStore { Size = op == 37 ? 4 : op == 39 ? 1 : 2, Addr = ea, Value = R(d) });
                    s.Add(Set(a, B("+", R(a), C((ulong)simm)))); return true;
                case 58:
                {
                    long ds = simm & ~3L; int kind = (int)(w & 3);
                    PX at = a == 0 ? C((ulong)ds) : ds == 0 ? R(a) : B("+", R(a), C((ulong)ds));
                    if (kind == 0) { s.Add(Set(d, new PLoad(8, at))); return true; }                                                // ld
                    if (kind == 2) { s.Add(Set(d, U("s32", new PLoad(4, at)))); return true; }                                      // lwa
                    if (kind == 1 && a != 0 && a != d) { s.Add(Set(a, B("+", R(a), C((ulong)ds)))); s.Add(Set(d, new PLoad(8, R(a)))); return true; }   // ldu
                    return false;
                }
                case 62:
                {
                    long ds = simm & ~3L; int kind = (int)(w & 3);
                    PX at = a == 0 ? C((ulong)ds) : ds == 0 ? R(a) : B("+", R(a), C((ulong)ds));
                    if (kind == 0) { s.Add(new PStore { Size = 8, Addr = at, Value = R(d) }); return true; }                        // std
                    if (kind == 1 && a != 0) { s.Add(new PStore { Size = 8, Addr = at, Value = R(d) }); s.Add(Set(a, B("+", R(a), C((ulong)ds)))); return true; }   // stdu
                    return false;
                }

                case 20: case 21: case 23:                                                                                         // rlwimi rlwinm rlwnm
                {
                    int mb = (int)(w >> 6) & 31, me = (int)(w >> 1) & 31;
                    PX amount = op == 23 ? B("&", R(b), C(31)) : C((ulong)b);
                    PX rot = (op != 23 && b == 0) ? U("u32", R(d)) : B("rotl32", U("u32", R(d)), amount);
                    ulong mask = Mask64(mb + 32, me + 32);
                    PX value;
                    if (mb <= me) value = mask == 0xFFFFFFFF ? rot : B("&", rot, C(mask));
                    else value = B("&", B("|", rot, B("<<", rot, C(32))), C(mask));           // a wrapped mask also keeps the upper copy
                    if (op == 20) value = B("|", mb <= me ? B("&", rot, C(mask)) : value, B("&", R(a), C(~mask)));
                    s.Add(Set(a, value));
                    if (rc) s.Add(Cr0(a));
                    return true;
                }
                case 30:
                {
                    int sh = b | (int)((w >> 1) & 1) << 5, m = (int)((w >> 6) & 31) | (int)((w >> 5) & 1) << 5, kind = (int)(w >> 2) & 7;
                    PX rot, value;
                    if (kind <= 3)
                    {
                        rot = sh == 0 ? R(d) : B("rotl64", R(d), C((ulong)sh));
                        ulong mask = kind == 0 ? Mask64(m, 63) : kind == 1 ? Mask64(0, m) : Mask64(m, 63 - sh);
                        value = mask == ulong.MaxValue ? rot : B("&", rot, C(mask));
                        if (kind == 3) value = B("|", B("&", rot, C(mask)), B("&", R(a), C(~mask)));
                    }
                    else if (kind == 4 || kind == 5)                                          // rldcl / rldcr (bit 30 selects)
                    {
                        bool right = (w & 2) != 0;
                        rot = B("rotl64", R(d), B("&", R(b), C(63)));
                        value = B("&", rot, C(right ? Mask64(0, m) : Mask64(m, 63)));
                    }
                    else return false;
                    s.Add(Set(a, value));
                    if (rc) s.Add(Cr0(a));
                    return true;
                }

                // floating point: loads and stores
                case 48: s.Add(Set("f" + d, U("f2d", new PLoad(4, ea)))); return true;                                             // lfs
                case 50: s.Add(Set("f" + d, new PLoad(8, ea))); return true;                                                       // lfd
                case 49: case 51:
                    if (a == 0) return false;
                    s.Add(Set(a, B("+", R(a), C((ulong)simm))));
                    s.Add(Set("f" + d, op == 49 ? U("f2d", new PLoad(4, R(a))) : (PX)new PLoad(8, R(a)))); return true;
                case 52: s.Add(new PStore { Size = 4, Addr = ea, Value = U("d2f", F(d)) }); return true;                           // stfs
                case 54: s.Add(new PStore { Size = 8, Addr = ea, Value = F(d) }); return true;                                     // stfd
                case 53: case 55:
                    if (a == 0) return false;
                    s.Add(new PStore { Size = op == 53 ? 4 : 8, Addr = ea, Value = op == 53 ? U("d2f", F(d)) : F(d) });
                    s.Add(Set(a, B("+", R(a), C((ulong)simm)))); return true;
                case 59: case 63: return LiftFloat(w, op == 59, d, a, b, rc, s);

                case 18:
                    if ((w & 1) != 0) s.Add(new PCall { Target = ins.Target }); else s.Add(new PJump { Target = ins.Target ?? 0 });
                    return ins.Target.HasValue;
                case 16:
                {
                    if ((w & 1) != 0 || !ins.Target.HasValue) return false;
                    PX cond = Condition(d, a, s);
                    s.Add(new PJump { Cond = cond, Target = ins.Target.Value });
                    return true;
                }
                case 19:
                {
                    int xo = (int)(w >> 1) & 0x3FF;
                    bool lk = (w & 1) != 0;
                    if (xo == 16 && !lk) { PX cond = Condition(d, a, s); s.Add(new PReturn { Cond = cond }); return true; }          // bclr
                    if (xo == 528)                                                                                                  // bcctr
                    {
                        if ((d & 4) == 0) return false;
                        PX cond = Condition(d, a, s);
                        if (!lk) { s.Add(new PIndirectJump { Cond = cond }); return true; }
                        if (cond != null) return false;
                        s.Add(new PCall { Indirect = new PReg("ctr") }); return true;
                    }
                    return false;
                }
                case 31: return Lift31(w, d, a, b, rc, a0, s);
            }
            return false;
        }

        private static PX F(int n) => new PReg("f" + n);

        // opcodes 59 (single precision: the result is rounded to a float) and 63 (double precision)
        private static bool LiftFloat(uint w, bool single, int d, int a, int b, bool rc, List<PS> s)
        {
            if (rc) return false;                                  // the record forms copy exception flags into cr1: not translated
            int c = (int)(w >> 6) & 31, low = (int)(w >> 1) & 31, xo = (int)(w >> 1) & 0x3FF;
            Func<PX, PX> round = v => single ? U("f32", v) : v;
            string dest = "f" + d;
            switch (low)
            {
                case 18: s.Add(Set(dest, round(B("f/", F(a), F(b))))); return true;
                case 20: s.Add(Set(dest, round(B("f-", F(a), F(b))))); return true;
                case 21: s.Add(Set(dest, round(B("f+", F(a), F(b))))); return true;
                case 22: s.Add(Set(dest, round(U("fsqrt", F(b))))); return true;
                case 25: s.Add(Set(dest, round(B("f*", F(a), F(c))))); return true;
                case 28: s.Add(Set(dest, round(new PFn("fmsub", F(a), F(c), F(b))))); return true;
                case 29: s.Add(Set(dest, round(new PFn("fmadd", F(a), F(c), F(b))))); return true;
                case 30: s.Add(Set(dest, U("fneg", round(new PFn("fmsub", F(a), F(c), F(b)))))); return true;
                case 31: s.Add(Set(dest, U("fneg", round(new PFn("fmadd", F(a), F(c), F(b)))))); return true;
                case 23: if (single) return false; s.Add(Set(dest, new PFn("fsel", F(a), F(c), F(b)))); return true;
                case 24: case 26: return false;                    // reciprocal estimates: the processor's own approximation
            }
            if (single) return false;
            switch (xo)
            {
                case 0: case 32: s.Add(new PCompare { Field = d >> 2, A = F(a), B = F(b), Float = true }); return true;             // fcmpu / fcmpo
                case 12: s.Add(Set(dest, U("f32", F(b)))); return true;                                                             // frsp
                case 15: s.Add(Set(dest, U("fctiwz", F(b)))); return true;
                case 815: s.Add(Set(dest, U("fctidz", F(b)))); return true;
                case 846: s.Add(Set(dest, U("fcfid", F(b)))); return true;
                case 40: s.Add(Set(dest, U("fneg", F(b)))); return true;
                case 72: s.Add(Set(dest, F(b))); return true;                                                                       // fmr
                case 136: s.Add(Set(dest, U("fnabs", F(b)))); return true;
                case 264: s.Add(Set(dest, U("fabs", F(b)))); return true;
            }
            return false;
        }

        private static PX LoadValue(int size, bool signed, PX at)
        {
            PX v = new PLoad(size, at);
            return signed ? U(size == 2 ? "s16" : "s32", v) : v;
        }

        // BO / BI of a conditional branch: emits the counter decrement and returns the condition (null = always)
        private static PX Condition(int bo, int bi, List<PS> s)
        {
            PX cond = null;
            if ((bo & 4) == 0)
            {
                s.Add(Set("ctr", B("-", new PReg("ctr"), C(1))));
                cond = B((bo & 2) != 0 ? "==" : "!=", new PReg("ctr"), C(0));
            }
            if ((bo & 16) == 0)
            {
                PX bit = new PReg("cr" + (bi >> 2) + "." + BitNames[bi & 3]);
                if ((bo & 8) == 0) bit = U("!", bit);
                cond = cond == null ? bit : B("&", cond, bit);
            }
            return cond;
        }

        private static bool Lift31(uint w, int d, int a, int b, bool rc, PX a0, List<PS> s)
        {
            int xo = (int)(w >> 1) & 0x3FF;
            PX index = a == 0 ? R(b) : B("+", R(a), R(b));
            switch (xo)
            {
                case 0: s.Add(new PCompare { Field = d >> 2, A = (d & 1) != 0 ? R(a) : U("s32", R(a)), B = (d & 1) != 0 ? R(b) : U("s32", R(b)), Signed = true }); return true;
                case 32: s.Add(new PCompare { Field = d >> 2, A = (d & 1) != 0 ? R(a) : U("u32", R(a)), B = (d & 1) != 0 ? R(b) : U("u32", R(b)), Signed = false }); return true;

                // arithmetic (results in rD); the overflow-recording forms are not translated
                case 266: s.Add(Set(d, B("+", R(a), R(b)))); break;
                case 40: s.Add(Set(d, B("-", R(b), R(a)))); break;
                case 104: s.Add(Set(d, U("-", R(a)))); break;
                case 235: s.Add(Set(d, B("*", U("s32", R(a)), U("s32", R(b))))); break;
                case 233: s.Add(Set(d, B("*", R(a), R(b)))); break;
                case 73: s.Add(Set(d, B("mulhs", R(a), R(b)))); break;
                case 9: s.Add(Set(d, B("mulhu", R(a), R(b)))); break;
                case 489: s.Add(Set(d, B("/s", R(a), R(b)))); break;
                case 457: s.Add(Set(d, B("/u", R(a), R(b)))); break;
                case 10: s.Add(Set("ca", B("<u", B("+", R(a), R(b)), R(a)))); s.Add(Set(d, B("+", R(a), R(b)))); break;              // addc
                case 8: s.Add(Set("ca", B("<=u", R(a), R(b)))); s.Add(Set(d, B("-", R(b), R(a)))); break;                             // subfc
                case 202:                                                                                                             // addze
                    // the sum needs the old carry and the new carry needs the old rA, so it goes through a temporary
                    s.Add(Set("tmp", B("+", R(a), new PReg("ca")))); s.Add(Set("ca", B("<u", new PReg("tmp"), R(a)))); s.Add(Set(d, new PReg("tmp")));
                    break;
                case 138:                                                                                                             // adde
                    s.Add(Set("tmp", B("+", R(a), R(b)))); s.Add(Set("tmp2", B("+", new PReg("tmp"), new PReg("ca"))));
                    s.Add(Set("ca", B("|", B("<u", new PReg("tmp"), R(a)), B("<u", new PReg("tmp2"), new PReg("tmp"))))); s.Add(Set(d, new PReg("tmp2")));
                    break;
                case 136:                                                                                                             // subfe
                    s.Add(Set("tmp", B("+", U("~", R(a)), R(b)))); s.Add(Set("tmp2", B("+", new PReg("tmp"), new PReg("ca"))));
                    s.Add(Set("ca", B("|", B("<u", new PReg("tmp"), R(b)), B("<u", new PReg("tmp2"), new PReg("tmp"))))); s.Add(Set(d, new PReg("tmp2")));
                    break;
                case 75: s.Add(Set(d, B(">>s", B("*", U("s32", R(a)), U("s32", R(b))), C(32)))); break;                                // mulhw
                case 11: s.Add(Set(d, B(">>", B("*", U("u32", R(a)), U("u32", R(b))), C(32)))); break;                                 // mulhwu
                case 491: s.Add(Set(d, U("s32", B("/s", U("s32", R(a)), U("s32", R(b)))))); break;                                     // divw (the upper half of the result is undefined by the processor)
                case 459: s.Add(Set(d, B("/u", U("u32", R(a)), U("u32", R(b))))); break;                                               // divwu

                // logical (results in rA)
                case 28: s.Add(Set(a, B("&", R(d), R(b)))); return Rc(a, rc, s);
                case 444: if (d == b && a == d && !rc) return true;      // "or rX,rX,rX": changes nothing (used as a scheduling hint)
                    s.Add(Set(a, d == b ? R(d) : B("|", R(d), R(b)))); return Rc(a, rc, s);
                case 316: s.Add(Set(a, B("^", R(d), R(b)))); return Rc(a, rc, s);
                case 476: s.Add(Set(a, U("~", B("&", R(d), R(b))))); return Rc(a, rc, s);
                case 124: s.Add(Set(a, U("~", d == b ? R(d) : B("|", R(d), R(b))))); return Rc(a, rc, s);
                case 60: s.Add(Set(a, B("&", R(d), U("~", R(b))))); return Rc(a, rc, s);
                case 412: s.Add(Set(a, B("|", R(d), U("~", R(b))))); return Rc(a, rc, s);
                case 284: s.Add(Set(a, U("~", B("^", R(d), R(b))))); return Rc(a, rc, s);
                case 954: s.Add(Set(a, U("s8", R(d)))); return Rc(a, rc, s);
                case 922: s.Add(Set(a, U("s16", R(d)))); return Rc(a, rc, s);
                case 986: s.Add(Set(a, U("s32", R(d)))); return Rc(a, rc, s);
                case 26: s.Add(Set(a, U("clz32", R(d)))); return Rc(a, rc, s);
                case 58: s.Add(Set(a, U("clz64", R(d)))); return Rc(a, rc, s);

                // shifts
                case 24: s.Add(Set(a, U("u32", B("<<", U("u32", R(d)), B("&", R(b), C(63)))))); return Rc(a, rc, s);                   // slw
                case 536: s.Add(Set(a, B(">>", U("u32", R(d)), B("&", R(b), C(63))))); return Rc(a, rc, s);                            // srw
                case 27: s.Add(Set(a, B("<<", R(d), B("&", R(b), C(127))))); return Rc(a, rc, s);                                      // sld
                case 539: s.Add(Set(a, B(">>", R(d), B("&", R(b), C(127))))); return Rc(a, rc, s);                                     // srd
                case 792: return ShiftRightAlgebraic(a, U("s32", R(d)), B("&", R(b), C(63)), rc, s);                                   // sraw
                case 824: return ShiftRightAlgebraic(a, U("s32", R(d)), C((ulong)b), rc, s);                                           // srawi
                case 794: return ShiftRightAlgebraic(a, R(d), B("&", R(b), C(127)), rc, s);                                            // srad
                case 826: case 827: return ShiftRightAlgebraic(a, R(d), C((ulong)(b | (xo & 1) << 5)), rc, s);                         // sradi

                // indexed loads and stores
                case 23: s.Add(Set(d, new PLoad(4, index))); return true;
                case 87: s.Add(Set(d, new PLoad(1, index))); return true;
                case 279: s.Add(Set(d, new PLoad(2, index))); return true;
                case 343: s.Add(Set(d, U("s16", new PLoad(2, index)))); return true;
                case 341: s.Add(Set(d, U("s32", new PLoad(4, index)))); return true;
                case 21: s.Add(Set(d, new PLoad(8, index))); return true;
                case 534: s.Add(Set(d, U("bswap32", new PLoad(4, index)))); return true;
                case 790: s.Add(Set(d, U("bswap16", new PLoad(2, index)))); return true;
                case 55: case 119: case 311: case 375: case 373: case 53:                                                             // with update
                {
                    if (a == 0 || a == d) return false;
                    int size = xo == 55 || xo == 373 ? 4 : xo == 119 ? 1 : xo == 53 ? 8 : 2;
                    s.Add(Set(a, B("+", R(a), R(b))));
                    PX v = new PLoad(size, R(a));
                    s.Add(Set(d, xo == 375 ? U("s16", v) : xo == 373 ? U("s32", v) : v));
                    return true;
                }
                case 151: s.Add(new PStore { Size = 4, Addr = index, Value = R(d) }); return true;
                case 215: s.Add(new PStore { Size = 1, Addr = index, Value = R(d) }); return true;
                case 407: s.Add(new PStore { Size = 2, Addr = index, Value = R(d) }); return true;
                case 149: s.Add(new PStore { Size = 8, Addr = index, Value = R(d) }); return true;
                case 662: s.Add(new PStore { Size = 4, Addr = index, Value = U("bswap32", R(d)) }); return true;
                case 918: s.Add(new PStore { Size = 2, Addr = index, Value = U("bswap16", R(d)) }); return true;
                case 183: case 247: case 439: case 181:
                    if (a == 0) return false;
                    s.Add(new PStore { Size = xo == 183 ? 4 : xo == 247 ? 1 : xo == 181 ? 8 : 2, Addr = index, Value = R(d) });
                    s.Add(Set(a, B("+", R(a), R(b))));
                    return true;

                // floating point, indexed
                case 535: s.Add(Set("f" + d, U("f2d", new PLoad(4, index)))); return true;                                            // lfsx
                case 599: s.Add(Set("f" + d, new PLoad(8, index))); return true;                                                      // lfdx
                case 567: case 631:
                    if (a == 0) return false;
                    s.Add(Set(a, B("+", R(a), R(b))));
                    s.Add(Set("f" + d, xo == 567 ? U("f2d", new PLoad(4, R(a))) : (PX)new PLoad(8, R(a)))); return true;
                case 663: s.Add(new PStore { Size = 4, Addr = index, Value = U("d2f", F(d)) }); return true;                          // stfsx
                case 727: s.Add(new PStore { Size = 8, Addr = index, Value = F(d) }); return true;                                    // stfdx
                case 983: s.Add(new PStore { Size = 4, Addr = index, Value = F(d) }); return true;                                    // stfiwx: the low 32 bits as they are
                case 695: case 759:
                    if (a == 0) return false;
                    s.Add(new PStore { Size = xo == 695 ? 4 : 8, Addr = index, Value = xo == 695 ? U("d2f", F(d)) : F(d) });
                    s.Add(Set(a, B("+", R(a), R(b)))); return true;

                case 339:                                                                                                             // mfspr
                {
                    int spr = a | b << 5;
                    if (spr == 8) { s.Add(Set(d, new PReg("lr"))); return true; }
                    if (spr == 9) { s.Add(Set(d, new PReg("ctr"))); return true; }
                    return false;
                }
                case 467:                                                                                                             // mtspr
                {
                    int spr = a | b << 5;
                    if (spr == 8) { s.Add(Set("lr", R(d))); return true; }
                    if (spr == 9) { s.Add(Set("ctr", R(d))); return true; }
                    return false;
                }
                default: return false;
            }
            // arithmetic forms come here: bit 21 (OE) asks for overflow recording, which is not translated
            if ((w & 0x400) != 0) return false;
            if (rc) s.Add(Cr0(d));
            return true;
        }

        private static bool Rc(int reg, bool rc, List<PS> s)
        {
            if (rc) s.Add(Cr0(reg));
            return true;
        }

        // rA = value >>s amount; carry = value is negative and one-bits were shifted out
        private static bool ShiftRightAlgebraic(int a, PX value, PX amount, bool rc, List<PS> s)
        {
            s.Add(Set("tmp", value));
            PX t = new PReg("tmp");
            s.Add(Set("ca", B("&", B("<s", t, C(0)), B("!=", B("<<", B(">>s", t, amount), amount), t))));
            s.Add(Set(a, B(">>s", t, amount)));
            if (rc) s.Add(Cr0(a));
            return true;
        }
    }

    /// <summary>Runs lifted statements on a register / memory state (used to check the translations and the decompiler's rewrites).</summary>
    public sealed class PpcState
    {
        public readonly Dictionary<string, ulong> Regs = new Dictionary<string, ulong>();
        public readonly Dictionary<ulong, byte> Memory = new Dictionary<ulong, byte>();
        /// <summary>Set by a branch, call or return: "jump X", "call X", "return", "jump ctr", "call ctr", or "asm".</summary>
        public string Flow;

        public ulong Get(string reg) => Regs.TryGetValue(reg, out ulong v) ? v : 0;

        /// <summary>Called for every memory access: address, size, true for a write.</summary>
        public Action<ulong, int, bool> OnAccess;
        /// <summary>What memory that was never written holds (zero when not set).</summary>
        public Func<ulong, byte> Default;

        public ulong Read(ulong addr, int size)
        {
            OnAccess?.Invoke(addr, size, false);
            ulong v = 0;
            for (int i = 0; i < size; i++) v = v << 8 | (Memory.TryGetValue(addr + (ulong)i, out byte x) ? x : Default != null ? Default(addr + (ulong)i) : (byte)0);
            return v;
        }

        public void Write(ulong addr, int size, ulong value)
        {
            OnAccess?.Invoke(addr, size, true);
            for (int i = 0; i < size; i++) Memory[addr + (ulong)i] = (byte)(value >> (8 * (size - 1 - i)));
        }

        public void Run(IEnumerable<PS> statements)
        {
            foreach (var st in statements)
            {
                if (Flow != null) return;
                if (st is PAssign asg) Regs[asg.Dest] = Eval(asg.Value);
                else if (st is PStore store) Write(Eval(store.Addr), store.Size, Eval(store.Value));
                else if (st is PCompare cmp)
                {
                    ulong x = Eval(cmp.A), y = Eval(cmp.B);
                    string f = "cr" + cmp.Field + ".";
                    if (cmp.Float)
                    {
                        double p = D(x), q = D(y);
                        Regs[f + "lt"] = p < q ? 1UL : 0; Regs[f + "gt"] = p > q ? 1UL : 0; Regs[f + "eq"] = p == q ? 1UL : 0; Regs[f + "so"] = double.IsNaN(p) || double.IsNaN(q) ? 1UL : 0;
                        continue;
                    }
                    bool lt = cmp.Signed ? (long)x < (long)y : x < y, gt = cmp.Signed ? (long)x > (long)y : x > y;
                    Regs[f + "lt"] = lt ? 1UL : 0; Regs[f + "gt"] = gt ? 1UL : 0; Regs[f + "eq"] = x == y ? 1UL : 0; Regs[f + "so"] = Get("so");
                }
                else if (st is PJump jump) { if (jump.Cond == null || Eval(jump.Cond) != 0) Flow = "jump " + jump.Target.ToString("X8"); }
                else if (st is PCall call) Flow = call.Target.HasValue ? "call " + call.Target.Value.ToString("X8") : "call ctr";
                else if (st is PReturn ret) { if (ret.Cond == null || Eval(ret.Cond) != 0) Flow = "return"; }
                else if (st is PIndirectJump ij) { if (ij.Cond == null || Eval(ij.Cond) != 0) Flow = "jump ctr"; }
                else Flow = "asm";
            }
        }

        private static double D(ulong bits) => BitConverter.Int64BitsToDouble((long)bits);
        private static ulong Bits(double d) => (ulong)BitConverter.DoubleToInt64Bits(d);

        // what "stfs" stores: the processor does not round - it repacks the double's sign, exponent and top fraction bits
        private static ulong DoubleToSingleBits(ulong d)
        {
            int exp = (int)(d >> 52) & 0x7FF;
            if (exp > 896 || (d & 0x7FFFFFFFFFFFFFFF) == 0) return ((d >> 32) & 0xC0000000) | ((d >> 29) & 0x3FFFFFFF);
            if (exp >= 874)
            {
                ulong frac = 0x8000000000000000 | ((d & 0x000FFFFFFFFFFFFF) << 11);      // 1.fraction, left aligned
                frac >>= 897 - exp;
                return ((d >> 32) & 0x80000000) | (frac >> 40);
            }
            return (d >> 32) & 0x80000000;                                               // too small for a float (the processor leaves this undefined)
        }

        private static ulong ToInteger(double v, bool wide)
        {
            if (double.IsNaN(v)) return wide ? 0x8000000000000000 : 0x80000000;
            double t = Math.Truncate(v);
            if (wide) return t >= 9223372036854775808.0 ? 0x7FFFFFFFFFFFFFFF : t < -9223372036854775808.0 ? 0x8000000000000000 : (ulong)(long)t;
            return t > 2147483647.0 ? 0x7FFFFFFF : t < -2147483648.0 ? 0x80000000 : (uint)(int)t;
        }

        public ulong Eval(PX e)
        {
            if (e is PConst c) return c.V;
            if (e is PFn fn)
            {
                double x = D(Eval(fn.Args[0])), y = D(Eval(fn.Args[1])), z = D(Eval(fn.Args[2]));
                switch (fn.Name)
                {
                    case "fmadd": return Bits(x * y + z);      // the processor rounds once (fused); here the product is rounded first
                    case "fmsub": return Bits(x * y - z);
                    case "fsel": return Bits(x >= 0.0 ? y : z);
                }
                throw new InvalidOperationException("unknown function " + fn.Name);
            }
            if (e is PReg r) return Get(r.Name);
            if (e is PLoad l) return Read(Eval(l.Addr), l.Size);
            if (e is PUn u)
            {
                ulong x = Eval(u.A);
                switch (u.Op)
                {
                    case "~": return ~x;
                    case "-": return (ulong)(-(long)x);
                    case "!": return x == 0 ? 1UL : 0;
                    case "s8": return (ulong)(sbyte)x;
                    case "s16": return (ulong)(short)x;
                    case "s32": return (ulong)(int)x;
                    case "u8": return (byte)x;
                    case "u16": return (ushort)x;
                    case "u32": return (uint)x;
                    case "clz32": { int n = 0; for (uint m = 0x80000000; m != 0 && ((uint)x & m) == 0; m >>= 1) n++; return (ulong)n; }
                    case "clz64": { int n = 0; for (ulong m = 0x8000000000000000; m != 0 && (x & m) == 0; m >>= 1) n++; return (ulong)n; }
                    case "fneg": return x ^ 0x8000000000000000;
                    case "fabs": return x & 0x7FFFFFFFFFFFFFFF;
                    case "fnabs": return x | 0x8000000000000000;
                    case "fsqrt": return Bits(Math.Sqrt(D(x)));
                    case "f32": return Bits((double)(float)D(x));
                    case "f2d": return Bits((double)BitConverter.ToSingle(BitConverter.GetBytes((uint)x), 0));
                    case "d2f": return DoubleToSingleBits(x);
                    case "fctiwz": return ToInteger(D(x), false);
                    case "fctidz": return ToInteger(D(x), true);
                    case "fcfid": return Bits((double)(long)x);
                    case "bswap16": return (ulong)(ushort)((x & 0xFF) << 8 | (x >> 8) & 0xFF);
                    case "bswap32": return (x & 0xFF) << 24 | (x & 0xFF00) << 8 | (x >> 8) & 0xFF00 | (x >> 24) & 0xFF;
                }
                throw new InvalidOperationException("unknown operator " + u.Op);
            }
            var bin = (PBin)e;
            ulong p = Eval(bin.A), q = Eval(bin.B);
            switch (bin.Op)
            {
                case "+": return p + q;
                case "-": return p - q;
                case "*": return p * q;
                case "&": return p & q;
                case "|": return p | q;
                case "^": return p ^ q;
                case "<<": return q >= 64 ? 0 : p << (int)q;
                case ">>": return q >= 64 ? 0 : p >> (int)q;
                case ">>s": return (ulong)((long)p >> (int)Math.Min(q, 63));
                case "/u": return q == 0 ? 0 : p / q;
                case "/s": return q == 0 || ((long)p == long.MinValue && (long)q == -1) ? 0 : (ulong)((long)p / (long)q);
                case "==": return p == q ? 1UL : 0;
                case "!=": return p != q ? 1UL : 0;
                case "<s": return (long)p < (long)q ? 1UL : 0;
                case ">s": return (long)p > (long)q ? 1UL : 0;
                case "<u": return p < q ? 1UL : 0;
                case ">u": return p > q ? 1UL : 0;
                case "<=u": return p <= q ? 1UL : 0;
                case "rotl32": { uint x = (uint)p; int n = (int)(q & 31); return n == 0 ? x : (x << n | x >> (32 - n)); }
                case "rotl64": { int n = (int)(q & 63); return n == 0 ? p : (p << n | p >> (64 - n)); }
                case "f+": return Bits(D(p) + D(q));
                case "f-": return Bits(D(p) - D(q));
                case "f*": return Bits(D(p) * D(q));
                case "f/": return Bits(D(p) / D(q));
                case "f<": return D(p) < D(q) ? 1UL : 0;
                case "f>": return D(p) > D(q) ? 1UL : 0;
                case "f==": return D(p) == D(q) ? 1UL : 0;
                case "mulhu": return (ulong)(((System.Numerics.BigInteger)p * q) >> 64);
                case "mulhs": return (ulong)(long)(((System.Numerics.BigInteger)(long)p * (long)q) >> 64);
            }
            throw new InvalidOperationException("unknown operator " + bin.Op);
        }
    }
}
