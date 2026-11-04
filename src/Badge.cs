using System;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class Badge : Form
    {
        public UsagePresentation Presentation;
        public BadgeLayoutSettings LayoutSettings = new BadgeLayoutSettings();
        public VisualTheme Theme = VisualTheme.Dark;
        public float ScaleFactor = 1;
        public Badge()
        {
            Text = "Codex Usage Overlay · Badge";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(1, 1, 1);
            TransparencyKey = BackColor;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000 | 0x00000020; return value; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            BadgeRenderer.Draw(e.Graphics, ClientRectangle, ScaleFactor, Theme, Presentation, LayoutSettings);
        }
    }
}
