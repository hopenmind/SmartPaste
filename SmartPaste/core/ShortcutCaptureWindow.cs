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
    /// A small, self-contained themed window that captures a keyboard shortcut directly from
    /// the tray, WITHOUT opening the main application window. It shares its validation core
    /// (<see cref="ShortcutValidation"/>) with the main-window editor, so both paths behave
    /// identically. This window belongs to the tray/lite subsystem.
    /// </summary>
    public sealed class ShortcutCaptureWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly string _tag;
        private readonly Action _onApplied;
        private string? _captured;

        private readonly TextBlock _capture;
        private readonly TextBlock _error;
        private readonly Button _apply;

        public ShortcutCaptureWindow(AppSettings settings, string tag, Action onApplied)
        {
            _settings = settings;
            _tag = tag;
            _onApplied = onApplied;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");

            // ── sheet ──
            var sheet = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Margin = new Thickness(30),
                Width = 400,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.5, Color = Color.FromRgb(0x14, 0x0A, 0x18) }
            };
            sheet.SetResourceReference(Border.BackgroundProperty, "Brush.Bg");
            sheet.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            sheet.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var stack = new StackPanel();

            var title = Text(Strings.Format(nameof(Strings.ShortcutEditorTitle), ShortcutValidation.LabelForTag(_tag)), 15, "Brush.Ink");
            title.FontWeight = FontWeights.SemiBold;
            stack.Children.Add(title);

            // capture box
            var box = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 16, 0, 0) };
            box.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
            _capture = Text(Strings.CapturePrompt, 19, "Brush.AccentInk");
            _capture.HorizontalAlignment = HorizontalAlignment.Center;
            _capture.FontWeight = FontWeights.SemiBold;
            box.Child = _capture;
            stack.Children.Add(box);

            _error = Text("", 12, "Brush.Ink");
            _error.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x57, 0x4B));
            _error.Margin = new Thickness(0, 10, 0, 0);
            _error.TextWrapping = TextWrapping.Wrap;
            _error.Visibility = Visibility.Collapsed;
            stack.Children.Add(_error);

            var hint = Text(Strings.CaptureHint, 11, "Brush.Muted");
            hint.Margin = new Thickness(0, 10, 0, 0);
            hint.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(hint);

            // buttons
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = new Button { Content = Strings.Cancel, MinWidth = 92, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand };
            if (TryFindResource("FlatButton") is Style fs) cancel.Style = fs;
            cancel.Click += (s, e) => Close();
            _apply = new Button { Content = Strings.Apply, MinWidth = 92, Cursor = Cursors.Hand, IsEnabled = false };
            if (TryFindResource("PrimaryButton") is Style ps) _apply.Style = ps;
            _apply.Click += (s, e) => ApplyCapture();
            row.Children.Add(cancel);
            row.Children.Add(_apply);
            stack.Children.Add(row);

            sheet.Child = stack;
            Content = sheet;

            Loaded += (s, e) => { Activate(); Keyboard.Focus(this); };
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            e.Handled = true;

            if (e.Key == Key.Escape) { Close(); return; }

            var mods = ShortcutValidation.CurrentModifiers();
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (ShortcutValidation.IsModifierKey(key))
            {
                _capture.Text = mods == ModifierKeys.None
                    ? Strings.CapturePrompt
                    : Strings.Format(nameof(Strings.CaptureModifiersOnly), ShortcutValidation.DescribeMods(mods));
                return;
            }

            string? token = ShortcutValidation.KeyToToken(key);
            if (token == null) { ShowError(Strings.ErrorUnsupported); return; }

            if (mods == ModifierKeys.None && !ShortcutValidation.IsFunctionKey(token)) { ShowError(Strings.ErrorNeedModifier); return; }
            if (mods == ModifierKeys.Shift) { ShowError(Strings.ErrorShiftOnly); return; }

            string combo = ShortcutValidation.BuildCombo(mods, token);
            _capture.Text = ShortcutParser.Format(combo);

            string? reserved = ShortcutValidation.ReservedReason(mods, token);
            if (reserved != null) { ShowError(Strings.Format(nameof(Strings.ErrorReserved), reserved)); return; }

            string? dup = ShortcutValidation.DuplicateOwner(_settings, combo, _tag);
            if (dup != null) { ShowError(Strings.Format(nameof(Strings.ErrorDuplicate), dup)); return; }

            _error.Visibility = Visibility.Collapsed;
            _captured = combo;
            _apply.IsEnabled = true;
        }

        private void ShowError(string msg)
        {
            _error.Text = msg;
            _error.Visibility = Visibility.Visible;
            _captured = null;
            _apply.IsEnabled = false;
        }

        private void ApplyCapture()
        {
            if (_captured == null) return;
            ShortcutValidation.ApplyToSettings(_settings, _tag, _captured);
            _onApplied();
            Close();
        }

        private TextBlock Text(string text, double size, string fgKey)
        {
            var tb = new TextBlock { Text = text, FontSize = size };
            tb.SetResourceReference(TextBlock.ForegroundProperty, fgKey);
            return tb;
        }
    }
}
