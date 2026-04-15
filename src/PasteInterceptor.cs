using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartPaste
{
    /// <summary>
    /// Intercepts the native Ctrl+V when SmartCopy content is available.
    ///
    /// Architecture:
    ///   1. Low-level keyboard hook (WH_KEYBOARD_LL) monitors all keystrokes
    ///   2. Clipboard format listener tracks whether SmartCopy content exists
    ///   3. When Ctrl+V is pressed AND SmartCopy content is tagged on clipboard:
    ///      → Suppress the keystroke
    ///      → Dispatch SmartInject on the UI thread
    ///      → SmartInject detects target app, sets optimal format, re-sends Ctrl+V
    ///   4. When no SmartCopy content → Ctrl+V passes through untouched
    ///
    /// The user never notices — Ctrl+V just works better.
    /// </summary>
    public class PasteInterceptor : IDisposable
    {
        // ── P/Invoke ─────────────────────────────────────────────────

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk,
            int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        // ── Constants ────────────────────────────────────────────────

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int VK_C = 0x43;
        private const int VK_V = 0x56;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        // ── State ────────────────────────────────────────────────────

        private readonly LowLevelKeyboardProc _hookProc;  // prevent GC
        private IntPtr _hookId;
        private readonly IntPtr _hwnd;
        private readonly Action _onSmartPaste;
        private readonly Action? _onSmartCopy;
        private HwndSource? _hwndSource;

        /// <summary>Fast in-memory flag — set by clipboard monitor, no disk I/O.</summary>
        private volatile bool _hasSmartContent;

        /// <summary>True while SmartInject is setting clipboard + sending Ctrl+V.</summary>
        private volatile bool _isInjecting;

        /// <summary>Master switch for Ctrl+V interception.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>When true, Ctrl+C triggers SmartCopy enhancement after normal copy.</summary>
        public bool OverrideCtrlC { get; set; } = false;

        // ── Construction ─────────────────────────────────────────────

        public PasteInterceptor(IntPtr hwnd, Action onSmartPaste, Action? onSmartCopy = null)
        {
            _hwnd = hwnd;
            _onSmartPaste = onSmartPaste;
            _onSmartCopy = onSmartCopy;

            // Install low-level keyboard hook
            _hookProc = HookCallback;
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                GetModuleHandle(module.ModuleName), 0);

            // Register for clipboard change notifications
            AddClipboardFormatListener(hwnd);
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        // ── Injection control (called by App.xaml.cs) ────────────────

        /// <summary>Call before SmartInject modifies the clipboard.</summary>
        public void BeginInject() => _isInjecting = true;

        /// <summary>Call after the injected Ctrl+V has been processed.</summary>
        public void EndInject() => _isInjecting = false;

        // ── Keyboard hook ────────────────────────────────────────────

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == WM_KEYDOWN && !_isInjecting)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // ── Ctrl+V interception (suppress + SmartInject) ──
                if (vkCode == VK_V && Enabled && _hasSmartContent && IsPureCtrl())
                {
                    Application.Current?.Dispatcher.BeginInvoke(_onSmartPaste);
                    return (IntPtr)1; // Suppress
                }

                // ── Ctrl+C interception (pass through + enhance after) ──
                if (vkCode == VK_C && OverrideCtrlC && _onSmartCopy != null && IsPureCtrl())
                {
                    // Let the normal Ctrl+C happen, then enhance the clipboard
                    Application.Current?.Dispatcher.BeginInvoke(_onSmartCopy);
                    // Do NOT suppress — fall through to CallNextHookEx
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Returns true only if Ctrl is held with NO other modifiers.
        /// </summary>
        private static bool IsPureCtrl()
        {
            bool ctrl  = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VK_SHIFT)   & 0x8000) != 0;
            bool alt   = (GetAsyncKeyState(VK_MENU)    & 0x8000) != 0;
            bool win   = (GetAsyncKeyState(VK_LWIN)    & 0x8000) != 0
                      || (GetAsyncKeyState(VK_RWIN)    & 0x8000) != 0;

            return ctrl && !shift && !alt && !win;
        }

        // ── Clipboard monitoring ─────────────────────────────────────

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
            IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE && !_isInjecting)
            {
                OnClipboardChanged();
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Called on every clipboard change. Checks whether our CopyId tag
        /// is present — if so, SmartCopy content is available for injection.
        /// </summary>
        private void OnClipboardChanged()
        {
            try
            {
                IDataObject? clip = Clipboard.GetDataObject();
                _hasSmartContent = clip?.GetDataPresent(FormatCache.CopyIdFormat) == true;
            }
            catch
            {
                // Clipboard locked by another app — keep previous state
            }
        }

        // ── Disposal ─────────────────────────────────────────────────

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            RemoveClipboardFormatListener(_hwnd);
            _hwndSource?.RemoveHook(WndProc);
        }
    }
}
