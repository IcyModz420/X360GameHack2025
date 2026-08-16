// Managed XDevkit-style interfaces (no COM interop attributes).
// Method surface mirrors xdevkit.dll / IXboxConsole.cs in the repo xdevkit folder.
#nullable disable
using System.Collections;
using System.Runtime.InteropServices;

namespace XDevkit
{
    public interface IXboxEventInfo { }

    public delegate void XboxEvents_OnStdNotifyEventHandler(
        XboxDebugEventType eventCode,
        IXboxEventInfo eventInfo);

    public delegate void XboxEvents_OnTextNotifyEventHandler(
        [MarshalAs(UnmanagedType.BStr)] string textNotification);

    public interface XboxEvents_Event
    {
        event XboxEvents_OnStdNotifyEventHandler OnStdNotify;
        event XboxEvents_OnTextNotifyEventHandler OnTextNotify;
    }

    public interface IXboxExecutable
    {
        string GetPEModuleName();
    }

    public interface IXboxStackFrame
    {
        bool TopOfStack { get; }
        bool Dirty { get; }
        IXboxStackFrame NextStackFrame { get; }
        bool GetRegister32(XboxRegisters32 register, out int value);
        void SetRegister32(XboxRegisters32 register, int value);
        bool GetRegister64(XboxRegisters64 register, out long value);
        void SetRegister64(XboxRegisters64 register, long value);
        bool GetRegisterDouble(XboxRegistersDouble register, out double value);
        void SetRegisterDouble(XboxRegistersDouble register, double value);
        bool GetRegisterVector_cpp(XboxRegistersVector register, out float value);
        void SetRegisterVector_cpp(XboxRegistersVector register, ref float value);
        bool GetRegisterVector(XboxRegistersVector register, float[] value);
        void SetRegisterVector(XboxRegistersVector register, float[] value);
        void FlushRegisterChanges();
        XBOX_FUNCTION_INFO FunctionInfo { get; }
        uint StackPointer { get; }
        uint ReturnAddress { get; }
    }

    public interface IXboxSection
    {
        XBOX_SECTION_INFO SectionInfo { get; }
    }

    public interface IXboxSections : IEnumerable
    {
        IXboxSection this[int index] { get; }
        int Count { get; }
        new IEnumerator GetEnumerator();
    }

    public interface IXboxModule
    {
        XBOX_MODULE_INFO ModuleInfo { get; }
        IXboxSections Sections { get; }
        void GetFunctionInfo(uint address, out XBOX_FUNCTION_INFO functionInfo);
        uint OriginalSize { get; }
        IXboxExecutable Executable { get; }
        uint GetEntryPointAddress();
    }

    public interface IXboxModules : IEnumerable
    {
        IXboxModule this[int index] { get; }
        int Count { get; }
        new IEnumerator GetEnumerator();
    }

    public interface IXboxThread
    {
        uint ThreadId { get; }
        XBOX_THREAD_INFO ThreadInfo { get; }
        XBOX_EVENT_INFO StopEventInfo { get; }
        IXboxStackFrame TopOfStack { get; }
        void Halt();
        void Continue(bool exception);
        void Suspend();
        void Resume();
        uint CurrentProcessor { get; }
        uint LastError { get; }
    }

    public interface IXboxThreads : IEnumerable
    {
        IXboxThread this[int index] { get; }
        int Count { get; }
        new IEnumerator GetEnumerator();
    }

    public interface IXboxMemoryRegion
    {
        int BaseAddress { get; }
        int RegionSize { get; }
        XboxMemoryRegionFlags Flags { get; }
    }

    public interface IXboxMemoryRegions : IEnumerable
    {
        IXboxMemoryRegion this[int index] { get; }
        int Count { get; }
        new IEnumerator GetEnumerator();
    }

    public interface IXboxFile
    {
        string Name { get; set; }
        object CreationTime { get; set; }
        object ChangeTime { get; set; }
        ulong Size { get; set; }
        bool IsReadOnly { get; set; }
        bool IsDirectory { get; }
    }

    public interface IXboxFiles : IEnumerable
    {
        IXboxFile this[int index] { get; }
        int Count { get; }
        new IEnumerator GetEnumerator();
    }

    public interface IXboxAutomation
    {
        void GetInputProcess(uint userIndex, out bool systemProcess);
        void BindController(uint userIndex, uint queueLength);
        void UnbindController(uint userIndex);
        void ConnectController(uint userIndex);
        void DisconnectController(uint userIndex);
        void SetGamepadState(uint userIndex, ref XBOX_AUTOMATION_GAMEPAD gamepad);
        void QueueGamepadState_cpp(
            uint userIndex,
            ref XBOX_AUTOMATION_GAMEPAD gamepadArray,
            ref uint timedDurationArray,
            ref uint countDurationArray,
            uint itemCount,
            out uint itemsAddedToQueue);
        bool QueueGamepadState(
            uint userIndex,
            ref XBOX_AUTOMATION_GAMEPAD gamepad,
            uint timedDuration,
            uint countDuration);
        void ClearGamepadQueue(uint userIndex);
        void QueryGamepadQueue(
            uint userIndex,
            out uint queueLength,
            out uint itemsInQueue,
            out uint timedDurationRemaining,
            out uint countDurationRemaining);
        void GetUserDefaultProfile(out long xuid);
        void SetUserDefaultProfile(long xuid);
    }

    public interface IXboxDebugTarget
    {
        string Name { get; }
        bool IsDump { get; }
        XboxManager XboxManager { get; }
        XboxConsole Console { get; }
        void ConnectAsDebugger(string debuggerName, XboxDebugConnectFlags flags);
        void DisconnectAsDebugger();
        bool IsDebuggerConnected(out string debuggerName, out string userName);
        IXboxModules Modules { get; }
        IXboxThreads Threads { get; }
        void GetMemory(uint address, uint bytesToRead, byte[] data, out uint bytesRead);
        void SetMemory(uint address, uint bytesToWrite, byte[] data, out uint bytesWritten);
        void GetMemory_cpp(uint address, uint bytesToRead, out byte data, out uint bytesRead);
        void SetMemory_cpp(uint address, uint bytesToWrite, ref byte data, out uint bytesWritten);
        void InvalidateMemoryCache(bool executablePages, uint address, uint size);
        bool MemoryCacheEnabled { get; set; }
        IXboxMemoryRegions MemoryRegions { get; }
        XBOX_PROCESS_INFO RunningProcessInfo { get; }
        void StopOn(XboxStopOnFlags stopOn, bool stop);
        void Stop(out bool alreadyStopped);
        void Go(out bool notStopped);
        void SetBreakpoint(uint address);
        void RemoveBreakpoint(uint address);
        void RemoveAllBreakpoints();
        void SetInitialBreakpoint();
        void SetDataBreakpoint(uint address, XboxBreakpointType type, uint size);
        void IsBreakpoint(uint address, out XboxBreakpointType type);
        void WriteDump(string filename, XboxDumpFlags type);
        void CopyEventInfo(out XBOX_EVENT_INFO eventInfoDest, ref XBOX_EVENT_INFO eventInfoSource);
        void FreeEventInfo(ref XBOX_EVENT_INFO eventInfo);
        void PgoStartDataCollection(uint pgoModule);
        void PgoStopDataCollection(uint pgoModule);
        void PgoSaveSnapshot(string phase, bool reset, uint pgoModule);
        void PgoSetAllocScale(uint pgoModule, uint bufferAllocScale);
    }

    public interface IXboxConsoles : IEnumerable
    {
        string this[int index] { get; }
        int Count { get; }
        new IEnumerator GetEnumerator();
    }

    public interface IXboxManager
    {
        string DefaultConsole { get; set; }
        IXboxConsoles Consoles { get; }
        void AddConsole(string xbox);
        void RemoveConsole(string xbox);
        XboxConsole OpenConsole(string xboxName);
        IXboxDebugTarget OpenDumpFile(string filename, string imageSearchPath);
        void SelectConsole(int parentWindow, out string selectedXbox, XboxAccessFlags desiredAccess, XboxSelectConsoleFlags flags);
        void RunAddConsoleWizard(int parentWindow, bool modal);
        void OpenWindowsExplorer(string xboxName, string path);
        string TranslateError(int hr);
        string SystemSymbolServerPath { get; }
        void SelectConsoleEx(long parentWindow, out string selectedXbox, XboxAccessFlags desiredAccess, XboxSelectConsoleFlags flags);
        void RunAddConsoleWizardEx(long parentWindow, bool modal);
    }

    public interface XboxManager : IXboxManager { }

    public interface IXboxConsole
    {
        string Name { get; set; }
        uint IPAddress { get; }
        uint IPAddressTitle { get; }
        object SystemTime { get; set; }
        bool Shared { get; set; }
        uint ConnectTimeout { get; set; }
        uint ConversationTimeout { get; set; }
        void FindConsole(uint retries, uint retryDelay);
        XboxManager XboxManager { get; }
        IXboxDebugTarget DebugTarget { get; }
        void Reboot(string name, string mediaDirectory, string cmdLine, XboxRebootFlags flags);
        void SetDefaultTitle(string titleName, string mediaDirectory, uint flags);
        XBOX_PROCESS_INFO RunningProcessInfo { get; }
        uint OpenConnection(string handler);
        void CloseConnection(uint connection);
        void SendTextCommand(uint connection, string command, out string response);
        void ReceiveSocketLine(uint connection, out string line);
        int ReceiveStatusResponse(uint connection, out string line);
        void SendBinary(uint connection, byte[] data, uint count);
        void ReceiveBinary(uint connection, byte[] data, uint count, out uint bytesReceived);
        void SendBinary_cpp(uint connection, ref byte data, uint count);
        void ReceiveBinary_cpp(uint connection, ref byte data, uint count, out uint bytesReceived);
        string Drives { get; }
        void GetDiskFreeSpace(ushort drive, out ulong freeBytesAvailableToCaller, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
        void MakeDirectory(string directory);
        void RemoveDirectory(string directory);
        IXboxFiles DirectoryFiles(string directory);
        void SendFile(string localName, string remoteName);
        void ReceiveFile(string localName, string remoteName);
        void ReadFileBytes(string filename, uint fileOffset, uint count, byte[] data, out uint bytesRead);
        void WriteFileBytes(string filename, uint fileOffset, uint count, byte[] data, out uint bytesWritten);
        void ReadFileBytes_cpp(string filename, uint fileOffset, uint count, out byte data, out uint bytesRead);
        void WriteFileBytes_cpp(string filename, uint fileOffset, uint count, ref byte data, out uint bytesWritten);
        void SetFileSize(string filename, uint fileOffset, XboxCreateDisposition createDisposition);
        IXboxFile GetFileObject(string filename);
        void RenameFile(string oldName, string newName);
        void DeleteFile(string filename);
        void ScreenShot(string filename);
        XboxDumpMode DumpMode { get; set; }
        void GetDumpSettings(out XBOX_DUMP_SETTINGS dumpMode);
        void SetDumpSettings(ref XBOX_DUMP_SETTINGS dumpMode);
        XboxEventDeferFlags EventDeferFlags { get; set; }
        XboxConsoleType ConsoleType { get; }
        void StartFileEventCapture();
        void StopFileEventCapture();
        IXboxAutomation XboxAutomation { get; }
        uint LoadDebuggerExtension(string extensionName);
        void UnloadDebuggerExtension(uint moduleHandle);
        XboxConsoleFeatures ConsoleFeatures { get; }
    }

    public interface XboxConsole : IXboxConsole, XboxEvents_Event { }
}
