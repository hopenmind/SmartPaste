using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SmartPaste
{
    public class GlobalHotkey : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        private IntPtr _hWnd;
        private int _id;

        public event EventHandler HotkeyPressed;

        public GlobalHotkey(uint modifiers, uint key, IntPtr hWnd, int id)
        {
            _hWnd = hWnd;
            _id = id;
            RegisterHotKey(hWnd, id, modifiers, key);
            ComponentDispatcher.ThreadPreprocessMessage += ThreadPreprocessMessageMethod;
        }

        private void ThreadPreprocessMessageMethod(ref MSG msg, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (!handled && msg.message == WM_HOTKEY && (int)msg.wParam == _id)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
        }

        public void Dispose()
        {
            UnregisterHotKey(_hWnd, _id);
            ComponentDispatcher.ThreadPreprocessMessage -= ThreadPreprocessMessageMethod;
        }
    }
}
