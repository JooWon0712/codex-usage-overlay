using System;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // Read-only preview. It never captures the mouse or edits element coordinates.
    internal sealed class BadgePreview : Control
    {
        private UsagePresentation presentation;
        private VisualTheme theme = VisualTheme.Dark;
        private BadgeLayoutSettings layout = new BadgeLayoutSettings();
        public float UiScale = 1;
        public UsagePresentation Presentation
        {
            get { return presentation; }
            set { if (ReferenceEquals(presentation, value)) return; presentation = value; Invalidate(); }
        }
        public VisualTheme Theme
        {
            get { return theme; }
            set { theme = value ?? VisualTheme.Dark; Invalidate(); }
        }
        public BadgeLayoutSettings LayoutSettings
        {
            get { return layout; }
            set { layout = value == null ? new BadgeLayoutSettings() : value.Copy(); layout.Normalize(); Invalidate(); }
        }
        public BadgePreview()
        {
            TabStop = false;
            AccessibleName = "오버레이 미리보기";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var fill = new SolidBrush(theme.Background)) e.Graphics.FillRectangle(fill, ClientRectangle);
            using (var edge = new Pen(theme.Border)) e.Graphics.DrawRectangle(edge, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            float margin = 16 * UiScale;
            float scale = Math.Min(UiScale, Math.Min((Width - 2 * margin) / layout.Width, (Height - 2 * margin) / layout.Height));
            if (scale <= 0) return;
            int width = (int)Math.Round(layout.Width * scale), height = (int)Math.Round(layout.Height * scale);
            BadgeRenderer.Draw(e.Graphics, new Rectangle((Width - width) / 2, (Height - height) / 2, width, height), scale, theme, presentation, layout);
        }
    }
}
