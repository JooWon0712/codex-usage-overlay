using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // A separate hit target keeps the layered badge click-through across processes.
    internal sealed class BadgeFormatButton : Form
    {
        private const int SizeDip = 24, RightInsetDip = 8, StackedRightInsetDip = 12;
        private VisualTheme theme = VisualTheme.Dark;
        private bool pressed, hovered, compact, dirty = true;
        private float scale = 1;
        private Rectangle presentedBounds;
        private IntPtr presentedWindow;
        internal event EventHandler ToggleRequested;

        internal BadgeFormatButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            SetStyle(ControlStyles.Selectable, false);
            SetAppearance(VisualTheme.Dark, false);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00080000 | 0x80; // NOACTIVATE | LAYERED | TOOLWINDOW
                return parameters;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) // WM_MOUSEACTIVATE
            {
                message.Result = new IntPtr(3); // MA_NOACTIVATE; keep the mouse click.
                return;
            }
            base.WndProc(ref message);
        }

        internal static Rectangle CalculateBounds(Rectangle badge, BadgeLayoutSettings layout, float scale)
        {
            int size = Math.Max(1, (int)Math.Round(SizeDip * scale));
            int inset = (int)Math.Round((layout.StackedDetail ? StackedRightInsetDip : RightInsetDip) * scale);
            int header = (int)Math.Round(layout.HeaderHeight * scale);
            if (badge.Width < inset + size || badge.Height < header || header < size) return Rectangle.Empty;
            return new Rectangle(badge.Right - inset - size, badge.Top + (header - size) / 2, size, size);
        }

        internal static void DrawChevron(Graphics graphics, Rectangle bounds, float scale, Color color, bool compact)
        {
            float centerX = bounds.Left + bounds.Width / 2f, centerY = bounds.Top + bounds.Height / 2f;
            float halfWidth = 4.5f * scale, rise = (compact ? 2.25f : -2.25f) * scale;
            var state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color, 1.65f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    graphics.DrawLines(pen, new[] { new PointF(centerX - halfWidth, centerY - rise),
                        new PointF(centerX, centerY + rise), new PointF(centerX + halfWidth, centerY - rise) });
            }
            finally { graphics.Restore(state); }
        }

        internal void SetAppearance(VisualTheme value, bool compact)
        {
            string action = compact ? "상세형" : "간결형";
            if (ReferenceEquals(theme, value) && Text == action) return;
            theme = value;
            this.compact = compact;
            Text = action;
            AccessibleName = action + "으로 전환";
            dirty = true;
        }

        internal bool Present(float badgeScale)
        {
            if (ClientSize.Width < 1 || ClientSize.Height < 1) return false;
            var window = Handle;
            Rectangle bounds = Bounds;
            if (!dirty && badgeScale == scale && presentedBounds == bounds && presentedWindow == window) return true;
            scale = badgeScale;
            using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // Alpha zero would miss hit testing. The badge paints the resting glyph.
                    graphics.Clear(Color.FromArgb(1, 0, 0, 0));
                    if (hovered)
                    {
                        bool contrast = SystemInformation.HighContrast;
                        Color highlight = contrast ? SystemColors.Highlight : Color.FromArgb(pressed ? 90 : 46, theme.Text);
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var fill = new SolidBrush(highlight)) graphics.FillEllipse(fill, Rectangle.Inflate(ClientRectangle, -2, -2));
                        DrawChevron(graphics, ClientRectangle, scale, contrast ? SystemColors.HighlightText : theme.Text, compact);
                    }
                }
                if (!LayeredBitmapPresenter.Present(window, bitmap, bounds.Location, 255)) { dirty = true; return false; }
            }
            presentedWindow = window;
            presentedBounds = bounds;
            dirty = false;
            return true;
        }

        private void RefreshSurface()
        {
            dirty = true;
            if (Visible) Present(scale);
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; RefreshSurface(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; RefreshSurface(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressed = true;
            Capture = true;
            RefreshSurface();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool activate = pressed && ClientRectangle.Contains(e.Location);
            pressed = false;
            Capture = false;
            RefreshSurface();
            var toggle = ToggleRequested;
            if (activate && toggle != null) toggle(this, EventArgs.Empty);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture || !pressed) return;
            pressed = false;
            RefreshSurface();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) return;
            pressed = hovered = false;
            dirty = true;
            if (IsHandleCreated && Capture) Capture = false;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            dirty = true;
            base.OnHandleDestroyed(e);
        }
    }
}
