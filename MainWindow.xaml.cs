using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

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
            
            var app = (App)Application.Current;
            _manager = app.pasteManager;
            _settings = app.Settings;

            if (_settings != null)
            {
                ChkStartMinimized.IsChecked = _settings.StartMinimized;
                ChkAutoStart.IsChecked = _settings.AutoStart;
                ChkHumanSim.IsChecked = _settings.HumanSimulation;
                ChkTypos.IsChecked = _settings.HumanTypos;
                SldSpeed.Value = _settings.DelayMilliseconds;
            }

            if (_manager != null)
            {
                _manager.DelayMilliseconds = (int)SldSpeed.Value;
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

        private void ChkHumanSim_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (_settings != null && ChkHumanSim.IsChecked.HasValue)
            {
                _settings.HumanSimulation = ChkHumanSim.IsChecked.Value;
                SettingsManager.Save(_settings);
                if (_manager != null)
                {
                    _manager.HumanSimulation = _settings.HumanSimulation;
                }
            }
        }

        private void ChkTypos_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (_settings != null && ChkTypos.IsChecked.HasValue)
            {
                _settings.HumanTypos = ChkTypos.IsChecked.Value;
                SettingsManager.Save(_settings);
                if (_manager != null)
                {
                    _manager.HumanTypos = _settings.HumanTypos;
                }
            }
        }

        private void SldSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            int val = (int)e.NewValue;
            if (_manager != null)
            {
                _manager.DelayMilliseconds = val;
            }
            if (_settings != null)
            {
                _settings.DelayMilliseconds = val;
                SettingsManager.Save(_settings);
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
            ChkHumanSim.IsChecked = false;
            ChkTypos.IsChecked = false;
            SldSpeed.Value = 30;

            if (_manager != null)
            {
                _manager.DelayMilliseconds = 30;
                _manager.HumanSimulation = false;
                _manager.HumanTypos = false;
            }

            AutoStartManager.SetAutoStart(false);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
