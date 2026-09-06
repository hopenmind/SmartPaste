using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SmartPaste.Localization;

namespace SmartPaste
{
    public partial class MainWindow : Window
    {
        private App AppInstance => (App)Application.Current;
        private AppSettings S => AppInstance.Settings;

        // Wheel geometry (matches the mockup)
        private const double CX = 234, CY = 234, RO = 208, RI = 140, GAP = 4;
        private int _hovered = -1;
        private bool _loading;

        private sealed class Feature
        {
            public string Key = "";
            public string Glyph = "";      // MDL2 glyph or plain text
            public bool GlyphIsText;
            public string Name = "";
            public string Desc = "";
            public Func<string> Shortcut = () => "";
            public string[]? Modes;
            public string[]? Opts;
        }

        private Feature[] _features = Array.Empty<Feature>();

        public MainWindow()
        {
            InitializeComponent();

            _features = new[]
            {
                new Feature{ Key="paste", Glyph="", Name=Strings.FeatureSmartPaste, Desc=Strings.FeatureSmartPasteDesc,
                             Shortcut=()=>S.SmartPasteShortcut1, Modes=new[]{Strings.ModeEnter,Strings.ModeSpace,Strings.ModeNormal} },
                new Feature{ Key="copy", Glyph="", Name=Strings.FeatureSmartCopy, Desc=Strings.FeatureSmartCopyDesc,
                             Shortcut=()=>S.SmartCopyShortcut, Opts=new[]{Strings.OptTargetAware, Strings.OptEmbedSvg} },
                new Feature{ Key="aot", Glyph="", Name=Strings.FeatureAlwaysOnTop, Desc=Strings.FeatureAlwaysOnTopDesc,
                             Shortcut=()=>S.AlwaysOnTopShortcut },
                new Feature{ Key="case", Glyph="Aa", GlyphIsText=true, Name=Strings.FeatureCaseConverter, Desc=Strings.FeatureCaseConverterDesc,
                             Shortcut=()=>S.CaseConverterShortcut },
            };

            ThemeManager.ThemeChanged += (s, e) => Dispatcher.Invoke(OnThemeChanged);
            // Minimize-to-tray: hide the window (and its taskbar button) on minimize when enabled.
            StateChanged += (s, e) => { if (WindowState == WindowState.Minimized && S.MinimizeToTray) Hide(); };
            Loaded += (s, e) =>
            {
                PopulateSettings();
                BuildWheel();
                ShowDefault();
                UpdateSunMoon();
                UpdateThemeButtons();
            };
        }

        // ── Resource helpers ─────────────────────────────────────────
        private Brush B(string key) => (Brush)FindResource(key);

        private void OnThemeChanged()
        {
            BuildWheel();
            if (_hovered >= 0) ShowFeature(_hovered); else ShowDefault();
            UpdateSunMoon();
            UpdateThemeButtons();
        }

        /// <summary>
        /// Re-applies the current settings to the wheel (and to the settings overlay when open).
        /// Called after the tray menu edits a setting, so an open window stays in sync.
        /// </summary>
        public void RefreshFromSettings()
        {
            if (!IsLoaded) return;   // wheel is built on Loaded; nothing to refresh before that
            BuildWheel();
            if (_hovered >= 0) ShowFeature(_hovered); else ShowDefault();
            if (SettingsOverlay.Visibility == Visibility.Visible || TeleworkOverlay.Visibility == Visibility.Visible)
                PopulateSettings();
        }

        // ── Window chrome ────────────────────────────────────────────
        private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Hide();   // keep running in the tray
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;   // never really close from the window; the tray owns the lifetime
            Hide();
        }

        // ── Theme ────────────────────────────────────────────────────
        private void ToggleMode_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleMode();
            S.ThemeMode = ThemeManager.CurrentMode;
            SettingsManager.Save(S);
        }
        private void PickAubergine_Click(object sender, RoutedEventArgs e) => SetBrand("Aubergine");
        private void PickPrune_Click(object sender, RoutedEventArgs e) => SetBrand("Prune");
        private void SetBrand(string brand)
        {
            ThemeManager.SetBrand(brand);
            S.ThemeBrand = ThemeManager.CurrentBrand;
            SettingsManager.Save(S);
        }
        private void UpdateSunMoon()
        {
            // Sun in light (click → go dark); moon in dark (click → go light)
            SunMoonGlyph.Text = ThemeManager.CurrentMode == "Dark" ? "" : "";
        }
        private void UpdateThemeButtons()
        {
            double a = ThemeManager.CurrentBrand == "Aubergine" ? 1 : 0.55;
            double p = ThemeManager.CurrentBrand == "Prune" ? 1 : 0.55;
            BtnThemeAubergine.Opacity = a;
            BtnThemePrune.Opacity = p;
        }

        // ── Overlays ─────────────────────────────────────────────────
        private void OpenSettings_Click(object sender, RoutedEventArgs e) { PopulateSettings(); ShowOverlay(SettingsOverlay); }
        private void OpenTelework_Click(object sender, RoutedEventArgs e) => OpenTeleworkOverlay();
        public void OpenTeleworkOverlay() { PopulateSettings(); ShowOverlay(TeleworkOverlay); }

        // ── Telework: type from a file ───────────────────────────────
        private string? _teleworkText;

        private void LoadTeleworkFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.TeleLoadFile,
                Filter = "Text (*.txt;*.md;*.csv;*.json;*.log;*.xml;*.html)|*.txt;*.md;*.csv;*.json;*.log;*.xml;*.html|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string text = System.IO.File.ReadAllText(dlg.FileName);
                _teleworkText = text;
                TeleFileStatus.Text = Strings.Format(nameof(Strings.TeleFileLoaded), System.IO.Path.GetFileName(dlg.FileName), text.Length);
                BtnTeleType.IsEnabled = text.Length > 0;
            }
            catch (Exception ex)
            {
                _teleworkText = null;
                TeleFileStatus.Text = ex.Message;
                BtnTeleType.IsEnabled = false;
            }
        }

        private async void TeleworkType_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_teleworkText)) return;
            AppInstance.RefreshHotkeys();   // push current Telework settings into the typing engine
            CloseOverlays();
            Hide();                         // step aside so the target field receives the keystrokes

            var pm = AppInstance.pasteManager;
            var hud = new TeleworkHud(() => pm.CancelTyping());   // red Stop square
            hud.Show();
            IDisposable? escapeWatch = null;
            try { escapeWatch = AppInstance.BeginTypingEscapeWatch(() => pm.CancelTyping()); } catch { }
            try
            {
                await pm.TypeFileText(_teleworkText);
            }
            finally
            {
                escapeWatch?.Dispose();
                hud.CloseHud();
            }
        }
        private void CloseOverlays_Click(object sender, RoutedEventArgs e) => CloseOverlays();
        private void Scrim_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, sender)) CloseOverlays();  // click on the scrim only
        }
        private void ShowOverlay(UIElement overlay)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            TeleworkOverlay.Visibility = Visibility.Collapsed;
            ShortcutEditorOverlay.Visibility = Visibility.Collapsed;
            overlay.Visibility = Visibility.Visible;
        }
        private void CloseOverlays()
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            TeleworkOverlay.Visibility = Visibility.Collapsed;
            ShortcutEditorOverlay.Visibility = Visibility.Collapsed;
        }

        // ── Settings population + wiring ─────────────────────────────
        private void PopulateSettings()
        {
            _loading = true;

            TglStartWindows.IsChecked = S.AutoStart;
            TglStartMin.IsChecked = S.StartMinimized;
            TglMinToTray.IsChecked = S.MinimizeToTray;
            TglIntercept.IsChecked = S.EnablePasteIntercept;

            TglTeleRhythm.IsChecked = S.TeleVariableRhythm;
            TglTeleMicro.IsChecked = S.TeleMicroPauses;
            TglTeleFlow.IsChecked = S.TeleFlowBursts;
            TglTeleBreath.IsChecked = S.TeleBreathingPauses;
            TglTeleTypos.IsChecked = S.TeleRealisticTypos;
            TglTeleCaps.IsChecked = S.TeleRandomCapsErrors;
            TglTeleDouble.IsChecked = S.TeleDoubleKeyStrokes;

            SldSpeed.Value = S.DelayMilliseconds;
            SpeedLabel.Text = Strings.Format(nameof(Strings.LabelBaseSpeed), S.DelayMilliseconds);

            KeySmartPasteEnter.Content = ShortcutParser.Format(S.SmartPasteShortcut1);
            KeySmartPasteSpace.Content = ShortcutParser.Format(S.SmartPasteShortcut2);
            KeySmartPasteNormal.Content = ShortcutParser.Format(S.SmartPasteShortcut3);
            KeyCase.Content = ShortcutParser.Format(S.CaseConverterShortcut);
            KeyAot.Content = ShortcutParser.Format(S.AlwaysOnTopShortcut);
            KeyCopy.Content = ShortcutParser.Format(S.SmartCopyShortcut);

            WireToggle(TglStartWindows, v => { S.AutoStart = v; AutoStartManager.SetAutoStart(v); });
            WireToggle(TglStartMin, v => S.StartMinimized = v);
            WireToggle(TglMinToTray, v => S.MinimizeToTray = v);
            WireToggle(TglIntercept, v => S.EnablePasteIntercept = v);
            WireToggle(TglTeleRhythm, v => S.TeleVariableRhythm = v);
            WireToggle(TglTeleMicro, v => S.TeleMicroPauses = v);
            WireToggle(TglTeleFlow, v => S.TeleFlowBursts = v);
            WireToggle(TglTeleBreath, v => S.TeleBreathingPauses = v);
            WireToggle(TglTeleTypos, v => S.TeleRealisticTypos = v);
            WireToggle(TglTeleCaps, v => S.TeleRandomCapsErrors = v);
            WireToggle(TglTeleDouble, v => S.TeleDoubleKeyStrokes = v);

            _loading = false;
        }

        private readonly HashSet<ToggleButton> _wired = new();
        private void WireToggle(ToggleButton t, Action<bool> apply)
        {
            if (!_wired.Add(t)) return;   // wire once
            RoutedEventHandler handler = (s, e) =>
            {
                if (_loading) return;
                apply(t.IsChecked == true);
                SettingsManager.Save(S);
                AppInstance.RefreshHotkeys();
            };
            t.Checked += handler;
            t.Unchecked += handler;
        }

        private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedLabel != null)
                SpeedLabel.Text = Strings.Format(nameof(Strings.LabelBaseSpeed), (int)e.NewValue);
            if (_loading) return;
            S.DelayMilliseconds = (int)e.NewValue;
            SettingsManager.Save(S);
            AppInstance.RefreshHotkeys();
        }

        // ── The wheel ────────────────────────────────────────────────
        private static Point Polar(double r, double deg)
        {
            double a = (deg - 90) * Math.PI / 180;
            return new Point(CX + r * Math.Cos(a), CY + r * Math.Sin(a));
        }

        private static Geometry Sector(double a1, double a2)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                Point p1 = Polar(RO, a1), p2 = Polar(RO, a2), p3 = Polar(RI, a2), p4 = Polar(RI, a1);
                bool large = (a2 - a1) > 180;
                c.BeginFigure(p1, true, true);
                c.ArcTo(p2, new Size(RO, RO), 0, large, SweepDirection.Clockwise, true, false);
                c.LineTo(p3, true, false);
                c.ArcTo(p4, new Size(RI, RI), 0, large, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        private void BuildWheel()
        {
            WheelCanvas.Children.Clear();
            int n = _features.Length;
            double step = 360.0 / n;
            var accent = B("Brush.Accent");
            var seg = B("Brush.WheelSeg");
            var wheelBg = B("Brush.WheelBg");
            var wheelInk = B("Brush.WheelInk");

            for (int i = 0; i < n; i++)
            {
                double center = i * step;
                double a1 = center - step / 2 + GAP / 2, a2 = center + step / 2 - GAP / 2;
                bool active = i == _hovered;

                var path = new Path { Data = Sector(a1, a2), Fill = active ? accent : seg };
                WheelCanvas.Children.Add(path);

                Point pt = Polar((RO + RI) / 2, center);
                var f = _features[i];
                var tb = new TextBlock
                {
                    Text = f.Glyph,
                    FontFamily = f.GlyphIsText ? new FontFamily("Segoe UI") : new FontFamily("Segoe MDL2 Assets"),
                    FontSize = f.GlyphIsText ? 20 : 21,
                    FontWeight = FontWeights.Bold,
                    Foreground = active ? wheelBg : wheelInk,
                    IsHitTestVisible = false,
                    TextAlignment = TextAlignment.Center,
                    Width = 40
                };
                Canvas.SetLeft(tb, pt.X - 20);
                Canvas.SetTop(tb, pt.Y - 15);
                WheelCanvas.Children.Add(tb);
            }

            var ring = new Ellipse
            {
                Width = (RO + 5) * 2,
                Height = (RO + 5) * 2,
                Stroke = accent,
                StrokeThickness = 2.4,
                Opacity = 0.85,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, CX - (RO + 5));
            Canvas.SetTop(ring, CY - (RO + 5));
            WheelCanvas.Children.Add(ring);
        }

        // ── Geometry-based hover ─────────────────────────────────────
        // The wheel disc is split into 4 quadrants by an "X" (diagonals). The central circle
        // (radius RI) is a neutral lock that keeps the current selection, so moving inward to
        // the options never deselects. Selection clears only when the pointer leaves the whole
        // wheel host, debounced to avoid flicker.
        private System.Windows.Threading.DispatcherTimer? _leaveTimer;

        private void Wheel_MouseMove(object sender, MouseEventArgs e)
        {
            _leaveTimer?.Stop();
            Point p = e.GetPosition(WheelHost);
            double dx = p.X - CX, dy = p.Y - CY;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < RI || dist > RO) return;   // central lock zone or outside ring → keep current

            double ang = Math.Atan2(dy, dx) * 180 / Math.PI;   // right=0, down=90, up=-90
            int q = ang >= -135 && ang < -45 ? 0    // top    → Smart Paste
                  : ang >= -45 && ang < 45   ? 1    // right  → Smart Copy
                  : ang >= 45 && ang < 135   ? 2    // bottom → Always On Top
                  : 3;                              // left   → Case Converter
            if (q != _hovered)
            {
                _hovered = q;
                BuildWheel();
                ShowFeature(q);
            }
        }

        private void Wheel_MouseLeave(object sender, MouseEventArgs e)
        {
            _leaveTimer ??= CreateLeaveTimer();
            _leaveTimer.Stop();
            _leaveTimer.Start();
        }

        private System.Windows.Threading.DispatcherTimer CreateLeaveTimer()
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (_hovered != -1)
                {
                    _hovered = -1;
                    BuildWheel();
                    ShowDefault();
                }
            };
            return t;
        }

        // ── Hub content ──────────────────────────────────────────────
        private TextBlock T(string text, double size, Brush fg, FontWeight? w = null, double top = 0)
            => new TextBlock
            {
                Text = text, FontSize = size, Foreground = fg,
                FontWeight = w ?? FontWeights.Normal, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, top, 0, 0)
            };

        private void ShowFeature(int i)
        {
            var f = _features[i];
            if (RunicTitle != null) RunicTitle.Text = f.Name.ToUpperInvariant();
            var ink = B("Brush.Ink"); var muted = B("Brush.Muted"); var accentInk = B("Brush.AccentInk");
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            var cico = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(13),
                Background = B("Brush.AccentSoft"), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock
                {
                    Text = f.Glyph,
                    FontFamily = f.GlyphIsText ? new FontFamily("Segoe UI") : new FontFamily("Segoe MDL2 Assets"),
                    FontSize = f.GlyphIsText ? 20 : 22, FontWeight = FontWeights.Bold, Foreground = accentInk,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            sp.Children.Add(cico);
            sp.Children.Add(T(f.Name, 16, ink, FontWeights.Bold));
            sp.Children.Add(T(Strings.StatusActive, 10.5, accentInk, FontWeights.SemiBold, 1));
            sp.Children.Add(T(f.Desc, 11, muted, null, 8));

            var kb = new Button { Content = ShortcutParser.Format(f.Shortcut()), Style = (Style)FindResource("KeyCap"),
                                  HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Tag = TagFor(f.Key) };
            kb.Click += RebindKey_Click;
            sp.Children.Add(kb);

            if (f.Modes != null)
            {
                var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
                string group = "mode_" + f.Key;   // one mutually-exclusive group per feature
                for (int m = 0; m < f.Modes.Length; m++)
                {
                    // RadioButton (with a shared GroupName) so picking a mode deselects the others,
                    // instead of stacking like independent toggles. Chip style targets ToggleButton,
                    // which RadioButton derives from, so the look is identical.
                    chips.Children.Add(new RadioButton { Content = f.Modes[m], Style = (Style)FindResource("Chip"),
                                                         GroupName = group, IsChecked = m == 0, Margin = new Thickness(3, 0, 3, 0) });
                }
                sp.Children.Add(chips);
            }
            else if (f.Opts != null)
            {
                foreach (var o in f.Opts)
                {
                    var row = new Grid { Width = 150, Margin = new Thickness(0, 7, 0, 0) };
                    row.Children.Add(new TextBlock { Text = o, FontSize = 11, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(new ToggleButton { Style = (Style)FindResource("Switch"), IsChecked = true, HorizontalAlignment = HorizontalAlignment.Right });
                    sp.Children.Add(row);
                }
            }

            HubContent.Content = sp;
        }

        private void ShowDefault()
        {
            if (RunicTitle != null) RunicTitle.Text = "";
            var ink = B("Brush.Ink"); var muted = B("Brush.Muted"); var faint = B("Brush.Faint"); var accentInk = B("Brush.AccentInk");
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            sp.Children.Add(new Image
            {
                Source = new BitmapImage(ThemeManager.EmblemUri),
                Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2)
            });

            var wm = new Rectangle { Width = 140, Height = 22, Fill = ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            wm.OpacityMask = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/assets/wordmark.png"))) { Stretch = Stretch.Uniform };
            sp.Children.Add(wm);

            sp.Children.Add(T(Strings.HubBy, 8.5, faint, FontWeights.SemiBold, 6));
            sp.Children.Add(T(Strings.HubCompany, 12, ink, FontWeights.Bold));
            sp.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/assets/hm.ico")),
                Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
            });

            int active = new[] { S.EnableSmartPaste, S.EnableSmartCopy, S.EnableAlwaysOnTop, S.EnableCaseConverter }.Count(x => x);
            var pill = new Border
            {
                Background = B("Brush.AccentSoft"), CornerRadius = new CornerRadius(999),
                Padding = new Thickness(13, 6, 13, 6), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock { Text = Strings.Format(nameof(Strings.HubRunning), active), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = accentInk }
            };
            sp.Children.Add(pill);
            sp.Children.Add(T(Strings.HubHint, 10.5, faint, null, 10));

            HubContent.Content = sp;
        }

        private static string TagFor(string featureKey) => featureKey switch
        {
            "paste" => "SmartPaste1",
            "copy" => "Copy",
            "aot" => "Aot",
            "case" => "Case",
            _ => ""
        };

        // ── Shortcut editor (live capture + OS-conflict detection) ───
        private string? _rebindTag;
        private string? _captured;

        private void RebindKey_Click(object sender, RoutedEventArgs e)
            => BeginRebind((sender as FrameworkElement)?.Tag as string);

        private void BeginRebind(string? tag)
        {
            _rebindTag = tag;
            if (string.IsNullOrEmpty(_rebindTag)) return;
            _captured = null;
            EditorTitle.Text = Strings.Format(nameof(Strings.ShortcutEditorTitle), ShortcutValidation.LabelForTag(_rebindTag));
            CaptureText.Text = Strings.CapturePrompt;
            EditorError.Visibility = Visibility.Collapsed;
            BtnEditorApply.IsEnabled = false;
            ShowOverlay(ShortcutEditorOverlay);
            Keyboard.Focus(ShortcutEditorOverlay);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (ShortcutEditorOverlay.Visibility != Visibility.Visible) { base.OnPreviewKeyDown(e); return; }
            e.Handled = true;

            if (e.Key == Key.Escape) { CloseOverlays(); return; }

            var mods = ShortcutValidation.CurrentModifiers();
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // ignore lone modifier presses (wait for the real key)
            if (ShortcutValidation.IsModifierKey(key))
            {
                CaptureText.Text = mods == ModifierKeys.None ? Strings.CapturePrompt : Strings.Format(nameof(Strings.CaptureModifiersOnly), ShortcutValidation.DescribeMods(mods));
                return;
            }

            string? token = ShortcutValidation.KeyToToken(key);
            if (token == null) { ShowEditorError(Strings.ErrorUnsupported); return; }

            if (mods == ModifierKeys.None && !ShortcutValidation.IsFunctionKey(token)) { ShowEditorError(Strings.ErrorNeedModifier); return; }
            if (mods == ModifierKeys.Shift) { ShowEditorError(Strings.ErrorShiftOnly); return; }

            string combo = ShortcutValidation.BuildCombo(mods, token);
            CaptureText.Text = ShortcutParser.Format(combo);

            string? reserved = ShortcutValidation.ReservedReason(mods, token);
            if (reserved != null) { ShowEditorError(Strings.Format(nameof(Strings.ErrorReserved), reserved)); return; }

            string? dup = ShortcutValidation.DuplicateOwner(S, combo, _rebindTag);
            if (dup != null) { ShowEditorError(Strings.Format(nameof(Strings.ErrorDuplicate), dup)); return; }

            EditorError.Visibility = Visibility.Collapsed;
            _captured = combo;
            BtnEditorApply.IsEnabled = true;
        }

        private void ShowEditorError(string msg)
        {
            EditorError.Text = msg;
            EditorError.Visibility = Visibility.Visible;
            _captured = null;
            BtnEditorApply.IsEnabled = false;
        }

        private void ApplyRebind_Click(object sender, RoutedEventArgs e)
        {
            if (_captured == null || _rebindTag == null) return;
            ShortcutValidation.ApplyToSettings(S, _rebindTag, _captured);
            SettingsManager.Save(S);
            AppInstance.RefreshHotkeys();
            PopulateSettings();
            if (_hovered >= 0) ShowFeature(_hovered);
            CloseOverlays();
        }
    }
}
