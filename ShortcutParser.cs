using System;
using System.Collections.Generic;
using WindowsInput.Native;

namespace SmartPaste
{
    public static class ShortcutParser
    {
        private static readonly Dictionary<string, uint> ModifierMap = new()
        {
            { "CTRL", GlobalHotkey.MOD_CONTROL },
            { "CONTROL", GlobalHotkey.MOD_CONTROL },
            { "ALT", GlobalHotkey.MOD_ALT },
            { "SHIFT", GlobalHotkey.MOD_SHIFT },
            { "WIN", GlobalHotkey.MOD_WIN },
        };

        private static readonly Dictionary<string, VirtualKeyCode> KeyMap = new()
        {
            { "A", VirtualKeyCode.VK_A }, { "B", VirtualKeyCode.VK_B }, { "C", VirtualKeyCode.VK_C },
            { "D", VirtualKeyCode.VK_D }, { "E", VirtualKeyCode.VK_E }, { "F", VirtualKeyCode.VK_F },
            { "G", VirtualKeyCode.VK_G }, { "H", VirtualKeyCode.VK_H }, { "I", VirtualKeyCode.VK_I },
            { "J", VirtualKeyCode.VK_J }, { "K", VirtualKeyCode.VK_K }, { "L", VirtualKeyCode.VK_L },
            { "M", VirtualKeyCode.VK_M }, { "N", VirtualKeyCode.VK_N }, { "O", VirtualKeyCode.VK_O },
            { "P", VirtualKeyCode.VK_P }, { "Q", VirtualKeyCode.VK_Q }, { "R", VirtualKeyCode.VK_R },
            { "S", VirtualKeyCode.VK_S }, { "T", VirtualKeyCode.VK_T }, { "U", VirtualKeyCode.VK_U },
            { "V", VirtualKeyCode.VK_V }, { "W", VirtualKeyCode.VK_W }, { "X", VirtualKeyCode.VK_X },
            { "Y", VirtualKeyCode.VK_Y }, { "Z", VirtualKeyCode.VK_Z },
            { "0", VirtualKeyCode.VK_0 }, { "1", VirtualKeyCode.VK_1 }, { "2", VirtualKeyCode.VK_2 },
            { "3", VirtualKeyCode.VK_3 }, { "4", VirtualKeyCode.VK_4 }, { "5", VirtualKeyCode.VK_5 },
            { "6", VirtualKeyCode.VK_6 }, { "7", VirtualKeyCode.VK_7 }, { "8", VirtualKeyCode.VK_8 },
            { "9", VirtualKeyCode.VK_9 },
            { "F1", VirtualKeyCode.F1 }, { "F2", VirtualKeyCode.F2 }, { "F3", VirtualKeyCode.F3 },
            { "F4", VirtualKeyCode.F4 }, { "F5", VirtualKeyCode.F5 }, { "F6", VirtualKeyCode.F6 },
            { "F7", VirtualKeyCode.F7 }, { "F8", VirtualKeyCode.F8 }, { "F9", VirtualKeyCode.F9 },
            { "F10", VirtualKeyCode.F10 }, { "F11", VirtualKeyCode.F11 }, { "F12", VirtualKeyCode.F12 },
            { "SPACE", VirtualKeyCode.SPACE }, { "ENTER", VirtualKeyCode.RETURN },
            { "TAB", VirtualKeyCode.TAB }, { "ESCAPE", VirtualKeyCode.ESCAPE },
            { "BACKSPACE", VirtualKeyCode.BACK }, { "DELETE", VirtualKeyCode.DELETE },
            { "INSERT", VirtualKeyCode.INSERT }, { "HOME", VirtualKeyCode.HOME },
            { "END", VirtualKeyCode.END }, { "PAGEUP", VirtualKeyCode.PRIOR },
            { "PAGEDOWN", VirtualKeyCode.NEXT },
        };

        public static bool TryParse(string shortcut, out uint modifiers, out VirtualKeyCode key)
        {
            modifiers = 0;
            key = VirtualKeyCode.VK_A;

            if (string.IsNullOrWhiteSpace(shortcut)) return false;

            var parts = shortcut.Split('+');
            if (parts.Length < 2) return false;

            foreach (var part in parts)
            {
                string trimmed = part.Trim().ToUpperInvariant();
                if (ModifierMap.TryGetValue(trimmed, out uint mod))
                {
                    modifiers |= mod;
                }
                else if (KeyMap.TryGetValue(trimmed, out VirtualKeyCode k))
                {
                    key = k;
                }
                else
                {
                    return false;
                }
            }

            return modifiers != 0;
        }

        public static string Format(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return "Not set";
            return shortcut.Replace("+", " + ");
        }
    }
}
