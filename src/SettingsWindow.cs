using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class SettingsWindow : Form
    {
        private readonly ISettingsHost app;
        private readonly RadioButton sidebar = new RadioButton(), title = new RadioButton();
        private readonly NumericUpDown x = new NumericUpDown(), y = new NumericUpDown();
        private readonly ComboBox themeChoice = new ComboBox();
        private readonly Label horizontal = new Label(), vertical = new Label(), detail = new Label(), summary = new Label(), positionStatus = new Label(), themeStatus = new Label();
        private VisualTheme theme = VisualTheme.Dark;
        private float uiScale = 1;
        private bool updating;
        private readonly bool previewMode;

        public SettingsWindow(ISettingsHost owner) : this(owner, null) { }

        // A preview host and explicit scale render the view without starting the application.
        internal SettingsWindow(ISettingsHost owner, float? previewScale)
        {
            app = owner;
            previewMode = previewScale.HasValue;
            Text = "Codex Usage Overlay";
            ClientSize = new Size(460, 564);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            AddLabel("Codex 잔여 한도", 22, 18, 410, 30, 15, true);
            AddLabel("ChatGPT 창이 활성화되면 표시됩니다. 클릭은 아래 앱으로 통과합니다.", 22, 54, 416, 38, 9, false);
            summary.SetBounds(32, 108, 388, 30);
            summary.Font = new Font(Font.FontFamily, 16, FontStyle.Bold, GraphicsUnit.Pixel);
            Controls.Add(summary);
            detail.SetBounds(32, 145, 388, 80);
            Controls.Add(detail);
            AddLabel("표시 위치", 22, 242, 410, 23, 9, true);
            sidebar.Text = "사이드바";
            sidebar.SetBounds(24, 271, 180, 27);
            Controls.Add(sidebar);
            title.Text = "세션 제목 아래";
            title.SetBounds(222, 271, 200, 27);
            Controls.Add(title);
            positionStatus.SetBounds(24, 302, 398, 17);
            positionStatus.Font = new Font(Font.FontFamily, 10, FontStyle.Regular, GraphicsUnit.Pixel);
            Controls.Add(positionStatus);
            horizontal.SetBounds(24, 324, 180, 24);
            Controls.Add(horizontal);
            vertical.SetBounds(222, 324, 200, 24);
            Controls.Add(vertical);
            x.AccessibleName = "가로 위치";
            x.Minimum = 0;
            x.Maximum = 4000;
            x.Increment = 4;
            x.SetBounds(24, 352, 180, 28);
            Controls.Add(x);
            y.AccessibleName = "세로 위치";
            y.Minimum = 0;
            y.Maximum = 3000;
            y.Increment = 4;
            y.SetBounds(222, 352, 200, 28);
            Controls.Add(y);
            AddLabel("제목 위치에 맞춰 배치합니다. 필요하면 위치와 간격을 보정하세요.", 24, 391, 398, 27, 8, false);
            AddLabel("테마", 24, 426, 56, 24, 9, true);
            themeChoice.AccessibleName = "테마";
            themeChoice.DropDownStyle = ComboBoxStyle.DropDownList;
            themeChoice.FlatStyle = FlatStyle.Flat;
            themeChoice.DrawMode = DrawMode.OwnerDrawFixed;
            themeChoice.Items.AddRange(new object[] { "Codex 따라가기", "Windows 따라가기", "라이트", "다크" });
            themeChoice.SetBounds(90, 422, 332, 28);
            themeChoice.DrawItem += DrawThemeChoice;
            Controls.Add(themeChoice);
            themeStatus.SetBounds(24, 455, 398, 18);
            themeStatus.Font = new Font(Font.FontFamily, 10, FontStyle.Regular, GraphicsUnit.Pixel);
            Controls.Add(themeStatus);
            AddButton("기본 위치", 24, 482, 94, 32, delegate { app.ResetPosition(); LoadPreferences(); });
            AddButton("지금 갱신", 126, 482, 94, 32, delegate { app.ManualRefresh(); });
            AddButton("앱에서 확인", 228, 482, 102, 32, delegate
            {
                WindowState = FormWindowState.Minimized;
                app.ActivateTarget();
            });
            AddButton("종료", 338, 482, 84, 32, delegate { app.Quit(); });
            AddLabel("최소화하면 표시를 유지합니다. X 또는 종료를 누르면 완전히 종료됩니다.", 24, 530, 398, 30, 8, false);
            sidebar.CheckedChanged += delegate { if (!updating && sidebar.Checked) app.SetPosition("sidebar"); };
            title.CheckedChanged += delegate { if (!updating && title.Checked) app.SetPosition(Preferences.BelowTitle); };
            themeChoice.SelectedIndexChanged += delegate
            {
                if (!updating)
                {
                    app.SetTheme(new[] { "codex", "system", "light", "dark" }[themeChoice.SelectedIndex]);
                    UpdateTheme();
                }
            };
            x.ValueChanged += delegate { SaveCoordinates(); };
            y.ValueChanged += delegate { SaveCoordinates(); };
            uiScale = previewScale ?? Math.Max(96, Native.GetDpiForWindow(Handle)) / 96f;
            if (uiScale != 1)
            {
                foreach (Control control in Controls)
                {
                    control.SetBounds((int)Math.Round(control.Left * uiScale), (int)Math.Round(control.Top * uiScale), (int)Math.Round(control.Width * uiScale), (int)Math.Round(control.Height * uiScale));
                    control.Font = new Font(control.Font.FontFamily, control.Font.Size * uiScale, control.Font.Style, GraphicsUnit.Pixel);
                }
                ClientSize = new Size((int)Math.Round(460 * uiScale), (int)Math.Round(564 * uiScale));
            }
            themeChoice.ItemHeight = (int)Math.Round(25 * uiScale);
            for (int index = 0; index < Controls.Count; index++) Controls[index].TabIndex = index;
            LoadPreferences();
            UpdateUsage();
            ApplyTheme(app.Theme);
        }

        private void AddLabel(string text, int left, int top, int width, int height, float size, bool bold)
        {
            var label = new Label { Text = text, Font = new Font(Font.FontFamily, size * 4 / 3, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel) };
            label.SetBounds(left, top, width, height);
            Controls.Add(label);
        }

        private void AddButton(string text, int left, int top, int width, int height, EventHandler click)
        {
            var button = new ThemedButton { Text = text, AccessibleName = text };
            button.SetBounds(left, top, width, height);
            button.Click += click;
            Controls.Add(button);
        }

        private void SaveCoordinates()
        {
            if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value);
        }

        public void LoadPreferences()
        {
            updating = true;
            try
            {
                bool side = app.Prefs.Position == "sidebar";
                sidebar.Checked = side;
                title.Checked = !side;
                x.Minimum = side ? 0 : -4000;
                x.Value = Math.Max((int)x.Minimum, Math.Min(4000, side ? app.Prefs.SidebarX : app.Prefs.TitleOffsetX));
                y.Value = Math.Max(0, Math.Min(3000, side ? app.Prefs.SidebarY : app.Prefs.TitleGap));
                horizontal.Text = side ? "가로: 창 왼쪽에서" : "가로: 제목 왼쪽 보정";
                vertical.Text = side ? "세로: 창 아래에서" : "세로: 제목 영역 아래 간격";
                themeChoice.SelectedIndex = app.Prefs.ThemeMode == "system" ? 1 : app.Prefs.ThemeMode == "light" ? 2 : app.Prefs.ThemeMode == "dark" ? 3 : 0;
            }
            finally { updating = false; }
        }

        public void UpdateUsage()
        {
            summary.Text = app.MainText;
            detail.Text = app.Details;
            UpdatePositionStatus();
        }

        public void UpdatePositionStatus()
        {
            positionStatus.Text = app.PositionStatus;
            UpdateTheme();
        }

        public void UpdateTheme()
        {
            if (!ReferenceEquals(theme, app.Theme)) ApplyTheme(app.Theme);
            themeStatus.Text = app.ThemeStatus;
        }

        private void ApplyTheme(VisualTheme value)
        {
            theme = value;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            foreach (Control control in Controls)
            {
                control.ForeColor = theme.Text;
                control.BackColor = control is Label ? Color.Transparent : theme.Background;
                var button = control as ThemedButton;
                if (button != null) button.Theme = theme;
                var radio = control as RadioButton;
                if (radio != null) { radio.FlatStyle = FlatStyle.Flat; radio.UseVisualStyleBackColor = false; }
                if (control is NumericUpDown || control is ComboBox)
                {
                    control.BackColor = theme.Surface;
                    foreach (Control child in control.Controls) { child.BackColor = theme.Surface; child.ForeColor = theme.Text; }
                }
            }
            detail.ForeColor = positionStatus.ForeColor = themeStatus.ForeColor = theme.Muted;
            ApplyCaptionTheme();
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCaptionTheme();
        }

        protected override bool ShowWithoutActivation
        {
            get { return previewMode || base.ShowWithoutActivation; }
        }

        private void ApplyCaptionTheme()
        {
            if (!IsHandleCreated) return;
            // Standard DWM attributes; unsupported Windows versions keep their normal frame.
            try
            {
                int dark = theme.IsDark && !SystemInformation.HighContrast ? 1 : 0;
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                int caption = SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(theme.Background);
                int text = SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(theme.Text);
                Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
                Native.DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private void DrawThemeChoice(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? theme.Hover : theme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            var bounds = e.Bounds;
            bounds.Inflate(-(int)(8 * uiScale), 0);
            TextRenderer.DrawText(e.Graphics, themeChoice.Items[e.Index].ToString(), themeChoice.Font, bounds, theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = uiScale, radius = 10 * s;
            var rect = new RectangleF(18 * s, 96 * s, 424 * s, 135 * s);
            using (var path = new GraphicsPath())
            using (var fill = new SolidBrush(theme.Surface))
            using (var edge = new Pen(theme.Border, Math.Max(1, s)))
            {
                float d = radius * 2;
                path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(edge, path);
            }
        }
    }
}
