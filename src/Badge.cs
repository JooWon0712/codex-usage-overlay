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
        private Bitmap surface;
        private UsagePresentation renderedPresentation;
        private BadgeLayoutSettings renderedLayout;
        private VisualTheme renderedTheme;
        private float renderedScale;
        private int renderedTransparency, lastVisibility=-1;
        private bool renderedContrast;
        private Point lastDestination;
        private IntPtr lastWindow;
        public Badge()
        {
            Text = "Codex Usage Overlay · Badge";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
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
            // The visible contents are submitted as a premultiplied alpha bitmap.
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        internal bool Present(byte visibility=255)
        {
            if(ClientSize.Width<1 || ClientSize.Height<1) return false;
            bool contrast=SystemInformation.HighContrast;
            bool dirty=surface==null || surface.Size!=ClientSize || renderedPresentation!=Presentation
                || renderedLayout!=LayoutSettings || renderedTheme!=Theme || renderedScale!=ScaleFactor
                || renderedContrast!=contrast || renderedTransparency!=LayoutSettings.BackgroundTransparency;
            if(dirty)
            {
                Bitmap updated=BadgeRenderer.CreateBitmap(ClientSize,ScaleFactor,Theme,Presentation,LayoutSettings);
                if(surface!=null) surface.Dispose(); surface=updated;
                renderedPresentation=Presentation; renderedLayout=LayoutSettings; renderedTheme=Theme; renderedScale=ScaleFactor;
                renderedContrast=contrast; renderedTransparency=LayoutSettings.BackgroundTransparency;
            }
            var window=Handle;
            if(!dirty && lastVisibility==visibility && lastDestination==Location && lastWindow==window) return true;
            bool result=LayeredBitmapPresenter.Present(window,surface,Location,visibility);
            if(result) {lastVisibility=visibility;lastDestination=Location;lastWindow=window;}
            else lastVisibility=-1;
            return result;
        }
        protected override void OnHandleDestroyed(EventArgs e) {lastWindow=IntPtr.Zero;lastVisibility=-1;base.OnHandleDestroyed(e);}
        protected override void Dispose(bool disposing)
        { if(disposing && surface!=null) {surface.Dispose();surface=null;} base.Dispose(disposing); }
    }
}
