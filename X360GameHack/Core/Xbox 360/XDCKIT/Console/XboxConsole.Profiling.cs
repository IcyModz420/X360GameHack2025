// =============================================================================
// XDCKIT.XboxConsole.Profiling.cs - Profiling, sampling, PGO, perf counters
// =============================================================================
// Wraps every xbdm.dll Dm*Profiling*, Dm*Sampling*, DmPgo*, DmIsFastCAPEnabled,
// DmFindPdbSignature, DmQueryAllocationTypeName, DmQueryPerformanceCounter,
// DmWalkPerformanceCounters, and DmCloseCounters export.
//
// Wire formats taken straight from the disassembled Microsoft xbdm.dll
// (see XBDM.asm "capctrl %s", "lopctrl %s", "pogoctrl ...", "QUERYPC ..." etc).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;

    public partial class XboxConsole
    {
        #region Code-coverage / instruction profiler (capctrl - DmStartProfiling)

        /// <summary>
        /// DmStartProfiling - wraps SDK <c>capctrl start name=%s buffersize=0x%x</c>.
        /// </summary>
        public void StartProfiling(string profileName, uint bufferSize)
        {
            if (string.IsNullOrEmpty(profileName)) throw new ArgumentNullException(nameof(profileName));
            SendTextCommand($"capctrl start name={profileName} buffersize=0x{bufferSize:X}");
        }

        /// <summary>DmStopProfiling - wraps SDK <c>capctrl stop</c>.</summary>
        public void StopProfiling() => SendTextCommand("capctrl stop");

        /// <summary>DmAbortProfiling - wraps SDK <c>capctrl abort</c>.</summary>
        public void AbortProfiling() => SendTextCommand("capctrl abort");

        /// <summary>
        /// DmSetProfilingOptions - wraps SDK <c>capctrl flags=0x%x</c>.
        /// </summary>
        public void SetProfilingOptions(uint flags)
            => SendTextCommand($"capctrl flags=0x{flags:X}");

        /// <summary>
        /// DmGetProfilingStatus - wraps SDK <c>capctrl profilingstatus</c>.
        /// Returns the raw status text (key=value form) or null on failure.
        /// </summary>
        public string GetProfilingStatus()
        {
            var resp = SendTextCommand("capctrl profilingstatus");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        /// <summary>
        /// DmIsFastCAPEnabled - wraps SDK <c>capctrl fastcapenabled</c>.
        /// Returns true if the console reports fast-cap support.
        /// </summary>
        public bool IsFastCapEnabled()
        {
            var resp = SendTextCommand("capctrl fastcapenabled");
            if (!resp.IsSuccess) return false;
            string s = resp.StatusMessage ?? string.Empty;
            return s.IndexOf("yes", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0
                || ParseUIntKvHex(s, "enabled") != 0
                || ParseUIntKvHex(s, "fastcapenabled") != 0;
        }

        #endregion

        #region Sampling profiler (lopctrl - DmStartSamplingProfiler)

        /// <summary>
        /// DmStartSamplingProfiler - wraps SDK
        /// <c>lopctrl start name=%s interval=0x%x flags=0x%x</c>.
        /// </summary>
        public void StartSamplingProfiler(string profileName, uint intervalCycles, uint flags)
        {
            if (string.IsNullOrEmpty(profileName)) throw new ArgumentNullException(nameof(profileName));
            SendTextCommand($"lopctrl start name={profileName} interval=0x{intervalCycles:X} flags=0x{flags:X}");
        }

        /// <summary>DmStopSamplingProfiler - wraps SDK <c>lopctrl stop</c>.</summary>
        public void StopSamplingProfiler() => SendTextCommand("lopctrl stop");

        /// <summary>
        /// DmGetSamplingProfilerInfo - wraps SDK <c>lopctrl info</c>.  Returns
        /// raw info text or null on failure.
        /// </summary>
        public string GetSamplingProfilerInfo()
        {
            var resp = SendTextCommand("lopctrl info");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        #endregion

        #region PGO / pogoctrl (DmPgoStartDataCollection / Stop / Save / SetAllocScale)

        /// <summary>DmPgoStartDataCollection - wraps SDK <c>pogoctrl start module=0x%x</c>.</summary>
        public void PgoStartDataCollection(uint moduleHandle)
            => SendTextCommand($"pogoctrl start module=0x{moduleHandle:X}");

        /// <summary>DmPgoStopDataCollection - wraps SDK <c>pogoctrl stop module=0x%x</c>.</summary>
        public void PgoStopDataCollection(uint moduleHandle)
            => SendTextCommand($"pogoctrl stop module=0x{moduleHandle:X}");

        /// <summary>
        /// DmPgoSaveSnapshot - wraps SDK
        /// <c>pogoctrl snapshot phase="%s" reset=%u module=0x%x</c>.
        /// </summary>
        public void PgoSaveSnapshot(string phaseName, bool resetCounts, uint moduleHandle)
        {
            string phase = phaseName ?? string.Empty;
            SendTextCommand($"pogoctrl snapshot phase=\"{phase}\" reset={(resetCounts ? 1 : 0)} module=0x{moduleHandle:X}");
        }

        /// <summary>DmPgoSetAllocScale - wraps SDK <c>pogoctrl allocscale module=0x%x scale=%u</c>.</summary>
        public void PgoSetAllocScale(uint moduleHandle, uint scale)
            => SendTextCommand($"pogoctrl allocscale module=0x{moduleHandle:X} scale={scale}");

        #endregion

        #region Allocation tracking (DmQueryAllocationTypeName)

        /// <summary>
        /// DmQueryAllocationTypeName - wraps SDK <c>memtrack cmd=querytype type=0x%x</c>.
        /// Returns the human-readable name for an allocation type id, or null
        /// if the console doesn't recognize it.
        /// </summary>
        public string QueryAllocationTypeName(ushort allocationTypeId)
        {
            var resp = SendTextCommand($"memtrack cmd=querytype type=0x{allocationTypeId:X}");
            if (!resp.IsSuccess) return null;
            string named = ParseKvLine(resp.StatusMessage, "name");
            return string.IsNullOrEmpty(named) ? resp.StatusMessage?.Trim() : named;
        }

        #endregion

        #region PDB lookup (DmFindPdbSignature)

        /// <summary>
        /// DmFindPdbSignature - wraps SDK <c>pdbinfo addr=0x%X</c>.  Returns
        /// the raw pdb GUID/age key=value line, or null if the address does
        /// not map to a loaded PE/XEX with a CodeView record.
        /// </summary>
        public string FindPdbSignature(uint moduleAddress)
        {
            var resp = SendTextCommand($"pdbinfo addr=0x{moduleAddress:X}");
            return resp.IsSuccess ? resp.StatusMessage?.Trim() : null;
        }

        #endregion

        #region Performance counters (QUERYPC / PCLIST)

        /// <summary>
        /// DmQueryPerformanceCounter - wraps SDK
        /// <c>QUERYPC NAME="%s" TYPE=0x%08x</c>.  Returns a 64-bit value
        /// assembled from the <c>valhi/vallo</c> reply pair (and an optional
        /// <c>ratehi/ratelo</c> rate via the out parameter).
        /// </summary>
        public ulong QueryPerformanceCounter(string counterName, uint type, out ulong rate)
        {
            rate = 0;
            if (string.IsNullOrEmpty(counterName)) throw new ArgumentNullException(nameof(counterName));
            var resp = SendTextCommand($"QUERYPC NAME=\"{counterName}\" TYPE=0x{type:X8}");
            if (!resp.IsSuccess) return 0;

            string line = resp.StatusMessage ?? string.Empty;
            uint vhi = ParseUIntKvHex(line, "valhi");
            uint vlo = ParseUIntKvHex(line, "vallo");
            uint rhi = ParseUIntKvHex(line, "ratehi");
            uint rlo = ParseUIntKvHex(line, "ratelo");
            rate = CombineHiLo(rhi, rlo);
            return CombineHiLo(vhi, vlo);
        }

        /// <summary>
        /// DmQueryPerformanceCounter convenience overload - drops the rate.
        /// </summary>
        public ulong QueryPerformanceCounter(string counterName, uint type = 0)
            => QueryPerformanceCounter(counterName, type, out _);

        /// <summary>
        /// DmWalkPerformanceCounters - wraps SDK <c>PCLIST</c>.  Returns each
        /// row of the 202 multi-line body parsed into a counter descriptor.
        /// Each row carries at least a <c>name=...</c> key.
        /// </summary>
        public List<XboxPerformanceCounter> WalkPerformanceCounters()
        {
            var list = new List<XboxPerformanceCounter>();
            var resp = SendTextCommand("PCLIST");
            if ((int)resp.Status != 202) return list;

            foreach (var line in SplitMultilineBody(resp.Body))
            {
                var pc = new XboxPerformanceCounter
                {
                    Name = ParseKvLine(line, "name") ?? string.Empty,
                    Type = ParseUIntKvHex(line, "type"),
                    RawLine = line
                };
                list.Add(pc);
            }
            return list;
        }

        /// <summary>
        /// DmCloseCounters - in genuine xbdm.dll this only frees a PC-side
        /// HLOCAL.  In XDCKIT counters are returned as plain .NET lists, so
        /// this is a no-op kept for source compatibility.
        /// </summary>
        public void CloseCounters() { /* no-op (no PC-side handle to free) */ }

        #endregion
    }
