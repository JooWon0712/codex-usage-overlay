using System;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // A padded frame around the native fixed-height edit/spinner. Its internals
    // keep their normal layout and DPI behavior; only this outer field is scaled.
    internal sealed class ThemedNumberField : UserControl
    {
        private readonly ThemedNumericUpDown number;
        private VisualTheme theme = VisualTheme.Dark;
        private float scale = 1;
        private bool hovered;
        public float ScaleFactor
        {
            get { return scale; }
            set { scale=Math.Max(0.5f,value); number.ScaleFactor=scale; PerformLayout(); Invalidate(); }
        }
        public VisualTheme Theme
        {
            get { return theme; }
            set { theme=value??VisualTheme.Dark; BackColor=theme.Background; number.Theme=theme; Invalidate(true); }
        }
        public ThemedNumberField(ThemedNumericUpDown number)
        {
            this.number=number; AutoScaleMode=AutoScaleMode.None; TabStop=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            Controls.Add(number);
            foreach (Control child in number.Controls)
            {
                child.MouseEnter += delegate { hovered=true; Invalidate(); };
                child.MouseLeave += delegate { hovered=false; Invalidate(); };
            }
            number.MouseEnter += delegate { hovered=true; Invalidate(); };
            number.MouseLeave += delegate { hovered=false; Invalidate(); };
            Theme=theme;
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if(number==null) return;
            int left=(int)Math.Round(12*scale),right=(int)Math.Round(6*scale);
            number.SetBounds(left,Math.Max(0,(Height-number.PreferredHeight)/2),Math.Max(1,Width-left-right),number.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        { InputChrome.Frame(e.Graphics,ClientRectangle,scale,theme,Parent==null?theme.Background:Parent.BackColor,ContainsFocus,hovered); }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
    }
}
