using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    // Paint the full extended caption so its title and window controls can
    // share a center line. The standard frame is used in high contrast.
    // https://learn.microsoft.com/windows/win32/dwm/customframe
    internal class SettingsFrame : Form
    {
        internal const int CaptionHeightDip = 44;
        private const int NativeCaptionControls = 0x00080000 | 0x00020000 | 0x00010000;
        private const int WmSystemCommand = 0x0112;
        private const int ScMove = 0xf010;
        private const int ScKeyMenu = 0xf100;
        protected readonly Panel ContentHost = new Panel();
        private readonly CaptionButton minimizeButton = new CaptionButton(false);
        private readonly CaptionButton closeButton = new CaptionButton(true);
        private readonly ContextMenuStrip captionMenu = new ContextMenuStrip();
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
            Controls.Add(minimizeButton);
            Controls.Add(closeButton);
            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            closeButton.Click += delegate { Close(); };
            captionMenu.Items.Add("이동", null, delegate { PostMessage(Handle, WmSystemCommand, new IntPtr(ScMove), IntPtr.Zero); });
            captionMenu.Items.Add("최소화", null, delegate { WindowState = FormWindowState.Minimized; });
            captionMenu.Items.Add(new ToolStripSeparator());
            captionMenu.Items.Add("닫기", null, delegate { Close(); });
            SetFrameScale(1f);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

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
        internal Button CaptionMinimizeButton { get { return minimizeButton; } }
        internal Button CaptionCloseButton { get { return closeButton; } }
        internal Rectangle ContentBounds { get { return ContentHost.Bounds; } }

        internal Rectangle CaptionIconBounds
        {
            get
            {
                int size = (int)Math.Round(20 * frameScale);
                return new Rectangle((int)Math.Round(14 * frameScale), (CaptionHeight - size) / 2, size, size);
            }
        }

        internal Rectangle CaptionTextBounds
        {
            get
            {
                int gap = (int)Math.Round(12 * frameScale);
                int left = CaptionIconBounds.Right + gap;
                int end = !ControlBox ? ClientSize.Width : MinimizeBox ? minimizeButton.Left : closeButton.Left;
                return new Rectangle(left, 0, Math.Max(0, end - left - gap), CaptionHeight);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                if (HasCustomCaption) parameters.Style &= ~NativeCaptionControls;
                return parameters;
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
            ApplyCaptionTheme();
            RefreshFrame(); Invalidate();
        }

        private void ApplyCaptionTheme()
        {
            if (!IsHandleCreated) return;
            try
            {
                bool highContrast = SystemInformation.HighContrast;
                int dark = frameTheme.IsDark && !highContrast ? 1 : 0;
                int caption = highContrast ? -1 : ColorTranslator.ToWin32(frameTheme.Background);
                int text = highContrast ? -1 : ColorTranslator.ToWin32(frameTheme.Text);
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
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
            if (minimizeButton == null || closeButton == null) return;
            bool useButtons = HasCustomCaption && ControlBox;
            closeButton.Visible = useButtons;
            minimizeButton.Visible = useButtons && MinimizeBox;
            if (!useButtons) return;

            int right = Math.Max(0, ClientSize.Width - (int)Math.Round(6 * frameScale));
            int width = Math.Min((int)Math.Round(46 * frameScale), right / (MinimizeBox ? 2 : 1));
            int height = Math.Min(CaptionHeight, (int)Math.Round(32 * frameScale));
            int top = (CaptionHeight - height) / 2;
            closeButton.SetBounds(right - width, top, width, height);
            minimizeButton.SetBounds(right - 2 * width, top, width, height);
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
                    UpdateStyles();
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
            ApplyCaptionTheme();
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
            using (var background = new SolidBrush(CaptionBackground))
                graphics.FillRectangle(background, 0, 0, ClientSize.Width, CaptionHeight);
            graphics.DrawIcon(applicationIcon, CaptionIconBounds);
            Rectangle textBounds = CaptionTextBounds;
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

        private Color CaptionBackground
        {
            get { return frameTheme.IsDark ? frameTheme.Hover : frameTheme.Surface; }
        }

        // WM_PRINT can add a synthetic desktop caption. Render only client controls.
        internal Bitmap RenderClientPreview()
        {
            var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(frameTheme.Background); PaintCaption(graphics);
                if (HasCustomCaption && ControlBox)
                {
                    closeButton.DrawCaption(graphics, closeButton.Bounds);
                    if (MinimizeBox) minimizeButton.DrawCaption(graphics, minimizeButton.Bounds);
                }
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
            if (ControlBox && (closeButton.Bounds.Contains(client) || (MinimizeBox && minimizeButton.Bounds.Contains(client)))) return 1;
            return ControlBox && CaptionIconBounds.Contains(client) ? 3 : 2; // HTSYSMENU, HTCAPTION
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HasCustomCaption && ControlBox)
            {
                if (keyData == (Keys.Alt | Keys.Space))
                {
                    OpenCaptionMenu(new Point(CaptionIconBounds.Left, CaptionHeight));
                    return true;
                }
                if (keyData == (Keys.Alt | Keys.F4))
                {
                    Close();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OpenCaptionMenu(Point location)
        {
            captionMenu.Items[1].Enabled = MinimizeBox;
            captionMenu.Show(this, location);
        }

        protected override void WndProc(ref Message m)
        {
            if (HasCustomCaption)
            {
                if (m.Msg == 0x83 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; } // WM_NCCALCSIZE
                if (m.Msg == 0x84) // WM_NCHITTEST, signed coordinates support monitors left/above primary.
                {
                    m.Result = new IntPtr(CaptionHitTest(ToClientPoint(m.LParam)));
                    return;
                }
                if (ControlBox && m.Msg == WmSystemCommand && (m.WParam.ToInt64() & 0xfff0) == ScKeyMenu && m.LParam.ToInt64() == (long)Keys.Space)
                {
                    OpenCaptionMenu(new Point(CaptionIconBounds.Left, CaptionHeight));
                    return;
                }
                if (ControlBox && m.Msg == 0xa1 && m.WParam.ToInt32() == 3) // WM_NCLBUTTONDOWN on the icon
                {
                    OpenCaptionMenu(new Point(CaptionIconBounds.Left, CaptionHeight));
                    return;
                }
                if (ControlBox && m.Msg == 0xa5 && (m.WParam.ToInt32() == 2 || m.WParam.ToInt32() == 3))
                {
                    OpenCaptionMenu(ToClientPoint(m.LParam)); // WM_NCRBUTTONUP
                    return;
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

        private Point ToClientPoint(IntPtr coordinates)
        {
            long packed = coordinates.ToInt64();
            return PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                captionMenu.Dispose();
                if (captionFont != null) captionFont.Dispose();
                applicationIcon.Dispose();
            }
        }

        private sealed class CaptionButton : Button
        {
            private readonly bool close;
            private bool hovered;

            internal CaptionButton(bool close)
            {
                this.close = close;
                AccessibleName = close ? "닫기" : "최소화";
                TabStop = true;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e) { DrawCaption(e.Graphics, ClientRectangle); }

            internal void DrawCaption(Graphics graphics, Rectangle bounds)
            {
                SettingsFrame frame = (SettingsFrame)Parent;
                Color text = frame.frameTheme.Text;
                Color background = frame.CaptionBackground;
                if (hovered)
                {
                    background = close ? Color.FromArgb(0xa9, 0x2d, 0x47) : frame.frameTheme.Pressed;
                    if (close) text = Color.White;
                }
                using (var brush = new SolidBrush(background))
                    graphics.FillRectangle(brush, bounds);

                var state = graphics.Save();
                try
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    float x = bounds.Left + bounds.Width / 2f, y = bounds.Top + bounds.Height / 2f;
                    float size = 4.5f * frame.frameScale;
                    using (var pen = new Pen(text, Math.Max(1, frame.frameScale)))
                    {
                        if (close)
                        {
                            graphics.DrawLine(pen, x - size, y - size, x + size, y + size);
                            graphics.DrawLine(pen, x - size, y + size, x + size, y - size);
                        }
                        else graphics.DrawLine(pen, x - size, y, x + size, y);
                    }
                }
                finally { graphics.Restore(state); }
                if (Focused)
                    ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -3, -3), text, background);
            }
        }
    }
}
