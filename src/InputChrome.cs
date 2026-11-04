using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PersonalUsageOverlay
{
    internal static class InputChrome
    {
        internal static void Frame(Graphics graphics, Rectangle bounds, float scale, VisualTheme theme, Color background, bool focused, bool hovered)
        {
            using (var brush = new SolidBrush(background)) graphics.FillRectangle(brush, bounds);
            float stroke = Math.Max(1, scale), inset = stroke / 2;
            var box = new RectangleF(bounds.Left + inset, bounds.Top + inset, Math.Max(1, bounds.Width - stroke), Math.Max(1, bounds.Height - stroke));
            using (var path = Rounded(box, 7 * scale))
            using (var fill = new SolidBrush(theme.Surface))
            using (var border = new Pen(focused || hovered ? theme.Accent : theme.Border, stroke))
            {
                var state = graphics.Save();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillPath(fill, path); graphics.DrawPath(border, path);
                graphics.Restore(state);
            }
        }

        internal static void Chevron(Graphics graphics, Rectangle bounds, float scale, Color color, bool up)
        {
            scale = Math.Min(scale, Math.Min((bounds.Width-4f)/9f, (bounds.Height-2f)/5f));
            if (scale <= 0) return;
            float cx = bounds.Left + bounds.Width / 2f, cy = bounds.Top + bounds.Height / 2f;
            float half = 3.5f * scale, rise = 2f * scale * (up ? -1 : 1);
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, Math.Max(1.3f, 1.35f * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                graphics.DrawLines(pen, new[] { new PointF(cx-half,cy-rise/2), new PointF(cx,cy+rise/2), new PointF(cx+half,cy-rise/2) });
            graphics.Restore(state);
        }

        private static GraphicsPath Rounded(RectangleF box, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
            var path = new GraphicsPath();
            path.AddArc(box.Left, box.Top, d, d, 180, 90); path.AddArc(box.Right-d, box.Top, d, d, 270, 90);
            path.AddArc(box.Right-d, box.Bottom-d, d, d, 0, 90); path.AddArc(box.Left, box.Bottom-d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }
}
