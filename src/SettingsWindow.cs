using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class SettingsWindow : SettingsFrame
    {
        private const int BodyWidth = 608;
        private const int BodyHeight = 782;
        private const int SideMargin = 28;
        private const int BottomMargin = 24;
        private const int ContentWidth = BodyWidth - 2 * SideMargin;
        private const int RightEdge = BodyWidth - SideMargin;
        private const int OverlayInset = 16;
        private const int OverlayContentWidth = ContentWidth - 2 * OverlayInset - 8;
        private const int ProblemContentHeight = 720;
        private readonly ISettingsHost app;
        private readonly List<Control> uiControls = new List<Control>();
        private readonly Dictionary<Control, Rectangle> layoutBounds = new Dictionary<Control, Rectangle>();
        private readonly Panel pageHost = new Panel();
        private readonly Panel overlayPage = new Panel(), positionPage = new Panel();
        private readonly ThemedButton[] pageButtons = new ThemedButton[2];
        private Panel[] pages;
        private readonly BadgePreview preview = new BadgePreview();
        private readonly ThemedCheckBox resetChoice = new ThemedCheckBox(), countdownChoice = new ThemedCheckBox(), planChoice = new ThemedCheckBox(), claudeChoice = new ThemedCheckBox();
        private readonly ThemedComboBox formatChoice = new ThemedComboBox(), positionChoice = new ThemedComboBox(), themeChoice = new ThemedComboBox();
        private readonly ThemedNumericUpDown x = new ThemedNumericUpDown(), y = new ThemedNumericUpDown();
        private readonly Label previewStatus = new Label(), positionStatus = new Label(), themeStatus = new Label(), horizontal = new Label(), vertical = new Label(), footer = new Label();
        private readonly Label connectionStatus = new Label();
        private readonly Label claudeIntro = new Label(), claudeStatus = new Label(), claudeSetupHint = new Label();
        private readonly ThemedButton problemToggle = new ThemedButton();
        private readonly TextBox problemDetails = new TextBox();
        private readonly ThemedTrackBar transparencyChoice = new ThemedTrackBar();
        private readonly Label transparencyStatus = new Label(), formatHint = new Label();
        private VisualTheme theme = VisualTheme.Dark;
        private readonly bool previewMode;
        private bool updating, problemExpanded;
        private float uiScale = 1;
        private float layoutScale = 1;

        internal SettingsWindow(ISettingsHost owner, float? previewScale, ServiceIcons icons = null)
        {
            app = owner;
            previewMode = previewScale.HasValue;
            Text = "Codex Usage Overlay";
            ClientSize = new Size(BodyWidth, BodyHeight);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = true; ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;

            BuildHeaderAndTabs();
            BuildOverlayPage(icons);
            BuildPositionPage();
            BuildFooter();
            BindSettingsEvents();

            uiScale = previewScale ?? Math.Max(96, Native.GetDpiForWindow(Handle)) / 96f;
            ScaleView(uiScale);
            ScaleInputs();
            if (!previewMode)
                FitWorkArea();
            SelectPage(0);
            LoadPreferences();
            UpdateUsage();
            ApplyTheme(app.Theme);
        }

        private void BuildHeaderAndTabs()
        {
            AddLabel(this, "Codex 잔여 한도", SideMargin, 18, 320, 30, 20, true);
            AddButton(this, "조회 기록", RightEdge - 204, 17, 96, delegate { ShowLookupHistory(); });
            AddButton(this, "버전 정보", RightEdge - 96, 17, 96, delegate
            {
                ShowDetails("버전 정보", PortableReleaseInfo.VersionText + "\n\n" + PortableReleaseInfo.Changes);
            });
            connectionStatus.SetBounds(SideMargin, 56, ContentWidth, 24);
            connectionStatus.TextAlign = ContentAlignment.MiddleLeft;
            connectionStatus.AutoEllipsis = true;
            AddUi(this, connectionStatus);
            pageHost.SetBounds(SideMargin, 130, ContentWidth, 578); AddUi(this, pageHost);
            pages = new[] { overlayPage, positionPage };
            string[] pageNames = { "오버레이", "표시 위치 · 테마" };
            const int tabGap = 8;
            const int tabWidth = (ContentWidth - tabGap) / 2;
            for (int index = 0; index < pages.Length; index++)
            {
                pages[index].Dock = DockStyle.Fill; AddUi(pageHost, pages[index]);
                int selected = index;
                var button = new ThemedButton { Text = pageNames[index], AccessibleName = pageNames[index] };
                button.SetBounds(SideMargin + index * (tabWidth + tabGap), 96, tabWidth, 30);
                button.Click += delegate { SelectPage(selected); };
                pageButtons[index] = button; AddUi(this, button);
            }
        }

        private void BuildOverlayPage(ServiceIcons icons)
        {
            AddLabel(overlayPage, "오버레이 미리보기", OverlayInset, 8, 240, 22, 12, true);
            AddLabel(overlayPage, "배지 형식", 293, 8, 72, 22, 12, false);
            AddChoice(overlayPage, formatChoice, "배지 형식", new[] { "상세형", "간결형" }, 369, 4, 159);
            preview.Icons = icons;
            preview.Presentation = UsagePresentation.CreatePreview(app.StartedAt);
            preview.AccessibleDescription = "프로그램 시작 시각으로 만든 예시 데이터입니다.";
            preview.SetBounds(OverlayInset, 40, OverlayContentWidth, 220); AddUi(overlayPage, preview);
            previewStatus.SetBounds(OverlayInset, 264, OverlayContentWidth, 20); previewStatus.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); AddUi(overlayPage, previewStatus);
            transparencyStatus.SetBounds(OverlayInset, 287, OverlayContentWidth, 22); AddUi(overlayPage, transparencyStatus);
            // The native control has eleven positions, so every mouse/keyboard
            // value maps to a supported ten-percent step without event re-entry.
            transparencyChoice.Minimum=0; transparencyChoice.Maximum=10; transparencyChoice.SmallChange=1; transparencyChoice.LargeChange=1;
            transparencyChoice.TickStyle=TickStyle.None; transparencyChoice.AutoSize=false; transparencyChoice.AccessibleName="배경 투명도";
            transparencyChoice.SetBounds(OverlayInset, 308, OverlayContentWidth, 24); AddUi(overlayPage, transparencyChoice);
            AddLabel(overlayPage, "Codex 표시 옵션", OverlayInset, 332, 138, 22, 14, true);
            formatHint.SetBounds(158, 332, 228, 22); formatHint.TextAlign = ContentAlignment.MiddleLeft; AddUi(overlayPage, formatHint);
            AddOption(resetChoice, "주간 초기화 날짜 · 시각", OverlayInset, 354, 268);
            AddOption(planChoice, "구독 요금제 (공통)", 296, 354, 232);
            planChoice.AccessibleName = "구독 요금제";
            planChoice.AccessibleDescription = "연결된 Codex와 Claude Code 계정에서 확인한 요금제를 배지에 표시합니다.";
            AddOption(countdownChoice, "초기화 디데이", OverlayInset, 382, 268);

            AddLabel(overlayPage, "Claude Code", OverlayInset, 415, OverlayContentWidth, 20, 14, true);
            AddOption(claudeChoice, "한도 표시", OverlayInset, 435, 268);
            claudeChoice.AccessibleName = "Claude Code 한도 표시";
            claudeIntro.Text = "이 PC의 Claude Code CLI에 로그인된 구독 계정의 5시간·주간 한도를 조회합니다.\n켜면 바로 확인하고 이후 보통 15분마다 자동으로 갱신합니다. 데스크톱 앱 없이도 동작합니다.";
            claudeChoice.AccessibleDescription = claudeIntro.Text;
            claudeIntro.SetBounds(OverlayInset, 465, OverlayContentWidth, 34); claudeIntro.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel);
            AddUi(overlayPage, claudeIntro);
            claudeStatus.SetBounds(OverlayInset, 501, OverlayContentWidth, 20); claudeStatus.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel);
            claudeStatus.AutoEllipsis = true; AddUi(overlayPage, claudeStatus);
            claudeSetupHint.SetBounds(OverlayInset, 523, OverlayContentWidth, 22); claudeSetupHint.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel);
            AddUi(overlayPage, claudeSetupHint);
            overlayPage.AutoScroll = true;
            problemToggle.Text = "문제 확인 ▸"; problemToggle.AccessibleName = "조회 실패 원인 펼치기";
            problemToggle.SetBounds(OverlayInset, 549, 132, 28); problemToggle.Visible = false;
            problemToggle.Click += delegate { SetProblemExpanded(!problemExpanded); }; AddUi(overlayPage, problemToggle);
            problemDetails.Multiline = true; problemDetails.ReadOnly = true; problemDetails.ScrollBars = ScrollBars.Vertical;
            problemDetails.SetBounds(OverlayInset, 588, OverlayContentWidth, 116); problemDetails.AccessibleName = "조회 실패 원인과 조치";
            problemDetails.Visible = false; AddUi(overlayPage, problemDetails);
        }

        private void BuildPositionPage()
        {
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
        }

        private void BuildFooter()
        {
            const int footerBottom = BodyHeight - BottomMargin;
            const int footerButtonTop = footerBottom - 32;
            footer.SetBounds(SideMargin, footerBottom - 44, 270, 44); footer.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); footer.Text = "최소화하면 표시를 유지합니다.\nX 또는 종료를 누르면 완전히 종료됩니다."; AddUi(this, footer);
            AddButton(this, "지금 갱신", RightEdge - 268, footerButtonTop, 84, delegate { app.ManualRefresh(); });
            AddButton(this, "앱에서 확인", RightEdge - 174, footerButtonTop, 86, delegate { WindowState = FormWindowState.Minimized; app.ActivateTarget(); });
            AddButton(this, "종료", RightEdge - 78, footerButtonTop, 78, delegate { app.Quit(); });
        }

        private void BindSettingsEvents()
        {
            resetChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            countdownChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            planChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            claudeChoice.CheckedChanged += delegate { SaveDisplayOptions(); };
            formatChoice.SelectedIndexChanged += delegate { if (formatChoice.SelectedIndex >= 0) SaveDisplayOptions(); };
            transparencyChoice.ValueChanged += delegate { SaveDisplayOptions(); };
            positionChoice.SelectedIndexChanged += delegate { if (!updating && positionChoice.SelectedIndex >= 0) { app.SetPosition(positionChoice.SelectedIndex == 0 ? "sidebar" : Preferences.BelowTitle); LoadPreferences(); } };
            themeChoice.SelectedIndexChanged += delegate { if (!updating && themeChoice.SelectedIndex >= 0) { app.SetTheme(new[] { "codex", "system", "light", "dark" }[themeChoice.SelectedIndex]); UpdateTheme(); } };
            x.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
            y.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
        }

        private void AddUi(Control parent, Control control)
        {
            // Preserve input coordinates even if WinForms shifts a child when
            // it is added to a scrollable panel or changes the DPI.
            layoutBounds.Add(control, control.Bounds);
            (parent == this ? ContentHost : parent).Controls.Add(control);
            uiControls.Add(control);
        }
        private void ScaleView(float scale)
        {
            float ratio = scale / layoutScale;
            // Only controls created here are scaled. Framework-owned spinner/edit children manage themselves.
            ContentHost.AutoScrollPosition = Point.Empty;
            ContentHost.AutoScrollMinSize = Size.Empty;
            ContentHost.AutoScroll = false;
            overlayPage.AutoScrollPosition = Point.Empty;
            var fonts = new Dictionary<Control, Font>();
            foreach (Control control in uiControls) fonts.Add(control, control.Font);
            foreach (Control control in uiControls)
            {
                if (control.Dock != DockStyle.Fill)
                {
                    Rectangle bounds = layoutBounds[control];
                    control.SetBounds((int)Math.Round(bounds.X * scale), (int)Math.Round(bounds.Y * scale),
                        (int)Math.Round(bounds.Width * scale), (int)Math.Round(bounds.Height * scale));
                }
                Font font = fonts[control];
                control.Font = new Font(font.FontFamily, font.Size * ratio, font.Style, GraphicsUnit.Pixel);
                var button = control as ThemedButton; if (button != null) button.ScaleFactor = scale;
                var number = control as ThemedNumberField; if (number != null) number.ScaleFactor = scale;
            }
            SetFrameScale(scale);
            layoutScale = uiScale = scale;
            if (problemExpanded) overlayPage.AutoScrollMinSize = new Size(0, (int)Math.Round(ProblemContentHeight * scale));
            SetContentSize((int)Math.Round(BodyWidth * scale), (int)Math.Round(BodyHeight * scale));
        }
        private void ScaleInputs()
        {
            preview.UiScale = uiScale; transparencyChoice.ScaleFactor = uiScale;
            foreach (var check in new[] { resetChoice, countdownChoice, planChoice, claudeChoice }) check.ScaleFactor = uiScale;
            foreach (var choice in new[] { formatChoice, positionChoice, themeChoice }) { choice.ScaleFactor = uiScale; choice.ItemHeight = (int)Math.Round(26 * uiScale); }
            foreach (var number in new[] { x, y }) number.ScaleFactor = uiScale;
        }
        private void FitWorkArea()
        {
            FitWorkArea(Screen.FromHandle(Handle).WorkingArea.Size);
        }

        internal void FitWorkArea(Size workArea)
        {
            var fullSize = new Size((int)Math.Round(BodyWidth * uiScale), (int)Math.Round(BodyHeight * uiScale));
            int width = Math.Min(fullSize.Width, Math.Max(320, workArea.Width - 48));
            int height = Math.Min(fullSize.Height, Math.Max(360, workArea.Height - CaptionHeight - (int)(64 * uiScale)));
            if (height < fullSize.Height && width == fullSize.Width)
            {
                // Reserve the vertical scrollbar in the window width so that
                // it does not consume the right padding or force a horizontal scroll.
                int scrollbar = Math.Max(SystemInformation.VerticalScrollBarWidth, (int)Math.Ceiling(17 * uiScale));
                width = Math.Min(width + scrollbar, workArea.Width - 48);
            }
            bool scroll = width < fullSize.Width || height < fullSize.Height;
            ContentHost.AutoScrollMinSize = scroll ? fullSize : Size.Empty;
            ContentHost.AutoScroll = scroll;
            SetContentSize(width, height);
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
        private void AddOption(ThemedCheckBox choice, string text, int left, int top, int width)
        {
            choice.Text = text;
            choice.AccessibleName = text;
            choice.Font = new Font(Font.FontFamily, 14, GraphicsUnit.Pixel);
            choice.SetBounds(left, top, width, 28);
            AddUi(overlayPage, choice);
        }
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
                y.Value = Math.Max(0, Math.Min(3000, side ? app.Prefs.EffectiveSidebarBottomGap : app.Prefs.TitleGap));
                horizontal.Text = side ? "가로: 창 왼쪽에서" : "가로: 기본 위치 보정";
                vertical.Text = side ? "세로: 창 아래 여백" : "세로: 제목 영역 아래 간격";
                themeChoice.SelectedIndex = app.Prefs.ThemeMode == "system" ? 1 : app.Prefs.ThemeMode == "light" ? 2 : app.Prefs.ThemeMode == "dark" ? 3 : 0;
                if (app.Prefs.BadgeLayout == null) app.Prefs.BadgeLayout = new BadgeLayoutSettings();
                // Migrate legacy percentages in memory only; loading never saves.
                app.Prefs.BadgeLayout.Normalize();
                preview.LayoutSettings = app.Prefs.BadgeLayout;
                formatChoice.SelectedIndex = preview.LayoutSettings.Compact ? 1 : 0;
                transparencyChoice.Value=preview.LayoutSettings.BackgroundTransparency / 10;
                resetChoice.Checked = IsVisible("weekly-reset"); countdownChoice.Checked = IsVisible("countdown");
                planChoice.Checked = preview.LayoutSettings.ShowPlan;
                claudeChoice.Checked = preview.LayoutSettings.ShowClaude;
            }
            finally { updating = false; }
            UpdateFormatOptions();
            UpdatePreviewStatus();
        }
        private bool IsVisible(string key) { return preview.LayoutSettings.Elements.Exists(element => element.Key == key && element.Visible); }
        private void SaveDisplayOptions()
        {
            if (updating) return;
            var layout = preview.LayoutSettings.Copy();
            layout.Compact = formatChoice.SelectedIndex == 1;
            layout.BackgroundTransparency=transparencyChoice.Value * 10;
            layout.ShowPlan = planChoice.Checked;
            layout.ShowClaude = claudeChoice.Checked;
            layout.Elements.Find(element => element.Key == "weekly-reset").Visible = resetChoice.Checked;
            layout.Elements.Find(element => element.Key == "countdown").Visible = countdownChoice.Checked;
            layout.Normalize(); preview.LayoutSettings = layout; app.SetBadgeLayout(layout);
            UpdateFormatOptions(); UpdatePreviewStatus();
        }
        private void UpdateFormatOptions()
        {
            bool detailed = !preview.LayoutSettings.Compact;
            countdownChoice.Enabled = detailed;
            formatHint.Text = detailed ? String.Empty : "디데이는 상세형에서 표시";
        }
        private void UpdatePreviewStatus()
        {
            previewStatus.Text = "예시 데이터 · " + (SystemInformation.HighContrast ? "고대비 배경" : "다크 배경") + " · 프로그램 시작 시각 기준";
            transparencyStatus.Text="배경 투명도 · "+(transparencyChoice.Value * 10)+"%"+(SystemInformation.HighContrast?" · 고대비에서는 불투명"
                : BadgeTextColors.Resolve(app.Theme,transparencyChoice.Value*10,false).Bright?" · 밝은 글자":" · 진한 글자");
            var claude = (app.Presentation == null ? null : app.Presentation.Claude) ?? ClaudeUsageDisplay.Awaiting;
            string status = claudeChoice.Checked ? "실제 Claude Code · " + claude.Status : String.Empty;
            if (claudeStatus.Text != status) claudeStatus.Text = status;
            string setupHint = claudeChoice.Checked ? (claude.SetupHint ?? String.Empty) : String.Empty;
            if (claudeSetupHint.Text != setupHint) claudeSetupHint.Text = setupHint;
        }
        public void UpdateUsage()
        {
            string status = ConnectionStatusText();
            if (connectionStatus.Text != status) connectionStatus.Text = status;
            UpdateProblemDetails();
            UpdatePositionStatus();
        }
        private string ConnectionStatusText()
        {
            string status = app.CodexConnectionStatus;
            return "Codex CLI · " + (String.IsNullOrWhiteSpace(status) ? "확인 중" : status);
        }
        private void ShowLookupHistory()
        {
            string path = app.CodexExecutablePath;
            string history = app.LookupHistoryText;
            ShowDetails("조회 기록", ConnectionStatusText()
                + "\n\n현재 조회에 선택된 경로\n" + (String.IsNullOrWhiteSpace(path) ? "확인할 수 없음" : path)
                + "\n\n이번 실행의 조회 기록 · 최대 30건\n" + (String.IsNullOrWhiteSpace(history) ? "조회 기록이 없습니다." : history));
        }
        private void ShowDetails(string title, string text)
        {
            using (var details = new OverlayDetailsWindow(title, text, theme, uiScale, Screen.FromControl(this).WorkingArea.Size))
                details.ShowDialog(this);
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
            overlayPage.AutoScrollMinSize = problemExpanded ? new Size(0, (int)Math.Round(ProblemContentHeight * uiScale)) : Size.Empty;
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
            previewStatus.ForeColor = positionStatus.ForeColor = themeStatus.ForeColor = footer.ForeColor = transparencyStatus.ForeColor = connectionStatus.ForeColor = claudeStatus.ForeColor = formatHint.ForeColor = theme.Muted;
            claudeIntro.ForeColor = theme.Muted;
            preview.Theme = theme; SetFrameTheme(theme); Invalidate(true);
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
    }
}
