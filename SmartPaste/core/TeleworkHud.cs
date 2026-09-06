using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SmartPaste.Localization;

namespace SmartPaste
{
    /// <summary>
    /// A small always-on-top heads-up display shown while Telework is typing from a file.
    /// It stays visible over the target app so the run can be stopped at any time via the red
    /// Stop square (mouse) or the Escape key (a temporary global hotkey handles Escape, since
    /// the typed-into target holds the foreground focus). It does not take focus itself.
    /// </summary>
    public sealed class TeleworkHud : Window
    {
        private static readonly Brush StopRed = Frozen("#D9433B");
        private static readonly Brush StopRedHover = Frozen("#C23A33");
        private bool _closed;

        public TeleworkHud(Action onStop)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;          // never steal focus from the typing target
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily("Segoe UI");

            var sheet = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 10, 18, 10),
                Margin = new Thickness(20),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.5, Color = Color.FromRgb(0x14, 0x0A, 0x18) }
            };
            sheet.SetResourceReference(Border.BackgroundProperty, "Brush.Bg");
            sheet.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            sheet.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

            var row = new StackPanel { Orientation = Orientation.Horizontal };

            // Red Stop square (mouse control)
            var stop = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(11),
                Background = StopRed,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Strings.TeleStop,
                Child = new Border
                {
                    Width = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(3),
                    Background = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            stop.MouseEnter += (s, e) => stop.Background = StopRedHover;
            stop.MouseLeave += (s, e) => stop.Background = StopRed;
            stop.MouseLeftButtonUp += (s, e) => onStop();
            row.Children.Add(stop);

            var texts = new StackPanel { Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = Strings.TeleTypingTitle, FontSize = 13.5, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Ink");
            var hint = new TextBlock { Text = Strings.TeleStopHint, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
            texts.Children.Add(title);
            texts.Children.Add(hint);
            row.Children.Add(texts);

            sheet.Child = row;
            Content = sheet;

            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + (wa.Width - ActualWidth) / 2;   // top-center
                Top = wa.Top + 24;
            };
        }

        /// <summary>Closes the HUD once (safe to call from the typing continuation).</summary>
        public void CloseHud()
        {
            if (_closed) return;
            _closed = true;
            try { Close(); } catch { }
        }

        private static Brush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
