using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace X360GameHack.Core.Formats
{
    /// <summary>One decoded PowerPC instruction.</summary>
    public sealed class PpcInstruction
    {
        public uint Address;
        public uint Word;
        /// <summary>False when the word is not an instruction of the Xbox 360 CPU (shown as ".long").</summary>
        public bool Valid;
        public string Mnemonic = "";
        public string Operands = "";

        /// <summary>Any branch (b, bc, blr, bctr and their variants).</summary>
        public bool IsBranch;
        /// <summary>The branch saves the return address (bl, bctrl, blrl...).</summary>
        public bool IsCall;
        /// <summary>The branch is only taken under a condition (CR bit and / or CTR).</summary>
        public bool IsConditional;
        /// <summary>Branch to the link register (blr and conditional forms).</summary>
        public bool ToLinkRegister;
        /// <summary>Branch to the count register (bctr and conditional forms).</summary>
        public bool ToCountRegister;
        /// <summary>Where a direct branch goes.</summary>
        public uint? Target;
        /// <summary>BO / BI of a conditional branch (for working out whether it will be taken).</summary>
        public int BranchOptions, ConditionBit;

        public string Text => Operands.Length == 0 ? Mnemonic : Mnemonic.PadRight(11) + " " + Operands;
        public override string ToString() => Address.ToString("X8") + "  " + Word.ToString("X8") + "  " + Text;
    }

    /// <summary>
    /// Disassembler for the Xbox 360's CPU (Xenon): 64-bit PowerPC, AltiVec and the console-only VMX128 instructions.
    /// The opcode table is checked against Xenia's public instruction list (tools/ppc-instructions.xml, BSD licence,
    /// Copyright (c) 2015 Ben Vanik) and the decoding against the Capstone disassembler - see the test harness.
    /// Words that are not a Xenon instruction, or that set bits an instruction requires to be zero, come back as
    /// ".long" rather than as a guess.
    /// </summary>
    public static class PowerPc
    {
        // "VALUE[/MASK] name[markers] operands". Without a mask, every bit that is not an operand or a marker must
        // equal VALUE (reserved bits must be zero). Markers: [o] OE, [.] Rc, [l] LK, [a] AA, [.21] / [.6] Rc of the
        // vector compares.
        private static readonly string[] Rows =
        {
            "08000000 tdi to,rA,simm", "0C000000 twi to,rA,simm", "1C000000 mulli rD,rA,simm", "20000000 subfic rD,rA,simm",
            "28000000 cmpli crfD,L,rA,uimm", "2C000000 cmpi crfD,L,rA,simm", "30000000 addic rD,rA,simm", "34000000 addic. rD,rA,simm",
            "38000000 addi rD,rA0,simm", "3C000000 addis rD,rA0,simm", "40000000 bc[l][a] bo,bi,bd", "44000002 sc lev", "48000000 b[l][a] li",
            "4C000000 mcrf crfD,crfS", "4C000020 bclr[l] bo,bi,bh", "4C000024 rfid", "4C000042 crnor crbD,crbA,crbB", "4C000102 crandc crbD,crbA,crbB",
            "4C00012C isync", "4C000182 crxor crbD,crbA,crbB", "4C0001C2 crnand crbD,crbA,crbB", "4C000202 crand crbD,crbA,crbB",
            "4C000242 creqv crbD,crbA,crbB", "4C000342 crorc crbD,crbA,crbB", "4C000382 cror crbD,crbA,crbB", "4C000420 bcctr[l] bo,bi,bh",
            "50000000 rlwimi[.] rA,rS,sh,mb,me", "54000000 rlwinm[.] rA,rS,sh,mb,me", "5C000000 rlwnm[.] rA,rS,rB,mb,me",
            "60000000 ori rA,rS,uimm", "64000000 oris rA,rS,uimm", "68000000 xori rA,rS,uimm", "6C000000 xoris rA,rS,uimm",
            "70000000 andi. rA,rS,uimm", "74000000 andis. rA,rS,uimm",
            "78000000 rldicl[.] rA,rS,sh6,mb6", "78000004 rldicr[.] rA,rS,sh6,mb6", "78000008 rldic[.] rA,rS,sh6,mb6", "7800000C rldimi[.] rA,rS,sh6,mb6",
            "78000010 rldcl[.] rA,rS,rB,mb6", "78000012 rldcr[.] rA,rS,rB,mb6",

            "7C000000 cmp crfD,L,rA,rB", "7C000008 tw to,rA,rB", "7C00000C lvsl vD,rA0,rB", "7C00000E lvebx vD,rA0,rB", "7C000010 subfc[o][.] rD,rA,rB",
            "7C000012 mulhdu[.] rD,rA,rB", "7C000014 addc[o][.] rD,rA,rB", "7C000016 mulhwu[.] rD,rA,rB", "7C000026 mfcr rD", "7C100026 mfocrf rD,crm",
            "7C000028 lwarx rD,rA0,rB,eh?", "7C00002A ldx rD,rA0,rB", "7C00002E lwzx rD,rA0,rB", "7C000030 slw[.] rA,rS,rB", "7C000034 cntlzw[.] rA,rS",
            "7C000036 sld[.] rA,rS,rB", "7C000038 and[.] rA,rS,rB", "7C000040 cmpl crfD,L,rA,rB", "7C00004C lvsr vD,rA0,rB", "7C00004E lvehx vD,rA0,rB",
            "7C000050 subf[o][.] rD,rA,rB", "7C00006A ldux rD,rA,rB", "7C00006C dcbst rA0,rB", "7C00006E lwzux rD,rA,rB", "7C000074 cntlzd[.] rA,rS",
            "7C000078 andc[.] rA,rS,rB", "7C000088 td to,rA,rB", "7C00008E lvewx vD,rA0,rB", "7C000092 mulhd[.] rD,rA,rB", "7C000096 mulhw[.] rD,rA,rB",
            "7C0000A6 mfmsr rD", "7C0000A8 ldarx rD,rA0,rB,eh?", "7C0000AC dcbf rA0,rB,l2?", "7C0000AE lbzx rD,rA0,rB", "7C0000CE lvx vD,rA0,rB",
            "7C0000D0 neg[o][.] rD,rA", "7C0000EE lbzux rD,rA,rB", "7C0000F8 nor[.] rA,rS,rB", "7C00010E stvebx vS,rA0,rB", "7C000110 subfe[o][.] rD,rA,rB",
            "7C000114 adde[o][.] rD,rA,rB", "7C000120 mtcrf crm,rS", "7C100120 mtocrf crm,rS", "7C000124 mtmsr rS,l15", "7C00012A stdx rS,rA0,rB",
            "7C00012D stwcx. rS,rA0,rB", "7C00012E stwx rS,rA0,rB", "7C00014E stvehx vS,rA0,rB", "7C000164 mtmsrd rS,l15", "7C00016A stdux rS,rA,rB",
            "7C00016E stwux rS,rA,rB", "7C00018E stvewx vS,rA0,rB", "7C000190 subfze[o][.] rD,rA", "7C000194 addze[o][.] rD,rA", "7C0001AD stdcx. rS,rA0,rB",
            "7C0001AE stbx rS,rA0,rB", "7C0001CE stvx vS,rA0,rB", "7C0001D0 subfme[o][.] rD,rA", "7C0001D2 mulld[o][.] rD,rA,rB", "7C0001D4 addme[o][.] rD,rA",
            "7C0001D6 mullw[o][.] rD,rA,rB", "7C0001EC dcbtst rA0,rB,th?", "7C0001EE stbux rS,rA,rB", "7C000214 add[o][.] rD,rA,rB", "7C00022C dcbt rA0,rB,th?",
            "7C00022E lhzx rD,rA0,rB", "7C000238 eqv[.] rA,rS,rB", "7C000264 tlbie rB", "7C00026E lhzux rD,rA,rB", "7C000278 xor[.] rA,rS,rB",
            "7C0002A6 mfspr rD,spr", "7C0002AA lwax rD,rA0,rB", "7C0002AE lhax rD,rA0,rB", "7C0002CE lvxl vD,rA0,rB", "7C0002E6 mftb rD,spr",
            "7C0002EA lwaux rD,rA,rB", "7C0002EE lhaux rD,rA,rB", "7C000324 slbmte rS,rB", "7C00032E sthx rS,rA0,rB", "7C000338 orc[.] rA,rS,rB",
            "7C000364 slbie rB", "7C00036E sthux rS,rA,rB", "7C000378 or[.] rA,rS,rB", "7C000392 divdu[o][.] rD,rA,rB", "7C000396 divwu[o][.] rD,rA,rB",
            "7C0003A6 mtspr spr,rS", "7C0003AC dcbi rA0,rB", "7C0003B8 nand[.] rA,rS,rB", "7C0003CE stvxl vS,rA0,rB", "7C0003D2 divd[o][.] rD,rA,rB",
            "7C0003D6 divw[o][.] rD,rA,rB", "7C0003E4 slbia", "7C000400 mcrxr crfD", "7C00040E lvlx vD,rA0,rB", "7C000428 ldbrx rD,rA0,rB",
            "7C00042A lswx rD,rA0,rB", "7C00042C lwbrx rD,rA0,rB", "7C00042E lfsx fD,rA0,rB", "7C000430 srw[.] rA,rS,rB", "7C000436 srd[.] rA,rS,rB",
            "7C00044E lvrx vD,rA0,rB", "7C00046C tlbsync", "7C00046E lfsux fD,rA,rB", "7C0004AA lswi rD,rA0,nb", "7C0004AC sync", "7C2004AC lwsync", "7C0004AE lfdx fD,rA0,rB",
            "7C0004EE lfdux fD,rA,rB", "7C00050E stvlx vS,rA0,rB", "7C000528 stdbrx rS,rA0,rB", "7C00052A stswx rS,rA0,rB", "7C00052C stwbrx rS,rA0,rB",
            "7C00052E stfsx fS,rA0,rB", "7C00054E stvrx vS,rA0,rB", "7C00056E stfsux fS,rA,rB", "7C0005AA stswi rS,rA0,nb", "7C0005AE stfdx fS,rA0,rB",
            "7C0005EE stfdux fS,rA,rB", "7C00060E lvlxl vD,rA0,rB", "7C00062C lhbrx rD,rA0,rB", "7C000630 sraw[.] rA,rS,rB", "7C000634 srad[.] rA,rS,rB",
            "7C00064E lvrxl vD,rA0,rB", "7C000670 srawi[.] rA,rS,sh", "7C000674 sradi[.] rA,rS,sh6", "7C0006A6 slbmfev rD,rB", "7C0006AC eieio",
            "7C00070E stvlxl vS,rA0,rB", "7C000726 slbmfee rD,rB", "7C00072C sthbrx rS,rA0,rB", "7C000734 extsh[.] rA,rS", "7C00074E stvrxl vS,rA0,rB",
            "7C000774 extsb[.] rA,rS", "7C0007AC icbi rA0,rB", "7C0007AE stfiwx fS,rA0,rB", "7C0007B4 extsw[.] rA,rS", "7C0007EC dcbz rA0,rB",
            "7C2007EC dcbz128 rA0,rB",

            "80000000 lwz rD,mem", "84000000 lwzu rD,mem", "88000000 lbz rD,mem", "8C000000 lbzu rD,mem", "90000000 stw rS,mem", "94000000 stwu rS,mem",
            "98000000 stb rS,mem", "9C000000 stbu rS,mem", "A0000000 lhz rD,mem", "A4000000 lhzu rD,mem", "A8000000 lha rD,mem", "AC000000 lhau rD,mem",
            "B0000000 sth rS,mem", "B4000000 sthu rS,mem", "B8000000 lmw rD,mem", "BC000000 stmw rS,mem", "C0000000 lfs fD,mem", "C4000000 lfsu fD,mem",
            "C8000000 lfd fD,mem", "CC000000 lfdu fD,mem", "D0000000 stfs fS,mem", "D4000000 stfsu fS,mem", "D8000000 stfd fS,mem", "DC000000 stfdu fS,mem",
            "E8000000 ld rD,mems", "E8000001 ldu rD,mems", "E8000002 lwa rD,mems", "F8000000 std rS,mems", "F8000001 stdu rS,mems",

            "EC000024 fdivs[.] fD,fA,fB", "EC000028 fsubs[.] fD,fA,fB", "EC00002A fadds[.] fD,fA,fB", "EC00002C fsqrts[.] fD,fB", "EC000030 fres[.] fD,fB",
            "EC000032 fmuls[.] fD,fA,fC", "EC000038 fmsubs[.] fD,fA,fC,fB", "EC00003A fmadds[.] fD,fA,fC,fB", "EC00003C fnmsubs[.] fD,fA,fC,fB",
            "EC00003E fnmadds[.] fD,fA,fC,fB",
            "FC000000 fcmpu crfD,fA,fB", "FC000018 frsp[.] fD,fB", "FC00001C fctiw[.] fD,fB", "FC00001E fctiwz[.] fD,fB", "FC000024 fdiv[.] fD,fA,fB",
            "FC000028 fsub[.] fD,fA,fB", "FC00002A fadd[.] fD,fA,fB", "FC00002C fsqrt[.] fD,fB", "FC00002E fsel[.] fD,fA,fC,fB", "FC000032 fmul[.] fD,fA,fC",
            "FC000034 frsqrte[.] fD,fB", "FC000038 fmsub[.] fD,fA,fC,fB", "FC00003A fmadd[.] fD,fA,fC,fB", "FC00003C fnmsub[.] fD,fA,fC,fB",
            "FC00003E fnmadd[.] fD,fA,fC,fB", "FC000040 fcmpo crfD,fA,fB", "FC00004C mtfsb1[.] crbN", "FC000050 fneg[.] fD,fB", "FC000080 mcrfs crfD,crfS",
            "FC00008C mtfsb0[.] crbN", "FC000090 fmr[.] fD,fB", "FC00010C mtfsfi[.] crfN,imm4", "FC000110 fnabs[.] fD,fB", "FC000210 fabs[.] fD,fB",
            "FC00048E mffs[.] fD", "FC00058E mtfsf[.] fm,fB", "FC00065C fctid[.] fD,fB", "FC00065E fctidz[.] fD,fB", "FC00069C fcfid[.] fD,fB",

            // AltiVec (144)
            "10000000 vaddubm vD,vA,vB", "10000002 vmaxub vD,vA,vB", "10000004 vrlb vD,vA,vB",
            "10000006 vcmpequb[.21] vD,vA,vB", "10000008 vmuloub vD,vA,vB", "1000000A vaddfp vD,vA,vB",
            "1000000C vmrghb vD,vA,vB", "1000000E vpkuhum vD,vA,vB", "10000020 vmhaddshs vD,vA,vB,vC",
            "10000021 vmhraddshs vD,vA,vB,vC", "10000022 vmladduhm vD,vA,vB,vC", "10000024 vmsumubm vD,vA,vB,vC",
            "10000025 vmsummbm vD,vA,vB,vC", "10000026 vmsumuhm vD,vA,vB,vC", "10000027 vmsumuhs vD,vA,vB,vC",
            "10000028 vmsumshm vD,vA,vB,vC", "10000029 vmsumshs vD,vA,vB,vC", "1000002A vsel vD,vA,vB,vC",
            "1000002B vperm vD,vA,vB,vC", "1000002C vsldoi vD,vA,vB,shb", "1000002E vmaddfp vD,vA,vC,vB",
            "1000002F vnmsubfp vD,vA,vC,vB", "10000040 vadduhm vD,vA,vB", "10000042 vmaxuh vD,vA,vB",
            "10000044 vrlh vD,vA,vB", "10000046 vcmpequh[.21] vD,vA,vB", "10000048 vmulouh vD,vA,vB",
            "1000004A vsubfp vD,vA,vB", "1000004C vmrghh vD,vA,vB", "1000004E vpkuwum vD,vA,vB",
            "10000080 vadduwm vD,vA,vB", "10000082 vmaxuw vD,vA,vB", "10000084 vrlw vD,vA,vB",
            "10000086 vcmpequw[.21] vD,vA,vB", "1000008C vmrghw vD,vA,vB", "1000008E vpkuhus vD,vA,vB",
            "100000C6 vcmpeqfp[.21] vD,vA,vB", "100000CE vpkuwus vD,vA,vB", "10000102 vmaxsb vD,vA,vB",
            "10000104 vslb vD,vA,vB", "10000108 vmulosb vD,vA,vB", "1000010A vrefp vD,vB",
            "1000010C vmrglb vD,vA,vB", "1000010E vpkshus vD,vA,vB", "10000142 vmaxsh vD,vA,vB",
            "10000144 vslh vD,vA,vB", "10000148 vmulosh vD,vA,vB", "1000014A vrsqrtefp vD,vB",
            "1000014C vmrglh vD,vA,vB", "1000014E vpkswus vD,vA,vB", "10000180 vaddcuw vD,vA,vB",
            "10000182 vmaxsw vD,vA,vB", "10000184 vslw vD,vA,vB", "1000018A vexptefp vD,vB",
            "1000018C vmrglw vD,vA,vB", "1000018E vpkshss vD,vA,vB", "100001C4 vsl vD,vA,vB",
            "100001C6 vcmpgefp[.21] vD,vA,vB", "100001CA vlogefp vD,vB", "100001CE vpkswss vD,vA,vB",
            "10000200 vaddubs vD,vA,vB", "10000202 vminub vD,vA,vB", "10000204 vsrb vD,vA,vB",
            "10000206 vcmpgtub[.21] vD,vA,vB", "10000208 vmuleub vD,vA,vB", "1000020A vrfin vD,vB",
            "1000020C vspltb vD,vB,u5", "1000020E vupkhsb vD,vB", "10000240 vadduhs vD,vA,vB",
            "10000242 vminuh vD,vA,vB", "10000244 vsrh vD,vA,vB", "10000246 vcmpgtuh[.21] vD,vA,vB",
            "10000248 vmuleuh vD,vA,vB", "1000024A vrfiz vD,vB", "1000024C vsplth vD,vB,u5",
            "1000024E vupkhsh vD,vB", "10000280 vadduws vD,vA,vB", "10000282 vminuw vD,vA,vB",
            "10000284 vsrw vD,vA,vB", "10000286 vcmpgtuw[.21] vD,vA,vB", "1000028A vrfip vD,vB",
            "1000028C vspltw vD,vB,u5", "1000028E vupklsb vD,vB", "100002C4 vsr vD,vA,vB",
            "100002C6 vcmpgtfp[.21] vD,vA,vB", "100002CA vrfim vD,vB", "100002CE vupklsh vD,vB",
            "10000300 vaddsbs vD,vA,vB", "10000302 vminsb vD,vA,vB", "10000304 vsrab vD,vA,vB",
            "10000306 vcmpgtsb[.21] vD,vA,vB", "10000308 vmulesb vD,vA,vB", "1000030A vcfux vD,vB,u5",
            "1000030C vspltisb vD,s5", "1000030E vpkpx vD,vA,vB", "10000340 vaddshs vD,vA,vB",
            "10000342 vminsh vD,vA,vB", "10000344 vsrah vD,vA,vB", "10000346 vcmpgtsh[.21] vD,vA,vB",
            "10000348 vmulesh vD,vA,vB", "1000034A vcfsx vD,vB,u5", "1000034C vspltish vD,s5",
            "1000034E vupkhpx vD,vB", "10000380 vaddsws vD,vA,vB", "10000382 vminsw vD,vA,vB",
            "10000384 vsraw vD,vA,vB", "10000386 vcmpgtsw[.21] vD,vA,vB", "1000038A vctuxs vD,vB,u5",
            "1000038C vspltisw vD,s5", "100003C6 vcmpbfp[.21] vD,vA,vB", "100003CA vctsxs vD,vB,u5",
            "100003CE vupklpx vD,vB", "10000400 vsububm vD,vA,vB", "10000402 vavgub vD,vA,vB",
            "10000404 vand vD,vA,vB", "1000040A vmaxfp vD,vA,vB", "1000040C vslo vD,vA,vB",
            "10000440 vsubuhm vD,vA,vB", "10000442 vavguh vD,vA,vB", "10000444 vandc vD,vA,vB",
            "1000044A vminfp vD,vA,vB", "1000044C vsro vD,vA,vB", "10000480 vsubuwm vD,vA,vB",
            "10000482 vavguw vD,vA,vB", "10000484 vor vD,vA,vB", "100004C4 vxor vD,vA,vB",
            "10000502 vavgsb vD,vA,vB", "10000504 vnor vD,vA,vB", "10000542 vavgsh vD,vA,vB",
            "10000580 vsubcuw vD,vA,vB", "10000582 vavgsw vD,vA,vB", "10000600 vsububs vD,vA,vB",
            "10000604 mfvscr vD", "10000608 vsum4ubs vD,vA,vB", "10000640 vsubuhs vD,vA,vB",
            "10000644 mtvscr vB", "10000648 vsum4shs vD,vA,vB", "10000680 vsubuws vD,vA,vB",
            "10000688 vsum2sws vD,vA,vB", "10000700 vsubsbs vD,vA,vB", "10000708 vsum4sbs vD,vA,vB",
            "10000740 vsubshs vD,vA,vB", "10000780 vsubsws vD,vA,vB", "10000788 vsumsws vD,vA,vB",
            // VMX128 (75) - in match order
            "10000003/FC0007F3 lvsl128 VD,rA0,rB", "10000043/FC0007F3 lvsr128 VD,rA0,rB",
            "10000083/FC0007F3 lvewx128 VD,rA0,rB", "100000C3/FC0007F3 lvx128 VD,rA0,rB",
            "10000183/FC0007F3 stvewx128 VD,rA0,rB", "100001C3/FC0007F3 stvx128 VD,rA0,rB",
            "100002C3/FC0007F3 lvxl128 VD,rA0,rB", "100003C3/FC0007F3 stvxl128 VD,rA0,rB",
            "10000403/FC0007F3 lvlx128 VD,rA0,rB", "10000443/FC0007F3 lvrx128 VD,rA0,rB",
            "10000503/FC0007F3 stvlx128 VD,rA0,rB", "10000543/FC0007F3 stvrx128 VD,rA0,rB",
            "10000603/FC0007F3 lvlxl128 VD,rA0,rB", "10000643/FC0007F3 lvrxl128 VD,rA0,rB",
            "10000703/FC0007F3 stvlxl128 VD,rA0,rB", "10000743/FC0007F3 stvrxl128 VD,rA0,rB",
            "10000010/FC000010 vsldoi128 VD,VA,VB,sh4", "14000000/FC000210 vperm128 VD,VA,VB,vc3",
            "18000230/FC0007F0 vcfpsxws128 VD,VB,u5", "18000270/FC0007F0 vcfpuxws128 VD,VB,u5",
            "180002B0/FC0007F0 vcsxwfp128 VD,VB,u5", "180002F0/FC0007F0 vcuxwfp128 VD,VB,u5",
            "18000330/FC0007F0 vrfim128 VD,VB", "18000370/FC0007F0 vrfin128 VD,VB",
            "180003B0/FC0007F0 vrfip128 VD,VB", "180003F0/FC0007F0 vrfiz128 VD,VB",
            "18000630/FC0007F0 vrefp128 VD,VB", "18000670/FC0007F0 vrsqrtefp128 VD,VB",
            "180006B0/FC0007F0 vexptefp128 VD,VB", "180006F0/FC0007F0 vlogefp128 VD,VB",
            "18000730/FC0007F0 vspltw128 VD,VB,u5", "18000770/FC0007F0 vspltisw128 VD,s5",
            "180007F0/FC0007F0 vupkd3d128 VD,VB,u5", "18000610/FC000730 vpkd3d128 VD,VB,u5,z2",
            "18000710/FC000730 vrlimi128 VD,VB,u5,z2", "18000210/FC000630 vpermwi128 VD,VB,perm",
            "18000000/FC000390 vcmpeqfp128[.6] VD,VA,VB", "18000080/FC000390 vcmpgefp128[.6] VD,VA,VB",
            "18000100/FC000390 vcmpgtfp128[.6] VD,VA,VB", "18000180/FC000390 vcmpbfp128[.6] VD,VA,VB",
            "18000200/FC000390 vcmpequw128[.6] VD,VA,VB", "14000010/FC0003D0 vaddfp128 VD,VA,VB",
            "14000050/FC0003D0 vsubfp128 VD,VA,VB", "14000090/FC0003D0 vmulfp128 VD,VA,VB",
            "140000D0/FC0003D0 vmaddfp128 VD,VA,VB", "14000110/FC0003D0 vmaddcfp128 VD,VA,VB",
            "14000150/FC0003D0 vnmsubfp128 VD,VA,VB", "14000190/FC0003D0 vmsum3fp128 VD,VA,VB",
            "140001D0/FC0003D0 vmsum4fp128 VD,VA,VB", "14000200/FC0003D0 vpkshss128 VD,VA,VB",
            "14000210/FC0003D0 vand128 VD,VA,VB", "14000240/FC0003D0 vpkshus128 VD,VA,VB",
            "14000250/FC0003D0 vandc128 VD,VA,VB", "14000280/FC0003D0 vpkswss128 VD,VA,VB",
            "14000290/FC0003D0 vnor128 VD,VA,VB", "140002C0/FC0003D0 vpkswus128 VD,VA,VB",
            "140002D0/FC0003D0 vor128 VD,VA,VB", "14000300/FC0003D0 vpkuhum128 VD,VA,VB",
            "14000310/FC0003D0 vxor128 VD,VA,VB", "14000340/FC0003D0 vpkuhus128 VD,VA,VB",
            "14000350/FC0003D0 vsel128 VD,VA,VB", "14000380/FC0003D0 vpkuwum128 VD,VA,VB",
            "14000390/FC0003D0 vslo128 VD,VA,VB", "140003C0/FC0003D0 vpkuwus128 VD,VA,VB",
            "140003D0/FC0003D0 vsro128 VD,VA,VB", "18000050/FC0003D0 vrlw128 VD,VA,VB",
            "180000D0/FC0003D0 vslw128 VD,VA,VB", "18000150/FC0003D0 vsraw128 VD,VA,VB",
            "180001D0/FC0003D0 vsrw128 VD,VA,VB", "18000280/FC0003D0 vmaxfp128 VD,VA,VB",
            "180002C0/FC0003D0 vminfp128 VD,VA,VB", "18000300/FC0003D0 vmrghw128 VD,VA,VB",
            "18000340/FC0003D0 vmrglw128 VD,VA,VB", "18000380/FC0003D0 vupkhsb128 VD,VB",
            "180003C0/FC0003D0 vupklsb128 VD,VB",
        };

        private sealed class Op
        {
            public uint Value, Mask, RcBit;
            public string Name;
            public string[] Operands;
            public bool Oe, Lk, Aa;
        }

        private static readonly List<Op>[] ByPrimary = new List<Op>[64];

        private static uint FieldMask(string token)
        {
            switch (token)
            {
                case "rD": case "rS": case "fD": case "fS": case "vD": case "vS": case "to": case "bo": case "crbD": case "crbN": return 0x03E00000;
                case "rA": case "rA0": case "fA": case "vA": case "bi": case "crbA": case "u5": case "s5": return 0x001F0000;
                case "rB": case "fB": case "vB": case "crbB": case "sh": case "nb": return 0x0000F800;
                case "fC": case "vC": case "mb": return 0x000007C0;
                case "me": return 0x0000003E;
                case "crfD": case "crfN": return 0x03800000;
                case "th?": return 0x03E00000;      // later-ISA hint fields: accepted, shown only when set
                case "l2?": return 0x00600000;
                case "eh?": return 0x00000001;
                case "crfS": return 0x001C0000;
                case "L": return 0x00200000;
                case "l15": return 0x00010000;
                case "simm": case "uimm": return 0x0000FFFF;
                case "mem": return 0x001FFFFF;
                case "mems": return 0x001FFFFC;
                case "sh6": return 0x0000F802;
                case "mb6": return 0x000007E0;
                case "spr": return 0x001FF800;
                case "crm": return 0x000FF000;
                case "fm": return 0x01FE0000;
                case "imm4": return 0x0000F000;
                case "lev": return 0x00000FE0;
                case "li": return 0x03FFFFFC;
                case "bd": return 0x0000FFFC;
                case "bh": return 0x00001800;
                case "shb": case "sh4": return 0x000003C0;
                case "VD": return 0x03E0000C;
                case "VA": return 0x001F0420;
                case "VB": return 0x0000F803;
                case "vc3": return 0x000001C0;
                case "perm": return 0x001F01C0;
                case "z2": return 0x000000C0;
                default: throw new InvalidOperationException("unknown operand token " + token);
            }
        }

        static PowerPc()
        {
            for (int i = 0; i < 64; i++) ByPrimary[i] = new List<Op>();
            foreach (string row in Rows)
            {
                string[] parts = row.Split(' ');
                var op = new Op { Operands = parts.Length > 2 ? parts[2].Split(',') : new string[0] };
                string[] vm = parts[0].Split('/');
                op.Value = uint.Parse(vm[0], NumberStyles.HexNumber);
                string name = parts[1];
                uint free = 0;
                if (name.Contains("[o]")) { op.Oe = true; free |= 0x400; name = name.Replace("[o]", ""); }
                if (name.Contains("[.21]")) { op.RcBit = 0x400; name = name.Replace("[.21]", ""); }
                if (name.Contains("[.6]")) { op.RcBit = 0x40; name = name.Replace("[.6]", ""); }
                if (name.Contains("[.]")) { op.RcBit = 1; name = name.Replace("[.]", ""); }
                if (name.Contains("[l]")) { op.Lk = true; free |= 1; name = name.Replace("[l]", ""); }
                if (name.Contains("[a]")) { op.Aa = true; free |= 2; name = name.Replace("[a]", ""); }
                free |= op.RcBit;
                foreach (string t in op.Operands) free |= FieldMask(t);
                op.Name = name;
                op.Mask = vm.Length > 1 ? uint.Parse(vm[1], NumberStyles.HexNumber) : ~free;
                if ((op.Value & ~op.Mask) != 0) throw new InvalidOperationException("opcode table: " + row);
                ByPrimary[op.Value >> 26].Add(op);
            }
        }

        // ---------------------------------------------------------------- formatting

        private static string Num(long v)
        {
            if (v < 0) return v >= -9 ? v.ToString(CultureInfo.InvariantCulture) : "-0x" + (-v).ToString("x");
            return v <= 9 ? v.ToString(CultureInfo.InvariantCulture) : "0x" + v.ToString("x");
        }

        private static readonly string[] CrBitNames = { "lt", "gt", "eq", "so" };

        /// <summary>A condition register bit the way assemblers write it: "eq" for CR0, "4*cr6+eq" for the others.</summary>
        public static string CrBit(int bit) => bit < 4 ? CrBitNames[bit] : "4*cr" + (bit >> 2) + "+" + CrBitNames[bit & 3];

        private static uint RD(uint w) => (w >> 21) & 0x1F;
        private static uint RA(uint w) => (w >> 16) & 0x1F;
        private static uint RB(uint w) => (w >> 11) & 0x1F;
        private static uint RC(uint w) => (w >> 6) & 0x1F;
        private static int Simm(uint w) => (short)(w & 0xFFFF);
        private static uint Sh6(uint w) => RB(w) | ((w >> 1) & 1) << 5;
        private static uint Mb6(uint w) => RC(w) | ((w >> 5) & 1) << 5;
        private static uint Spr(uint w) => RA(w) | RB(w) << 5;

        private static string Operand(string token, uint w, uint address, bool absolute)
        {
            switch (token)
            {
                case "rD": case "rS": return "r" + RD(w);
                case "rA": return "r" + RA(w);
                case "rA0": return RA(w) == 0 ? "0" : "r" + RA(w);
                case "rB": return "r" + RB(w);
                case "fD": case "fS": return "f" + RD(w);
                case "fA": return "f" + RA(w);
                case "fB": return "f" + RB(w);
                case "fC": return "f" + RC(w);
                case "vD": case "vS": return "v" + RD(w);
                case "vA": return "v" + RA(w);
                case "vB": return "v" + RB(w);
                case "vC": return "v" + RC(w);
                case "to": case "bo": case "crbN": return Num(RD(w));
                case "bi": return CrBit((int)RA(w));
                case "crbD": return CrBit((int)RD(w));
                case "crbA": return CrBit((int)RA(w));
                case "crbB": return CrBit((int)RB(w));
                case "crfD": return "cr" + (w >> 23 & 7);
                case "crfN": return Num(w >> 23 & 7);
                case "crfS": return "cr" + (w >> 18 & 7);
                case "th?": return RD(w) == 0 ? null : Num(RD(w));
                case "l2?": return (w >> 21 & 3) == 0 ? null : Num(w >> 21 & 3);
                case "eh?": return (w & 1) == 0 ? null : "1";
                case "L": return Num(w >> 21 & 1);
                case "l15": return Num(w >> 16 & 1);
                case "simm": return Num(Simm(w));
                case "uimm": return Num(w & 0xFFFF);
                case "mem": return Num(Simm(w)) + "(" + (RA(w) == 0 ? "0" : "r" + RA(w)) + ")";
                case "mems": return Num((short)(w & 0xFFFC)) + "(" + (RA(w) == 0 ? "0" : "r" + RA(w)) + ")";
                case "sh": case "nb": return Num(RB(w));
                case "mb": return Num(RC(w));
                case "me": return Num(w >> 1 & 0x1F);
                case "sh6": return Num(Sh6(w));
                case "mb6": return Num(Mb6(w));
                case "spr": return Num(Spr(w));
                case "crm": return Num(w >> 12 & 0xFF);
                case "fm": return Num(w >> 17 & 0xFF);
                case "imm4": return Num(w >> 12 & 0xF);
                case "lev": return Num(w >> 5 & 0x7F);
                case "bh": return Num(w >> 11 & 3);
                case "u5": return Num(RA(w));
                case "s5": return Num(((int)RA(w) ^ 16) - 16);
                case "shb": case "sh4": return Num(w >> 6 & 0xF);
                case "li": return "0x" + BranchTarget(w, address, absolute, true).ToString("x");
                case "bd": return "0x" + BranchTarget(w, address, absolute, false).ToString("x");
                case "VD": return "v" + (RD(w) | (w >> 2 & 3) << 5);
                case "VA": return "v" + (RA(w) | (w >> 5 & 1) << 5 | (w >> 10 & 1) << 6);
                case "VB": return "v" + (RB(w) | (w & 3) << 5);
                case "vc3": return "v" + (w >> 6 & 7);
                case "perm": return Num(RA(w) | (w >> 6 & 7) << 5);
                case "z2": return Num(w >> 6 & 3);
                default: throw new InvalidOperationException(token);
            }
        }

        private static uint BranchTarget(uint w, uint address, bool absolute, bool longForm)
        {
            int disp = longForm ? ((int)(w & 0x03FFFFFC) ^ 0x02000000) - 0x02000000 : (short)(w & 0xFFFC);
            return absolute ? (uint)disp : address + (uint)disp;
        }

        // ---------------------------------------------------------------- decode

        /// <summary>Decodes the instruction <paramref name="word"/> located at <paramref name="address"/>.</summary>
        public static PpcInstruction Decode(uint word, uint address)
        {
            var ins = new PpcInstruction { Address = address, Word = word };
            Op op = null;
            foreach (var candidate in ByPrimary[word >> 26])
                if ((word & candidate.Mask) == candidate.Value) { op = candidate; break; }
            if (op == null)
            {
                ins.Mnemonic = ".long";
                ins.Operands = "0x" + word.ToString("X8");
                return ins;
            }
            ins.Valid = true;
            if (Simplified(op, word, address, ins)) return ins;

            var name = new StringBuilder(op.Name);
            if (op.Oe && (word & 0x400) != 0) name.Append('o');
            if (op.Lk && (word & 1) != 0) name.Append('l');
            if (op.Aa && (word & 2) != 0) name.Append('a');
            if (op.RcBit != 0 && (word & op.RcBit) != 0) name.Append('.');
            ins.Mnemonic = name.ToString();
            bool absolute = op.Aa && (word & 2) != 0;
            var parts = new List<string>(op.Operands.Length);
            foreach (string t in op.Operands)
            {
                string text = Operand(t, word, address, absolute);
                if (text != null) parts.Add(text);
            }
            ins.Operands = string.Join(", ", parts);
            return ins;
        }

        private static readonly Dictionary<uint, string> TrapConditions = new Dictionary<uint, string>
        {
            { 16, "lt" }, { 20, "le" }, { 4, "eq" }, { 12, "ge" }, { 8, "gt" }, { 24, "ne" }, { 2, "llt" }, { 6, "lle" }, { 5, "lge" }, { 1, "lgt" }, { 31, "u" }
        };

        private static readonly string[] TrueConditions = { "lt", "gt", "eq", "so" };
        private static readonly string[] FalseConditions = { "ge", "le", "ne", "ns" };

        /// <summary>The simplified (extended) mnemonics every PowerPC assembler and debugger uses. False = print the base form.</summary>
        private static bool Simplified(Op op, uint w, uint address, PpcInstruction ins)
        {
            string dot = op.RcBit != 0 && (w & op.RcBit) != 0 ? "." : "";
            switch (op.Name)
            {
                case "addi":
                    if (RA(w) != 0) return false;
                    return Set(ins, "li", "r" + RD(w) + ", " + Num(Simm(w)));
                case "addis":
                    if (RA(w) != 0) return false;
                    return Set(ins, "lis", "r" + RD(w) + ", " + Num(w & 0xFFFF));
                case "ori":
                    return w == 0x60000000 && Set(ins, "nop", "");
                case "or":
                    return RD(w) == RB(w) && Set(ins, "mr" + dot, "r" + RA(w) + ", r" + RD(w));
                case "nor":
                    return RD(w) == RB(w) && Set(ins, "not" + dot, "r" + RA(w) + ", r" + RD(w));
                case "vor":
                    return RA(w) == RB(w) && Set(ins, "vmr", "v" + RD(w) + ", v" + RA(w));
                case "vnor":
                    return RA(w) == RB(w) && Set(ins, "vnot", "v" + RD(w) + ", v" + RA(w));

                case "cmpi": case "cmpli": case "cmp": case "cmpl":
                {
                    bool logical = op.Name.StartsWith("cmpl"), immediate = op.Name.EndsWith("i");
                    string name = "cmp" + (logical ? "l" : "") + ((w >> 21 & 1) != 0 ? "d" : "w") + (immediate ? "i" : "");
                    uint crf = w >> 23 & 7;
                    string last = immediate ? (logical ? Num(w & 0xFFFF) : Num(Simm(w))) : "r" + RB(w);
                    return Set(ins, name, (crf != 0 ? "cr" + crf + ", " : "") + "r" + RA(w) + ", " + last);
                }

                case "tw": case "td": case "twi": case "tdi":
                {
                    bool immediate = op.Name.EndsWith("i");
                    if (w == 0x7FE00008) return Set(ins, "trap", "");
                    if (!TrapConditions.TryGetValue(RD(w), out string cc)) return false;
                    string name = op.Name.Substring(0, 2) + cc + (immediate ? "i" : "");
                    return Set(ins, name, "r" + RA(w) + ", " + (immediate ? Num(Simm(w)) : "r" + RB(w)));
                }

                case "rlwinm":
                {
                    uint sh = RB(w), mb = RC(w), me = w >> 1 & 0x1F;
                    string regs = "r" + RA(w) + ", r" + RD(w) + ", ";
                    if (sh == 0 && me == 31) return Set(ins, "clrlwi" + dot, regs + Num(mb));
                    if (mb == 0 && me == 31) return Set(ins, "rotlwi" + dot, regs + Num(sh));
                    if (mb == 0 && me == 31 - sh) return Set(ins, "slwi" + dot, regs + Num(sh));
                    if (me == 31 && sh == 32 - mb) return Set(ins, "srwi" + dot, regs + Num(mb));
                    return false;
                }
                case "rlwnm":
                    return RC(w) == 0 && (w >> 1 & 0x1F) == 31 && Set(ins, "rotlw" + dot, "r" + RA(w) + ", r" + RD(w) + ", r" + RB(w));
                case "rldicl":
                {
                    uint sh = Sh6(w), mb = Mb6(w);
                    string regs = "r" + RA(w) + ", r" + RD(w) + ", ";
                    if (sh == 0) return Set(ins, "clrldi" + dot, regs + Num(mb));
                    if (mb == 0) return Set(ins, "rotldi" + dot, regs + Num(sh));
                    if (sh == 64 - mb) return Set(ins, "srdi" + dot, regs + Num(mb));
                    return false;
                }
                case "rldicr":
                {
                    uint sh = Sh6(w), me = Mb6(w);
                    return me == 63 - sh && Set(ins, "sldi" + dot, "r" + RA(w) + ", r" + RD(w) + ", " + Num(sh));
                }
                case "rldcl":
                    return Mb6(w) == 0 && Set(ins, "rotld" + dot, "r" + RA(w) + ", r" + RD(w) + ", r" + RB(w));

                case "crxor":
                    return RD(w) == RA(w) && RA(w) == RB(w) && Set(ins, "crclr", CrBit((int)RD(w)));
                case "creqv":
                    return RD(w) == RA(w) && RA(w) == RB(w) && Set(ins, "crset", CrBit((int)RD(w)));
                case "crnor":
                    return RA(w) == RB(w) && Set(ins, "crnot", CrBit((int)RD(w)) + ", " + CrBit((int)RA(w)));
                case "cror":
                    return RA(w) == RB(w) && Set(ins, "crmove", CrBit((int)RD(w)) + ", " + CrBit((int)RA(w)));

                case "mfspr":
                    switch (Spr(w))
                    {
                        case 1: return Set(ins, "mfxer", "r" + RD(w));
                        case 8: return Set(ins, "mflr", "r" + RD(w));
                        case 9: return Set(ins, "mfctr", "r" + RD(w));
                        default: return false;
                    }
                case "mtspr":
                    switch (Spr(w))
                    {
                        case 1: return Set(ins, "mtxer", "r" + RD(w));
                        case 8: return Set(ins, "mtlr", "r" + RD(w));
                        case 9: return Set(ins, "mtctr", "r" + RD(w));
                        default: return false;
                    }
                case "mftb":
                    if (Spr(w) == 268) return Set(ins, "mftb", "r" + RD(w));
                    if (Spr(w) == 269) return Set(ins, "mftbu", "r" + RD(w));
                    return false;
                case "mtmsr": case "mtmsrd":
                    return (w >> 16 & 1) == 0 && Set(ins, op.Name, "r" + RD(w));
                case "sc":
                    return (w >> 5 & 0x7F) == 0 && Set(ins, "sc", "");

                case "b":
                    ins.IsBranch = true;
                    ins.IsCall = (w & 1) != 0;
                    ins.Target = BranchTarget(w, address, (w & 2) != 0, true);
                    return false;
                case "bc": case "bclr": case "bcctr":
                    return Branch(op, w, address, ins);
            }
            return false;
        }

        private static bool Set(PpcInstruction ins, string mnemonic, string operands)
        {
            ins.Mnemonic = mnemonic;
            ins.Operands = operands;
            return true;
        }

        private static bool Branch(Op op, uint w, uint address, PpcInstruction ins)
        {
            uint bo = RD(w), bi = RA(w);
            bool lk = (w & 1) != 0, aa = op.Name == "bc" && (w & 2) != 0;
            bool toLr = op.Name == "bclr", toCtr = op.Name == "bcctr";
            ins.IsBranch = true;
            ins.IsCall = lk;
            ins.ToLinkRegister = toLr;
            ins.ToCountRegister = toCtr;
            ins.BranchOptions = (int)bo;
            ins.ConditionBit = (int)bi;
            ins.IsConditional = (bo & 0x14) != 0x14;
            string target = null;
            if (op.Name == "bc")
            {
                ins.Target = BranchTarget(w, address, aa, false);
                target = "0x" + ins.Target.Value.ToString("x");
            }
            else if ((w >> 11 & 3) != 0) return false;                  // a branch hint field: show the base form
            string suffix = (toLr ? "lr" : toCtr ? "ctr" : "") + (lk ? "l" : "") + (aa ? "a" : "");

            bool ignoreCondition = (bo & 0x10) != 0, noCounter = (bo & 0x04) != 0;
            if (ignoreCondition && noCounter)
            {
                // always: only the plain blr / bctr have a short form
                if (op.Name == "bc" || bo != 20 || bi != 0) return false;
                return Set(ins, "b" + suffix, "");
            }
            if (!noCounter && toCtr) return false;                      // bcctr cannot count
            if (noCounter)
            {
                // condition only
                bool wanted = (bo & 0x08) != 0;
                string hint = (bo & 3) == 2 ? "-" : (bo & 3) == 3 ? "+" : "";
                string name = "b" + (wanted ? TrueConditions[bi & 3] : FalseConditions[bi & 3]) + suffix + hint;
                string cr = bi >> 2 != 0 ? "cr" + (bi >> 2) : null;
                return Set(ins, name, cr != null && target != null ? cr + ", " + target : cr ?? target ?? "");
            }
            string count = (bo & 0x02) != 0 ? "bdz" : "bdnz";
            if (ignoreCondition)
            {
                // counter only
                if (bi != 0) return false;
                string hint = (bo & 0x09) == 0x08 ? "-" : (bo & 0x09) == 0x09 ? "+" : "";
                if ((bo & 0x09) == 0x01) return false;
                return Set(ins, count + suffix + hint, target ?? "");
            }
            // counter and condition
            if ((bo & 1) != 0) return false;
            string both = count + ((bo & 0x08) != 0 ? "t" : "f") + suffix;
            return Set(ins, both, CrBit((int)bi) + (target != null ? ", " + target : ""));
        }

        // ---------------------------------------------------------------- helpers for callers

        /// <summary>Decodes big-endian code, one instruction per 4 bytes.</summary>
        public static List<PpcInstruction> Disassemble(byte[] code, int offset, int byteCount, uint address)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));
            if (offset < 0 || byteCount < 0 || offset > code.Length - byteCount) throw new ArgumentOutOfRangeException(nameof(byteCount));
            var list = new List<PpcInstruction>(byteCount / 4);
            for (int i = 0; i + 4 <= byteCount; i += 4)
            {
                uint w = (uint)(code[offset + i] << 24 | code[offset + i + 1] << 16 | code[offset + i + 2] << 8 | code[offset + i + 3]);
                list.Add(Decode(w, address + (uint)i));
            }
            return list;
        }

        /// <summary>
        /// Whether a conditional branch will be taken with the given condition register and count register (the count
        /// register is the value BEFORE the branch runs; the branch decrements it first when it counts).
        /// </summary>
        public static bool BranchTaken(PpcInstruction ins, uint cr, ulong ctr)
        {
            if (ins == null || !ins.IsBranch) return false;
            if (!ins.IsConditional) return true;
            int bo = ins.BranchOptions;
            bool counterOk = (bo & 0x04) != 0 || (((uint)(ctr - 1) == 0) == ((bo & 0x02) != 0));   // titles run in 32-bit mode: the low word counts
            bool bit = (cr >> (31 - ins.ConditionBit) & 1) != 0;
            bool conditionOk = (bo & 0x10) != 0 || bit == ((bo & 0x08) != 0);
            return counterOk && conditionOk;
        }
    }
}
