using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace SmartPaste
{
    public partial class MainWindow : Window
    {
        private AppSettings? _settings;
        private bool _isInitializing = true;

        // ── Shortcut editor state ──────────────────────────────────
        private Border? _activeShortcutBorder;
        private string _activeShortcutId = "";
        private string _selectedKey = "";
        private Button? _selectedKeyButton;

        // ── Shortcut descriptions for popup ────────────────────────
        private static readonly Dictionary<string, string> ShortcutDescriptions = new()
        {
            { "SP1", "Pastes text word-by-word, pressing Enter after each word. Ideal for filling keyword fields, tags, or any form requiring one entry per line." },
            { "SP2", "Pastes text word-by-word, inserting a Space between each word. Useful for reformatting delimited lists into normal spaced text." },
            { "SP3", "Types clipboard text character by character as if you were typing it manually. Works universally with any application." },
            { "SC",  "Copies web content including formatted text, math equations, and images. Experimental feature." },
            { "CC",  "Cycles selected text through different cases: lowercase → UPPERCASE → Title Case → aLtErNaTiNg." },
            { "AOT", "Pins the currently focused window so it stays above all other windows. Press again to unpin." },
            { "Tele","Opens the Telework settings tab to configure realistic typing simulation." },
        };

        // ── Known Windows shortcuts for conflict detection ─────────
        private static readonly HashSet<string> SystemShortcuts = new()
        {
            "Ctrl+A", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y",
            "Ctrl+S", "Ctrl+P", "Ctrl+O", "Ctrl+N", "Ctrl+W", "Ctrl+F",
            "Ctrl+T", "Ctrl+H", "Ctrl+J", "Ctrl+L", "Ctrl+R",
            "Alt+F4", "Alt+Tab",
            "Win+D", "Win+E", "Win+L", "Win+R", "Win+S", "Win+I", "Win+Tab",
        };

        public MainWindow()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            _settings = app.Settings;

            if (_settings != null)
            {
                ChkStartMinimized.IsChecked = _settings.StartMinimized;
                ChkAutoStart.IsChecked = _settings.AutoStart;

                ChkEnableSP.IsChecked = _settings.EnableSmartPaste;
                ChkEnableCC.IsChecked = _settings.EnableCaseConverter;
                ChkEnableAOT.IsChecked = _settings.EnableAlwaysOnTop;

                ChkEnableSCBeta.IsChecked = _settings.EnableSmartCopy;

                TxtSP1.Text = FormatShortcut(_settings.SmartPasteShortcut1);
                TxtSP2.Text = FormatShortcut(_settings.SmartPasteShortcut2);
                TxtSP3.Text = FormatShortcut(_settings.SmartPasteShortcut3);
                TxtSC.Text = FormatShortcut(_settings.SmartCopyShortcut);
                TxtCC.Text = FormatShortcut(_settings.CaseConverterShortcut);
                TxtAOT.Text = FormatShortcut(_settings.AlwaysOnTopShortcut);
                TxtTele.Text = FormatShortcut(_settings.TeleworkShortcut);

                ChkTeleVariable.IsChecked = _settings.TeleVariableRhythm;
                ChkTeleMicroPauses.IsChecked = _settings.TeleMicroPauses;
                ChkTeleFlowBursts.IsChecked = _settings.TeleFlowBursts;
                ChkTeleBreathing.IsChecked = _settings.TeleBreathingPauses;
                ChkTeleEndOfLine.IsChecked = _settings.TeleEndOfLinePause;

                ChkTeleTypos.IsChecked = _settings.TeleRealisticTypos;
                ChkTeleCapsErrors.IsChecked = _settings.TeleRandomCapsErrors;
                ChkTeleDoubleKey.IsChecked = _settings.TeleDoubleKeyStrokes;
                ChkTeleCursorNav.IsChecked = _settings.TeleCursorNavigation;
                ChkTeleAutoCorrect.IsChecked = _settings.TeleAutoCorrectMistakes;

                SldTeleDelay.Value = _settings.TelePasteDelay;
                SldTeleChunk.Value = _settings.TeleWordChunkSize;
                SldTeleBreath.Value = _settings.TeleBreathingInterval;

                SldSpeed.Value = _settings.DelayMilliseconds;
            }

            GenerateKeyboard();

            _isInitializing = false;
            UpdateHomeStatus();
        }

        // ════════════════════════════════════════════════════════════ //
        //  VIRTUAL KEYBOARD — shortcut editor                           //
        // ════════════════════════════════════════════════════════════ //

        private static readonly string[][] KeyboardRows =
        {
            new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" },
            new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L" },
            new[] { "Z", "X", "C", "V", "B", "N", "M" },
            new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
            new[] { "F1", "F2", "F3", "F4", "F5", "F6" },
            new[] { "F7", "F8", "F9", "F10", "F11", "F12" },
            new[] { "Space", "Enter", "Tab" },
        };

        private void GenerateKeyboard()
        {
            var keyStyle = (Style)FindResource("KeyBtn");

            foreach (var row in KeyboardRows)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };

                foreach (var key in row)
                {
                    var btn = new Button
                    {
                        Content = key,
                        Style = keyStyle,
                        Tag = key,
                    };

                    if (key == "Space" || key == "Enter" || key == "Tab")
                        btn.Width = 70;

                    btn.Click += KeyButton_Click;
                    rowPanel.Children.Add(btn);
                }

                KeyRowsPanel.Children.Add(rowPanel);
            }
        }

        private void ShortcutBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                string id = GetShortcutId(border);
                string? currentShortcut = GetShortcutValue(id);

                OpenShortcutPopup(border, id, currentShortcut);
                e.Handled = true;
            }
        }

        private void OpenShortcutPopup(Border border, string id, string? currentShortcut)
        {
            _activeShortcutBorder = border;
            _activeShortcutId = id;

            // Set title and description
            string label = GetShortcutLabel(id);
            PopupTitle.Text = $"Configure — {label}";

            if (ShortcutDescriptions.TryGetValue(id, out var desc))
                PopupDescription.Text = desc;
            else
                PopupDescription.Text = "";

            // Parse current shortcut to pre-select modifiers and key
            ResetKeyboardSelection();
            ParseShortcutToPopup(currentShortcut);

            UpdatePopupPreview();

            // Show backdrop + popup
            ShortcutBackdrop.Visibility = Visibility.Visible;
            ShortcutPopup.PlacementTarget = this;
            ShortcutPopup.IsOpen = true;
        }

        private void CloseShortcutPopup()
        {
            ShortcutPopup.IsOpen = false;
            ShortcutBackdrop.Visibility = Visibility.Collapsed;
            _activeShortcutBorder = null;
            _activeShortcutId = "";
            _selectedKey = "";
            _selectedKeyButton = null;
        }

        private void ParseShortcutToPopup(string? shortcut)
        {
            ModCtrl.IsChecked = false;
            ModShift.IsChecked = false;
            ModAlt.IsChecked = false;
            ModWin.IsChecked = false;

            if (string.IsNullOrWhiteSpace(shortcut)) return;

            var parts = shortcut.Split('+');
            foreach (var part in parts)
            {
                string p = part.Trim();

                if (p.Equals("Ctrl", System.StringComparison.OrdinalIgnoreCase))
                    ModCtrl.IsChecked = true;
                else if (p.Equals("Shift", System.StringComparison.OrdinalIgnoreCase))
                    ModShift.IsChecked = true;
                else if (p.Equals("Alt", System.StringComparison.OrdinalIgnoreCase))
                    ModAlt.IsChecked = true;
                else if (p.Equals("Win", System.StringComparison.OrdinalIgnoreCase))
                    ModWin.IsChecked = true;
                else
                    SelectKeyByName(p);
            }
        }

        private void ResetKeyboardSelection()
        {
            _selectedKey = "";
            if (_selectedKeyButton != null)
            {
                ResetKeyButtonVisual(_selectedKeyButton);
                _selectedKeyButton = null;
            }
        }

        private void SelectKeyByName(string keyName)
        {
            foreach (var rowPanel in KeyRowsPanel.Children.OfType<StackPanel>())
            {
                foreach (var btn in rowPanel.Children.OfType<Button>())
                {
                    if ((btn.Tag as string)?.Equals(keyName, System.StringComparison.OrdinalIgnoreCase) == true)
                    {
                        SelectKeyButton(btn);
                        return;
                    }
                }
            }
        }

        private void SelectKeyButton(Button btn)
        {
            if (_selectedKeyButton != null)
                ResetKeyButtonVisual(_selectedKeyButton);

            _selectedKeyButton = btn;
            _selectedKey = btn.Tag as string ?? "";

            btn.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF));
            btn.Foreground = Brushes.White;
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF));
        }

        private static void ResetKeyButtonVisual(Button btn)
        {
            btn.Background = Brushes.White;
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));
        }

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                SelectKeyButton(btn);
                UpdatePopupPreview();
            }
        }

        private void Modifier_Click(object sender, RoutedEventArgs e)
        {
            UpdatePopupPreview();
        }

        private void UpdatePopupPreview()
        {
            var parts = new List<string>();
            if (ModCtrl.IsChecked == true) parts.Add("Ctrl");
            if (ModShift.IsChecked == true) parts.Add("Shift");
            if (ModAlt.IsChecked == true) parts.Add("Alt");
            if (ModWin.IsChecked == true) parts.Add("Win");

            bool hasKey = !string.IsNullOrEmpty(_selectedKey);
            bool hasCtrlAltWin = ModCtrl.IsChecked == true || ModAlt.IsChecked == true || ModWin.IsChecked == true;

            // Build preview text
            if (parts.Count > 0 && hasKey)
            {
                parts.Add(_selectedKey);
                PopupPreview.Text = string.Join(" + ", parts);
            }
            else if (parts.Count > 0)
            {
                PopupPreview.Text = string.Join(" + ", parts) + " + ...";
            }
            else if (hasKey)
            {
                PopupPreview.Text = "... + " + _selectedKey;
            }
            else
            {
                PopupPreview.Text = "Select modifiers and a key";
            }

            // Validation
            PopupErrorBox.Visibility = Visibility.Collapsed;
            PopupWarningBox.Visibility = Visibility.Collapsed;

            if (!hasCtrlAltWin)
            {
                PopupError.Text = "At least one of Ctrl, Alt, or Win is required. Shift alone conflicts with normal typing.";
                PopupErrorBox.Visibility = Visibility.Visible;
                PopupApplyBtn.IsEnabled = false;
                return;
            }

            if (!hasKey)
            {
                PopupError.Text = "Please select a key from the keyboard below.";
                PopupErrorBox.Visibility = Visibility.Visible;
                PopupApplyBtn.IsEnabled = false;
                return;
            }

            PopupApplyBtn.IsEnabled = true;

            // Conflict detection
            var checkParts = new List<string>();
            if (ModCtrl.IsChecked == true) checkParts.Add("Ctrl");
            if (ModShift.IsChecked == true) checkParts.Add("Shift");
            if (ModAlt.IsChecked == true) checkParts.Add("Alt");
            if (ModWin.IsChecked == true) checkParts.Add("Win");
            checkParts.Add(_selectedKey);
            string shortcutStr = string.Join("+", checkParts);

            if (SystemShortcuts.Contains(shortcutStr))
            {
                PopupWarning.Text = $"\"{shortcutStr}\" is a common system shortcut. It may conflict with Windows behavior. Consider adding Shift to differentiate.";
                PopupWarningBox.Visibility = Visibility.Visible;
            }
        }

        private void PopupApply_Click(object sender, RoutedEventArgs e)
        {
            if (!PopupApplyBtn.IsEnabled) return;
            if (_settings == null || _activeShortcutBorder == null) return;

            var parts = new List<string>();
            if (ModCtrl.IsChecked == true) parts.Add("Ctrl");
            if (ModShift.IsChecked == true) parts.Add("Shift");
            if (ModAlt.IsChecked == true) parts.Add("Alt");
            if (ModWin.IsChecked == true) parts.Add("Win");
            parts.Add(_selectedKey);

            string shortcut = string.Join("+", parts);

            if (!ShortcutParser.TryParse(shortcut, out _, out _))
            {
                PopupError.Text = "This key combination is not supported.";
                PopupErrorBox.Visibility = Visibility.Visible;
                return;
            }

            ApplyShortcutToBorder(shortcut);
            CloseShortcutPopup();
        }

        private void ApplyShortcutToBorder(string shortcut)
        {
            if (_settings == null || _activeShortcutBorder == null) return;

            string display = FormatShortcut(shortcut);

            switch (_activeShortcutId)
            {
                case "SP1": _settings.SmartPasteShortcut1 = shortcut; TxtSP1.Text = display; break;
                case "SP2": _settings.SmartPasteShortcut2 = shortcut; TxtSP2.Text = display; break;
                case "SP3": _settings.SmartPasteShortcut3 = shortcut; TxtSP3.Text = display; break;
                case "SC":  _settings.SmartCopyShortcut = shortcut; TxtSC.Text = display; break;
                case "CC":  _settings.CaseConverterShortcut = shortcut; TxtCC.Text = display; break;
                case "AOT": _settings.AlwaysOnTopShortcut = shortcut; TxtAOT.Text = display; break;
                case "Tele":_settings.TeleworkShortcut = shortcut; TxtTele.Text = display; break;
            }

            Save();
            ((App)Application.Current).RefreshHotkeys();
            UpdateHomeStatus();
        }

        private void PopupCancel_Click(object sender, RoutedEventArgs e)
        {
            CloseShortcutPopup();
        }

        private void ShortcutBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseShortcutPopup();
        }

        // ── Shortcut ID / value helpers ────────────────────────────

        private static string GetShortcutId(Border border) => border.Name switch
        {
            "BorderSP1" => "SP1",
            "BorderSP2" => "SP2",
            "BorderSP3" => "SP3",
            "BorderSC"  => "SC",
            "BorderCC"  => "CC",
            "BorderAOT" => "AOT",
            "BorderTele"=> "Tele",
            _ => ""
        };

        private static string GetShortcutLabel(string id) => id switch
        {
            "SP1" => "Smart Paste (Enter)",
            "SP2" => "Smart Paste (Space)",
            "SP3" => "Smart Paste (Normal)",
            "SC"  => "Smart Copy (Beta)",
            "CC"  => "Case Converter",
            "AOT" => "Always On Top",
            "Tele"=> "Telework Mode",
            _ => "Shortcut"
        };

        private string? GetShortcutValue(string id) => id switch
        {
            "SP1" => _settings?.SmartPasteShortcut1,
            "SP2" => _settings?.SmartPasteShortcut2,
            "SP3" => _settings?.SmartPasteShortcut3,
            "SC"  => _settings?.SmartCopyShortcut,
            "CC"  => _settings?.CaseConverterShortcut,
            "AOT" => _settings?.AlwaysOnTopShortcut,
            "Tele"=> _settings?.TeleworkShortcut,
            _ => null
        };

        private static string FormatShortcut(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return "";
            return shortcut.Replace("+", " + ");
        }

        // ════════════════════════════════════════════════════════════ //
        //  HOME PAGE                                                    //
        // ════════════════════════════════════════════════════════════ //

        private void HomeCardShortcuts_Click(object sender, MouseButtonEventArgs e) => SwitchToTab(1);
        private void HomeCardTelework_Click(object sender, MouseButtonEventArgs e) => SwitchToTab(2);
        private void HomeCardFunctions_Click(object sender, MouseButtonEventArgs e) => SwitchToTab(3);
        private void HomeCardBeta_Click(object sender, MouseButtonEventArgs e) => SwitchToTab(4);
        private void HomeCardAbout_Click(object sender, MouseButtonEventArgs e) => SwitchToTab(5);

        private void QuickLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            if (e.Uri.OriginalString == "tab:shortcuts") SwitchToTab(1);
            else if (e.Uri.OriginalString == "tab:about") SwitchToTab(5);
            e.Handled = true;
        }

        private void UpdateHomeStatus()
        {
            if (_settings == null) return;

            var green = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            var gray  = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            var amber = new SolidColorBrush(Color.FromRgb(217, 119, 6));

            if (TxtHomeSpStatus != null)
            {
                bool on = _settings.EnableSmartPaste;
                TxtHomeSpStatus.Text = on ? "Active" : "Disabled";
                TxtHomeSpStatus.Foreground = on ? green : gray;
            }
            if (TxtHomeCcStatus != null)
            {
                bool on = _settings.EnableCaseConverter;
                TxtHomeCcStatus.Text = on ? "Active" : "Disabled";
                TxtHomeCcStatus.Foreground = on ? green : gray;
            }
            if (TxtHomeAotStatus != null)
            {
                bool on = _settings.EnableAlwaysOnTop;
                TxtHomeAotStatus.Text = on ? "Active" : "Disabled";
                TxtHomeAotStatus.Foreground = on ? green : gray;
            }
            if (TxtHomeScStatus != null)
            {
                bool on = _settings.EnableSmartCopy;
                TxtHomeScStatus.Text = on ? "Enabled (Beta)" : "Disabled";
                TxtHomeScStatus.Foreground = on ? amber : gray;
            }
        }

        // ════════════════════════════════════════════════════════════ //
        //  SETTINGS HANDLERS                                            //
        // ════════════════════════════════════════════════════════════ //

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        public void SwitchToTab(int index)
        {
            if (this.Content is Grid grid)
            {
                var tabControl = grid.Children.OfType<TabControl>().FirstOrDefault();
                if (tabControl != null && index >= 0 && index < tabControl.Items.Count)
                {
                    tabControl.SelectedIndex = index;
                }
            }
        }

        private void Save()
        {
            if (_settings != null) SettingsManager.Save(_settings);
        }

        private void ChkStartMinimized_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null || !ChkStartMinimized.IsChecked.HasValue) return;
            _settings.StartMinimized = ChkStartMinimized.IsChecked.Value;
            Save();
        }

        private void ChkAutoStart_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null || !ChkAutoStart.IsChecked.HasValue) return;
            _settings.AutoStart = ChkAutoStart.IsChecked.Value;
            Save();
            AutoStartManager.SetAutoStart(_settings.AutoStart);
        }

        private void Func_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null) return;
            if (ChkEnableSP.IsChecked.HasValue)  _settings.EnableSmartPaste    = ChkEnableSP.IsChecked.Value;
            if (ChkEnableCC.IsChecked.HasValue)  _settings.EnableCaseConverter  = ChkEnableCC.IsChecked.Value;
            if (ChkEnableAOT.IsChecked.HasValue) _settings.EnableAlwaysOnTop   = ChkEnableAOT.IsChecked.Value;
            Save();
            ((App)Application.Current).RefreshHotkeys();
            UpdateHomeStatus();
        }

        private void Beta_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null) return;
            if (ChkEnableSCBeta.IsChecked.HasValue) _settings.EnableSmartCopy = ChkEnableSCBeta.IsChecked.Value;
            Save();
            ((App)Application.Current).RefreshHotkeys();
            UpdateHomeStatus();
        }

        private void Tele_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null) return;
            if (ChkTeleVariable.IsChecked.HasValue)    _settings.TeleVariableRhythm     = ChkTeleVariable.IsChecked.Value;
            if (ChkTeleMicroPauses.IsChecked.HasValue) _settings.TeleMicroPauses        = ChkTeleMicroPauses.IsChecked.Value;
            if (ChkTeleFlowBursts.IsChecked.HasValue)  _settings.TeleFlowBursts         = ChkTeleFlowBursts.IsChecked.Value;
            if (ChkTeleBreathing.IsChecked.HasValue)   _settings.TeleBreathingPauses    = ChkTeleBreathing.IsChecked.Value;
            if (ChkTeleEndOfLine.IsChecked.HasValue)   _settings.TeleEndOfLinePause     = ChkTeleEndOfLine.IsChecked.Value;
            if (ChkTeleTypos.IsChecked.HasValue)       _settings.TeleRealisticTypos     = ChkTeleTypos.IsChecked.Value;
            if (ChkTeleCapsErrors.IsChecked.HasValue)  _settings.TeleRandomCapsErrors   = ChkTeleCapsErrors.IsChecked.Value;
            if (ChkTeleDoubleKey.IsChecked.HasValue)   _settings.TeleDoubleKeyStrokes   = ChkTeleDoubleKey.IsChecked.Value;
            if (ChkTeleCursorNav.IsChecked.HasValue)   _settings.TeleCursorNavigation   = ChkTeleCursorNav.IsChecked.Value;
            if (ChkTeleAutoCorrect.IsChecked.HasValue) _settings.TeleAutoCorrectMistakes= ChkTeleAutoCorrect.IsChecked.Value;
            Save();
        }

        private void SldTeleDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _settings == null) return;
            _settings.TelePasteDelay = (int)e.NewValue;
            TxtTeleDelay.Text = ((int)e.NewValue).ToString();
            Save();
        }

        private void SldTeleChunk_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _settings == null) return;
            _settings.TeleWordChunkSize = (int)e.NewValue;
            TxtTeleChunk.Text = ((int)e.NewValue).ToString();
            Save();
        }

        private void SldTeleBreath_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _settings == null) return;
            _settings.TeleBreathingInterval = (int)e.NewValue;
            TxtTeleBreath.Text = ((int)e.NewValue).ToString();
            Save();
        }

        private void SldSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            int val = (int)e.NewValue;
            if (_settings != null)
            {
                _settings.DelayMilliseconds = val;
                Save();
            }
            if (TxtSpeedLabel != null)
                TxtSpeedLabel.Text = $"Base delay: {val} ms";
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _settings = new AppSettings();
            SettingsManager.Save(_settings);

            ChkStartMinimized.IsChecked = false;
            ChkAutoStart.IsChecked = false;
            ChkEnableSP.IsChecked = true;
            ChkEnableSCBeta.IsChecked = false;
            ChkEnableCC.IsChecked = true;
            ChkEnableAOT.IsChecked = true;

            TxtSP1.Text = FormatShortcut(_settings.SmartPasteShortcut1);
            TxtSP2.Text = FormatShortcut(_settings.SmartPasteShortcut2);
            TxtSP3.Text = FormatShortcut(_settings.SmartPasteShortcut3);
            TxtSC.Text = FormatShortcut(_settings.SmartCopyShortcut);
            TxtCC.Text = FormatShortcut(_settings.CaseConverterShortcut);
            TxtAOT.Text = FormatShortcut(_settings.AlwaysOnTopShortcut);
            TxtTele.Text = FormatShortcut(_settings.TeleworkShortcut);

            ChkTeleVariable.IsChecked = true;
            ChkTeleMicroPauses.IsChecked = true;
            ChkTeleFlowBursts.IsChecked = true;
            ChkTeleBreathing.IsChecked = true;
            ChkTeleEndOfLine.IsChecked = true;
            ChkTeleTypos.IsChecked = false;
            ChkTeleCapsErrors.IsChecked = false;
            ChkTeleDoubleKey.IsChecked = false;
            ChkTeleCursorNav.IsChecked = false;
            ChkTeleAutoCorrect.IsChecked = false;

            SldTeleDelay.Value = 100;
            SldTeleChunk.Value = 5;
            SldTeleBreath.Value = 15;
            SldSpeed.Value = 30;

            AutoStartManager.SetAutoStart(false);

            ((App)Application.Current).RefreshHotkeys();
            UpdateHomeStatus();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
