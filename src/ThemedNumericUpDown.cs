using System;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // The native edit and spin buttons still own input, repeat and validation.
    internal sealed class ThemedNumericUpDown : NumericUpDown
    {
        private VisualTheme theme = VisualTheme.Dark;
        private Control editor, buttons;
        private SpinPaintWindow spinPaint;
        private float scale = 1;
        public float ScaleFactor
        {
            get { return scale; }
            set { scale = Math.Max(0.5f, value); Invalidate(true); }
        }
        public VisualTheme Theme
        {
            get { return theme; }
            set
            {
                theme = value ?? VisualTheme.Dark; BackColor = theme.Surface; ForeColor = theme.Text;
                if (editor != null) { editor.BackColor = theme.Surface; editor.ForeColor = theme.Text; }
                Invalidate(true);
            }
        }

        public ThemedNumericUpDown()
        {
            BorderStyle = BorderStyle.None; AutoSize = false;
            foreach (Control child in Controls)
            {
                if (child is TextBox) editor = child;
                else buttons = child;
            }
            if (buttons != null)
            {
                spinPaint = new SpinPaintWindow(this, buttons);
                buttons.HandleCreated += delegate { spinPaint.Attach(); };
                buttons.HandleDestroyed += delegate { spinPaint.Detach(); };
                if (buttons.IsHandleCreated) spinPaint.Attach();
            }
            Theme = theme;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && spinPaint != null) spinPaint.Detach();
            base.Dispose(disposing);
        }

        private sealed class SpinPaintWindow : NativeWindow
        {
            private readonly ThemedNumericUpDown owner;
            private readonly Control control;
            public SpinPaintWindow(ThemedNumericUpDown owner, Control control) { this.owner=owner; this.control=control; }
            public void Attach() { if (Handle != IntPtr.Zero) ReleaseHandle(); AssignHandle(control.Handle); }
            public void Detach() { if (Handle != IntPtr.Zero) ReleaseHandle(); }
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0xf)
                { using (var graphics = Graphics.FromHwnd(Handle)) Draw(graphics); }
                else if ((message.Msg == 0x317 || message.Msg == 0x318) && message.WParam != IntPtr.Zero)
                { using (var graphics = Graphics.FromHdc(message.WParam)) Draw(graphics); }
            }
            private void Draw(Graphics graphics)
            {
                var theme = owner.theme; var bounds = control.ClientRectangle;
                using (var fill = new SolidBrush(theme.Surface)) graphics.FillRectangle(fill, bounds);
                int half = bounds.Height/2;
                Color color = owner.Enabled ? theme.Text : theme.Muted;
                InputChrome.Chevron(graphics, new Rectangle(0,0,bounds.Width,half), owner.scale, color, true);
                InputChrome.Chevron(graphics, new Rectangle(0,half,bounds.Width,bounds.Height-half), owner.scale, color, false);
            }
        }
    }
}
