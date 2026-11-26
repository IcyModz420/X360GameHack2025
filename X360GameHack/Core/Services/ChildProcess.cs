using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace X360GameHack.Core.Services
{
    /// <summary>
    /// A console program started with its input, output and error on pipes this program owns, without a window.
    /// System.Diagnostics.Process cannot be used when the program reads answers from its input: on .NET Framework it writes the
    /// console encoding's byte order mark into that pipe as soon as the program starts (StandardInput.AutoFlush), and on a PC
    /// whose console uses UTF-8 the program then reads EF BB BF in front of the first answer (abgx360 takes that line as
    /// neither y nor n and asks again). Here the input pipe carries exactly the bytes written to it. No shell is involved.
    /// </summary>
    public sealed class ChildProcess : IDisposable
    {
        public Stream Input { get; private set; }
        public Stream Output { get; private set; }
        public Stream Error { get; private set; }
        public int Id { get; private set; }
        private SafeProcessHandle _process;

        public static ChildProcess Start(string exe, string arguments, string workDir)
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), bInheritHandle = true };
            SafeFileHandle inRead = null, inWrite = null, outRead = null, outWrite = null, errRead = null, errWrite = null;
            try
            {
                if (!CreatePipe(out inRead, out inWrite, ref sa, 0) || !CreatePipe(out outRead, out outWrite, ref sa, 0) || !CreatePipe(out errRead, out errWrite, ref sa, 0))
                    throw new Win32Exception();
                // this program's ends must not be inherited, or the program never sees the end of its input / we never see the end of its output
                foreach (var h in new[] { inWrite, outRead, errRead })
                    if (!SetHandleInformation(h, HANDLE_FLAG_INHERIT, 0)) throw new Win32Exception();
                var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), dwFlags = STARTF_USESTDHANDLES, hStdInput = inRead.DangerousGetHandle(), hStdOutput = outWrite.DangerousGetHandle(), hStdError = errWrite.DangerousGetHandle() };
                var cmd = new StringBuilder(AbgxOptions.Quote(exe) + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
                if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, true, CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero,
                                   string.IsNullOrEmpty(workDir) ? null : workDir, ref si, out PROCESS_INFORMATION pi))
                    throw new Win32Exception();
                CloseHandle(pi.hThread);
                var cp = new ChildProcess { Id = pi.dwProcessId, _process = new SafeProcessHandle(pi.hProcess, true) };
                cp.Input = new FileStream(inWrite, FileAccess.Write, 1, false);
                cp.Output = new FileStream(outRead, FileAccess.Read, 4096, false);
                cp.Error = new FileStream(errRead, FileAccess.Read, 1024, false);
                inWrite = outRead = errRead = null;   // owned by the streams now
                return cp;
            }
            finally
            {
                // the program's ends: it has its own copies now
                inRead?.Dispose(); outWrite?.Dispose(); errWrite?.Dispose();
                inWrite?.Dispose(); outRead?.Dispose(); errRead?.Dispose();
            }
        }

        /// <summary>Writes the bytes as they are and flushes them.</summary>
        public void Write(byte[] bytes)
        {
            Input.Write(bytes, 0, bytes.Length);
            Input.Flush();
        }

        public bool WaitForExit(int milliseconds) => WaitForSingleObject(_process, milliseconds < 0 ? INFINITE : (uint)milliseconds) == 0;

        public bool HasExited => WaitForExit(0);

        public int ExitCode => GetExitCodeProcess(_process, out uint c) ? unchecked((int)c) : -1;

        public void Kill() { try { TerminateProcess(_process, 1); } catch { } }

        public void Dispose()
        {
            try { Input?.Dispose(); } catch { }
            try { Output?.Dispose(); } catch { }
            try { Error?.Dispose(); } catch { }
            _process?.Dispose();
        }

        // ---------------------------------------------------------------- Win32

        private const uint HANDLE_FLAG_INHERIT = 1, STARTF_USESTDHANDLES = 0x100, CREATE_NO_WINDOW = 0x08000000, CREATE_UNICODE_ENVIRONMENT = 0x400, INFINITE = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb; public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
            public uint dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SECURITY_ATTRIBUTES sa, int size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle h, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeProcessHandle h, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle h, uint code);
    }
}
