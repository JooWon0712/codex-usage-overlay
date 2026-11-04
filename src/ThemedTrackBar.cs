using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class ThemedTrackBar : TrackBar
    {
        [DllImport("user32.dll",EntryPoint="SendMessageW")]
        private static extern IntPtr GetRectangle(IntPtr window,int message,IntPtr value,ref Native.RECT rectangle);
        private VisualTheme theme=VisualTheme.Dark;
        public float ScaleFactor=1;
        public VisualTheme Theme
        {
            get { return theme; }
            set {theme=value??VisualTheme.Dark;BackColor=theme.Background;Invalidate();}
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if(SystemInformation.HighContrast) return;
            if(message.Msg==0xf)
            { using(var graphics=Graphics.FromHwnd(Handle)) DrawSlider(graphics); }
            else if((message.Msg==0x317 || message.Msg==0x318) && message.WParam!=IntPtr.Zero)
            { using(var graphics=Graphics.FromHdc(message.WParam)) DrawSlider(graphics); }
        }
        private void DrawSlider(Graphics graphics)
        {
            Native.RECT thumb=new Native.RECT(),channel=new Native.RECT();
            GetRectangle(Handle,0x419,IntPtr.Zero,ref thumb); GetRectangle(Handle,0x41a,IntPtr.Zero,ref channel);
            if(thumb.Right<=thumb.Left || channel.Right<=channel.Left) return;
            using(var fill=new SolidBrush(BackColor)) graphics.FillRectangle(fill,ClientRectangle);
            var state=graphics.Save(); graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float center=(thumb.Left+thumb.Right)/2f,y=(channel.Top+channel.Bottom)/2f;
            using(var track=new Pen(theme.Border,Math.Max(2,3*ScaleFactor)) {StartCap=LineCap.Round,EndCap=LineCap.Round})
                graphics.DrawLine(track,channel.Left,y,channel.Right,y);
            using(var selected=new Pen(Enabled?theme.Accent:theme.Muted,Math.Max(2,3*ScaleFactor)) {StartCap=LineCap.Round,EndCap=LineCap.Round})
                graphics.DrawLine(selected,channel.Left,y,center,y);
            float radius=Math.Max(1,Math.Min(7*ScaleFactor,Math.Min((thumb.Right-thumb.Left)/2f-1,Math.Min(y-thumb.Top,thumb.Bottom-y)-1)));
            using(var fill=new SolidBrush(Enabled?theme.Accent:theme.Muted)) graphics.FillEllipse(fill,center-radius,y-radius,radius*2,radius*2);
            graphics.Restore(state);
            if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics,Rectangle.Inflate(ClientRectangle,-1,-1),theme.Text,BackColor);
        }
        protected override void OnValueChanged(EventArgs e) {base.OnValueChanged(e);Invalidate();}
        protected override void OnGotFocus(EventArgs e) {base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e) {base.OnLostFocus(e);Invalidate();}
    }
}
