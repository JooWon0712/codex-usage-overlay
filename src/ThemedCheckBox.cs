using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class ThemedCheckBox : CheckBox
    {
        public VisualTheme Theme = VisualTheme.Dark;
        public float ScaleFactor = 1;

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UseVisualStyleBackColor = false;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var fill = new SolidBrush(Parent == null ? Theme.Background : Parent.BackColor))
                e.Graphics.FillRectangle(fill, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var theme = Theme ?? VisualTheme.Dark;
            float scale = ScaleFactor, side = 14 * scale;
            var box = new RectangleF(1 * scale, (Height - side) / 2, side, side);
            Color text = Enabled ? theme.Text : theme.Muted;
            using (var fill = new SolidBrush(Checked && Enabled ? theme.Accent : theme.Surface))
            using (var border = new Pen(Enabled ? theme.Accent : theme.Muted, Math.Max(1, scale)))
            {
                e.Graphics.FillRectangle(fill, box);
                e.Graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            }
            if (Checked)
            {
                Color tick = !Enabled ? theme.Muted : SystemInformation.HighContrast ? SystemColors.HighlightText : theme.Background;
                using (var pen = new Pen(tick, Math.Max(1.5f, 1.7f * scale)))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(pen, new[] { new PointF(box.Left + 3 * scale, box.Top + 7 * scale), new PointF(box.Left + 6 * scale, box.Top + 10 * scale), new PointF(box.Left + 12 * scale, box.Top + 3 * scale) });
                }
            }
            var bounds = new Rectangle((int)(22 * scale), 0, Math.Max(0, Width - (int)(22 * scale)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, text, theme.Background);
        }
    }
}
