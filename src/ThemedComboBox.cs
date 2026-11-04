using System;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // Keep the native list, selection, keyboard and accessibility behavior.
    // Only the closed field's chrome is painted over the native paint result.
    internal sealed class ThemedComboBox : ComboBox
    {
        private VisualTheme theme = VisualTheme.Dark;
        private bool hovered;
        public float ScaleFactor = 1;
        public VisualTheme Theme
        {
            get { return theme; }
            set { theme = value ?? VisualTheme.Dark; BackColor = theme.Surface; ForeColor = theme.Text; Invalidate(); }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0xf)
            {
                using (var graphics = Graphics.FromHwnd(Handle)) PaintField(graphics);
            }
            else if ((message.Msg == 0x317 || message.Msg == 0x318) && message.WParam != IntPtr.Zero)
            {
                using (var graphics = Graphics.FromHdc(message.WParam)) PaintField(graphics);
            }
        }

        internal void PaintField(Graphics graphics)
        {
            if (Width < 2 || Height < 2) return;
            float scale = Math.Max(0.5f, ScaleFactor);
            InputChrome.Frame(graphics, ClientRectangle, scale, theme, Parent == null ? theme.Background : Parent.BackColor, Focused || DroppedDown, hovered);
            int arrowWidth = (int)Math.Round(32 * scale), padding = (int)Math.Round(12 * scale);
            var arrow = new Rectangle(Width-arrowWidth, 0, arrowWidth, Height);
            InputChrome.Chevron(graphics, arrow, scale, Enabled ? theme.Text : theme.Muted, DroppedDown);
            var text = new Rectangle(padding, 0, Math.Max(0, Width-arrowWidth-padding), Height);
            TextRenderer.DrawText(graphics, Text, Font, text, Enabled ? theme.Text : theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered=true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnDropDown(EventArgs e) { base.OnDropDown(e); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    }
}
