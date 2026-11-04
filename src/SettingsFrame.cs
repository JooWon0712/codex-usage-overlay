using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // Only the title/icon are client-painted. Windows owns the caption buttons,
    // system menu, move loop, minimize and close commands.
    // https://learn.microsoft.com/windows/win32/dwm/customframe
    internal class SettingsFrame : Form
    {
        internal const int CaptionHeightDip = 44;
        protected readonly Panel ContentHost = new Panel();
        private bool customFrame = CanUseCustomFrame(), changingFrame;
        private float frameScale = 1f;
        private Font captionFont;
        private Size requestedClientSize;
        private readonly Icon applicationIcon;
        private VisualTheme frameTheme = VisualTheme.Dark;

        protected SettingsFrame()
        {
            applicationIcon = LoadApplicationIcon();
            ShowIcon = true; Icon = applicationIcon;
            Controls.Add(ContentHost);
            SetFrameScale(1f);
        }

        private static Icon LoadApplicationIcon()
        {
            using (Stream stream = typeof(SettingsFrame).Assembly.GetManifestResourceStream("PersonalUsageOverlay.AppIcon"))
            {
                if (stream == null) throw new InvalidOperationException("Embedded application icon is missing.");
                using (var source = new Icon(stream)) return (Icon)source.Clone();
            }
        }

        internal bool HasCustomCaption { get { return customFrame && FormBorderStyle != FormBorderStyle.None; } }
        internal int CaptionHeight { get { return HasCustomCaption ? (int)Math.Round(CaptionHeightDip * frameScale) : 0; } }
        internal Rectangle CaptionIconBounds
        {
            get
            {
                int size = (int)Math.Round(20 * frameScale);
                return new Rectangle((int)Math.Round(14 * frameScale), (CaptionHeight - size) / 2, size, size);
            }
        }
        internal Rectangle NativeCaptionButtons
        {
            get
            {
                Native.RECT rect;
                if (IsHandleCreated && Native.DwmGetWindowRectAttribute(Handle, 5, out rect, Marshal.SizeOf(typeof(Native.RECT))) == 0
                    && rect.Right > rect.Left && rect.Bottom > rect.Top
                    && rect.Left >= 0 && rect.Right <= ClientSize.Width && rect.Top >= 0 && rect.Bottom <= CaptionHeight)
                    return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                return new Rectangle(Math.Max(0, ClientSize.Width - (int)Math.Round(138 * frameScale)), 0,
                    (int)Math.Round(138 * frameScale), CaptionHeight);
            }
        }

        protected void SetContentSize(int width, int height)
        { ClientSize = new Size(width, height + CaptionHeight); LayoutContent(); }

        protected override void SetClientSizeCore(int width, int height)
        {
            requestedClientSize = new Size(width, height);
            base.SetClientSizeCore(width, height);
            // WinForms still adds the original caption metrics for WS_CAPTION.
            // Our NCCALCSIZE makes the entire outer rectangle the client instead.
            if (HasCustomCaption && IsHandleCreated)
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, width, height, 0x16);
        }

        protected void SetFrameScale(float scale)
        {
            frameScale = Math.Max(1f, scale);
            if (captionFont != null) captionFont.Dispose();
            captionFont = new Font("Segoe UI", 13 * frameScale, FontStyle.Regular, GraphicsUnit.Pixel);
            LayoutContent(); Invalidate();
        }

        protected void SetFrameTheme(VisualTheme value)
        {
            frameTheme = value; ContentHost.BackColor = value.Background;
            RefreshFrame(); Invalidate();
        }

        private static bool CanUseCustomFrame()
        {
            if (SystemInformation.HighContrast) return false;
            try { bool enabled; return Native.DwmIsCompositionEnabled(out enabled) == 0 && enabled; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        private void LayoutContent()
        {
            if (ContentHost == null) return;
            ContentHost.SetBounds(0, CaptionHeight, ClientSize.Width, Math.Max(0, ClientSize.Height - CaptionHeight));
        }

        private void RefreshFrame()
        {
            if (!IsHandleCreated || changingFrame) return;
            changingFrame = true;
            try
            {
                bool available = CanUseCustomFrame();
                var margins = new Native.MARGINS { Top = available && FormBorderStyle != FormBorderStyle.None ? (int)Math.Round(CaptionHeightDip * frameScale) : 0 };
                try { if (Native.DwmExtendFrameIntoClientArea(Handle, ref margins) != 0) available = false; }
                catch (DllNotFoundException) { available = false; }
                catch (EntryPointNotFoundException) { available = false; }
                if (available != customFrame)
                {
                    Size bodySize = ContentHost.Size;
                    customFrame = available;
                    Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37); // FRAMECHANGED | NOACTIVATE | NOMOVE | NOSIZE | NOZORDER
                    SetContentSize(bodySize.Width, bodySize.Height);
                }
                LayoutContent();
            }
            finally { changingFrame = false; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshFrame();
            if (customFrame && !requestedClientSize.IsEmpty)
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, requestedClientSize.Width, requestedClientSize.Height, 0x36);
        }
        protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); LayoutContent(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected virtual void OnFrameDpiChanged(float scale) { SetFrameScale(scale); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintCaption(e.Graphics);
        }

        private void PaintCaption(Graphics graphics)
        {
            if (!HasCustomCaption || captionFont == null) return;
            Rectangle iconBounds = CaptionIconBounds;
            graphics.DrawIcon(applicationIcon, iconBounds);
            int left = iconBounds.Right + (int)Math.Round(12 * frameScale);
            Rectangle textBounds = new Rectangle(left, 0, Math.Max(0, NativeCaptionButtons.Left - left - (int)Math.Round(12 * frameScale)), CaptionHeight);
            // GDI TextRenderer clears glyph alpha in the extended DWM frame.
            // GDI+ retains coverage so the title stays opaque and antialiased.
            var state = graphics.Save();
            try
            {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var brush = new SolidBrush(frameTheme.Text))
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    graphics.DrawString(Text, captionFont, brush, textBounds, format);
                }
            }
            finally { graphics.Restore(state); }
        }

        // WM_PRINT adds a synthetic old non-client caption on custom DWM forms.
        // This developer sample contains our client area only; OS caption buttons
        // require an actual Windows screen capture, not a DrawToBitmap claim.
        internal Bitmap RenderClientPreview()
        {
            var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(frameTheme.Background); PaintCaption(graphics);
                using (var content = new Bitmap(ContentHost.Width, ContentHost.Height))
                {
                    ContentHost.DrawToBitmap(content, new Rectangle(Point.Empty, ContentHost.Size));
                    graphics.DrawImageUnscaled(content, ContentHost.Location);
                }
            }
            return bitmap;
        }

        internal int CaptionHitTest(Point client)
        {
            if (!HasCustomCaption || client.Y < 0 || client.Y >= CaptionHeight || client.X < 0 || client.X >= ClientSize.Width) return 1;
            if (NativeCaptionButtons.Contains(client)) return 1; // DWM gets first refusal in WndProc.
            return CaptionIconBounds.Contains(client) ? 3 : 2; // HTSYSMENU, HTCAPTION
        }

        protected override void WndProc(ref Message m)
        {
            if (HasCustomCaption)
            {
                if (m.Msg == 0x83 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; } // WM_NCCALCSIZE
                IntPtr result;
                // Keep Form's paint/activation/lifetime messages in WinForms.
                // DWM only consumes non-client input for the standard buttons.
                bool captionInput = m.Msg == 0x84 || m.Msg == 0x86 || (m.Msg >= 0xa0 && m.Msg <= 0xad) || m.Msg == 0x2a2;
                if (captionInput && Native.DwmDefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam, out result)) { m.Result = result; return; }
                if (m.Msg == 0x84) // WM_NCHITTEST, signed coordinates support monitors left/above primary.
                {
                    long packed = m.LParam.ToInt64();
                    var client = PointToClient(new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff))));
                    m.Result = new IntPtr(CaptionHitTest(client)); return;
                }
            }
            base.WndProc(ref m);
            if (m.Msg == 0x6 || m.Msg == 0x31e || m.Msg == 0x31a) RefreshFrame();
            if (m.Msg == 0x2e0)
            {
                OnFrameDpiChanged((m.WParam.ToInt64() & 0xffff) / 96f);
                RefreshFrame();
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { if (captionFont != null) captionFont.Dispose(); applicationIcon.Dispose(); }
        }
    }
}
