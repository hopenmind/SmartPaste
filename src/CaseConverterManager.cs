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
        private GlobalHotkey? _hotkey;
        private InputSimulator _simulator = new InputSimulator();

        public void RegisterHotkey(IntPtr hwnd, string shortcut)
        {
            UnregisterHotkey();
            if (ShortcutParser.TryParse(shortcut, out uint modifiers, out VirtualKeyCode key))
            {
                _hotkey = new GlobalHotkey(modifiers, (uint)key, hwnd, 9004);
                _hotkey.HotkeyPressed += (s, e) => ToggleCase();
            }
        }

        public void UnregisterHotkey()
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }

        private async void ToggleCase()
        {
            _simulator ??= new InputSimulator();
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
            await Task.Delay(100);

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
            UnregisterHotkey();
        }
    }
}
