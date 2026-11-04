using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class ThemedButton : Button
    {
        private VisualTheme theme = VisualTheme.Dark;
        private bool mouseInside;
        private bool mousePressed;
        private bool keyboardPressed;
        private bool defaultButton;
        private bool selected;
        public float ScaleFactor;

        public ThemedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = theme.Surface;
            ForeColor = theme.Text;
            TextAlign = ContentAlignment.MiddleCenter;
        }

        public VisualTheme Theme
        {
            get { return theme; }
            set
            {
                theme = value ?? VisualTheme.Dark;
                BackColor = theme.Surface;
                ForeColor = theme.Text;
                Invalidate();
            }
        }

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
            }
        }

        public override void NotifyDefault(bool value)
        {
            defaultButton = value;
            base.NotifyDefault(value);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var fill = new SolidBrush(Parent == null ? theme.Background : Parent.BackColor))
                e.Graphics.FillRectangle(fill, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // ButtonBase is opaque on the target framework, so the paint path
            // cannot depend on OnPaintBackground running before this method.
            using (var background = new SolidBrush(Parent == null ? theme.Background : Parent.BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);
            if (ClientSize.Width <= 1 || ClientSize.Height <= 1)
            {
                return;
            }

            float scale = ScaleFactor > 0 ? ScaleFactor : Math.Max(1f, e.Graphics.DpiX / 96f);
            bool pressed = Enabled && (keyboardPressed || (mousePressed && mouseInside));
            Color fill = !Enabled ? theme.Surface : pressed ? theme.Pressed : mouseInside || selected ? theme.Hover : theme.Surface;
            Color text = Enabled ? theme.Text : theme.Muted;
            Color border = Enabled && (selected || defaultButton || mouseInside || pressed) ? theme.Accent : theme.Border;
            float stroke = Math.Min(Math.Max(1f, scale), Math.Min(ClientSize.Width, ClientSize.Height) / 2f);
            RectangleF bounds = new RectangleF(stroke / 2f, stroke / 2f,
                Math.Max(0f, ClientSize.Width - stroke), Math.Max(0f, ClientSize.Height - stroke));

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = RoundedRectangle(bounds, 8f * scale))
                using (Brush brush = new SolidBrush(fill))
                using (Pen pen = new Pen(border, stroke))
                {
                    e.Graphics.FillPath(brush, shape);
                    e.Graphics.DrawPath(pen, shape);
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }

            int horizontalPadding = Math.Max(1, (int)Math.Round(8f * scale));
            int verticalPadding = Math.Max(1, (int)Math.Round(3f * scale));
            Rectangle textBounds = new Rectangle(horizontalPadding, verticalPadding,
                Math.Max(0, ClientSize.Width - horizontalPadding * 2),
                Math.Max(0, ClientSize.Height - verticalPadding * 2));
            if (pressed)
            {
                int offset = Math.Max(1, (int)Math.Round(scale));
                textBounds.Offset(offset, offset);
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
                | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
            if (!UseMnemonic)
            {
                flags |= TextFormatFlags.NoPrefix;
            }
            else if (!ShowKeyboardCues)
            {
                flags |= TextFormatFlags.HidePrefix;
            }

            if (RightToLeft == RightToLeft.Yes)
            {
                flags |= TextFormatFlags.RightToLeft;
            }

            if (textBounds.Width > 0 && textBounds.Height > 0)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, text, flags);
            }

            if (Focused && ShowFocusCues && Enabled)
            {
                int inset = Math.Max(2, (int)Math.Round(4f * scale));
                Rectangle focus = Rectangle.Inflate(ClientRectangle, -inset, -inset);
                if (focus.Width > 0 && focus.Height > 0)
                {
                    ControlPaint.DrawFocusRectangle(e.Graphics, focus, text, fill);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            mouseInside = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            mouseInside = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool inside = ClientRectangle.Contains(e.Location);
            if (mouseInside != inside)
            {
                mouseInside = inside;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                mousePressed = true;
                mouseInside = ClientRectangle.Contains(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                mousePressed = false;
            }

            base.OnMouseUp(e);
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture)
            {
                mousePressed = false;
            }

            base.OnMouseCaptureChanged(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None && Enabled)
            {
                keyboardPressed = true;
            }

            base.OnKeyDown(e);
            Invalidate();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                keyboardPressed = false;
            }

            base.OnKeyUp(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            keyboardPressed = false;
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled)
            {
                mousePressed = false;
                keyboardPressed = false;
            }

            base.OnEnabledChanged(e);
            Invalidate();
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0f)
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
