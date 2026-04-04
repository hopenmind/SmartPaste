using System.Windows;
using System.Windows.Controls;

namespace SmartPaste
{
    public partial class MainWindow : Window
    {
        private SmartPasteManager _manager;

        public MainWindow()
        {
            InitializeComponent();
            
            // Get manager from App
            var app = (App)Application.Current;
            _manager = app.pasteManager;

            if (_manager != null)
            {
                SldSpeed.Value = _manager.DelayMilliseconds;
                
                foreach (ComboBoxItem item in CmbSeparator.Items)
                {
                    if (item.Content.ToString() == _manager.SplitSeparator)
                    {
                        CmbSeparator.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void CmbSeparator_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_manager != null && CmbSeparator.SelectedItem is ComboBoxItem item)
            {
                _manager.SplitSeparator = item.Content.ToString();
            }
        }

        private void SldSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_manager != null)
            {
                _manager.DelayMilliseconds = (int)e.NewValue;
            }
            if (TxtSpeedLabel != null)
            {
                TxtSpeedLabel.Text = $"Typing speed : {(int)e.NewValue} ms";
            }
        }
    }
}