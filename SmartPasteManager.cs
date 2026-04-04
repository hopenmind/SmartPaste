using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace SmartPaste
{
    public class SmartPasteManager : IDisposable
    {
        private GlobalHotkey _hotkeyMode1;
        private GlobalHotkey _hotkeyMode2;
        private GlobalHotkey _hotkeyMode3;
        private InputSimulator _simulator;

        public int DelayMilliseconds { get; set; } = 30; // Typing speed

        public SmartPasteManager()
        {
            _simulator = new InputSimulator();
            
            // Mode 1: Ctrl + Shift + V
            var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? new Window()).EnsureHandle();
            
            _hotkeyMode1 = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT, (uint)VirtualKeyCode.VK_V, hwnd, 9001);
            _hotkeyMode1.HotkeyPressed += (s, e) => Paste(1);

            // Mode 2: Ctrl + Alt + V
            _hotkeyMode2 = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, (uint)VirtualKeyCode.VK_V, hwnd, 9002);
            _hotkeyMode2.HotkeyPressed += (s, e) => Paste(2);

            // Mode 3: Ctrl + Win + V
            _hotkeyMode3 = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_WIN, (uint)VirtualKeyCode.VK_V, hwnd, 9003);
            _hotkeyMode3.HotkeyPressed += (s, e) => Paste(3);
        }

        private async void Paste(int mode)
        {
            if (!Clipboard.ContainsText()) return;
            string text = Clipboard.GetText();
            
            if (string.IsNullOrWhiteSpace(text)) return;

            // L'application cible a besoin d'un court instant pour retrouver son focus
            // après l'interception du raccourci
            await Task.Delay(100);

            await Task.Run(() =>
            {
                switch (mode)
                {
                    case 1:
                        PasteMode1(text);
                        break;
                    case 2:
                        PasteMode2(text);
                        break;
                    case 3:
                        PasteMode3(text);
                        break;
                }
            });
        }

        private void PasteMode1(string text)
        {
            // Mode 1: Frappe normale, lettre par lettre
            _simulator.Keyboard.TextEntry(text);
        }

        private void PasteMode2(string text)
        {
            // Mode 2: Mot par mot avec ESPACE (on utilise le séparateur choisi)
            string[] items = SplitText(text);
            for (int i = 0; i < items.Length; i++)
            {
                _simulator.Keyboard.TextEntry(items[i]);
                if (i < items.Length - 1)
                {
                    Thread.Sleep(DelayMilliseconds);
                    _simulator.Keyboard.KeyPress(VirtualKeyCode.SPACE);
                    Thread.Sleep(DelayMilliseconds);
                }
            }
        }

        private void PasteMode3(string text)
        {
            // Mode 3: Mot par mot avec ENTREE
            string[] items = SplitText(text);
            for (int i = 0; i < items.Length; i++)
            {
                _simulator.Keyboard.TextEntry(items[i]);
                Thread.Sleep(DelayMilliseconds);
                _simulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                Thread.Sleep(DelayMilliseconds);
            }
        }

        private string[] SplitText(string text)
        {
            char[] delimiters = new[] 
            { 
                '\r', '\n', ',', ';', ':', '.', '/', '\\', '|', '•', '·', 
                '，', '；', '：', '。', '、', '｜' 
            };

            if (text.IndexOfAny(delimiters) >= 0)
            {
                return text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .ToArray();
            }
            
            // Fallback: If no strong delimiters are found, assume it's separated by spaces
            return text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .ToArray();
        }

        public void Dispose()
        {
            _hotkeyMode1?.Dispose();
            _hotkeyMode2?.Dispose();
            _hotkeyMode3?.Dispose();
        }
    }
}