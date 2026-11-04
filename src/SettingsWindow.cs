using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class SettingsWindow : Form
    {
        private readonly ISettingsHost app;
        private readonly List<Control> uiControls = new List<Control>();
        private readonly Panel pageHost = new Panel();
        private readonly Panel overlayPage = new Panel(), positionPage = new Panel(), dataPage = new Panel();
        private readonly ThemedButton[] pageButtons = new ThemedButton[3];
        private Panel[] pages;
        private readonly BadgePreview preview = new BadgePreview();
        private readonly ThemedCheckBox indicatorChoice = new ThemedCheckBox(), resetChoice = new ThemedCheckBox(), countdownChoice = new ThemedCheckBox();
        private readonly ComboBox positionChoice = new ComboBox(), themeChoice = new ComboBox();
        private readonly NumericUpDown x = new NumericUpDown(), y = new NumericUpDown();
        private readonly Label previewStatus = new Label(), positionStatus = new Label(), themeStatus = new Label(), horizontal = new Label(), vertical = new Label(), footer = new Label();
        private readonly TextBox detail = new TextBox();
        private VisualTheme theme = VisualTheme.Dark;
        private readonly bool previewMode;
        private bool updating;
        private float uiScale = 1;

        public SettingsWindow(ISettingsHost owner) : this(owner, null) { }
        internal SettingsWindow(ISettingsHost owner, float? previewScale)
        {
            app = owner;
            previewMode = previewScale.HasValue;
            Text = "Codex Usage Overlay";
            ClientSize = new Size(600, 582);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = true; ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            AddLabel(this, "Codex 잔여 한도", 22, 18, 556, 30, 20, true);
            AddLabel(this, "추가 항목을 켜고 끄면서 오버레이 미리보기를 확인하세요.", 22, 56, 556, 24, 12, false);
            pageHost.SetBounds(20, 130, 560, 380); AddUi(this, pageHost);
            pages = new[] { overlayPage, positionPage, dataPage };
            string[] pageNames = { "오버레이", "표시 위치 · 테마", "조회 상세" };
            for (int index = 0; index < pages.Length; index++)
            {
                pages[index].Dock = DockStyle.Fill; AddUi(pageHost, pages[index]);
                int selected = index;
                var button = new ThemedButton { Text = pageNames[index], AccessibleName = pageNames[index] };
                button.SetBounds(20 + index * 188, 96, 182, 30);
                button.Click += delegate { SelectPage(selected); };
                pageButtons[index] = button; AddUi(this, button);
            }

            AddLabel(overlayPage, "오버레이 미리보기", 16, 12, 520, 22, 12, true);
            preview.SetBounds(16, 42, 520, 140); AddUi(overlayPage, preview);
            previewStatus.SetBounds(16, 192, 520, 20); previewStatus.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); AddUi(overlayPage, previewStatus);
            AddLabel(overlayPage, "추가 항목 표시", 16, 224, 520, 22, 12, true);
            AddOption(indicatorChoice, "상태 아이콘", 254);
            AddOption(resetChoice, "주간 초기화 날짜 · 시각", 286);
            AddOption(countdownChoice, "초기화 디데이", 318);
            AddLabel(overlayPage, "남은 한도와 갱신 시각 · 조회 상태는 항상 표시됩니다.", 16, 352, 520, 22, 11, false);

            AddLabel(positionPage, "표시 위치", 24, 22, 508, 23, 12, true);
            AddChoice(positionPage, positionChoice, "표시 위치", new[] { "사이드바", "세션 제목 아래" }, 24, 52, 400);
            positionStatus.SetBounds(24, 90, 508, 32); AddUi(positionPage, positionStatus);
            horizontal.SetBounds(24, 130, 180, 24); AddUi(positionPage, horizontal);
            vertical.SetBounds(240, 130, 200, 24); AddUi(positionPage, vertical);
            AddNumber(positionPage, x, "가로 위치", 24, 158, 180, -4000, 4000);
            AddNumber(positionPage, y, "세로 위치", 240, 158, 184, 0, 3000);
            AddButton(positionPage, "기본 위치", 24, 204, 110, delegate { app.ResetPosition(); LoadPreferences(); });
            AddLabel(positionPage, "테마", 24, 264, 508, 24, 12, true);
            AddChoice(positionPage, themeChoice, "테마", new[] { "Codex 따라가기", "Windows 따라가기", "라이트", "다크" }, 24, 294, 400);
            themeStatus.SetBounds(24, 334, 508, 36); AddUi(positionPage, themeStatus);

            detail.Multiline = true; detail.ReadOnly = true; detail.ScrollBars = ScrollBars.Vertical;
            detail.SetBounds(16, 16, 520, 274); detail.AccessibleName = "한도 조회 상세"; AddUi(dataPage, detail);
            AddLabel(dataPage, "초기화 날짜와 디데이는 Windows 표시 시간대의 달력 날짜를 기준으로 합니다.", 16, 310, 520, 48, 12, false);
            footer.SetBounds(22, 530, 274, 44); footer.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); footer.Text = "최소화하면 표시를 유지합니다.\nX 또는 종료를 누르면 완전히 종료됩니다."; AddUi(this, footer);
            AddButton(this, "지금 갱신", 310, 532, 84, delegate { app.ManualRefresh(); });
            AddButton(this, "앱에서 확인", 404, 532, 86, delegate { WindowState = FormWindowState.Minimized; app.ActivateTarget(); });
            AddButton(this, "종료", 500, 532, 78, delegate { app.Quit(); });

            indicatorChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            resetChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            countdownChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            positionChoice.SelectedIndexChanged += delegate { if (!updating && positionChoice.SelectedIndex >= 0) { app.SetPosition(positionChoice.SelectedIndex == 0 ? "sidebar" : Preferences.BelowTitle); LoadPreferences(); } };
            themeChoice.SelectedIndexChanged += delegate { if (!updating && themeChoice.SelectedIndex >= 0) { app.SetTheme(new[] { "codex", "system", "light", "dark" }[themeChoice.SelectedIndex]); UpdateTheme(); } };
            x.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
            y.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
            uiScale = previewScale ?? Math.Max(96, Native.GetDpiForWindow(Handle)) / 96f;
            ScaleView(uiScale);
            preview.UiScale = uiScale;
            foreach (var check in new[] { indicatorChoice, resetChoice, countdownChoice }) check.ScaleFactor = uiScale;
            foreach (var choice in new[] { positionChoice, themeChoice }) choice.ItemHeight = (int)Math.Round(25 * uiScale);
            if (!previewMode)
            {
                var fullSize = ClientSize; var work = Screen.FromHandle(Handle).WorkingArea;
                AutoScrollMinSize = fullSize; AutoScroll = true;
                ClientSize = new Size(Math.Min(fullSize.Width, Math.Max(320, work.Width - 48)), Math.Min(fullSize.Height, Math.Max(360, work.Height - (int)(64 * uiScale))));
            }
            SelectPage(0); LoadPreferences(); UpdateUsage(); ApplyTheme(app.Theme);
        }

        private void AddUi(Control parent, Control control) { parent.Controls.Add(control); uiControls.Add(control); }
        private void ScaleView(float scale)
        {
            // Only controls created here are scaled. Framework-owned spinner/edit children manage themselves.
            var snapshots = new List<Tuple<Control, Rectangle, Font>>();
            foreach (var control in uiControls) snapshots.Add(Tuple.Create(control, control.Bounds, control.Font));
            foreach (var item in snapshots)
            {
                if (item.Item1.Dock != DockStyle.Fill) item.Item1.SetBounds((int)Math.Round(item.Item2.X * scale), (int)Math.Round(item.Item2.Y * scale), (int)Math.Round(item.Item2.Width * scale), (int)Math.Round(item.Item2.Height * scale));
                item.Item1.Font = new Font(item.Item3.FontFamily, item.Item3.Size * scale, item.Item3.Style, GraphicsUnit.Pixel);
                var button = item.Item1 as ThemedButton; if (button != null) button.ScaleFactor = scale;
            }
            ClientSize = new Size((int)Math.Round(600 * scale), (int)Math.Round(582 * scale));
        }
        private void AddLabel(Control parent, string text, int left, int top, int width, int height, float size, bool bold)
        {
            var label = new Label { Text = text, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel) };
            label.SetBounds(left, top, width, height); AddUi(parent, label);
        }
        private void AddOption(ThemedCheckBox choice, string text, int top)
        { choice.Text = text; choice.AccessibleName = text; choice.SetBounds(16, top, 520, 26); AddUi(overlayPage, choice); }
        private void AddButton(Control parent, string text, int left, int top, int width, EventHandler click)
        {
            var button = new ThemedButton { Text = text, AccessibleName = text }; button.SetBounds(left, top, width, 32); button.Click += click; AddUi(parent, button);
        }
        private void AddChoice(Control parent, ComboBox choice, string label, string[] items, int left, int top, int width)
        {
            choice.AccessibleName = label; choice.DropDownStyle = ComboBoxStyle.DropDownList; choice.FlatStyle = FlatStyle.Flat; choice.DrawMode = DrawMode.OwnerDrawFixed;
            choice.Items.AddRange(items); choice.SetBounds(left, top, width, 28); choice.DrawItem += DrawChoice; AddUi(parent, choice);
        }
        private void AddNumber(Control parent, NumericUpDown number, string label, int left, int top, int width, int min, int max)
        { number.AccessibleName = label; number.Minimum = min; number.Maximum = max; number.SetBounds(left, top, width, 28); AddUi(parent, number); }

        public void LoadPreferences()
        {
            updating = true;
            try
            {
                bool side = app.Prefs.Position == "sidebar";
                positionChoice.SelectedIndex = side ? 0 : 1;
                x.Minimum = side ? 0 : -4000;
                x.Value = Math.Max(x.Minimum, Math.Min(x.Maximum, side ? app.Prefs.SidebarX : app.Prefs.TitleOffsetX));
                y.Value = Math.Max(0, Math.Min(3000, side ? (app.Prefs.SidebarBottomGap ?? (int)Math.Max(0L, Math.Min(3000L, (long)app.Prefs.SidebarY - 46))) : app.Prefs.TitleGap));
                horizontal.Text = side ? "가로: 창 왼쪽에서" : "가로: 제목 왼쪽 보정";
                vertical.Text = side ? "세로: 창 아래 여백" : "세로: 제목 영역 아래 간격";
                themeChoice.SelectedIndex = app.Prefs.ThemeMode == "system" ? 1 : app.Prefs.ThemeMode == "light" ? 2 : app.Prefs.ThemeMode == "dark" ? 3 : 0;
                preview.LayoutSettings = app.Prefs.BadgeLayout;
                indicatorChoice.Checked = IsVisible("indicator"); resetChoice.Checked = IsVisible("weekly-reset"); countdownChoice.Checked = IsVisible("countdown");
            }
            finally { updating = false; }
            UpdatePreviewStatus();
        }
        private bool IsVisible(string key) { return preview.LayoutSettings.Elements.Exists(element => element.Key == key && element.Visible); }
        private void SaveDisplayOptions()
        {
            if (updating) return;
            var layout = preview.LayoutSettings.Copy();
            layout.Elements.Find(element => element.Key == "indicator").Visible = indicatorChoice.Checked;
            layout.Elements.Find(element => element.Key == "weekly-reset").Visible = resetChoice.Checked;
            layout.Elements.Find(element => element.Key == "countdown").Visible = countdownChoice.Checked;
            layout.Normalize(); preview.LayoutSettings = layout; app.SetBadgeLayout(layout); UpdatePreviewStatus();
        }
        private void UpdatePreviewStatus()
        { previewStatus.Text = app.IsSample ? "예시 데이터 · 개발 미리보기" : "현재 조회 상태 · 항목 변경 즉시 반영"; }
        public void UpdateUsage()
        {
            string details = app.Details.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            if (detail.Text != details) detail.Text = details;
            if (!ReferenceEquals(preview.Presentation, app.Presentation)) preview.Presentation = app.Presentation;
            UpdatePositionStatus();
        }
        public void UpdatePositionStatus() { positionStatus.Text = app.PositionStatus; UpdateTheme(); }
        private void SelectPage(int index)
        {
            if (index < 0 || index >= pages.Length) return;
            for (int current = 0; current < pages.Length; current++)
            {
                pages[current].Visible = current == index;
                pageButtons[current].Selected = current == index;
            }
            pages[index].BringToFront();
        }
        internal void SelectPreviewPage(int index) { if (previewMode) SelectPage(index); }
        public void UpdateTheme()
        { if (!ReferenceEquals(theme, app.Theme)) ApplyTheme(app.Theme); themeStatus.Text = app.ThemeStatus; }
        private void ApplyTheme(VisualTheme value)
        {
            theme = value ?? VisualTheme.Dark; BackColor = theme.Background; ForeColor = theme.Text;
            foreach (Control control in uiControls)
            {
                control.ForeColor = theme.Text; control.BackColor = control is Label ? Color.Transparent : theme.Background;
                var button = control as ThemedButton; if (button != null) button.Theme = theme;
                var check = control as ThemedCheckBox; if (check != null) check.Theme = theme;
                if (control is NumericUpDown || control is ComboBox || control is TextBox) control.BackColor = theme.Surface;
            }
            previewStatus.ForeColor = positionStatus.ForeColor = themeStatus.ForeColor = footer.ForeColor = theme.Muted;
            preview.Theme = theme; ApplyCaptionTheme(); Invalidate(true);
        }
        private void DrawChoice(object sender, DrawItemEventArgs e)
        {
            var choice = (ComboBox)sender; if (e.Index < 0 || e.Index >= choice.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0, contrast = SystemInformation.HighContrast;
            Color fill = selected ? (contrast ? SystemColors.Highlight : theme.Hover) : theme.Surface;
            Color text = selected && contrast ? SystemColors.HighlightText : theme.Text;
            using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
            var bounds = e.Bounds; bounds.Inflate(-(int)(8 * uiScale), 0);
            TextRenderer.DrawText(e.Graphics, choice.Items[e.Index].ToString(), choice.Font, bounds, text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform); e.DrawFocusRectangle();
        }
        protected override bool ShowWithoutActivation { get { return previewMode || base.ShowWithoutActivation; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyCaptionTheme(); }
        private void ApplyCaptionTheme()
        {
            if (!IsHandleCreated) return;
            try
            {
                int dark = theme.IsDark && !SystemInformation.HighContrast ? 1 : 0, caption = SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(theme.Background), text = SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(theme.Text);
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int)); Native.DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
