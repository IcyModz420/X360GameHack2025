// =============================================================================
// X360GameHack Disk Reader launcher - the program Windows elevates when X360GameHack needs a physical disk.
//
// WHY IT EXISTS: the disk reader itself (X360GameHack.DiskReaderCore.exe) is a .NET program, and a .NET program loads
// whatever runtime option the environment it starts with names - a profiler (COR_ENABLE_PROFILING / COR_PROFILER /
// COR_PROFILER_PATH), an AppDomain manager (APPDOMAIN_MANAGER_ASM / _TYPE), COMPlus_ runtime switches. An elevated
// program gets the user's environment from HKCU\Environment, which any program running as the user can change. So
// without this launcher, something running as the user could put such an option there, wait for the user to accept
// the Disk Reader's permission prompt, and have its own code loaded into an administrator process.
//
// WHAT IT DOES, and nothing else:
//   * it is native code (no .NET runtime), so none of those options can load anything into it,
//   * it only ever starts X360GameHack.DiskReaderCore.exe from its own folder (Program Files: only administrators
//     can change it),
//   * it gives the helper a NEW environment holding only the Windows paths every program needs, worked out from
//     Windows itself - nothing from the user's environment reaches the elevated helper,
//   * it refuses to start the helper while a .NET runtime option is set for this user in the registry
//     (HKCU\Software\Microsoft\.NETFramework holds values) - the runtime reads options from there as well,
//   * the helper runs in a job object, so it ends when this launcher ends,
//   * its arguments go to the helper unchanged; the helper's exit code is its own.
// Its own exit codes: 90 the helper could not be started, 91 a .NET runtime option is set for this user,
// 92 not running elevated, 93 the command line is too long.
// =============================================================================
#define WIN32_LEAN_AND_MEAN
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <wchar.h>

#ifdef LAUNCHER_TEST
/* the test build (build-launcher.ps1 -Test): same code, but it starts envdump.exe, reads its "runtime options" from a
   key of its own, and runs without administrator rights - so the clean environment can be proven without elevation */
#define CORE_NAME L"envdump.exe"
#define OPTIONS_KEY L"Software\\X360GameHack-launcher-test"
#define REQUIRE_ELEVATION 0
#else
#define CORE_NAME L"X360GameHack.DiskReaderCore.exe"
#define OPTIONS_KEY L"Software\\Microsoft\\.NETFramework"
#define REQUIRE_ELEVATION 1
#endif
#define MAX_CMD 4096

/* True when this process runs with an elevated (administrator) token. */
static BOOL IsElevated(void)
{
    HANDLE token = NULL;
    TOKEN_ELEVATION e;
    DWORD got = 0;
    BOOL elevated = FALSE;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return FALSE;
    if (GetTokenInformation(token, TokenElevation, &e, sizeof(e), &got)) elevated = e.TokenIsElevated != 0;
    CloseHandle(token);
    return elevated;
}

/* True when HKCU\Software\Microsoft\.NETFramework holds any value: the .NET runtime reads its options from there by name
   (profiler, alternative JIT, debugging switches...) for every .NET program the user starts - elevated ones too. Fails
   closed: a key that exists but cannot be read counts as set. Subkeys (policy, security settings) are not options. */
static BOOL RuntimeOptionsSet(void)
{
    HKEY key = NULL;
    DWORD values = 0;
    LONG r = RegOpenKeyExW(HKEY_CURRENT_USER, OPTIONS_KEY, 0, KEY_READ, &key);
    if (r == ERROR_FILE_NOT_FOUND) return FALSE;
    if (r != ERROR_SUCCESS) return TRUE;
    r = RegQueryInfoKeyW(key, NULL, NULL, NULL, NULL, NULL, NULL, &values, NULL, NULL, NULL, NULL);
    RegCloseKey(key);
    return r != ERROR_SUCCESS || values > 0;
}

/* Appends "name=value\0" to the block; FALSE when it would not fit. */
static BOOL Put(wchar_t *block, size_t cap, size_t *used, const wchar_t *name, const wchar_t *value)
{
    size_t n = wcslen(name), v = wcslen(value);
    if (*used + n + 1 + v + 2 > cap) return FALSE;
    wmemcpy(block + *used, name, n); *used += n;
    block[(*used)++] = L'=';
    wmemcpy(block + *used, value, v); *used += v;
    block[(*used)++] = L'\0';
    return TRUE;
}

/* The helper's whole environment, from Windows' own answers (never from environment variables), sorted by name as
   Windows keeps environment blocks: ComSpec, PATH, SystemDrive, SystemRoot, TEMP, TMP, windir. */
static BOOL CleanEnvironment(wchar_t *block, size_t cap)
{
    wchar_t win[MAX_PATH], sys[MAX_PATH], path[3 * MAX_PATH + 8], temp[MAX_PATH + 8], cmd[MAX_PATH + 16], drive[4];
    size_t used = 0;
    UINT w = GetSystemWindowsDirectoryW(win, MAX_PATH), s = GetSystemDirectoryW(sys, MAX_PATH);
    if (w == 0 || w >= MAX_PATH || s == 0 || s >= MAX_PATH || w < 3 || win[1] != L':') return FALSE;
    drive[0] = win[0]; drive[1] = L':'; drive[2] = L'\0';
    if (swprintf(path, ARRAYSIZE(path), L"%s;%s;%s\\Wbem", sys, win, sys) < 0) return FALSE;
    if (swprintf(temp, ARRAYSIZE(temp), L"%s\\Temp", win) < 0) return FALSE;
    if (swprintf(cmd, ARRAYSIZE(cmd), L"%s\\cmd.exe", sys) < 0) return FALSE;
    if (!Put(block, cap, &used, L"ComSpec", cmd) || !Put(block, cap, &used, L"PATH", path) ||
        !Put(block, cap, &used, L"SystemDrive", drive) || !Put(block, cap, &used, L"SystemRoot", win) ||
        !Put(block, cap, &used, L"TEMP", temp) || !Put(block, cap, &used, L"TMP", temp) ||
        !Put(block, cap, &used, L"windir", win)) return FALSE;
    block[used] = L'\0';                         /* the block ends with an empty string */
    return TRUE;
}

/* What follows the program name on this process's command line (with its leading space), passed on unchanged. */
static const wchar_t *Arguments(void)
{
    const wchar_t *c = GetCommandLineW();
    if (*c == L'"') { c++; while (*c && *c != L'"') c++; if (*c == L'"') c++; }
    else while (*c && *c != L' ' && *c != L'\t') c++;
    return c;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR commandLine, int show)
{
    static wchar_t env[2048];
    wchar_t self[MAX_PATH], dir[MAX_PATH], core[MAX_PATH], cmd[MAX_CMD];
    const wchar_t *args;
    wchar_t *slash;
    DWORD n, code = 90;
    HANDLE job;
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limit;
    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    (void)instance; (void)previous; (void)commandLine; (void)show;

    /* only ever load system DLLs, from System32 */
    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);

#if REQUIRE_ELEVATION
    if (!IsElevated()) return 92;
#else
    (void)IsElevated;
#endif
    if (RuntimeOptionsSet()) return 91;

    n = GetModuleFileNameW(NULL, self, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return 90;
    wcscpy_s(dir, MAX_PATH, self);
    slash = wcsrchr(dir, L'\\');
    if (slash == NULL) return 90;
    *slash = L'\0';
    if (swprintf(core, MAX_PATH, L"%s\\%s", dir, CORE_NAME) < 0) return 90;
    if (GetFileAttributesW(core) == INVALID_FILE_ATTRIBUTES) return 90;

    args = Arguments();
    if (wcslen(core) + wcslen(args) + 3 >= MAX_CMD) return 93;
    if (swprintf(cmd, MAX_CMD, L"\"%s\"%s", core, args) < 0) return 93;
    if (!CleanEnvironment(env, ARRAYSIZE(env))) return 90;

    /* the helper ends when this launcher ends (the program closing it, or it being stopped) */
    job = CreateJobObjectW(NULL, NULL);
    if (job == NULL) return 90;
    ZeroMemory(&limit, sizeof(limit));
    limit.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION;
    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limit, sizeof(limit))) { CloseHandle(job); return 90; }

    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));
    if (!CreateProcessW(core, cmd, NULL, NULL, FALSE, CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED | CREATE_NO_WINDOW,
                        env, dir, &si, &pi)) { CloseHandle(job); return 90; }
    if (!AssignProcessToJobObject(job, pi.hProcess))
    {
        TerminateProcess(pi.hProcess, 90);
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess); CloseHandle(job);
        return 90;
    }
    ResumeThread(pi.hThread);
    CloseHandle(pi.hThread);
    WaitForSingleObject(pi.hProcess, INFINITE);
    if (!GetExitCodeProcess(pi.hProcess, &code)) code = 90;
    CloseHandle(pi.hProcess);
    CloseHandle(job);
    return (int)code;
}
