using System.Collections.Generic;
using System.Windows.Input;
using SmartPaste.Localization;

namespace SmartPaste
{
    /// <summary>
    /// Shared core for keyboard-shortcut capture and validation, used by BOTH the main
    /// window overlay and the tray-only capture window, so the two editing paths behave
    /// identically ("parallel management" of the same functions).
    /// </summary>
    public static class ShortcutValidation
    {
        public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

        /// <summary>
        /// Current modifiers, including the Windows key. WPF's <see cref="Keyboard.Modifiers"/> never
        /// reports Win (the shell owns it), so its live key state is polled and folded in.
        /// </summary>
        public static ModifierKeys CurrentModifiers()
        {
            var mods = Keyboard.Modifiers;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                mods |= ModifierKeys.Windows;
            return mods;
        }

        /// <summary>Maps a WPF Key to a canonical token, or null when the key is unsupported.</summary>
        public static string? KeyToToken(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return k.ToString();
            if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
            if (k >= Key.NumPad0 && k <= Key.NumPad9) return ((int)(k - Key.NumPad0)).ToString();
            if (k >= Key.F1 && k <= Key.F12) return "F" + (int)(k - Key.F1 + 1);
            return k switch
            {
                Key.Space => "SPACE", Key.Enter => "ENTER", Key.Tab => "TAB", Key.Back => "BACKSPACE",
                Key.Delete => "DELETE", Key.Insert => "INSERT", Key.Home => "HOME", Key.End => "END",
                Key.PageUp => "PAGEUP", Key.PageDown => "PAGEDOWN",
                _ => null
            };
        }

        /// <summary>True when the token names a function key (F1..F12), which is valid without a modifier.</summary>
        public static bool IsFunctionKey(string token) => token.Length >= 2 && token[0] == 'F' && char.IsDigit(token[1]);

        public static string BuildCombo(ModifierKeys m, string token)
        {
            var parts = new List<string>();
            if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(token);
            return string.Join("+", parts);
        }

        public static string DescribeMods(ModifierKeys m)
        {
            var parts = new List<string>();
            if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            return string.Join(" + ", parts);
        }

        // Canonical "mods sorted + key" for reserved/duplicate comparison.
        private static string Canonical(ModifierKeys m, string token)
        {
            var parts = new List<string>();
            if (m.HasFlag(ModifierKeys.Control)) parts.Add("CTRL");
            if (m.HasFlag(ModifierKeys.Alt)) parts.Add("ALT");
            if (m.HasFlag(ModifierKeys.Shift)) parts.Add("SHIFT");
            if (m.HasFlag(ModifierKeys.Windows)) parts.Add("WIN");
            parts.Sort();
            parts.Add(token.ToUpperInvariant());
            return string.Join("+", parts);
        }

        /// <summary>Returns a human reason when the combo is reserved by Windows, else null.</summary>
        public static string? ReservedReason(ModifierKeys m, string token)
        {
            var map = new Dictionary<string, string>
            {
                ["WIN+L"] = Strings.ReservedLock,
                ["ALT+TAB"] = Strings.ReservedTaskSwitch,
                ["ALT+F4"] = Strings.ReservedCloseWindow,
                ["WIN+D"] = Strings.ReservedShowDesktop,
                ["WIN+E"] = Strings.ReservedExplorer,
                ["WIN+R"] = Strings.ReservedRun,
                ["WIN+S"] = Strings.ReservedSearch,
                ["WIN+I"] = Strings.ReservedSettings,
                ["WIN+TAB"] = Strings.ReservedTaskView,
                ["ALT+CTRL+DELETE"] = Strings.ReservedSecurity,
                ["CTRL+SHIFT+ESCAPE"] = Strings.ReservedTaskManager,
                ["WIN+X"] = Strings.ReservedQuickLink,
                ["WIN+A"] = Strings.ReservedActionCenter,
                ["WIN+V"] = Strings.ReservedClipboardHistory,
                ["SHIFT+WIN+S"] = Strings.ReservedSnip,
                ["WIN+P"] = Strings.ReservedProject,
                ["WIN+G"] = Strings.ReservedGameBar,
                ["WIN+H"] = Strings.ReservedVoiceTyping,
                ["WIN+U"] = Strings.ReservedAccessibility,
                ["WIN+PERIOD"] = Strings.ReservedEmoji,
            };
            return map.TryGetValue(Canonical(m, token), out var reason) ? reason : null;
        }

        /// <summary>Returns the label of another shortcut already bound to this combo (excluding the tag being edited), else null.</summary>
        public static string? DuplicateOwner(AppSettings s, string combo, string? excludeTag)
        {
            string norm = combo.ToUpperInvariant();
            string editingLabel = LabelForTag(excludeTag ?? "");
            var owners = new (string val, string label)[]
            {
                (s.SmartPasteShortcut1, Strings.ShortcutSmartPasteEnter),
                (s.SmartPasteShortcut2, Strings.ShortcutSmartPasteSpace),
                (s.SmartPasteShortcut3, Strings.ShortcutSmartPasteNormal),
                (s.CaseConverterShortcut, Strings.ShortcutCaseConverter),
                (s.AlwaysOnTopShortcut, Strings.ShortcutAlwaysOnTop),
                (s.SmartCopyShortcut, Strings.ShortcutSmartCopy),
                (s.TeleworkShortcut, Strings.ShortcutTelework),
            };
            foreach (var (val, label) in owners)
                if (!string.IsNullOrEmpty(val) && val.ToUpperInvariant() == norm && label != editingLabel)
                    return label;
            return null;
        }

        public static string LabelForTag(string tag) => tag switch
        {
            "SmartPaste1" => Strings.ShortcutSmartPasteEnter,
            "SmartPaste2" => Strings.ShortcutSmartPasteSpace,
            "SmartPaste3" => Strings.ShortcutSmartPasteNormal,
            "Case" => Strings.ShortcutCaseConverter,
            "Aot" => Strings.ShortcutAlwaysOnTop,
            "Copy" => Strings.ShortcutSmartCopy,
            _ => ""
        };

        /// <summary>Writes a captured combo into the settings property identified by the tag.</summary>
        public static void ApplyToSettings(AppSettings s, string tag, string combo)
        {
            switch (tag)
            {
                case "SmartPaste1": s.SmartPasteShortcut1 = combo; break;
                case "SmartPaste2": s.SmartPasteShortcut2 = combo; break;
                case "SmartPaste3": s.SmartPasteShortcut3 = combo; break;
                case "Case": s.CaseConverterShortcut = combo; break;
                case "Aot": s.AlwaysOnTopShortcut = combo; break;
                case "Copy": s.SmartCopyShortcut = combo; break;
            }
        }
    }
}
