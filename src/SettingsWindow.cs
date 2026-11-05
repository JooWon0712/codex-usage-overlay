using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class SettingsWindow : SettingsFrame
    {
        private readonly ISettingsHost app;
        private readonly List<Control> uiControls = new List<Control>();
        private readonly Panel pageHost = new Panel();
        private readonly Panel overlayPage = new Panel(), positionPage = new Panel();
        private readonly ThemedButton[] pageButtons = new ThemedButton[2];
        private Panel[] pages;
        private readonly BadgePreview preview = new BadgePreview();
        private readonly ThemedCheckBox resetChoice = new ThemedCheckBox(), countdownChoice = new ThemedCheckBox(), planChoice = new ThemedCheckBox();
        private readonly ThemedComboBox positionChoice = new ThemedComboBox(), themeChoice = new ThemedComboBox();
        private readonly ThemedNumericUpDown x = new ThemedNumericUpDown(), y = new ThemedNumericUpDown();
        private readonly Label previewStatus = new Label(), positionStatus = new Label(), themeStatus = new Label(), horizontal = new Label(), vertical = new Label(), footer = new Label();
        private readonly ThemedButton problemToggle = new ThemedButton();
        private readonly ThemedButton updateCheck = new ThemedButton();
        private readonly TextBox problemDetails = new TextBox();
        private readonly ThemedTrackBar transparencyChoice = new ThemedTrackBar();
        private readonly Label transparencyStatus = new Label();
        private VisualTheme theme = VisualTheme.Dark;
        private readonly bool previewMode;
        private bool updating, problemExpanded;
        private float uiScale = 1;
        private float layoutScale = 1;

        public SettingsWindow(ISettingsHost owner) : this(owner, null) { }
        internal SettingsWindow(ISettingsHost owner, float? previewScale)
        {
            app = owner;
            previewMode = previewScale.HasValue;
            Text = "Codex Usage Overlay";
            ClientSize = new Size(600, 630);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = true; ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            AddLabel(this, "Codex 잔여 한도", 22, 18, 396, 30, 20, true);
            updateCheck.Text = "업데이트 확인";
            updateCheck.AccessibleName = "업데이트 확인";
            updateCheck.AccessibleDescription = "기본 브라우저에서 GitHub Releases를 엽니다. 설치 파일은 직접 내려받아 실행합니다.";
            updateCheck.SetBounds(440, 18, 138, 30);
            updateCheck.Click += delegate { app.OpenReleases(); };
            AddUi(this, updateCheck);
            AddLabel(this, "두 탭에서 설정을 바꾸며 Codex 위 실제 배지를 확인하세요.", 22, 56, 556, 24, 12, false);
            pageHost.SetBounds(20, 130, 560, 428); AddUi(this, pageHost);
            pages = new[] { overlayPage, positionPage };
            string[] pageNames = { "오버레이", "표시 위치 · 테마" };
            for (int index = 0; index < pages.Length; index++)
            {
                pages[index].Dock = DockStyle.Fill; AddUi(pageHost, pages[index]);
                int selected = index;
                var button = new ThemedButton { Text = pageNames[index], AccessibleName = pageNames[index] };
                button.SetBounds(20 + index * 284, 96, 276, 30);
                button.Click += delegate { SelectPage(selected); };
                pageButtons[index] = button; AddUi(this, button);
            }

            AddLabel(overlayPage, "오버레이 미리보기", 16, 12, 520, 22, 12, true);
            preview.Presentation = UsagePresentation.CreatePreview(app.StartedAt);
            preview.AccessibleDescription = "프로그램 시작 시각으로 만든 예시 데이터입니다.";
            preview.SetBounds(16, 42, 520, 116); AddUi(overlayPage, preview);
            previewStatus.SetBounds(16, 162, 520, 20); previewStatus.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); AddUi(overlayPage, previewStatus);
            transparencyStatus.SetBounds(16,194,520,24); AddUi(overlayPage,transparencyStatus);
            // The native control has eleven positions, so every mouse/keyboard
            // value maps to a supported ten-percent step without event re-entry.
            transparencyChoice.Minimum=0; transparencyChoice.Maximum=10; transparencyChoice.SmallChange=1; transparencyChoice.LargeChange=1;
            transparencyChoice.TickStyle=TickStyle.None; transparencyChoice.AutoSize=false; transparencyChoice.AccessibleName="배경 투명도";
            transparencyChoice.SetBounds(16,220,520,30); AddUi(overlayPage,transparencyChoice);
            AddLabel(overlayPage, "추가 항목 표시", 16, 262, 520, 24, 14, true);
            AddOption(resetChoice, "주간 초기화 날짜 · 시각", 294);
            resetChoice.Width = 268;
            AddOption(planChoice, "구독 요금제", 294);
            planChoice.SetBounds(296, 294, 240, 30);
            planChoice.AccessibleDescription = "연결된 Codex 계정에서 확인한 요금제 종류를 갱신 시각 옆에 표시합니다.";
            AddOption(countdownChoice, "초기화 디데이", 330);
            AddLabel(overlayPage, "남은 한도와 갱신 시각 · 조회 상태는 항상 표시됩니다.", 16, 366, 520, 22, 11, false);
            overlayPage.AutoScroll = true;
            problemToggle.Text = "문제 확인 ▸"; problemToggle.AccessibleName = "조회 실패 원인 펼치기";
            problemToggle.SetBounds(16, 398, 132, 28); problemToggle.Visible = false;
            problemToggle.Click += delegate { SetProblemExpanded(!problemExpanded); }; AddUi(overlayPage, problemToggle);
            problemDetails.Multiline = true; problemDetails.ReadOnly = true; problemDetails.ScrollBars = ScrollBars.Vertical;
            problemDetails.SetBounds(16, 438, 520, 116); problemDetails.AccessibleName = "조회 실패 원인과 조치";
            problemDetails.Visible = false; AddUi(overlayPage, problemDetails);

            AddLabel(positionPage, "표시 위치", 24, 22, 508, 23, 12, true);
            AddChoice(positionPage, positionChoice, "표시 위치", new[] { "사이드바", "세션 제목 아래" }, 24, 52, 400);
            positionStatus.SetBounds(24, 90, 508, 32); AddUi(positionPage, positionStatus);
            horizontal.SetBounds(24, 130, 180, 24); AddUi(positionPage, horizontal);
            vertical.SetBounds(240, 130, 200, 24); AddUi(positionPage, vertical);
            AddNumber(positionPage, x, "가로 위치", 24, 158, 180, -4000, 4000);
            AddNumber(positionPage, y, "세로 위치", 240, 158, 184, 0, 3000);
            AddButton(positionPage, "기본 위치", 24, 204, 110, delegate { app.ResetPosition(); LoadPreferences(); });
            AddLabel(positionPage, "설정 중 Codex에 표시 · 설정 창이 겹치면 옆으로 옮기세요.", 24, 242, 508, 20, 11, false);
            AddLabel(positionPage, "테마", 24, 264, 508, 24, 12, true);
            AddChoice(positionPage, themeChoice, "테마", new[] { "Codex 따라가기", "Windows 따라가기", "라이트", "다크" }, 24, 294, 400);
            themeStatus.SetBounds(24, 334, 508, 36); AddUi(positionPage, themeStatus);

            footer.SetBounds(22, 578, 274, 44); footer.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); footer.Text = "최소화하면 표시를 유지합니다.\nX 또는 종료를 누르면 완전히 종료됩니다."; AddUi(this, footer);
            AddButton(this, "지금 갱신", 310, 580, 84, delegate { app.ManualRefresh(); });
            AddButton(this, "앱에서 확인", 404, 580, 86, delegate { WindowState = FormWindowState.Minimized; app.ActivateTarget(); });
            AddButton(this, "종료", 500, 580, 78, delegate { app.Quit(); });

            resetChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            countdownChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            planChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            transparencyChoice.ValueChanged += delegate { SaveDisplayOptions(); };
            positionChoice.SelectedIndexChanged += delegate { if (!updating && positionChoice.SelectedIndex >= 0) { app.SetPosition(positionChoice.SelectedIndex == 0 ? "sidebar" : Preferences.BelowTitle); LoadPreferences(); } };
            themeChoice.SelectedIndexChanged += delegate { if (!updating && themeChoice.SelectedIndex >= 0) { app.SetTheme(new[] { "codex", "system", "light", "dark" }[themeChoice.SelectedIndex]); UpdateTheme(); } };
            x.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
            y.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
            uiScale = previewScale ?? Math.Max(96, Native.GetDpiForWindow(Handle)) / 96f;
            ScaleView(uiScale);
            ScaleInputs();
            if (!previewMode)
                FitWorkArea();
            SelectPage(0); LoadPreferences(); UpdateUsage(); ApplyTheme(app.Theme);
        }

        private void AddUi(Control parent, Control control) { (parent == this ? ContentHost : parent).Controls.Add(control); uiControls.Add(control); }
        private void ScaleView(float scale)
        {
            float ratio = scale / layoutScale;
            // Only controls created here are scaled. Framework-owned spinner/edit children manage themselves.
            var snapshots = new List<Tuple<Control, Rectangle, Font>>();
            foreach (var control in uiControls) snapshots.Add(Tuple.Create(control, control.Bounds, control.Font));
            foreach (var item in snapshots)
            {
                if (item.Item1.Dock != DockStyle.Fill) item.Item1.SetBounds((int)Math.Round(item.Item2.X * ratio), (int)Math.Round(item.Item2.Y * ratio), (int)Math.Round(item.Item2.Width * ratio), (int)Math.Round(item.Item2.Height * ratio));
                item.Item1.Font = new Font(item.Item3.FontFamily, item.Item3.Size * ratio, item.Item3.Style, GraphicsUnit.Pixel);
                var button = item.Item1 as ThemedButton; if (button != null) button.ScaleFactor = scale;
                var number = item.Item1 as ThemedNumberField; if (number != null) number.ScaleFactor = scale;
            }
            SetFrameScale(scale);
            layoutScale = uiScale = scale;
            SetContentSize((int)Math.Round(600 * scale), (int)Math.Round(630 * scale));
        }
        private void ScaleInputs()
        {
            preview.UiScale = uiScale; transparencyChoice.ScaleFactor = uiScale;
            foreach (var check in new[] { resetChoice, countdownChoice, planChoice }) check.ScaleFactor = uiScale;
            foreach (var choice in new[] { positionChoice, themeChoice }) { choice.ScaleFactor = uiScale; choice.ItemHeight = (int)Math.Round(26 * uiScale); }
            foreach (var number in new[] { x, y }) number.ScaleFactor = uiScale;
        }
        private void FitWorkArea()
        {
            var fullSize = new Size((int)Math.Round(600 * uiScale), (int)Math.Round(630 * uiScale));
            var work = Screen.FromHandle(Handle).WorkingArea;
            ContentHost.AutoScrollMinSize = fullSize; ContentHost.AutoScroll = true;
            SetContentSize(Math.Min(fullSize.Width, Math.Max(320, work.Width - 48)), Math.Min(fullSize.Height, Math.Max(360, work.Height - CaptionHeight - (int)(64 * uiScale))));
        }
        protected override void OnFrameDpiChanged(float scale)
        {
            if (pages == null) { base.OnFrameDpiChanged(scale); return; }
            ScaleView(scale); ScaleInputs(); if (!previewMode) FitWorkArea();
        }
        private void AddLabel(Control parent, string text, int left, int top, int width, int height, float size, bool bold)
        {
            var label = new Label { Text = text, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel) };
            label.SetBounds(left, top, width, height); AddUi(parent, label);
        }
        private void AddOption(ThemedCheckBox choice, string text, int top)
        { choice.Text = text; choice.AccessibleName = text; choice.Font=new Font(Font.FontFamily,14,GraphicsUnit.Pixel); choice.SetBounds(16, top, 520, 30); AddUi(overlayPage, choice); }
        private void AddButton(Control parent, string text, int left, int top, int width, EventHandler click)
        {
            var button = new ThemedButton { Text = text, AccessibleName = text }; button.SetBounds(left, top, width, 32); button.Click += click; AddUi(parent, button);
        }
        private void AddChoice(Control parent, ComboBox choice, string label, string[] items, int left, int top, int width)
        {
            choice.AccessibleName = label; choice.DropDownStyle = ComboBoxStyle.DropDownList; choice.FlatStyle = FlatStyle.Flat; choice.DrawMode = DrawMode.OwnerDrawFixed;
            choice.Items.AddRange(items); choice.SetBounds(left, top, width, 28); choice.DrawItem += DrawChoice; AddUi(parent, choice);
        }
        private void AddNumber(Control parent, ThemedNumericUpDown number, string label, int left, int top, int width, int min, int max)
        {
            number.AccessibleName=label; number.Minimum=min; number.Maximum=max;
            var field=new ThemedNumberField(number) { AccessibleName=label };
            field.SetBounds(left,top,width,32); AddUi(parent,field);
        }

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
                horizontal.Text = side ? "가로: 창 왼쪽에서" : "가로: 기본 위치 보정";
                vertical.Text = side ? "세로: 창 아래 여백" : "세로: 제목 영역 아래 간격";
                themeChoice.SelectedIndex = app.Prefs.ThemeMode == "system" ? 1 : app.Prefs.ThemeMode == "light" ? 2 : app.Prefs.ThemeMode == "dark" ? 3 : 0;
                if (app.Prefs.BadgeLayout == null) app.Prefs.BadgeLayout = new BadgeLayoutSettings();
                // Migrate legacy percentages in memory only; loading never saves.
                app.Prefs.BadgeLayout.Normalize();
                preview.LayoutSettings = app.Prefs.BadgeLayout;
                transparencyChoice.Value=preview.LayoutSettings.BackgroundTransparency / 10;
                resetChoice.Checked = IsVisible("weekly-reset"); countdownChoice.Checked = IsVisible("countdown");
                planChoice.Checked = preview.LayoutSettings.ShowPlan;
            }
            finally { updating = false; }
            UpdatePreviewStatus();
        }
        private bool IsVisible(string key) { return preview.LayoutSettings.Elements.Exists(element => element.Key == key && element.Visible); }
        private void SaveDisplayOptions()
        {
            if (updating) return;
            var layout = preview.LayoutSettings.Copy();
            layout.BackgroundTransparency=transparencyChoice.Value * 10;
            layout.ShowPlan = planChoice.Checked;
            layout.Elements.Find(element => element.Key == "weekly-reset").Visible = resetChoice.Checked;
            layout.Elements.Find(element => element.Key == "countdown").Visible = countdownChoice.Checked;
            layout.Normalize(); preview.LayoutSettings = layout; app.SetBadgeLayout(layout); UpdatePreviewStatus();
        }
        private void UpdatePreviewStatus()
        {
            previewStatus.Text = "예시 데이터 · " + (SystemInformation.HighContrast ? "고대비 배경" : "다크 배경") + " · 프로그램 시작 시각 기준";
            transparencyStatus.Text="배경 투명도 · "+(transparencyChoice.Value * 10)+"%"+(SystemInformation.HighContrast?" · 고대비에서는 불투명"
                : BadgeTextColors.Resolve(app.Theme,transparencyChoice.Value*10,false).Bright?" · 밝은 글자":" · 진한 글자");
        }
        public void UpdateUsage()
        {
            UpdateProblemDetails();
            UpdatePositionStatus();
        }
        private void UpdateProblemDetails()
        {
            var presentation = app.Presentation;
            bool failed = presentation != null && presentation.HasQueryFailure;
            problemToggle.Visible = failed;
            string details = failed ? presentation.FailureDetails.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) : String.Empty;
            if (problemDetails.Text != details) problemDetails.Text = details;
            if (!failed && problemExpanded) SetProblemExpanded(false);
        }
        private void SetProblemExpanded(bool expanded)
        {
            problemExpanded = expanded && app.Presentation != null && app.Presentation.HasQueryFailure;
            problemToggle.Text = problemExpanded ? "문제 확인 ▾" : "문제 확인 ▸";
            problemToggle.AccessibleName = problemExpanded ? "조회 실패 원인 접기" : "조회 실패 원인 펼치기";
            problemDetails.Visible = problemExpanded;
            overlayPage.AutoScrollMinSize = problemExpanded ? new Size(0, (int)Math.Round(570 * uiScale)) : Size.Empty;
            if (problemExpanded) overlayPage.ScrollControlIntoView(problemDetails);
            else overlayPage.AutoScrollPosition = Point.Empty;
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
        internal void SetPreviewProblemExpanded(bool expanded) { if (previewMode) SetProblemExpanded(expanded); }
        public void UpdateTheme()
        { if (!ReferenceEquals(theme, app.Theme)) ApplyTheme(app.Theme); themeStatus.Text = app.ThemeStatus; UpdatePreviewStatus(); }
        private void ApplyTheme(VisualTheme value)
        {
            theme = value ?? VisualTheme.Dark; BackColor = theme.Background; ForeColor = theme.Text;
            foreach (Control control in uiControls)
            {
                control.ForeColor = theme.Text; control.BackColor = control is Label ? Color.Transparent : theme.Background;
                var button = control as ThemedButton; if (button != null) button.Theme = theme;
                var check = control as ThemedCheckBox; if (check != null) check.Theme = theme;
                var choice = control as ThemedComboBox; if (choice != null) choice.Theme = theme;
                var field = control as ThemedNumberField; if (field != null) field.Theme = theme;
                var slider = control as ThemedTrackBar; if (slider != null) slider.Theme = theme;
                if (control is ComboBox || control is TextBox) control.BackColor = theme.Surface;
            }
            previewStatus.ForeColor = positionStatus.ForeColor = themeStatus.ForeColor = footer.ForeColor = transparencyStatus.ForeColor = theme.Muted;
            preview.Theme = theme; SetFrameTheme(theme); ApplyCaptionTheme(); Invalidate(true);
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
