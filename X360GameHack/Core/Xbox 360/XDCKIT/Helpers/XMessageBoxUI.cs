// =============================================================================
// XDCKIT.XMessageBoxUI.cs - Native Xam message box helper
// =============================================================================
// Pops up a native Xbox 360 message box on the console (the same one Xam uses
// for "Sign-in", "Continue?", etc.) and asynchronously polls for the user's
// button press.  Multiple boxes can be stacked; we keep the per-process
// state in `XMessageBoxTracking` so they don't stomp on each other.
//
// Usage:
//
//     var box = new XMessageBoxUI(
//         console,
//         "Title",
//         "Are you sure?",
//         new[] { "Yes", "No" },
//         XMessageBoxIcons.XMB_WARNINGICON,
//         selectedButtonIndex: 1);
//
//     box.MessageBoxUIResult += (_, p) =>
//     {
//         Console.WriteLine($"User picked button {p.Result} (code 0x{p.Code:X})");
//     };
//
//     box.Show();
// =============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

    public sealed class XMessageBoxUIProgress : EventArgs
    {
        public uint Result { get; }
        public uint Code { get; }
        public XMessageBoxUIProgress(uint result, uint code) { Result = result; Code = code; }
    }

    /// <summary>Bookkeeping for a single live native message box.</summary>
    internal sealed class ActiveXMessageBox
    {
        public uint Size;
        public byte[] XOverlappedBytes;
        public ActiveXMessageBox(uint size, byte[] xOverlappedBytes)
        {
            Size = size;
            XOverlappedBytes = xOverlappedBytes;
        }
    }

    /// <summary>Per-process registry of active native message boxes.</summary>
    internal static class XMessageBoxTracking
    {
        public static readonly object Sync = new object();
        public static readonly List<ActiveXMessageBox> ActiveMessageBoxes = new List<ActiveXMessageBox>();
    }

    /// <summary>Wrapper around <c>XamShowMessageBoxUI</c>.</summary>
    public sealed class XMessageBoxUI
    {
        public XboxConsole Console { get; }
        public string Title { get; }
        public string Message { get; }
        public string[] Buttons { get; }
        public XMessageBoxIcons Icon { get; }
        public int SelectedButton { get; }

        public event EventHandler<XMessageBoxUIProgress> MessageBoxUIResult;

        private volatile uint _xOverlappedAddr;
        private volatile uint _resultAddr;
        private volatile bool _isOpen;
        private ActiveXMessageBox _activeRecord;

        public bool IsMessageBoxOpen => _isOpen;

        public XMessageBoxUI(XboxConsole console, string title, string message,
                             string[] buttons, XMessageBoxIcons icon, int selectedButtonIndex)
        {
            Console = console ?? throw new ArgumentNullException(nameof(console));
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            Buttons = buttons ?? new string[0];
            Icon = icon;
            SelectedButton = selectedButtonIndex;
        }

        #region Show

        public bool Show()
        {
            if (_isOpen)
                throw new InvalidOperationException("This XMessageBox is already open.");
            if (Buttons.Length < 1 || Buttons.Length > 3)
                throw new ArgumentException("XMessageBox requires 1-3 buttons.", nameof(Buttons));
            if (!Console.Connected)
                throw new InvalidOperationException("Console is not connected.");

            // ---------------------------------------------------------------
            // Reserve a scratch region in xam's heap by resolving an XAM
            // export and adding a small offset that lands inside its data.
            // ---------------------------------------------------------------
            uint freeMemory = Console.Rpc.ResolveFunction("xam.xex", 2601) + 0x3000;
            if (freeMemory == 0)
                throw new InvalidOperationException("ResolveFunction failed - cannot allocate Xam scratch.");

            uint xOverlapped = freeMemory;
            uint result = freeMemory + 0x20;

            // Stack on top of any other live boxes.
            lock (XMessageBoxTracking.Sync)
            {
                foreach (var mb in XMessageBoxTracking.ActiveMessageBoxes)
                    freeMemory += mb.Size;
            }

            // Write title / message / buttons into console memory.
            byte[] titleBytes = XboxExtensions.WCHAR(Title);
            uint titleAddr = result + 0x10;
            Console.SetMemory(titleAddr, titleBytes);

            byte[] msgBytes = XboxExtensions.WCHAR(Message);
            uint msgAddr = titleAddr + (uint)titleBytes.Length;
            Console.SetMemory(msgAddr, msgBytes);

            var buttonBytes = new List<byte[]>(Buttons.Length);
            foreach (var s in Buttons) buttonBytes.Add(XboxExtensions.WCHAR(s));

            uint buttonAddr = msgAddr + (uint)msgBytes.Length;
            uint origButtonAddr = buttonAddr;
            foreach (var b in buttonBytes)
            {
                Console.SetMemory(buttonAddr, b);
                buttonAddr += (uint)b.Length;
            }

            // Build the array of WCHAR* button pointers.
            uint endAddr = freeMemory;
            for (int i = 0; i < buttonBytes.Count; i++)
            {
                endAddr = buttonAddr + ((uint)i * 4);
                Console.WriteUInt32(endAddr, origButtonAddr);
                origButtonAddr += (uint)buttonBytes[i].Length;
            }

            // XamShowMessageBoxUI is xam.xex ordinal 0x2CA.
            uint xamShowMessageBoxUI = Console.Rpc.ResolveFunction("xam.xex", 0x2CA);
            const uint LocalClientIndex = 0;
            int ret = Console.Rpc.Call<int>(
                xamShowMessageBoxUI,
                LocalClientIndex,
                titleAddr,
                msgAddr,
                (uint)buttonBytes.Count,
                buttonAddr,
                (uint)SelectedButton,
                (uint)Icon,
                result,
                xOverlapped);

            if (ret != 0x3E5)
            {
                _isOpen = false;
                return false;
            }

            _isOpen = true;
            _xOverlappedAddr = xOverlapped;
            _resultAddr = _xOverlappedAddr + 0x20;

            // Snapshot the current XOVERLAPPED so we can restore it when this box closes.
            var xOverlappedBytes = Console.GetMemory(_xOverlappedAddr, 0x20);

            uint size = (endAddr + 4) - freeMemory;
            while ((size & 3) != 0) size++;

            _activeRecord = new ActiveXMessageBox(size, xOverlappedBytes);
            lock (XMessageBoxTracking.Sync)
                XMessageBoxTracking.ActiveMessageBoxes.Add(_activeRecord);

            var poller = new Thread(PollResult) { IsBackground = true, Name = "XDCKIT.XMessageBoxUI poller" };
            poller.Start();
            return true;
        }

        #endregion

        #region Polling

        private void PollResult()
        {
            try
            {
                while (_isOpen)
                {
                    ActiveXMessageBox top;
                    lock (XMessageBoxTracking.Sync)
                        top = XMessageBoxTracking.ActiveMessageBoxes.LastOrDefault();

                    // Only poll if WE are the top-most box (otherwise we'd race the next layer).
                    if (top == _activeRecord)
                    {
                        uint code = Console.ReadUInt32(_xOverlappedAddr);
                        uint userResult = 0;

                        switch (code)
                        {
                            case 0:                  // user clicked one of the buttons
                                userResult = Console.ReadUInt32(_resultAddr);
                                _isOpen = false;
                                break;
                            case 0x65b:              // user pressed B / Back
                            case 0x4c7:              // user pressed Guide
                                userResult = 420;
                                _isOpen = false;
                                break;
                            case 0x3e5:              // still pending
                                break;
                            default:                 // unhandled -> bail
                                userResult = 710;
                                _isOpen = false;
                                break;
                        }

                        if (!_isOpen)
                        {
                            RemoveFromTracking(_activeRecord);
                            MessageBoxUIResult?.Invoke(this, new XMessageBoxUIProgress(userResult, code));
                        }
                    }

                    Thread.Sleep(200);
                }
            }
            catch
            {
                // Console disconnected mid-poll: fail closed.
                _isOpen = false;
                RemoveFromTracking(_activeRecord);
            }
        }

        private void RemoveFromTracking(ActiveXMessageBox box)
        {
            if (box == null) return;
            lock (XMessageBoxTracking.Sync)
            {
                XMessageBoxTracking.ActiveMessageBoxes.Remove(box);
                // Restore whatever box is now on top by writing its XOVERLAPPED back.
                var newTop = XMessageBoxTracking.ActiveMessageBoxes.LastOrDefault();
                if (newTop != null && Console.Connected)
                {
                    try { Console.SetMemory(_xOverlappedAddr, newTop.XOverlappedBytes); }
                    catch { /* best effort */ }
                }
            }
        }

        #endregion
    }