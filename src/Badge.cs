using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class Badge : Form
    {
        public string MainText = "한도 확인 중", DetailText = "공식 조회 · 연결 대기";
        public VisualTheme Theme = VisualTheme.Dark;
        public UsageTone Tone = UsageTone.Attention;
        public double? LowWeeklyRemaining;
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

        protected override bool ShowWithoutActivation
        {
            get
            {
                return true;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000 | 0x00000020;
                return p;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                m.Result = new IntPtr(-1);
                return;
            }

            if (m.Msg == 0x21)
            {
                m.Result = new IntPtr(3);
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var theme = Theme;
            Color stateColor = Tone == UsageTone.Low ? theme.Danger : Tone == UsageTone.Normal ? theme.Success : theme.Attention;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = ScaleFactor, radius = 9 * s;
            var box = new RectangleF(0.7f, 0.7f, Width - 1.4f, Height - 1.4f);
            using (var path = Rounded(box, radius))
            using (var fill = new SolidBrush(theme.Surface))
            using (var stroke = new Pen(theme.Border, Math.Max(1, s)))
            {
                g.FillPath(fill, path);
                g.DrawPath(stroke, path);
            }

            if (LowWeeklyRemaining.HasValue)
            {
                var ring = new RectangleF(8 * s, 7 * s, 15 * s, 15 * s);
                using (var track = new Pen(theme.RingTrack, 2 * s))
                    g.DrawEllipse(track, ring);
                using (var arc = new Pen(stateColor, 2 * s))
                {
                    arc.StartCap = LineCap.Round;
                    arc.EndCap = LineCap.Round;
                    if (LowWeeklyRemaining.Value > 0)
                        g.DrawArc(arc, ring, -90, (float)(360 * LowWeeklyRemaining.Value / 100));
                }

                if (LowWeeklyRemaining.Value == 0)
                {
                    using (var mark = new Pen(stateColor, 1.6f * s))
                    {
                        g.DrawLine(mark, 15.5f * s, 11 * s, 15.5f * s, 15 * s);
                        g.DrawLine(mark, 15.5f * s, 17 * s, 15.5f * s, 17.5f * s);
                    }
                }
                else
                    using (var center = new SolidBrush(theme.RingCenter))
                        g.FillPolygon(center, new[] { new PointF(15.5f * s, 11 * s), new PointF(19 * s, 14.5f * s), new PointF(15.5f * s, 18 * s), new PointF(12 * s, 14.5f * s) });
            }
            else
                using (var dot = new SolidBrush(stateColor))
                    g.FillEllipse(dot, 10 * s, 11 * s, 5 * s, 5 * s);
            using (var mainFont = new Font("맑은 고딕", 12.1f * s, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subFont = new Font("맑은 고딕", 10 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                int mainLeft = (int)((LowWeeklyRemaining.HasValue ? 29 : 22) * s);
                TextRenderer.DrawText(g, MainText, mainFont, new Rectangle(mainLeft, (int)(5 * s), Width - mainLeft - (int)(8 * s), (int)(19 * s)), LowWeeklyRemaining.HasValue ? theme.DangerText : theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, DetailText, subFont, new Rectangle((int)(10 * s), (int)(25 * s), Width - (int)(20 * s), (int)(16 * s)), stateColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
