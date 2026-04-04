using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;

namespace SmartPaste
{
    public partial class MainWindow : Window
    {
        private AppSettings? _settings;
        private bool _isInitializing = true;
        private Border? _activeShortcutBorder;
        private HashSet<string> _pressedKeys = new();
        private bool _isCapturing;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private static readonly Dictionary<int, string> VKeyToName = new()
        {
            { 0x10, "Shift" }, { 0x11, "Ctrl" }, { 0x12, "Alt" },
            { 0x5B, "Win" }, { 0x5C, "Win" },
            { 0x41, "A" }, { 0x42, "B" }, { 0x43, "C" }, { 0x44, "D" },
            { 0x45, "E" }, { 0x46, "F" }, { 0x47, "G" }, { 0x48, "H" },
            { 0x49, "I" }, { 0x4A, "J" }, { 0x4B, "K" }, { 0x4C, "L" },
            { 0x4D, "M" }, { 0x4E, "N" }, { 0x4F, "O" }, { 0x50, "P" },
            { 0x51, "Q" }, { 0x52, "R" }, { 0x53, "S" }, { 0x54, "T" },
            { 0x55, "U" }, { 0x56, "V" }, { 0x57, "W" }, { 0x58, "X" },
            { 0x59, "Y" }, { 0x5A, "Z" },
            { 0x30, "0" }, { 0x31, "1" }, { 0x32, "2" }, { 0x33, "3" },
            { 0x34, "4" }, { 0x35, "5" }, { 0x36, "6" }, { 0x37, "7" },
            { 0x38, "8" }, { 0x39, "9" },
            { 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" }, { 0x73, "F4" },
            { 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" }, { 0x77, "F8" },
            { 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" },
            { 0x20, "Space" }, { 0x0D, "Enter" }, { 0x09, "Tab" },
            { 0x1B, "Escape" }, { 0x08, "Backspace" }, { 0x2E, "Delete" },
            { 0x24, "Home" }, { 0x23, "End" }, { 0x21, "PageUp" },
            { 0x22, "PageDown" }, { 0x2D, "Insert" },
            { 0x25, "Left" }, { 0x26, "Up" }, { 0x27, "Right" }, { 0x28, "Down" },
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
                ChkEnableSC.IsChecked = _settings.EnableSmartCopy;
                ChkEnableCC.IsChecked = _settings.EnableCaseConverter;
                ChkEnableAOT.IsChecked = _settings.EnableAlwaysOnTop;

                TxtSP1.Text = _settings.SmartPasteShortcut1;
                TxtSP2.Text = _settings.SmartPasteShortcut2;
                TxtSP3.Text = _settings.SmartPasteShortcut3;
                TxtSC.Text = _settings.SmartCopyShortcut;
                TxtCC.Text = _settings.CaseConverterShortcut;
                TxtAOT.Text = _settings.AlwaysOnTopShortcut;
                TxtTele.Text = _settings.TeleworkShortcut;

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

            _isInitializing = false;

            ComponentDispatcher.ThreadPreprocessMessage += ThreadPreprocessMessage;
        }

        private void ThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            if (!_isCapturing) return;

            if (msg.message == WM_KEYDOWN || msg.message == WM_SYSKEYDOWN)
            {
                int vKey = (int)msg.wParam & 0xFFFF;
                if (VKeyToName.TryGetValue(vKey, out var name))
                {
                    _pressedKeys.Add(name);
                    UpdateShortcutDisplay();
                }
                handled = true;
            }
            else if (msg.message == WM_KEYUP || msg.message == WM_SYSKEYUP)
            {
                int vKey = (int)msg.wParam & 0xFFFF;
                if (VKeyToName.TryGetValue(vKey, out var name))
                {
                    _pressedKeys.Remove(name);
                }

                if (_pressedKeys.Count == 0)
                {
                    SaveCurrentShortcut();
                    ExitCaptureMode();
                }
                handled = true;
            }
        }

        private void ShortcutBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                if (_isCapturing) ExitCaptureMode();

                _activeShortcutBorder = border;
                _pressedKeys.Clear();
                _isCapturing = true;

                border.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 64, 175));
                border.BorderThickness = new Thickness(2);

                var grid = border.Child as Grid;
                if (grid != null)
                {
                    var textBlock = grid.Children.OfType<TextBlock>().FirstOrDefault();
                    var image = grid.Children.OfType<Image>().FirstOrDefault();
                    if (textBlock != null) textBlock.Visibility = Visibility.Collapsed;
                    if (image != null) image.Visibility = Visibility.Visible;
                }

                TxtShortcutError.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void UpdateShortcutDisplay()
        {
            if (_activeShortcutBorder == null) return;
            var grid = _activeShortcutBorder.Child as Grid;
            if (grid == null) return;

            var textBlock = grid.Children.OfType<TextBlock>().FirstOrDefault();
            var image = grid.Children.OfType<Image>().FirstOrDefault();
            if (textBlock == null) return;

            if (_pressedKeys.Count > 0)
            {
                textBlock.Text = string.Join(" + ", _pressedKeys);
                textBlock.Visibility = Visibility.Visible;
                if (image != null) image.Visibility = Visibility.Collapsed;
            }
            else
            {
                textBlock.Visibility = Visibility.Collapsed;
                if (image != null) image.Visibility = Visibility.Visible;
            }
        }

        private void SaveCurrentShortcut()
        {
            if (_settings == null || _activeShortcutBorder == null) return;

            var orderedKeys = new List<string>();
            string? mainKey = null;

            foreach (var k in _pressedKeys)
            {
                if (k == "Ctrl" || k == "Shift" || k == "Alt" || k == "Win")
                    orderedKeys.Add(k);
                else
                    mainKey = k;
            }

            if (mainKey != null)
            {
                orderedKeys.Add(mainKey);
                var shortcutText = string.Join("+", orderedKeys);

                if (ShortcutParser.TryParse(shortcutText, out _, out _))
                {
                    TxtShortcutError.Visibility = Visibility.Collapsed;

                    if (_activeShortcutBorder == BorderSP1) { _settings.SmartPasteShortcut1 = shortcutText; TxtSP1.Text = shortcutText; }
                    else if (_activeShortcutBorder == BorderSP2) { _settings.SmartPasteShortcut2 = shortcutText; TxtSP2.Text = shortcutText; }
                    else if (_activeShortcutBorder == BorderSP3) { _settings.SmartPasteShortcut3 = shortcutText; TxtSP3.Text = shortcutText; }
                    else if (_activeShortcutBorder == BorderSC) { _settings.SmartCopyShortcut = shortcutText; TxtSC.Text = shortcutText; }
                    else if (_activeShortcutBorder == BorderCC) { _settings.CaseConverterShortcut = shortcutText; TxtCC.Text = shortcutText; }
                    else if (_activeShortcutBorder == BorderAOT) { _settings.AlwaysOnTopShortcut = shortcutText; TxtAOT.Text = shortcutText; }
                    else if (_activeShortcutBorder == BorderTele) { _settings.TeleworkShortcut = shortcutText; TxtTele.Text = shortcutText; }

                    Save();
                    ((App)Application.Current).RefreshHotkeys();
                }
                else
                {
                    TxtShortcutError.Text = "Invalid shortcut. At least one modifier (Ctrl/Alt/Shift/Win) and one key are required.";
                    TxtShortcutError.Visibility = Visibility.Visible;
                }
            }
            else
            {
                TxtShortcutError.Text = "Invalid shortcut. At least one modifier and one key are required.";
                TxtShortcutError.Visibility = Visibility.Visible;
            }
        }

        private void ExitCaptureMode()
        {
            _isCapturing = false;
            _pressedKeys.Clear();

            if (_activeShortcutBorder != null)
            {
                _activeShortcutBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
                _activeShortcutBorder.BorderThickness = new Thickness(1);

                var grid = _activeShortcutBorder.Child as Grid;
                if (grid != null)
                {
                    var textBlock = grid.Children.OfType<TextBlock>().FirstOrDefault();
                    var image = grid.Children.OfType<Image>().FirstOrDefault();
                    if (textBlock != null && string.IsNullOrEmpty(textBlock.Text))
                    {
                        textBlock.Visibility = Visibility.Collapsed;
                        if (image != null) image.Visibility = Visibility.Visible;
                    }
                }

                _activeShortcutBorder = null;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ComponentDispatcher.ThreadPreprocessMessage -= ThreadPreprocessMessage;
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
            if (ChkEnableSP.IsChecked.HasValue) _settings.EnableSmartPaste = ChkEnableSP.IsChecked.Value;
            if (ChkEnableSC.IsChecked.HasValue) _settings.EnableSmartCopy = ChkEnableSC.IsChecked.Value;
            if (ChkEnableCC.IsChecked.HasValue) _settings.EnableCaseConverter = ChkEnableCC.IsChecked.Value;
            if (ChkEnableAOT.IsChecked.HasValue) _settings.EnableAlwaysOnTop = ChkEnableAOT.IsChecked.Value;
            Save();
            ((App)Application.Current).RefreshHotkeys();
        }

        private void Tele_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null) return;
            if (ChkTeleVariable.IsChecked.HasValue) _settings.TeleVariableRhythm = ChkTeleVariable.IsChecked.Value;
            if (ChkTeleMicroPauses.IsChecked.HasValue) _settings.TeleMicroPauses = ChkTeleMicroPauses.IsChecked.Value;
            if (ChkTeleFlowBursts.IsChecked.HasValue) _settings.TeleFlowBursts = ChkTeleFlowBursts.IsChecked.Value;
            if (ChkTeleBreathing.IsChecked.HasValue) _settings.TeleBreathingPauses = ChkTeleBreathing.IsChecked.Value;
            if (ChkTeleEndOfLine.IsChecked.HasValue) _settings.TeleEndOfLinePause = ChkTeleEndOfLine.IsChecked.Value;
            if (ChkTeleTypos.IsChecked.HasValue) _settings.TeleRealisticTypos = ChkTeleTypos.IsChecked.Value;
            if (ChkTeleCapsErrors.IsChecked.HasValue) _settings.TeleRandomCapsErrors = ChkTeleCapsErrors.IsChecked.Value;
            if (ChkTeleDoubleKey.IsChecked.HasValue) _settings.TeleDoubleKeyStrokes = ChkTeleDoubleKey.IsChecked.Value;
            if (ChkTeleCursorNav.IsChecked.HasValue) _settings.TeleCursorNavigation = ChkTeleCursorNav.IsChecked.Value;
            if (ChkTeleAutoCorrect.IsChecked.HasValue) _settings.TeleAutoCorrectMistakes = ChkTeleAutoCorrect.IsChecked.Value;
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
            {
                TxtSpeedLabel.Text = $"Base delay: {val} ms";
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _settings = new AppSettings();
            SettingsManager.Save(_settings);

            ChkStartMinimized.IsChecked = false;
            ChkAutoStart.IsChecked = false;
            ChkEnableSP.IsChecked = true;
            ChkEnableSC.IsChecked = true;
            ChkEnableCC.IsChecked = true;
            ChkEnableAOT.IsChecked = true;

            TxtSP1.Text = "Ctrl+Shift+V";
            TxtSP2.Text = "Ctrl+Alt+V";
            TxtSP3.Text = "Ctrl+Win+V";
            TxtSC.Text = "Ctrl+Shift+C";
            TxtCC.Text = "Ctrl+Win+C";
            TxtAOT.Text = "Ctrl+Alt+T";
            TxtTele.Text = "Ctrl+Shift+T";

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
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
