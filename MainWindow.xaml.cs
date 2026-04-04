using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

namespace SmartPaste
{
    public partial class MainWindow : Window
    {
        private AppSettings? _settings;
        private bool _isInitializing = true;

        public MainWindow()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            _settings = app.Settings;

            if (_settings != null)
            {
                // Startup
                ChkStartMinimized.IsChecked = _settings.StartMinimized;
                ChkAutoStart.IsChecked = _settings.AutoStart;

                // Function toggles
                ChkEnableSP.IsChecked = _settings.EnableSmartPaste;
                ChkEnableSC.IsChecked = _settings.EnableSmartCopy;
                ChkEnableCC.IsChecked = _settings.EnableCaseConverter;
                ChkEnableAOT.IsChecked = _settings.EnableAlwaysOnTop;

                // Shortcuts
                TxtSP1.Text = _settings.SmartPasteShortcut1;
                TxtSP2.Text = _settings.SmartPasteShortcut2;
                TxtSP3.Text = _settings.SmartPasteShortcut3;
                TxtSC.Text = _settings.SmartCopyShortcut;
                TxtCC.Text = _settings.CaseConverterShortcut;
                TxtAOT.Text = _settings.AlwaysOnTopShortcut;

                // Telework core
                ChkTeleVariable.IsChecked = _settings.TeleVariableRhythm;
                ChkTeleMicroPauses.IsChecked = _settings.TeleMicroPauses;
                ChkTeleFlowBursts.IsChecked = _settings.TeleFlowBursts;
                ChkTeleBreathing.IsChecked = _settings.TeleBreathingPauses;
                ChkTeleEndOfLine.IsChecked = _settings.TeleEndOfLinePause;

                // Telework errors
                ChkTeleTypos.IsChecked = _settings.TeleRealisticTypos;
                ChkTeleCapsErrors.IsChecked = _settings.TeleRandomCapsErrors;
                ChkTeleDoubleKey.IsChecked = _settings.TeleDoubleKeyStrokes;
                ChkTeleCursorNav.IsChecked = _settings.TeleCursorNavigation;
                ChkTeleAutoCorrect.IsChecked = _settings.TeleAutoCorrectMistakes;

                // Telework timing
                SldTeleDelay.Value = _settings.TelePasteDelay;
                SldTeleChunk.Value = _settings.TeleWordChunkSize;
                SldTeleBreath.Value = _settings.TeleBreathingInterval;

                // Typing speed
                SldSpeed.Value = _settings.DelayMilliseconds;
            }

            _isInitializing = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void Save()
        {
            if (_settings != null) SettingsManager.Save(_settings);
        }

        // --- Startup ---
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

        // --- Function Toggles ---
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

        // --- Shortcut Editing ---
        private void TxtShortcut_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || _settings == null) return;

            bool allValid = true;
            TxtShortcutError.Visibility = Visibility.Collapsed;

            if (TrySetShortcut(TxtSP1.Text, s => _settings!.SmartPasteShortcut1 = s, out bool v1)) allValid &= v1;
            if (TrySetShortcut(TxtSP2.Text, s => _settings!.SmartPasteShortcut2 = s, out bool v2)) allValid &= v2;
            if (TrySetShortcut(TxtSP3.Text, s => _settings!.SmartPasteShortcut3 = s, out bool v3)) allValid &= v3;
            if (TrySetShortcut(TxtSC.Text, s => _settings!.SmartCopyShortcut = s, out bool v4)) allValid &= v4;
            if (TrySetShortcut(TxtCC.Text, s => _settings!.CaseConverterShortcut = s, out bool v5)) allValid &= v5;
            if (TrySetShortcut(TxtAOT.Text, s => _settings!.AlwaysOnTopShortcut = s, out bool v6)) allValid &= v6;

            if (!allValid)
            {
                TxtShortcutError.Text = "Invalid shortcut format. Use: Ctrl+Shift+V, Alt+F1, Ctrl+Win+C, etc.";
                TxtShortcutError.Visibility = Visibility.Visible;
            }

            Save();
            ((App)Application.Current).RefreshHotkeys();
        }

        private bool TrySetShortcut(string text, System.Action<string> setter, out bool valid)
        {
            if (string.IsNullOrWhiteSpace(text)) { valid = false; return false; }
            valid = ShortcutParser.TryParse(text, out _, out _);
            if (valid) setter(text);
            return true;
        }

        // --- Telework ---
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

        // --- Typing Speed ---
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

        // --- Reset ---
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
