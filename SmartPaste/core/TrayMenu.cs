using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartPaste.Localization;

namespace SmartPaste
{
    /// <summary>
    /// Builds the system-tray context menu that exposes the whole settings surface
    /// (feature toggles, Telework options, shortcuts, colour theme, startup) directly,
    /// so a minimized app is never forced to re-open just to change a setting.
    /// The menu is self-contained on purpose: it doubles as the complete UI of the
    /// upcoming tray-only "lite" edition, built into the full app rather than forked.
    /// </summary>
    public sealed class TrayMenu
    {
        private readonly AppSettings _settings;
        private readonly Action _onSettingsChanged;      // persist settings + re-apply hotkeys/typing
        private readonly Action _onOpenWindow;
        private readonly Action _onOpenTelework;         // show window + Telework overlay (sliders live there)
        private readonly Action<string> _onEditShortcut; // show window + rebind editor for a shortcut tag
        private readonly Action _onExit;

        // Canonical brand accent colours for the theme swatches (independent of the active theme).
        private static readonly Brush AubergineSwatch = Frozen("#C6862B");
        private static readonly Brush PruneSwatch = Frozen("#C9F04A");

        // Every pill toggle registers its live getter so the menu re-syncs to the source of truth.
        private readonly List<(MenuItem item, Func<bool> get)> _toggles = new();
        // Every shortcut row registers its live gesture getter so the badge stays current.
        private readonly List<(MenuItem item, Func<string> gesture)> _shortcuts = new();
        private MenuItem _brandAubergine = null!, _brandPrune = null!;

        /// <summary>The composed WPF context menu, ready to assign to the tray icon.</summary>
        public ContextMenu Menu { get; }

        public TrayMenu(AppSettings settings, Action onSettingsChanged, Action onOpenWindow,
                        Action onOpenTelework, Action<string> onEditShortcut, Action onExit)
        {
            _settings = settings;
            _onSettingsChanged = onSettingsChanged;
            _onOpenWindow = onOpenWindow;
            _onOpenTelework = onOpenTelework;
            _onEditShortcut = onEditShortcut;
            _onExit = onExit;

            Menu = Build();
            // The menu always reflects the live source of truth, even after edits made elsewhere.
            Menu.Opened += (s, e) => SyncState();
            ThemeManager.ThemeChanged += (s, e) => SyncState();
        }

        private ContextMenu Build()
        {
            var menu = new ContextMenu();

            var open = new MenuItem { Header = Strings.TrayOpen, FontWeight = FontWeights.SemiBold };
            open.Click += (s, e) => _onOpenWindow();
            menu.Items.Add(open);
            menu.Items.Add(new Separator());

            // ── Functions ──
            menu.Items.Add(Toggle(Strings.FeatureSmartPaste,    () => _settings.EnableSmartPaste,     v => _settings.EnableSmartPaste = v));
            menu.Items.Add(Toggle(Strings.FeatureSmartCopy,     () => _settings.EnableSmartCopy,      v => _settings.EnableSmartCopy = v));
            menu.Items.Add(Toggle(Strings.FeatureCaseConverter, () => _settings.EnableCaseConverter,  v => _settings.EnableCaseConverter = v));
            menu.Items.Add(Toggle(Strings.FeatureAlwaysOnTop,   () => _settings.EnableAlwaysOnTop,    v => _settings.EnableAlwaysOnTop = v));
            menu.Items.Add(Toggle(Strings.InterceptCtrlV,       () => _settings.EnablePasteIntercept, v => _settings.EnablePasteIntercept = v));
            menu.Items.Add(new Separator());

            // ── Telework (submenu of humanization options; sliders live in the panel) ──
            menu.Items.Add(BuildTeleworkSubmenu());
            // ── Shortcuts (submenu of bindings; click a row to rebind) ──
            menu.Items.Add(BuildShortcutsSubmenu());
            menu.Items.Add(new Separator());

            // ── Colour theme (brand pair behaves as a radio group) ──
            _brandAubergine = RadioBrand(Strings.ThemeAubergine, "Aubergine", AubergineSwatch);
            _brandPrune     = RadioBrand(Strings.ThemePrune, "Prune", PruneSwatch);
            menu.Items.Add(_brandAubergine);
            menu.Items.Add(_brandPrune);
            menu.Items.Add(Toggle(Strings.TrayDarkMode,
                () => ThemeManager.CurrentMode == "Dark",
                v =>
                {
                    ThemeManager.Apply(ThemeManager.CurrentBrand, v ? "Dark" : "Light");
                    _settings.ThemeMode = ThemeManager.CurrentMode;
                }));
            menu.Items.Add(new Separator());

            // ── Startup ──
            menu.Items.Add(Toggle(Strings.StartWithWindows,
                () => _settings.AutoStart,
                v => { _settings.AutoStart = v; AutoStartManager.SetAutoStart(v); }));
            menu.Items.Add(Toggle(Strings.StartMinimized,
                () => _settings.StartMinimized,
                v => _settings.StartMinimized = v));
            menu.Items.Add(Toggle(Strings.MinimizeToTray,
                () => _settings.MinimizeToTray,
                v => _settings.MinimizeToTray = v));
            menu.Items.Add(new Separator());

            var exit = new MenuItem { Header = Strings.TrayExit };
            exit.Click += (s, e) => _onExit();
            menu.Items.Add(exit);

            return menu;
        }

        private MenuItem BuildTeleworkSubmenu()
        {
            var sub = new MenuItem { Header = Strings.TeleworkButton, Style = Res("TrayMenuSub") };

            var panel = new MenuItem { Header = Strings.TrayOpenPanel };
            panel.Click += (s, e) => _onOpenTelework();
            sub.Items.Add(panel);
            sub.Items.Add(new Separator());

            // Core rhythm
            sub.Items.Add(Toggle(Strings.TeleVariableRhythm, () => _settings.TeleVariableRhythm, v => _settings.TeleVariableRhythm = v));
            sub.Items.Add(Toggle(Strings.TeleMicroPauses,    () => _settings.TeleMicroPauses,    v => _settings.TeleMicroPauses = v));
            sub.Items.Add(Toggle(Strings.TeleFlowBursts,     () => _settings.TeleFlowBursts,     v => _settings.TeleFlowBursts = v));
            sub.Items.Add(Toggle(Strings.TeleBreathing,      () => _settings.TeleBreathingPauses, v => _settings.TeleBreathingPauses = v));
            sub.Items.Add(Toggle(Strings.TeleEndOfLine,      () => _settings.TeleEndOfLinePause, v => _settings.TeleEndOfLinePause = v));
            sub.Items.Add(new Separator());

            // Realistic errors
            sub.Items.Add(Toggle(Strings.TeleTypos,       () => _settings.TeleRealisticTypos,     v => _settings.TeleRealisticTypos = v));
            sub.Items.Add(Toggle(Strings.TeleCapsErrors,  () => _settings.TeleRandomCapsErrors,   v => _settings.TeleRandomCapsErrors = v));
            sub.Items.Add(Toggle(Strings.TeleDoubleKeys,  () => _settings.TeleDoubleKeyStrokes,   v => _settings.TeleDoubleKeyStrokes = v));
            sub.Items.Add(Toggle(Strings.TeleCursorNav,   () => _settings.TeleCursorNavigation,   v => _settings.TeleCursorNavigation = v));
            sub.Items.Add(Toggle(Strings.TeleAutoCorrect, () => _settings.TeleAutoCorrectMistakes, v => _settings.TeleAutoCorrectMistakes = v));

            return sub;
        }

        private MenuItem BuildShortcutsSubmenu()
        {
            var sub = new MenuItem { Header = Strings.TrayShortcuts, Style = Res("TrayMenuSub") };
            sub.Items.Add(Shortcut(Strings.ShortcutSmartPasteEnter,  "SmartPaste1", () => _settings.SmartPasteShortcut1));
            sub.Items.Add(Shortcut(Strings.ShortcutSmartPasteSpace,  "SmartPaste2", () => _settings.SmartPasteShortcut2));
            sub.Items.Add(Shortcut(Strings.ShortcutSmartPasteNormal, "SmartPaste3", () => _settings.SmartPasteShortcut3));
            sub.Items.Add(Shortcut(Strings.ShortcutSmartCopy,        "Copy",        () => _settings.SmartCopyShortcut));
            sub.Items.Add(Shortcut(Strings.ShortcutCaseConverter,    "Case",        () => _settings.CaseConverterShortcut));
            sub.Items.Add(Shortcut(Strings.ShortcutAlwaysOnTop,      "Aot",         () => _settings.AlwaysOnTopShortcut));
            return sub;
        }

        /// <summary>A checkable toggle rendered as a pill switch; click writes back and re-applies.</summary>
        private MenuItem Toggle(string header, Func<bool> get, Action<bool> set)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = get(),
                StaysOpenOnClick = true,   // let the user flip several settings without reopening
                Style = Res("TrayMenuToggle")
            };
            item.Click += (s, e) =>
            {
                set(item.IsChecked);       // IsChecked was already toggled by the checkable behavior
                _onSettingsChanged();
            };
            _toggles.Add((item, get));
            return item;
        }

        /// <summary>A shortcut row: feature name + current gesture badge; click opens the rebind editor.</summary>
        private MenuItem Shortcut(string label, string tag, Func<string> gesture)
        {
            var item = new MenuItem
            {
                Header = label,
                InputGestureText = ShortcutParser.Format(gesture()),
                Style = Res("TrayMenuShortcut")
            };
            item.Click += (s, e) => _onEditShortcut(tag);
            _shortcuts.Add((item, gesture));
            return item;
        }

        /// <summary>A brand entry (colour swatch + check); selecting one deselects the other.</summary>
        private MenuItem RadioBrand(string header, string brand, Brush swatch)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = ThemeManager.CurrentBrand == brand,
                StaysOpenOnClick = true,
                Style = Res("TrayMenuBrand"),
                Tag = swatch               // the swatch colour, read by the template
            };
            item.Click += (s, e) => SetBrand(brand);
            return item;
        }

        private void SetBrand(string brand)
        {
            ThemeManager.SetBrand(brand);
            _settings.ThemeBrand = ThemeManager.CurrentBrand;
            _brandAubergine.IsChecked = ThemeManager.CurrentBrand == "Aubergine";
            _brandPrune.IsChecked = ThemeManager.CurrentBrand == "Prune";
            _onSettingsChanged();
        }

        /// <summary>Re-reads every value from the live settings + theme so the menu never drifts.</summary>
        private void SyncState()
        {
            foreach (var (item, get) in _toggles)
                item.IsChecked = get();
            foreach (var (item, gesture) in _shortcuts)
                item.InputGestureText = ShortcutParser.Format(gesture());

            _brandAubergine.IsChecked = ThemeManager.CurrentBrand == "Aubergine";
            _brandPrune.IsChecked = ThemeManager.CurrentBrand == "Prune";
        }

        private static Style Res(string key) => (Style)Application.Current.FindResource(key);

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
