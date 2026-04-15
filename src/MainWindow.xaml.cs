using System;
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
using System.Windows.Threading;

namespace SmartPaste
{
    public partial class MainWindow : Window
    {
        private AppSettings? _settings;
        private bool _isInitializing = true;
        private Border? _activeShortcutBorder;
        private HashSet<string> _pressedKeys = new();
        private bool _isCapturing;

        // Dashboard state
        private DispatcherTimer? _dashTimer;
        private DateTime _dashStartTime;
        private WindowState _savedWindowState;
        private double _savedWidth, _savedHeight;
        private WindowStyle _savedWindowStyle;
        private ResizeMode _savedResizeMode;

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
                ChkEnablePI.IsChecked = _settings.EnablePasteIntercept;
                ChkEnableCC.IsChecked = _settings.EnableCaseConverter;
                ChkEnableAOT.IsChecked = _settings.EnableAlwaysOnTop;
                ChkOverrideV.IsChecked = _settings.EnablePasteIntercept;
                ChkOverrideC.IsChecked = _settings.OverrideCtrlC;

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

            BtnLang.Content = (_settings?.Language ?? "en").ToUpperInvariant();
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

                border.BorderBrush = (SolidColorBrush)FindResource("AccentPrimary");
                border.BorderThickness = new Thickness(2);

                if (border.Child is TextBlock tb)
                    tb.Text = "...";

                TxtShortcutError.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void UpdateShortcutDisplay()
        {
            if (_activeShortcutBorder?.Child is not TextBlock textBlock) return;

            textBlock.Text = _pressedKeys.Count > 0
                ? string.Join(" + ", _pressedKeys)
                : "...";
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
                _activeShortcutBorder.BorderBrush = (SolidColorBrush)FindResource("BorderDefault");
                _activeShortcutBorder.BorderThickness = new Thickness(1);
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
            if (ChkEnablePI.IsChecked.HasValue) _settings.EnablePasteIntercept = ChkEnablePI.IsChecked.Value;
            if (ChkEnableCC.IsChecked.HasValue) _settings.EnableCaseConverter = ChkEnableCC.IsChecked.Value;
            if (ChkEnableAOT.IsChecked.HasValue) _settings.EnableAlwaysOnTop = ChkEnableAOT.IsChecked.Value;
            if (ChkOverrideV.IsChecked.HasValue) _settings.EnablePasteIntercept = ChkOverrideV.IsChecked.Value;
            if (ChkOverrideC.IsChecked.HasValue) _settings.OverrideCtrlC = ChkOverrideC.IsChecked.Value;
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
            ChkEnablePI.IsChecked = true;
            ChkEnableCC.IsChecked = true;
            ChkEnableAOT.IsChecked = true;
            ChkOverrideV.IsChecked = true;
            ChkOverrideC.IsChecked = false;

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

        // ── Dashboard (Command Center) ──────────────────────────────

        private void BtnDashLaunch_Click(object sender, RoutedEventArgs e)
        {
            // Save window state
            _savedWindowState = WindowState;
            _savedWidth = Width;
            _savedHeight = Height;
            _savedWindowStyle = WindowStyle;
            _savedResizeMode = ResizeMode;

            // Sync quick-settings from main settings
            ChkDashVariable.IsChecked = ChkTeleVariable.IsChecked;
            ChkDashMicro.IsChecked = ChkTeleMicroPauses.IsChecked;
            ChkDashFlow.IsChecked = ChkTeleFlowBursts.IsChecked;
            ChkDashTypos.IsChecked = ChkTeleTypos.IsChecked;
            ChkDashBreath.IsChecked = ChkTeleBreathing.IsChecked;
            SldDashDelay.Value = SldTeleDelay.Value;

            // Load auto-writer settings
            LoadAutoWriterSettings();

            // Go fullscreen
            var tabCtrl = this.Content is Grid g ? g.Children.OfType<TabControl>().FirstOrDefault() : null;
            if (tabCtrl != null) tabCtrl.Visibility = Visibility.Collapsed;
            Dashboard.Visibility = Visibility.Visible;

            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Maximized;
        }

        private void BtnDashExit_Click(object sender, RoutedEventArgs e)
        {
            // Stop if running
            var app = (App)Application.Current;
            if (app.pasteManager.DashActive)
                app.pasteManager.DashCancel();

            _dashTimer?.Stop();

            // Restore window
            Dashboard.Visibility = Visibility.Collapsed;
            var tabCtrl = this.Content is Grid g ? g.Children.OfType<TabControl>().FirstOrDefault() : null;
            if (tabCtrl != null) tabCtrl.Visibility = Visibility.Visible;

            WindowState = _savedWindowState;
            WindowStyle = _savedWindowStyle;
            Width = _savedWidth;
            Height = _savedHeight;

            BtnDashStart.IsEnabled = true;
            BtnDashPause.IsEnabled = false;
            BtnDashStop.IsEnabled = false;
            DashCountdownPanel.Visibility = Visibility.Collapsed;
        }

        private async void BtnDashStart_Click(object sender, RoutedEventArgs e)
        {
            var app = (App)Application.Current;
            bool isAuto = RbAuto.IsChecked == true;

            // Validate input
            if (!isAuto && string.IsNullOrWhiteSpace(TxtDashInput.Text)) return;
            if (isAuto && LstSources.Items.Count == 0) return;

            BtnDashStart.IsEnabled = false;
            BtnDashPause.IsEnabled = true;
            BtnDashStop.IsEnabled = true;
            TxtDashInput.IsEnabled = false;

            // Countdown 3-2-1
            DashCountdownPanel.Visibility = Visibility.Visible;
            for (int i = 3; i >= 1; i--)
            {
                TxtDashCountdown.Text = i.ToString();
                await System.Threading.Tasks.Task.Delay(1000);
            }
            DashCountdownPanel.Visibility = Visibility.Collapsed;

            // Start progress timer
            _dashStartTime = DateTime.Now;
            _dashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _dashTimer.Tick += DashTimer_Tick;
            _dashTimer.Start();

            TxtDashStatus.Text = "ACTIVE";
            TxtDashStatus.Foreground = (SolidColorBrush)FindResource("AccentPrimary");

            if (isAuto)
            {
                // Auto Writer mode
                _autoWriter = new AutoWriterEngine(app.pasteManager)
                {
                    Sources = LstSources.Items.Cast<string>().ToList(),
                    Targets = LstTargets.Items.Cast<string>().ToList(),
                    MinIntervalSec = (int)SldAutoMin.Value,
                    MaxIntervalSec = (int)SldAutoMax.Value,
                    Loop = ChkAutoLoop.IsChecked == true,
                    ClearBeforeTyping = ChkAutoClear.IsChecked == true
                };
                await _autoWriter.RunAsync();
            }
            else
            {
                // Manual mode
                await app.pasteManager.DashTypeAsync(TxtDashInput.Text, 0);
            }

            // Done
            _dashTimer?.Stop();
            DashTimer_Tick(null, EventArgs.Empty);
            TxtDashStatus.Text = "DONE";
            TxtDashStatus.Foreground = (SolidColorBrush)FindResource("TextMuted");
            BtnDashStart.IsEnabled = true;
            BtnDashPause.IsEnabled = false;
            BtnDashStop.IsEnabled = false;
            TxtDashInput.IsEnabled = true;
        }

        private void BtnDashPause_Click(object sender, RoutedEventArgs e)
        {
            var pm = ((App)Application.Current).pasteManager;
            if (pm.DashPaused)
            {
                pm.DashResume();
                BtnDashPause.Content = FindResource("DashPause");
                TxtDashStatus.Text = "ACTIVE";
                TxtDashStatus.Foreground = (SolidColorBrush)FindResource("AccentPrimary");
            }
            else
            {
                pm.DashPause();
                BtnDashPause.Content = "RESUME";
                TxtDashStatus.Text = "PAUSED";
                TxtDashStatus.Foreground = (SolidColorBrush)FindResource("TextTertiary");
            }
        }

        private void BtnDashStop_Click(object sender, RoutedEventArgs e)
        {
            _autoWriter?.Stop();
            ((App)Application.Current).pasteManager.DashCancel();
        }

        private void DashTimer_Tick(object? sender, EventArgs e)
        {
            var app = (App)Application.Current;
            var pm = app.pasteManager;

            // ── Progress display ──
            int progress = pm.DashProgress;
            int total = pm.DashTotal;
            TxtDashChars.Text = $"{progress:N0} / {total:N0}";
            PrgDash.Value = total > 0 ? (double)progress / total * 100 : 0;
            var elapsed = DateTime.Now - _dashStartTime;
            TxtDashElapsed.Text = elapsed.ToString(@"mm\:ss");

            // ── Status + preview ──
            if (_autoWriter != null && _autoWriter.IsRunning)
            {
                TxtDashStatus.Text = $"AUTO — Cycle {_autoWriter.CycleCount + 1}";
                TxtDashPreview.Text = _autoWriter.StatusMessage;
            }
            else if (pm.DashActive)
            {
                string input = TxtDashInput.Text;
                if (progress > 0 && progress <= input.Length)
                {
                    int start = Math.Max(0, progress - 200);
                    TxtDashPreview.Text = input.Substring(start, progress - start);
                }
            }

            // ── Scheduler logic ──
            if (_settings != null && _settings.SchedulerEnabled)
            {
                var state = WorkScheduler.GetCurrentState(_settings.WeekSchedule, true);
                TxtScheduleStatus.Text = WorkScheduler.GetStatusText(state, _settings.WeekSchedule);

                // Apply energy curve
                pm.EnergyMultiplier = WorkScheduler.GetEnergyMultiplier(_settings.WeekSchedule);

                // Auto-start when it's work time
                if (state == ScheduleState.Working && !_schedulerAutoRunning
                    && (_autoWriter == null || !_autoWriter.IsRunning)
                    && !pm.DashActive
                    && RbAuto.IsChecked == true && LstSources.Items.Count > 0)
                {
                    _schedulerAutoRunning = true;
                    BtnDashStart_Click(this, new RoutedEventArgs()); // Launch auto-writer
                }

                // Auto-stop on lunch/end of day
                if (state != ScheduleState.Working && _schedulerAutoRunning)
                {
                    _schedulerAutoRunning = false;
                    _autoWriter?.Stop();
                    pm.DashCancel();
                    TxtDashStatus.Text = WorkScheduler.GetStatusText(state, _settings.WeekSchedule);
                    TxtDashStatus.Foreground = (SolidColorBrush)FindResource("TextTertiary");
                    BtnDashStart.IsEnabled = true;
                    BtnDashPause.IsEnabled = false;
                    BtnDashStop.IsEnabled = false;
                    TxtDashInput.IsEnabled = true;
                }
            }
            else
            {
                TxtScheduleStatus.Text = "";
                pm.EnergyMultiplier = 1.0;
            }
        }

        // ── Schedule day view model (for binding) ─────────────────────

        public class ScheduleDayVM : System.ComponentModel.INotifyPropertyChanged
        {
            private readonly ScheduleDay _day;
            public string DayLabel { get; }
            public bool Enabled { get => _day.Enabled; set { _day.Enabled = value; OnChanged(); } }
            public string Start { get => _day.Start; set { _day.Start = value; OnChanged(); } }
            public string End { get => _day.End; set { _day.End = value; OnChanged(); } }

            public ScheduleDayVM(ScheduleDay day, string label) { _day = day; DayLabel = label; }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged() => PropertyChanged?.Invoke(this, new(null));
        }

        private List<ScheduleDayVM>? _weekVMs;
        private bool _schedulerAutoRunning;

        // ── Dashboard mode toggle ────────────────────────────────────

        private void DashMode_Click(object sender, RoutedEventArgs e)
        {
            bool isAuto = RbAuto.IsChecked == true;
            PanelManual.Visibility = isAuto ? Visibility.Collapsed : Visibility.Visible;
            PanelAuto.Visibility = isAuto ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Auto Writer: source/target management ────────────────────

        private AutoWriterEngine? _autoWriter;

        private void BtnAddSourceFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add text source",
                Filter = "Text files|*.txt;*.md;*.log;*.html;*.htm;*.rtf|All files|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (string file in dlg.FileNames)
                    if (!LstSources.Items.Contains(file))
                        LstSources.Items.Add(file);
                SaveAutoWriterSettings();
            }
        }

        private void BtnAddSourceUrl_Click(object sender, RoutedEventArgs e)
        {
            // Quick URL input dialog
            var dlg = new Window
            {
                Title = "Add URL",
                Width = 450, Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            var tb = new TextBox { Text = "https://", Margin = new Thickness(16, 16, 16, 8) };
            var btn = new Button { Content = "Add", Padding = new Thickness(24, 6, 24, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 16, 16) };
            btn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
            var sp = new StackPanel();
            sp.Children.Add(tb);
            sp.Children.Add(btn);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(tb.Text) && tb.Text.StartsWith("http"))
            {
                LstSources.Items.Add(tb.Text);
                SaveAutoWriterSettings();
            }
        }

        private void BtnAddTarget_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select target application",
                Filter = "Executables|*.exe|All files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                string display = $"{System.IO.Path.GetFileNameWithoutExtension(dlg.FileName)} — {dlg.FileName}";
                LstTargets.Items.Add(dlg.FileName);
                SaveAutoWriterSettings();
            }
        }

        private void LstSources_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && LstSources.SelectedItem != null)
            {
                LstSources.Items.Remove(LstSources.SelectedItem);
                SaveAutoWriterSettings();
            }
        }

        private void LstTargets_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && LstTargets.SelectedItem != null)
            {
                LstTargets.Items.Remove(LstTargets.SelectedItem);
                SaveAutoWriterSettings();
            }
        }

        private void SldAutoInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            if (TxtAutoMin != null) TxtAutoMin.Text = ((int)SldAutoMin.Value).ToString();
            if (TxtAutoMax != null) TxtAutoMax.Text = ((int)SldAutoMax.Value).ToString();
            SaveAutoWriterSettings();
        }

        private void SaveAutoWriterSettings()
        {
            if (_settings == null) return;
            _settings.AutoWriterSources = LstSources.Items.Cast<string>().ToList();
            _settings.AutoWriterTargets = LstTargets.Items.Cast<string>().ToList();
            _settings.AutoWriterMinInterval = (int)SldAutoMin.Value;
            _settings.AutoWriterMaxInterval = (int)SldAutoMax.Value;
            _settings.AutoWriterLoop = ChkAutoLoop.IsChecked == true;
            _settings.AutoWriterClearBefore = ChkAutoClear.IsChecked == true;
            Save();
        }

        private void LoadAutoWriterSettings()
        {
            if (_settings == null) return;
            LstSources.Items.Clear();
            foreach (var s in _settings.AutoWriterSources) LstSources.Items.Add(s);
            LstTargets.Items.Clear();
            foreach (var t in _settings.AutoWriterTargets) LstTargets.Items.Add(t);
            SldAutoMin.Value = _settings.AutoWriterMinInterval;
            SldAutoMax.Value = _settings.AutoWriterMaxInterval;
            ChkAutoLoop.IsChecked = _settings.AutoWriterLoop;
            ChkAutoClear.IsChecked = _settings.AutoWriterClearBefore;

            // Scheduler
            ChkScheduler.IsChecked = _settings.SchedulerEnabled;
            string[] dayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            _weekVMs = new();
            for (int i = 0; i < 7 && i < _settings.WeekSchedule.Count; i++)
                _weekVMs.Add(new ScheduleDayVM(_settings.WeekSchedule[i], dayNames[i]));
            WeekGrid.ItemsSource = _weekVMs;

            // Clock picker — load from today's schedule
            var today = _settings.WeekSchedule[(int)DateTime.Now.DayOfWeek];
            if (TimeSpan.TryParse(today.Start, out var ts)) ClockPicker.StartHour = ts.TotalHours;
            if (TimeSpan.TryParse(today.End, out var te)) ClockPicker.EndHour = te.TotalHours;
            if (TimeSpan.TryParse(today.LunchStart, out var tls)) ClockPicker.LunchStartHour = tls.TotalHours;
            if (TimeSpan.TryParse(today.LunchEnd, out var tle)) ClockPicker.LunchEndHour = tle.TotalHours;
            ClockPicker.ScheduleChanged += OnClockChanged;
        }

        private void OnClockChanged()
        {
            if (_settings == null) return;
            // Apply clock values to all enabled days
            string start = FormatHour(ClockPicker.StartHour);
            string end = FormatHour(ClockPicker.EndHour);
            string ls = FormatHour(ClockPicker.LunchStartHour);
            string le = FormatHour(ClockPicker.LunchEndHour);

            foreach (var day in _settings.WeekSchedule)
            {
                if (!day.Enabled) continue;
                day.Start = start;
                day.End = end;
                day.LunchStart = ls;
                day.LunchEnd = le;
            }
            Save();
        }

        private static string FormatHour(double h)
        {
            int hours = (int)h;
            int mins = (int)((h - hours) * 60);
            return $"{hours:D2}:{mins:D2}";
        }

        private void ChkScheduler_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null) return;
            _settings.SchedulerEnabled = ChkScheduler.IsChecked == true;
            Save();
        }

        private void ScheduleDay_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _settings == null) return;
            Save();
        }

        // ── Delay slider ─────────────────────────────────────────────

        private void SldDashDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _settings == null) return;
            _settings.TelePasteDelay = (int)e.NewValue;
            if (TxtDashDelay != null) TxtDashDelay.Text = ((int)e.NewValue).ToString();
            Save();
            ((App)Application.Current).RefreshHotkeys();
        }

        // ── Language ─────────────────────────────────────────────────

        private static readonly string[] Languages = { "en", "fr", "br" };

        private void BtnLang_Click(object sender, RoutedEventArgs e)
        {
            var app = (App)Application.Current;
            int idx = Array.IndexOf(Languages, app.Settings.Language);
            string next = Languages[(idx + 1) % Languages.Length];
            app.SetLanguage(next);
            BtnLang.Content = next.ToUpperInvariant();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
