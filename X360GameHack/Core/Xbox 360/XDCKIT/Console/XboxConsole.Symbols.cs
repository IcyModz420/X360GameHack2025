// =============================================================================
// XDCKIT.XboxConsole.Symbols.cs - Local PC-side symbol/dbghelp helpers
// =============================================================================
// The following xbdm.dll exports do NOT speak to the console at all - they
// run entirely on the PC side using dbghelp.dll, _NT_SYMBOL_PATH, and the
// Microsoft public symbol server.  They appear here as a thin compatibility
// surface so existing XDevkit-style code links and runs against XDCKIT:
//
//   DmGetDbgHelpPath
//   DmGetSystemSymbolServerPathA / DmGetSystemSymbolServerPathW
//   DmSetSymbolSearchPath
//   DmLoadSymbolsForModule / DmLoadSymbolsForModuleEx
//   DmLoadSymbolsForAllLoadedModules
//   DmUnloadSymbolsForModule
//   DmGetSourceLineFromAddress
//   DmGetSymbolFromAddress
//
// XDCKIT does not embed dbghelp.dll, but it does manage the per-console
// symbol search path that any caller wiring up dbghelp themselves can
// consume via <see cref="XboxConsole.SymbolSearchPath"/>.
// =============================================================================
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

    public partial class XboxConsole
    {
        #region Search path / dbghelp discovery

        /// <summary>
        /// Per-console symbol search path.  Stored locally so subsequent
        /// dbghelp-based callers can pick it up.  Default is the value of
        /// <c>_NT_SYMBOL_PATH</c> (or empty when unset).
        /// </summary>
        public string SymbolSearchPath { get; private set; }
            = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH") ?? string.Empty;

        /// <summary>DmSetSymbolSearchPath — store the dbghelp search path for this console.</summary>
        public void SetSymbolSearchPath(string searchPath) => SymbolSearchPath = searchPath ?? string.Empty;

        /// <summary>DmGetDbgHelpPath — best-effort lookup of the dbghelp.dll location.</summary>
        public static string GetDbgHelpPath()
        {
            // Prefer the one in the current process directory, then PATH.
            try
            {
                string here = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dbghelp.dll");
                if (System.IO.File.Exists(here)) return here;
            }
            catch { /* ignore */ }

            try
            {
                string env = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in env.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string p;
                    try { p = Path.Combine(dir.Trim(), "dbghelp.dll"); } catch { continue; }
                    if (System.IO.File.Exists(p)) return p;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        /// <summary>DmGetSystemSymbolServerPathA — Microsoft public symbol server URL.</summary>
        public static string GetSystemSymbolServerPathA()
            => "SRV*" + Path.Combine(Path.GetTempPath(), "Symbols") + "*https://msdl.microsoft.com/download/symbols";

        /// <summary>DmGetSystemSymbolServerPathW — wide-string version.</summary>
        public static string GetSystemSymbolServerPathW() => GetSystemSymbolServerPathA();

        #endregion

        #region Symbol resolution (local; no xbdm wire activity)

        /// <summary>
        /// DmLoadSymbolsForModule - PC-side dbghelp work.  Without dbghelp.dll
        /// embedded in XDCKIT this is a no-op; it returns zero so callers
        /// know no symbols were loaded.  Use <see cref="GetModules"/> to
        /// fetch the on-console module list - that does work.
        /// </summary>
        public uint LoadSymbolsForModule(string moduleName, uint baseAddress = 0,
                                         uint size = 0, uint flags = 0) => 0;

        /// <summary>DmLoadSymbolsForModuleEx — PC-side dbghelp.  See <see cref="LoadSymbolsForModule"/>.</summary>
        public uint LoadSymbolsForModuleEx(string moduleName, uint baseAddress, uint size,
                                           uint flags, string searchPath) => 0;

        /// <summary>
        /// DmLoadSymbolsForAllLoadedModules — would walk the on-console
        /// module list + load PDBs for each.  XDCKIT pulls the module list
        /// (<see cref="GetModules"/>) and leaves PDB resolution to the
        /// caller's preferred dbghelp wrapper.  Returns the module count.
        /// </summary>
        public int LoadSymbolsForAllLoadedModules() => GetModules()?.Count ?? 0;

        /// <summary>DmUnloadSymbolsForModule — PC-side dbghelp; no-op without dbghelp.</summary>
        public void UnloadSymbolsForModule(string moduleName) { /* no-op */ }

        /// <summary>
        /// DmGetSymbolFromAddress — PC-side dbghelp.  Returns null when no
        /// symbol provider is configured.
        /// </summary>
        public string GetSymbolFromAddress(uint address, out uint displacement)
        {
            displacement = 0;
            return null;
        }

        /// <summary>
        /// DmGetSourceLineFromAddress — PC-side dbghelp.  Returns null when
        /// no symbol provider is configured.
        /// </summary>
        public string GetSourceLineFromAddress(uint address, out uint line)
        {
            line = 0;
            return null;
        }

        #endregion
    }
