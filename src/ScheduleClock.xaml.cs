using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SmartPaste
{
    /// <summary>
    /// Analog clock schedule picker — 80s aesthetic.
    ///
    /// Shows a 12-hour clock face with draggable arcs:
    ///   Blue arc  = work period (morning + afternoon)
    ///   Grey gap  = lunch break
    ///   4 handles = Start, LunchStart, LunchEnd, End
    ///
    /// Drag handles snap to 15-minute increments.
    /// Internal values are 24-hour (e.g. 13.5 = 13:30).
    /// </summary>
    public partial class ScheduleClock : UserControl
    {
        // ── Constants ────────────────────────────────────────────────
        private const double CX = 130, CY = 130, R = 108;
        private const double ArcR = 92;  // radius of the schedule arcs
        private const double HandleR = 8;

        // ── Colors (80s aesthetic) ───────────────────────────────────
        private static readonly Brush FaceBrush = new SolidColorBrush(Color.FromRgb(26, 26, 32));
        private static readonly Brush RingBrush = new SolidColorBrush(Color.FromRgb(55, 55, 62));
        private static readonly Brush TickBrush = new SolidColorBrush(Color.FromRgb(140, 140, 150));
        private static readonly Brush HourBrush = new SolidColorBrush(Color.FromRgb(170, 170, 175));
        private static readonly Brush WorkBrush = new SolidColorBrush(Color.FromRgb(96, 165, 250));  // Blue-400
        private static readonly Brush LunchBrush = new SolidColorBrush(Color.FromRgb(70, 70, 78));
        private static readonly Brush HandleFill = new SolidColorBrush(Color.FromRgb(240, 240, 245));
        private static readonly Brush HandleStroke = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        private static readonly Brush CenterDot = new SolidColorBrush(Color.FromRgb(96, 165, 250));

        // ── Schedule values (24h) ────────────────────────────────────
        public double StartHour { get; set; } = 9.0;
        public double LunchStartHour { get; set; } = 12.0;
        public double LunchEndHour { get; set; } = 13.5;
        public double EndHour { get; set; } = 17.0;

        public event Action? ScheduleChanged;

        // ── Drag state ───────────────────────────────────────────────
        private string? _dragging;

        // Ranges each handle is constrained to
        private static readonly (double min, double max)[] HandleRanges =
        {
            (6.0, 11.75),   // Start
            (10.0, 14.0),   // LunchStart
            (11.5, 15.5),   // LunchEnd
            (14.0, 21.0),   // End
        };

        public ScheduleClock()
        {
            InitializeComponent();
            Loaded += (_, _) => Redraw();
        }

        public void SetSchedule(double start, double lunchStart, double lunchEnd, double end)
        {
            StartHour = start;
            LunchStartHour = lunchStart;
            LunchEndHour = lunchEnd;
            EndHour = end;
            Redraw();
        }

        // ── Drawing ──────────────────────────────────────────────────

        private void Redraw()
        {
            Dial.Children.Clear();

            // Face
            AddEllipse(CX, CY, R, FaceBrush, RingBrush, 2);

            // Minute ticks (every 5 min = every 2.5°)
            for (int m = 0; m < 60; m++)
            {
                double angle = m / 60.0 * 360;
                double outerR = R - 4;
                double innerR = m % 5 == 0 ? R - 16 : R - 9;
                double thick = m % 5 == 0 ? 1.5 : 0.7;
                double opacity = m % 5 == 0 ? 0.6 : 0.25;
                AddLine(CX, CY, innerR, outerR, angle, TickBrush, thick, opacity);
            }

            // Hour labels
            for (int h = 1; h <= 12; h++)
            {
                double angle = h / 12.0 * 360;
                var pt = Polar(CX, CY, R - 28, angle);
                var tb = new TextBlock
                {
                    Text = h.ToString(),
                    FontSize = 13,
                    FontWeight = FontWeights.Light,
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    Foreground = HourBrush
                };
                tb.Measure(new Size(999, 999));
                Canvas.SetLeft(tb, pt.X - tb.DesiredSize.Width / 2);
                Canvas.SetTop(tb, pt.Y - tb.DesiredSize.Height / 2);
                Dial.Children.Add(tb);
            }

            // Schedule arcs
            DrawArc(StartHour, LunchStartHour, WorkBrush, 7);
            DrawArc(LunchStartHour, LunchEndHour, LunchBrush, 3);
            DrawArc(LunchEndHour, EndHour, WorkBrush, 7);

            // Center dot
            AddEllipse(CX, CY, 4, CenterDot, null, 0);

            // Handles
            DrawHandle(StartHour, "S");
            DrawHandle(LunchStartHour, "L1");
            DrawHandle(LunchEndHour, "L2");
            DrawHandle(EndHour, "E");

            // Digital readout
            TxtWorkAM.Text = $"{FormatH(StartHour)}-{FormatH(LunchStartHour)}";
            TxtLunch.Text = $"{FormatH(LunchStartHour)}-{FormatH(LunchEndHour)}";
            TxtWorkPM.Text = $"{FormatH(LunchEndHour)}-{FormatH(EndHour)}";
        }

        private void DrawArc(double fromHour, double toHour, Brush stroke, double thickness)
        {
            double startAngle = HourToAngle(fromHour);
            double endAngle = HourToAngle(toHour);
            var p1 = Polar(CX, CY, ArcR, startAngle);
            var p2 = Polar(CX, CY, ArcR, endAngle);

            double sweep = endAngle - startAngle;
            if (sweep < 0) sweep += 360;

            var fig = new PathFigure { StartPoint = p1, IsClosed = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = p2,
                Size = new Size(ArcR, ArcR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = sweep > 180
            });

            Dial.Children.Add(new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }

        private void DrawHandle(double hour, string tag)
        {
            var pt = Polar(CX, CY, ArcR, HourToAngle(hour));
            var ell = new Ellipse
            {
                Width = HandleR * 2, Height = HandleR * 2,
                Fill = HandleFill,
                Stroke = HandleStroke,
                StrokeThickness = 2.5,
                Tag = tag,
                Cursor = Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 6, ShadowDepth = 1, Opacity = 0.3, Color = Colors.Black
                }
            };
            Canvas.SetLeft(ell, pt.X - HandleR);
            Canvas.SetTop(ell, pt.Y - HandleR);
            Dial.Children.Add(ell);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private void AddEllipse(double cx, double cy, double r, Brush? fill, Brush? stroke, double thick)
        {
            var ell = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thick
            };
            Canvas.SetLeft(ell, cx - r);
            Canvas.SetTop(ell, cy - r);
            Dial.Children.Add(ell);
        }

        private void AddLine(double cx, double cy, double r1, double r2, double angleDeg,
            Brush stroke, double thick, double opacity)
        {
            var p1 = Polar(cx, cy, r1, angleDeg);
            var p2 = Polar(cx, cy, r2, angleDeg);
            Dial.Children.Add(new Line
            {
                X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
                Stroke = stroke, StrokeThickness = thick, Opacity = opacity
            });
        }

        /// <summary>12-hour clock: 12 at top, clockwise.</summary>
        private static double HourToAngle(double hour24)
        {
            return (hour24 % 12) / 12.0 * 360;
        }

        private static Point Polar(double cx, double cy, double r, double angleDeg)
        {
            double rad = (angleDeg - 90) * Math.PI / 180;
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }

        private static string FormatH(double hour24)
        {
            int h = (int)hour24;
            int m = (int)((hour24 - h) * 60);
            return $"{h:D2}:{m:D2}";
        }

        // ── Mouse interaction ────────────────────────────────────────

        private void Dial_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Ellipse ell && ell.Tag is string tag)
            {
                _dragging = tag;
                Dial.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Dial_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging == null) return;

            var pos = e.GetPosition(Dial);
            double dx = pos.X - CX;
            double dy = pos.Y - CY;

            // Angle from top, clockwise (in degrees)
            double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
            if (angle < 0) angle += 360;

            // Convert to 12-hour clock value
            double clockH = angle / 360.0 * 12;

            // Resolve to 24h using handle constraints
            int idx = _dragging switch { "S" => 0, "L1" => 1, "L2" => 2, _ => 3 };
            var (min, max) = HandleRanges[idx];

            // Try AM and PM interpretations, pick the one in range
            double am = clockH;
            double pm = clockH + 12;
            double hour24;
            if (am >= min && am <= max) hour24 = am;
            else if (pm >= min && pm <= max) hour24 = pm;
            else hour24 = Math.Abs(am - (min + max) / 2) < Math.Abs(pm - (min + max) / 2)
                ? Math.Clamp(am, min, max)
                : Math.Clamp(pm, min, max);

            // Snap to 15 minutes
            hour24 = Math.Round(hour24 * 4) / 4.0;

            switch (_dragging)
            {
                case "S": StartHour = hour24; break;
                case "L1": LunchStartHour = hour24; break;
                case "L2": LunchEndHour = hour24; break;
                case "E": EndHour = hour24; break;
            }

            Redraw();
        }

        private void Dial_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging != null)
            {
                _dragging = null;
                Dial.ReleaseMouseCapture();
                ScheduleChanged?.Invoke();
            }
        }
    }
}
