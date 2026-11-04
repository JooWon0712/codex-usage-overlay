using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class SettingsWindow : Form
    {
        private static readonly string[] ElementKeys = { "remaining", "updated", "weekly-reset", "countdown", "indicator" };
        private readonly ISettingsHost app;
        private readonly TabControl tabs = new TabControl();
        private readonly TabPage editPage = new TabPage("배치 편집"), positionPage = new TabPage("표시 위치 · 테마"), dataPage = new TabPage("조회 상세");
        private readonly BadgeEditor editor = new BadgeEditor();
        private readonly ComboBox elementChoice = new ComboBox(), positionChoice = new ComboBox(), themeChoice = new ComboBox();
        private readonly NumericUpDown elementX = new NumericUpDown(), elementY = new NumericUpDown(), elementWidth = new NumericUpDown(), elementFont = new NumericUpDown(), badgeWidth = new NumericUpDown(), badgeHeight = new NumericUpDown(), x = new NumericUpDown(), y = new NumericUpDown();
        private readonly ThemedCheckBox visibleChoice = new ThemedCheckBox(), boldChoice = new ThemedCheckBox();
        private readonly Label previewStatus = new Label(), elementNote = new Label(), positionStatus = new Label(), themeStatus = new Label(), horizontal = new Label(), vertical = new Label(), footer = new Label();
        private readonly TextBox detail = new TextBox();
        private ThemedButton forwardButton, backwardButton;
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
            ClientSize = new Size(760, 612);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            AddLabel(this, "Codex 잔여 한도", 22, 18, 716, 30, 20, true);
            AddLabel(this, "배지 안의 항목을 끌어 배치하고 크기와 글꼴을 조절하세요.", 22, 56, 716, 24, 12, false);
            tabs.SetBounds(20, 96, 720, 446);
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(236, 30);
            tabs.TabPages.AddRange(new[] { editPage, positionPage, dataPage });
            tabs.DrawItem += DrawTab;
            Controls.Add(tabs);

            AddLabel(editPage, "항목 드래그: 이동   ·   오른쪽 아래 핸들: 폭 조절   ·   방향키: 1 / Shift: 10", 16, 12, 666, 22, 11, false);
            editor.SetBounds(16, 42, 470, 250);
            editor.AccessibleName = "오버레이 배치 편집기";
            editPage.Controls.Add(editor);
            AddLabel(editPage, "편집할 항목", 510, 12, 172, 22, 12, true);
            AddChoice(editPage, elementChoice, "편집할 항목", new[] { "남은 한도", "갱신 시각 · 상태", "주간 초기화 날짜", "초기화 디데이", "상태 아이콘" }, 510, 40, 172);
            visibleChoice.Text = "항목 표시"; visibleChoice.SetBounds(510, 80, 172, 22); editPage.Controls.Add(visibleChoice);
            boldChoice.Text = "굵게"; boldChoice.SetBounds(510, 108, 172, 22); editPage.Controls.Add(boldChoice);
            AddLabel(editPage, "가로 X", 510, 142, 76, 20, 11, false);
            AddLabel(editPage, "세로 Y", 600, 142, 76, 20, 11, false);
            AddNumber(editPage, elementX, "항목 가로 위치", 510, 164, 76, 0, 600);
            AddNumber(editPage, elementY, "항목 세로 위치", 600, 164, 76, 0, 300);
            AddLabel(editPage, "항목 폭", 510, 202, 76, 20, 11, false);
            AddLabel(editPage, "글자 크기", 600, 202, 76, 20, 11, false);
            AddNumber(editPage, elementWidth, "항목 폭", 510, 224, 76, 18, 600);
            AddNumber(editPage, elementFont, "글자 크기", 600, 224, 76, 9, 28);
            elementFont.DecimalPlaces = 1; elementFont.Increment = 0.5m;
            forwardButton = AddButton(editPage, "앞으로", 510, 268, 78, delegate { ChangeZOrder(true); });
            backwardButton = AddButton(editPage, "뒤로", 598, 268, 78, delegate { ChangeZOrder(false); });
            elementNote.SetBounds(510, 312, 172, 64); elementNote.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); editPage.Controls.Add(elementNote);
            previewStatus.SetBounds(16, 302, 470, 36); previewStatus.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); editPage.Controls.Add(previewStatus);
            AddLabel(editPage, "배지 폭", 16, 351, 66, 22, 12, false);
            AddNumber(editPage, badgeWidth, "배지 전체 폭", 86, 346, 90, 180, 600);
            AddLabel(editPage, "높이", 202, 351, 40, 22, 12, false);
            AddNumber(editPage, badgeHeight, "배지 전체 높이", 246, 346, 90, 60, 300);
            AddButton(editPage, "초기 배치 복원", 350, 346, 136, delegate { SetEditorLayout(new BadgeLayoutSettings()); SaveEditorLayout(); });
            AddLabel(editPage, "배치와 크기는 자동 저장됩니다. 날짜·디데이는 실제 주간 초기화 데이터를 사용합니다.", 16, 386, 666, 22, 11, false);

            AddLabel(positionPage, "표시 위치", 24, 22, 640, 23, 12, true);
            AddChoice(positionPage, positionChoice, "표시 위치", new[] { "사이드바", "세션 제목 아래" }, 24, 52, 400);
            positionStatus.SetBounds(24, 90, 644, 32); positionPage.Controls.Add(positionStatus);
            horizontal.SetBounds(24, 130, 180, 24); positionPage.Controls.Add(horizontal);
            vertical.SetBounds(240, 130, 200, 24); positionPage.Controls.Add(vertical);
            AddNumber(positionPage, x, "가로 위치", 24, 158, 180, -4000, 4000);
            AddNumber(positionPage, y, "세로 위치", 240, 158, 184, 0, 3000);
            AddButton(positionPage, "기본 위치", 24, 204, 110, delegate { app.ResetPosition(); LoadPreferences(); });
            AddLabel(positionPage, "테마", 24, 264, 400, 24, 12, true);
            AddChoice(positionPage, themeChoice, "테마", new[] { "Codex 따라가기", "Windows 따라가기", "라이트", "다크" }, 24, 294, 400);
            themeStatus.SetBounds(24, 334, 644, 36); positionPage.Controls.Add(themeStatus);

            detail.Multiline = true; detail.ReadOnly = true; detail.ScrollBars = ScrollBars.Vertical;
            detail.SetBounds(24, 24, 644, 302); detail.AccessibleName = "한도 조회 상세"; dataPage.Controls.Add(detail);
            AddLabel(dataPage, "초기화 날짜와 디데이는 Windows 표시 시간대의 달력 날짜를 기준으로 합니다.", 24, 342, 644, 42, 12, false);
            footer.SetBounds(22, 558, 404, 42); footer.Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel); footer.Text = "최소화하면 표시를 유지합니다.\nX 또는 종료를 누르면 완전히 종료됩니다."; Controls.Add(footer);
            AddButton(this, "지금 갱신", 440, 562, 100, delegate { app.ManualRefresh(); });
            AddButton(this, "앱에서 확인", 550, 562, 100, delegate { WindowState = FormWindowState.Minimized; app.ActivateTarget(); });
            AddButton(this, "종료", 660, 562, 80, delegate { app.Quit(); });

            editor.SelectionChanged += delegate { UpdateElementControls(); };
            editor.LayoutChanged += delegate { UpdateElementControls(); SaveEditorLayout(); };
            elementChoice.SelectedIndexChanged += delegate { if (!updating && elementChoice.SelectedIndex >= 0) { editor.SelectedKey = ElementKeys[elementChoice.SelectedIndex]; UpdateElementControls(); } };
            foreach (var number in new[] { elementX, elementY, elementWidth, elementFont }) number.ValueChanged += delegate { ApplyElementValues(); };
            visibleChoice.CheckedChanged += delegate { ApplyElementValues(); };
            boldChoice.CheckedChanged += delegate { ApplyElementValues(); };
            badgeWidth.ValueChanged += delegate { ApplyBadgeSize(); };
            badgeHeight.ValueChanged += delegate { ApplyBadgeSize(); };
            positionChoice.SelectedIndexChanged += delegate { if (!updating && positionChoice.SelectedIndex >= 0) { app.SetPosition(positionChoice.SelectedIndex == 0 ? "sidebar" : Preferences.BelowTitle); LoadPreferences(); } };
            themeChoice.SelectedIndexChanged += delegate { if (!updating && themeChoice.SelectedIndex >= 0) { app.SetTheme(new[] { "codex", "system", "light", "dark" }[themeChoice.SelectedIndex]); UpdateTheme(); } };
            x.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
            y.ValueChanged += delegate { if (!updating) app.SetCoordinates((int)x.Value, (int)y.Value); };
            uiScale = previewScale ?? Math.Max(96, Native.GetDpiForWindow(Handle)) / 96f;
            ScaleView(uiScale);
            editor.UiScale = uiScale;
            visibleChoice.ScaleFactor = boldChoice.ScaleFactor = uiScale;
            foreach (var choice in new[] { elementChoice, positionChoice, themeChoice }) choice.ItemHeight = (int)Math.Round(25 * uiScale);
            tabs.ItemSize = new Size((int)Math.Round(236 * uiScale), (int)Math.Round(30 * uiScale));
            if (!previewMode)
            {
                var fullSize = ClientSize;
                var work = Screen.FromHandle(Handle).WorkingArea;
                AutoScrollMinSize = fullSize;
                AutoScroll = true;
                ClientSize = new Size(Math.Min(fullSize.Width, Math.Max(320, work.Width - 48)), Math.Min(fullSize.Height, Math.Max(360, work.Height - (int)(64 * uiScale))));
            }
            LoadPreferences();
            UpdateUsage();
            ApplyTheme(app.Theme);
        }

        private static void Collect(Control parent, List<Control> result)
        { foreach (Control child in parent.Controls) { result.Add(child); Collect(child, result); } }
        private void ScaleView(float scale)
        {
            var controls = new List<Control>(); Collect(this, controls);
            var snapshots = new List<Tuple<Control, Rectangle, Font>>();
            foreach (var control in controls) snapshots.Add(Tuple.Create(control, control.Bounds, control.Font));
            foreach (var item in snapshots)
            {
                if (!(item.Item1 is TabPage)) item.Item1.SetBounds((int)Math.Round(item.Item2.X * scale), (int)Math.Round(item.Item2.Y * scale), (int)Math.Round(item.Item2.Width * scale), (int)Math.Round(item.Item2.Height * scale));
                item.Item1.Font = new Font(item.Item3.FontFamily, item.Item3.Size * scale, item.Item3.Style, GraphicsUnit.Pixel);
            }
            ClientSize = new Size((int)Math.Round(760 * scale), (int)Math.Round(612 * scale));
        }
        private void AddLabel(Control parent, string text, int left, int top, int width, int height, float size, bool bold)
        {
            var label = new Label { Text = text, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel) };
            label.SetBounds(left, top, width, height); parent.Controls.Add(label);
        }
        private ThemedButton AddButton(Control parent, string text, int left, int top, int width, EventHandler click)
        {
            var button = new ThemedButton { Text = text, AccessibleName = text }; button.SetBounds(left, top, width, 32); button.Click += click; parent.Controls.Add(button); return button;
        }
        private void AddChoice(Control parent, ComboBox choice, string label, string[] items, int left, int top, int width)
        {
            choice.AccessibleName = label; choice.DropDownStyle = ComboBoxStyle.DropDownList; choice.FlatStyle = FlatStyle.Flat; choice.DrawMode = DrawMode.OwnerDrawFixed;
            choice.Items.AddRange(items); choice.SetBounds(left, top, width, 28); choice.DrawItem += DrawChoice; parent.Controls.Add(choice);
        }
        private void AddNumber(Control parent, NumericUpDown number, string label, int left, int top, int width, int min, int max)
        { number.AccessibleName = label; number.Minimum = min; number.Maximum = max; number.SetBounds(left, top, width, 28); parent.Controls.Add(number); }

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
                SetEditorLayout(app.Prefs.BadgeLayout ?? new BadgeLayoutSettings());
            }
            finally { updating = false; }
            UpdateElementControls();
        }
        private void SetEditorLayout(BadgeLayoutSettings value)
        {
            editor.Layout = value.Copy(); editor.Layout.Normalize();
            if (String.IsNullOrEmpty(editor.SelectedKey)) editor.SelectedKey = "remaining";
            UpdateElementControls();
        }
        private BadgeElement SelectedElement()
        { return editor.Layout == null ? null : editor.Layout.Elements.Find(element => element.Key == editor.SelectedKey); }
        private void UpdateElementControls()
        {
            bool before = updating; updating = true;
            try
            {
                var layout = editor.Layout; var element = SelectedElement(); if (layout == null) return;
                badgeWidth.Value = layout.Width; badgeHeight.Value = layout.Height;
                bool selected = element != null;
                elementX.Enabled = elementY.Enabled = elementWidth.Enabled = elementFont.Enabled = visibleChoice.Enabled = boldChoice.Enabled = selected;
                forwardButton.Enabled = backwardButton.Enabled = selected;
                previewStatus.Text = (app.IsSample ? "예시 데이터 · 개발 미리보기" : "현재 조회 상태 · 실시간 미리보기") + "\n" + layout.Width + " × " + layout.Height + " DIP · 겹치는 항목은 앞으로/뒤로 조절하세요.";
                if (!selected) { elementChoice.SelectedIndex = -1; elementNote.Text = "항목을 클릭하거나 위 목록에서 선택하세요."; return; }
                elementChoice.SelectedIndex = Array.IndexOf(ElementKeys, element.Key);
                elementX.Value = element.X; elementY.Value = element.Y; elementWidth.Value = element.Width; elementFont.Value = (decimal)element.FontSize;
                bool indicator = element.Key == "indicator", required = element.Key == "remaining" || element.Key == "updated";
                visibleChoice.Checked = element.Visible; visibleChoice.Enabled = !required;
                boldChoice.Checked = element.Bold; boldChoice.Enabled = !indicator;
                elementWidth.Enabled = elementFont.Enabled = !indicator;
                elementNote.Text = required ? "한도와 갱신 상태는 항상 표시합니다." : "표시를 끈 항목은 목록에서 다시 켤 수 있습니다.";
            }
            finally { updating = before; }
        }
        private void ApplyElementValues()
        {
            if (updating) return; var element = SelectedElement(); if (element == null) return;
            element.X = (int)elementX.Value; element.Y = (int)elementY.Value; element.Width = (int)elementWidth.Value; element.FontSize = (float)elementFont.Value;
            element.Visible = visibleChoice.Checked; element.Bold = boldChoice.Checked;
            editor.Layout.Normalize(); UpdateElementControls(); SaveEditorLayout(); editor.Invalidate();
        }
        private void ApplyBadgeSize()
        {
            if (updating || editor.Layout == null) return;
            editor.Layout.Width = (int)badgeWidth.Value; editor.Layout.Height = (int)badgeHeight.Value;
            editor.Layout.Normalize(); UpdateElementControls(); SaveEditorLayout(); editor.Invalidate();
        }
        private void ChangeZOrder(bool front)
        {
            var element = SelectedElement(); if (element == null) return;
            editor.Layout.Elements.Remove(element);
            if (front) editor.Layout.Elements.Add(element); else editor.Layout.Elements.Insert(0, element);
            SaveEditorLayout(); editor.Invalidate();
        }
        private void SaveEditorLayout() { if (!updating && editor.Layout != null) app.SetBadgeLayout(editor.Layout); }
        public void UpdateUsage()
        {
            string details = app.Details.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            if (detail.Text != details) detail.Text = details;
            if (!ReferenceEquals(editor.Presentation, app.Presentation)) editor.Presentation = app.Presentation;
            UpdatePositionStatus();
        }
        public void UpdatePositionStatus() { positionStatus.Text = app.PositionStatus; UpdateTheme(); }
        internal void SelectPreviewPage(int index) { if (previewMode && index >= 0 && index < tabs.TabPages.Count) tabs.SelectedIndex = index; }
        public void UpdateTheme()
        { if (!ReferenceEquals(theme, app.Theme)) ApplyTheme(app.Theme); themeStatus.Text = app.ThemeStatus; }
        private void ApplyTheme(VisualTheme value)
        {
            theme = value ?? VisualTheme.Dark; BackColor = theme.Background; ForeColor = theme.Text;
            var controls = new List<Control>(); Collect(this, controls);
            foreach (Control control in controls)
            {
                control.ForeColor = theme.Text; control.BackColor = control is Label ? Color.Transparent : theme.Background;
                var button = control as ThemedButton; if (button != null) button.Theme = theme;
                var check = control as ThemedCheckBox; if (check != null) check.Theme = theme;
                var page = control as TabPage; if (page != null) page.UseVisualStyleBackColor = false;
                if (control is NumericUpDown || control is ComboBox || control is TextBox) control.BackColor = theme.Surface;
            }
            previewStatus.ForeColor = elementNote.ForeColor = positionStatus.ForeColor = themeStatus.ForeColor = footer.ForeColor = theme.Muted;
            editor.Theme = theme; ApplyCaptionTheme(); Invalidate(true);
        }
        private void DrawChoice(object sender, DrawItemEventArgs e)
        {
            var choice = (ComboBox)sender; if (e.Index < 0 || e.Index >= choice.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0, contrast = SystemInformation.HighContrast;
            Color fill = selected ? (contrast ? SystemColors.Highlight : theme.Hover) : theme.Surface;
            Color text = selected && contrast ? SystemColors.HighlightText : theme.Text;
            using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
            var bounds = e.Bounds; bounds.Inflate(-(int)(8 * uiScale), 0);
            TextRenderer.DrawText(e.Graphics, choice.Items[e.Index].ToString(), choice.Font, bounds, text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); e.DrawFocusRectangle();
        }
        private void DrawTab(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
            bool selected = tabs.SelectedIndex == e.Index;
            using (var fill = new SolidBrush(selected ? theme.Surface : theme.Background)) e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, selected ? theme.Text : theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected) using (var edge = new Pen(theme.Accent, Math.Max(1, 2 * uiScale))) e.Graphics.DrawLine(edge, e.Bounds.Left + 6, e.Bounds.Bottom - 2, e.Bounds.Right - 6, e.Bounds.Bottom - 2);
            e.DrawFocusRectangle();
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
