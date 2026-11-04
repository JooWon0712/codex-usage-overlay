using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class BadgeRenderer
    {
        public static RectangleF ElementBounds(BadgeElement element)
        {
            if (element == null)
                return RectangleF.Empty;
            return new RectangleF(element.X, element.Y, element.Width, BadgeLayoutSettings.ElementHeight(element));
        }

        public static void Draw(Graphics g, Rectangle bounds, float scale, VisualTheme theme,
            UsagePresentation presentation, BadgeLayoutSettings layout)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0)
                return;
            if (Single.IsNaN(scale) || Single.IsInfinity(scale) || scale <= 0)
                scale = 1f;
            theme = theme ?? VisualTheme.Dark;
            BadgeLayoutSettings normalized = layout == null ? new BadgeLayoutSettings() : layout.Copy();
            normalized.Normalize();
            double physicalWidth = (double)normalized.Width * scale;
            double physicalHeight = (double)normalized.Height * scale;
            if (physicalWidth > Int32.MaxValue || physicalHeight > Int32.MaxValue
                || physicalWidth < 1d || physicalHeight < 1d)
                return;

            var canvas = new RectangleF(bounds.Left, bounds.Top, (float)physicalWidth, (float)physicalHeight);
            RectangleF clip = RectangleF.Intersect(canvas, bounds);
            if (clip.Width <= 0 || clip.Height <= 0)
                return;
            UsageTone tone = presentation == null ? UsageTone.Attention : presentation.Tone;
            Color stateColor = tone == UsageTone.Low ? theme.Danger
                : tone == UsageTone.Normal ? theme.Success : theme.Attention;
            double? lowWeekly = presentation == null ? null : presentation.LowWeeklyRemaining;
            if (lowWeekly.HasValue && (Double.IsNaN(lowWeekly.Value) || Double.IsInfinity(lowWeekly.Value)
                || lowWeekly.Value < 0 || lowWeekly.Value > 100))
                lowWeekly = null;

            GraphicsState saved = g.Save();
            try
            {
                // Use device coordinates for both GDI+ and TextRenderer. The latter
                // does not apply Graphics.ScaleTransform to its text rectangles.
                g.ResetTransform();
                g.SetClip(clip, CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float inset = 0.7f * scale;
                var surface = new RectangleF(canvas.Left + inset, canvas.Top + inset,
                    Math.Max(0f, canvas.Width - inset * 2f), Math.Max(0f, canvas.Height - inset * 2f));
                using (GraphicsPath path = Rounded(surface, 9f * scale))
                using (Brush fill = new SolidBrush(theme.Surface))
                using (Pen border = new Pen(theme.Border, Math.Max(1f, scale)))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                foreach (BadgeElement element in normalized.Elements)
                {
                    if (!element.Visible)
                        continue;
                    RectangleF logical = ElementBounds(element);
                    var physical = new RectangleF(bounds.Left + logical.Left * scale,
                        bounds.Top + logical.Top * scale, logical.Width * scale, logical.Height * scale);
                    if (element.Key == "indicator")
                    {
                        DrawIndicator(g, physical, scale, theme, stateColor, lowWeekly);
                        continue;
                    }

                    string text;
                    Color color;
                    if (element.Key == "remaining")
                    {
                        text = TextOr(presentation == null ? null : presentation.MainText, "한도 확인 중");
                        color = lowWeekly.HasValue ? theme.DangerText : theme.Text;
                    }
                    else if (element.Key == "updated")
                    {
                        text = TextOr(presentation == null ? null : presentation.DetailText, "연결 대기");
                        color = stateColor;
                    }
                    else if (element.Key == "weekly-reset")
                    {
                        text = TextOr(presentation == null ? null : presentation.ResetDateText, "주간 초기화 확인 불가");
                        color = theme.Muted;
                    }
                    else if (element.Key == "countdown")
                    {
                        text = TextOr(presentation == null ? null : presentation.CountdownText, "확인 불가");
                        if (!normalized.Elements.Exists(item => item.Key == "weekly-reset" && item.Visible))
                            text = "초기화 · " + text;
                        color = lowWeekly.HasValue ? theme.DangerText : theme.Accent;
                    }
                    else
                        continue;

                    Rectangle textBounds = Rectangle.Round(physical);
                    if (textBounds.Width <= 0 || textBounds.Height <= 0)
                        continue;
                    using (var font = new Font("Segoe UI", element.FontSize * scale,
                        element.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        TextRenderer.DrawText(g, text, font, textBounds, color,
                            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                            | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                    }
                }
            }
            finally
            {
                g.Restore(saved);
            }
        }

        private static void DrawIndicator(Graphics g, RectangleF bounds, float scale, VisualTheme theme,
            Color stateColor, double? lowWeekly)
        {
            float unit = Math.Min(bounds.Width / 18f, scale);
            if (unit <= 0)
                return;
            float centerX = bounds.Left + bounds.Width / 2f;
            float centerY = bounds.Top + bounds.Height / 2f;
            if (!lowWeekly.HasValue)
            {
                using (Brush dot = new SolidBrush(stateColor))
                    g.FillEllipse(dot, centerX - 2.5f * unit, centerY - 2.5f * unit, 5f * unit, 5f * unit);
                return;
            }

            var ring = new RectangleF(centerX - 7.5f * unit, centerY - 7.5f * unit, 15f * unit, 15f * unit);
            using (Pen track = new Pen(theme.RingTrack, 2f * unit))
                g.DrawEllipse(track, ring);
            using (Pen arc = new Pen(stateColor, 2f * unit))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                if (lowWeekly.Value > 0)
                    g.DrawArc(arc, ring, -90f, (float)(360d * lowWeekly.Value / 100d));
            }

            if (lowWeekly.Value == 0)
            {
                using (Pen mark = new Pen(stateColor, 1.6f * unit))
                {
                    g.DrawLine(mark, centerX, centerY - 4f * unit, centerX, centerY);
                    g.DrawLine(mark, centerX, centerY + 2.5f * unit, centerX, centerY + 3f * unit);
                }
            }
            else
            {
                using (Brush center = new SolidBrush(theme.RingCenter))
                    g.FillPolygon(center, new[]
                    {
                        new PointF(centerX, centerY - 3.5f * unit),
                        new PointF(centerX + 3.5f * unit, centerY),
                        new PointF(centerX, centerY + 3.5f * unit),
                        new PointF(centerX - 3.5f * unit, centerY)
                    });
            }
        }

        private static string TextOr(string text, string fallback)
        {
            return String.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
