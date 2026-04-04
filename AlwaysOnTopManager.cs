using System;
using System.Runtime.InteropServices;
using System.Windows;
using WindowsInput.Native;

namespace SmartPaste
{
    public class AlwaysOnTopManager : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            else
                return GetWindowLongPtr32(hWnd, nIndex);
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOPMOST = 0x00000008;

        private GlobalHotkey _hotkey;

        public AlwaysOnTopManager()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? new Window()).EnsureHandle();
            
            // Ctrl + Alt + T
            _hotkey = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, (uint)VirtualKeyCode.VK_T, hwnd, 9005);
            _hotkey.HotkeyPressed += (s, e) => ToggleAlwaysOnTop();
        }

        private void ToggleAlwaysOnTop()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
                bool isTopMost = (exStyle & WS_EX_TOPMOST) == WS_EX_TOPMOST;

                IntPtr insertAfter = isTopMost ? HWND_NOTOPMOST : HWND_TOPMOST;
                SetWindowPos(hWnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
        }

        public void Dispose()
        {
            _hotkey?.Dispose();
        }
    }
}