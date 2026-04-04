using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace SmartPaste
{
    public class CaseConverterManager : IDisposable
    {
        private GlobalHotkey _hotkey;
        private InputSimulator _simulator;

        public CaseConverterManager()
        {
            _simulator = new InputSimulator();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? new Window()).EnsureHandle();
            
            // Ctrl + Win + C
            _hotkey = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_WIN, (uint)VirtualKeyCode.VK_C, hwnd, 9004);
            _hotkey.HotkeyPressed += (s, e) => ToggleCase();
        }

        private async void ToggleCase()
        {
            // Simulate Ctrl+C
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
            
            await Task.Delay(100); // Wait for copy to clipboard
            
            string text = string.Empty;
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }
            
            if (!string.IsNullOrEmpty(text))
            {
                string newText = text;
                string titleCase = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());
                
                if (text == text.ToLower())
                {
                    newText = text.ToUpper();
                }
                else if (text == text.ToUpper())
                {
                    newText = titleCase;
                }
                else if (text == titleCase)
                {
                    // Alternating case (SpongeBob case)
                    char[] chars = text.ToCharArray();
                    bool upper = false;
                    for (int i = 0; i < chars.Length; i++)
                    {
                        if (char.IsLetter(chars[i]))
                        {
                            chars[i] = upper ? char.ToUpper(chars[i]) : char.ToLower(chars[i]);
                            upper = !upper;
                        }
                    }
                    newText = new string(chars);
                }
                else
                {
                    newText = text.ToLower();
                }
                
                Clipboard.SetText(newText);
                await Task.Delay(50);
                
                _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
            }
        }

        public void Dispose()
        {
            _hotkey?.Dispose();
        }
    }
}