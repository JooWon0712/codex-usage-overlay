using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class BadgeEditor : Control
    {
        private UsagePresentation presentation;
        private VisualTheme theme = VisualTheme.Dark;
        private BadgeLayoutSettings layout = new BadgeLayoutSettings();
        private string selectedKey;
        private bool fitToView = true;
        private float uiScale;
        private bool dragging, resizing;
        private BadgeLayoutSettings dragLayout;
        private string dragKey;
        private Point dragStart;
        private int dragX, dragY, dragWidth;
        private float dragScale;

        public event EventHandler LayoutChanged;
        public event EventHandler SelectionChanged;

        public BadgeEditor()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleName = "오버레이 배치 편집";
            AccessibleDescription = "항목을 선택해 끌거나 방향키로 이동합니다. Shift와 방향키는 10씩 이동합니다.";
            Size = new Size(470, 250);
            layout.Normalize();
        }

        public UsagePresentation Presentation
        {
            get { return presentation; }
            set { presentation = value; Invalidate(); }
        }

        public VisualTheme Theme
        {
            get { return theme; }
            set { theme = value ?? VisualTheme.Dark; Invalidate(); }
        }

        // This is the shared edit model, not a snapshot. Control.Layout is an
        // unrelated inherited event; hiding it does not replace its layout engine.
        public new BadgeLayoutSettings Layout
        {
            get { return layout; }
            set
            {
                CancelDrag();
                layout = value ?? new BadgeLayoutSettings();
                layout.Normalize();
                string key = FindElement(layout, selectedKey) == null ? null : selectedKey;
                SetSelection(key);
                Invalidate();
            }
        }

        public string SelectedKey
        {
            get { return selectedKey; }
            set { SetSelection(FindElement(layout, value) == null ? null : value); }
        }

        public bool FitToView
        {
            get { return fitToView; }
            set
            {
                if (fitToView == value) return;
                CancelDrag();
                fitToView = value;
                Invalidate();
            }
        }

        // Zero follows this control's DPI. An explicit scale also supports the
        // isolated settings renderer, whose target DPI may differ from its HWND.
        public float UiScale
        {
            get { return uiScale; }
            set
            {
                float next = Single.IsNaN(value) || Single.IsInfinity(value) || value <= 0f
                    ? 0f : Math.Min(value, 8f);
                if (uiScale == next) return;
                CancelDrag();
                uiScale = next;
                Invalidate();
            }
        }

        public float RenderScale
        {
            get
            {
                float scale;
                GetBadgeBounds(out scale);
                return scale;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            VisualTheme colors = theme;
            using (Brush background = new SolidBrush(colors.Background))
                e.Graphics.FillRectangle(background, ClientRectangle);
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
                using (Pen border = new Pen(colors.Border))
                    e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            float scale;
            Rectangle bounds = GetBadgeBounds(out scale);
            if (scale <= 0f || bounds.Width <= 0 || bounds.Height <= 0)
                return;
            BadgeRenderer.Draw(e.Graphics, bounds, scale, colors, presentation, layout);
            BadgeElement selected = FindElement(layout, selectedKey);
            if (selected != null)
            {
                RectangleF elementBounds = Project(selected, bounds, scale);
                using (Pen outline = new Pen(colors.Accent, Math.Max(1f, EffectiveUiScale())))
                {
                    outline.DashStyle = DashStyle.Dot;
                    e.Graphics.DrawRectangle(outline, elementBounds.X, elementBounds.Y,
                        elementBounds.Width, elementBounds.Height);
                }
                if (CanResize(selected))
                {
                    RectangleF handle = ResizeHandle(elementBounds);
                    using (Brush fill = new SolidBrush(colors.Accent))
                        e.Graphics.FillRectangle(fill, handle);
                    using (Pen stroke = new Pen(colors.Surface))
                        e.Graphics.DrawRectangle(stroke, handle.X, handle.Y, handle.Width, handle.Height);
                }
            }

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics,
                    Rectangle.Inflate(ClientRectangle, -3, -3), colors.Text, colors.Background);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled || IsDisposed)
                return;
            Focus();
            if (IsDisposed || !Enabled) return;

            float scale;
            Rectangle bounds = GetBadgeBounds(out scale);
            if (scale <= 0f) return;
            BadgeLayoutSettings targetLayout = layout;
            BadgeElement target = FindElement(targetLayout, selectedKey);
            bool resize = target != null && CanResize(target)
                && ResizeHandle(Project(target, bounds, scale)).Contains(e.Location);
            if (!resize)
                target = HitTest(e.Location, bounds, scale);
            string key = target == null ? null : target.Key;
            SetSelection(key);

            // Selection handlers may replace the model or close the settings
            // window. Never capture a stale object after invoking user code.
            if (IsDisposed || !Enabled || key == null || !Object.ReferenceEquals(layout, targetLayout)
                || !String.Equals(selectedKey, key, StringComparison.Ordinal))
                return;
            target = FindElement(layout, key);
            if (target == null || !target.Visible) return;
            dragLayout = layout;
            dragKey = key;
            dragStart = e.Location;
            dragX = target.X;
            dragY = target.Y;
            dragWidth = target.Width;
            dragScale = scale;
            resizing = resize && CanResize(target);
            dragging = true;
            Capture = true;
            Cursor = resizing ? Cursors.SizeWE : Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsDisposed) return;
            if (!dragging)
            {
                UpdateCursor(e.Location);
                return;
            }
            if (!Capture || !Object.ReferenceEquals(layout, dragLayout)
                || !String.Equals(selectedKey, dragKey, StringComparison.Ordinal))
            {
                CancelDrag();
                return;
            }
            BadgeElement element = FindElement(layout, dragKey);
            if (element == null || !element.Visible || dragScale <= 0f)
            {
                CancelDrag();
                return;
            }
            int dx = DragDelta((double)e.X - dragStart.X, dragScale, layout.Width);
            int dy = DragDelta((double)e.Y - dragStart.Y, dragScale, layout.Height);
            ApplyEdit(element, resizing ? element.X : dragX + dx,
                resizing ? element.Y : dragY + dy, resizing ? dragWidth + dx : element.Width);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
                CancelDrag();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) CancelDrag();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!dragging) Cursor = Cursors.Default;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down
                || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || e.Control || e.Alt || IsDisposed) return;
            BadgeElement element = FindElement(layout, selectedKey);
            if (element == null) return;
            int step = e.Shift ? 10 : 1, dx = 0, dy = 0;
            if (e.KeyCode == Keys.Left) dx = -step;
            else if (e.KeyCode == Keys.Right) dx = step;
            else if (e.KeyCode == Keys.Up) dy = -step;
            else if (e.KeyCode == Keys.Down) dy = step;
            else return;
            BadgeLayoutSettings targetLayout = layout;
            string key = selectedKey;
            CancelDrag();
            if (IsDisposed || !Object.ReferenceEquals(layout, targetLayout)
                || !String.Equals(selectedKey, key, StringComparison.Ordinal)) return;
            element = FindElement(layout, key);
            if (element == null) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            ApplyEdit(element, element.X + dx, element.Y + dy, element.Width);
        }

        protected override void OnResize(EventArgs e)
        {
            CancelDrag();
            base.OnResize(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            CancelDrag();
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled) CancelDrag();
            base.OnEnabledChanged(e);
        }

        private void ApplyEdit(BadgeElement element, int x, int y, int width)
        {
            int oldX = element.X, oldY = element.Y, oldWidth = element.Width;
            element.Width = element.Key == "indicator" ? oldWidth : Clamp(width, 1, layout.Width - element.X);
            element.X = Clamp(x, 0, layout.Width - element.Width);
            element.Y = Clamp(y, 0, layout.Height - BadgeLayoutSettings.ElementHeight(element));
            layout.Normalize();
            if (oldX == element.X && oldY == element.Y && oldWidth == element.Width)
                return;
            Invalidate();
            EventHandler changed = LayoutChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        private void SetSelection(string key)
        {
            if (String.Equals(selectedKey, key, StringComparison.Ordinal)) return;
            CancelDrag();
            selectedKey = key;
            Invalidate();
            EventHandler changed = SelectionChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        private void CancelDrag()
        {
            dragging = false;
            resizing = false;
            dragLayout = null;
            dragKey = null;
            if (Capture) Capture = false;
            Cursor = Cursors.Default;
        }

        private void UpdateCursor(Point point)
        {
            float scale;
            Rectangle bounds = GetBadgeBounds(out scale);
            if (scale <= 0f)
            {
                Cursor = Cursors.Default;
                return;
            }
            BadgeElement selected = FindElement(layout, selectedKey);
            if (selected != null && CanResize(selected)
                && ResizeHandle(Project(selected, bounds, scale)).Contains(point))
                Cursor = Cursors.SizeWE;
            else
                Cursor = HitTest(point, bounds, scale) == null ? Cursors.Default : Cursors.SizeAll;
        }

        private BadgeElement HitTest(Point point, Rectangle bounds, float scale)
        {
            if (!bounds.Contains(point) || layout.Elements == null) return null;
            for (int i = layout.Elements.Count - 1; i >= 0; i--)
            {
                BadgeElement element = layout.Elements[i];
                if (element != null && element.Visible && Project(element, bounds, scale).Contains(point))
                    return element;
            }
            return null;
        }

        private Rectangle GetBadgeBounds(out float scale)
        {
            float dpi = EffectiveUiScale();
            float margin = 16f * dpi;
            float availableWidth = ClientSize.Width - margin * 2f;
            float availableHeight = ClientSize.Height - margin * 2f;
            scale = 0f;
            if (layout == null || layout.Width <= 0 || layout.Height <= 0
                || availableWidth <= 0f || availableHeight <= 0f)
                return Rectangle.Empty;
            scale = fitToView ? Math.Min(dpi,
                Math.Min(availableWidth / layout.Width, availableHeight / layout.Height)) : dpi;
            int width = (int)Math.Round(layout.Width * scale);
            int height = (int)Math.Round(layout.Height * scale);
            if (width <= 0 || height <= 0)
            {
                scale = 0f;
                return Rectangle.Empty;
            }
            int left = (int)Math.Round(margin + Math.Max(0f, (availableWidth - width) / 2f));
            int top = (int)Math.Round(margin + Math.Max(0f, (availableHeight - height) / 2f));
            return new Rectangle(left, top, width, height);
        }

        private float EffectiveUiScale()
        {
            if (uiScale > 0f) return uiScale;
            if (IsHandleCreated)
                try
                {
                    uint dpi = Native.GetDpiForWindow(Handle);
                    if (dpi >= 48 && dpi <= 768) return dpi / 96f;
                }
                catch (EntryPointNotFoundException) { }
                catch (DllNotFoundException) { }
            return 1f;
        }

        private RectangleF ResizeHandle(RectangleF elementBounds)
        {
            float size = 6f * EffectiveUiScale();
            return new RectangleF(elementBounds.Right - size / 2f,
                elementBounds.Bottom - size / 2f, size, size);
        }

        private static RectangleF Project(BadgeElement element, Rectangle bounds, float scale)
        {
            RectangleF logical = BadgeRenderer.ElementBounds(element);
            return new RectangleF(bounds.Left + logical.Left * scale, bounds.Top + logical.Top * scale,
                logical.Width * scale, logical.Height * scale);
        }

        private static bool CanResize(BadgeElement element)
        {
            return element.Visible && element.Key != "indicator";
        }

        private static BadgeElement FindElement(BadgeLayoutSettings settings, string key)
        {
            if (settings == null || settings.Elements == null || key == null) return null;
            foreach (BadgeElement element in settings.Elements)
                if (element != null && String.Equals(element.Key, key, StringComparison.Ordinal))
                    return element;
            return null;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int DragDelta(double physicalDelta, float scale, int extent)
        {
            // Crossing an entire badge already reaches the applicable edge.
            // Bound before converting, including during very small fit scales.
            return (int)Math.Max(-extent, Math.Min(extent, Math.Round(physicalDelta / scale)));
        }
    }
}
