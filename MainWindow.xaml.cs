using System.Windows;
using System.Windows.Controls;

namespace SmartPaste
{
    public partial class MainWindow : Window
    {
        private SmartPasteManager _manager;
        private AppSettings _settings;
        private bool _isInitializing = true;

        public MainWindow()
        {
            InitializeComponent();
            
            // Get manager and settings from App
            var app = (App)Application.Current;
            _manager = app.pasteManager;
            _settings = app.Settings;

            if (_settings != null)
            {
                ChkStartMinimized.IsChecked = _settings.StartMinimized;
                ChkAutoStart.IsChecked = _settings.AutoStart;
            }

            if (_manager != null)
            {
                SldSpeed.Value = _manager.DelayMilliseconds;
            }
            
            _isInitializing = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void ChkStartMinimized_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (_settings != null && ChkStartMinimized.IsChecked.HasValue)
            {
                _settings.StartMinimized = ChkStartMinimized.IsChecked.Value;
                SettingsManager.Save(_settings);
            }
        }

        private void ChkAutoStart_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (_settings != null && ChkAutoStart.IsChecked.HasValue)
            {
                _settings.AutoStart = ChkAutoStart.IsChecked.Value;
                SettingsManager.Save(_settings);
                AutoStartManager.SetAutoStart(_settings.AutoStart);
            }
        }

        private void SldSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            if (_manager != null)
            {
                int val = (int)e.NewValue;
                _manager.DelayMilliseconds = val;
                if (_settings != null)
                {
                    _settings.DelayMilliseconds = val;
                    SettingsManager.Save(_settings);
                }
            }
            if (TxtSpeedLabel != null)
            {
                TxtSpeedLabel.Text = $"Typing speed : {(int)e.NewValue} ms";
            }
        }
    }
}