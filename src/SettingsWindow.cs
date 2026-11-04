using System;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class SettingsWindow : Form
    {
        private readonly OverlayContext app;
        private readonly RadioButton sidebar = new RadioButton(), title = new RadioButton();
        private readonly NumericUpDown x = new NumericUpDown(), y = new NumericUpDown();
        private readonly Label vertical = new Label(), detail = new Label(), summary = new Label();
        private bool updating;
        public SettingsWindow(OverlayContext owner)
        {
            app = owner;
            Text = "Codex Usage Overlay";
            ClientSize = new Size(460, 490);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            BackColor = Color.FromArgb(248, 250, 252);
            AutoScaleMode = AutoScaleMode.None;
            AddLabel("Codex 잔여 한도", 22, 18, 410, 30, 15, true);
            AddLabel("ChatGPT 창이 활성화되면 표시됩니다. 클릭은 아래 앱으로 통과합니다.", 22, 54, 416, 38, 9, false);
            summary.SetBounds(22, 100, 410, 30);
            summary.Font = new Font(Font.FontFamily, 16, FontStyle.Bold, GraphicsUnit.Pixel);
            Controls.Add(summary);
            detail.SetBounds(22, 135, 416, 92);
            detail.ForeColor = Color.FromArgb(71, 85, 105);
            Controls.Add(detail);
            AddLabel("표시 위치", 22, 232, 410, 23, 9, true);
            sidebar.Text = "사이드바";
            sidebar.SetBounds(24, 261, 180, 27);
            Controls.Add(sidebar);
            title.Text = "세션 제목 아래";
            title.SetBounds(222, 261, 200, 27);
            Controls.Add(title);
            AddLabel("가로: 창 왼쪽에서", 24, 307, 180, 24, 9, false);
            vertical.SetBounds(222, 307, 200, 24);
            Controls.Add(vertical);
            x.AccessibleName = "가로 위치";
            x.Minimum = 0;
            x.Maximum = 4000;
            x.Increment = 4;
            x.SetBounds(24, 335, 180, 28);
            Controls.Add(x);
            y.AccessibleName = "세로 위치";
            y.Minimum = 0;
            y.Maximum = 3000;
            y.Increment = 4;
            y.SetBounds(222, 335, 200, 28);
            Controls.Add(y);
            AddLabel("창 콘텐츠 영역 기준 위치입니다. 제목 아래에 맞춰 가로·세로를 보정하세요.", 24, 375, 410, 35, 8, false);
            AddButton("기본 위치", 24, 418, 94, 31, delegate
            {
                app.ResetPosition();
                LoadPreferences();
            });
            AddButton("지금 갱신", 126, 418, 94, 31, delegate
            {
                app.ManualRefresh();
            });
            AddButton("앱에서 확인", 228, 418, 102, 31, delegate
            {
                WindowState = FormWindowState.Minimized;
                app.ActivateTarget();
            });
            AddButton("닫기", 338, 418, 84, 31, delegate
            {
                Close();
            });
            AddLabel("최소화해도 오버레이는 계속 표시됩니다.\nX를 누르면 완전히 종료됩니다.", 24, 456, 310, 32, 8, false);
            AddButton("종료", 350, 456, 72, 28, delegate
            {
                app.Quit();
            });
            sidebar.CheckedChanged += delegate
            {
                if (!updating && sidebar.Checked)
                {
                    app.SetPosition("sidebar");
                }
            };
            title.CheckedChanged += delegate
            {
                if (!updating && title.Checked)
                {
                    app.SetPosition(Preferences.BelowTitle);
                }
            };
            x.ValueChanged += delegate
            {
                SaveCoordinates();
            };
            y.ValueChanged += delegate
            {
                SaveCoordinates();
            };
            float uiScale = Math.Max(96, Native.GetDpiForWindow(Handle)) / 96f;
            if (uiScale != 1)
            {
                foreach (Control control in Controls)
                {
                    control.SetBounds((int)Math.Round(control.Left * uiScale), (int)Math.Round(control.Top * uiScale), (int)Math.Round(control.Width * uiScale), (int)Math.Round(control.Height * uiScale));
                    control.Font = new Font(control.Font.FontFamily, control.Font.Size * uiScale, control.Font.Style, GraphicsUnit.Pixel);
                }

                ClientSize = new Size((int)Math.Round(460 * uiScale), (int)Math.Round(490 * uiScale));
            }

            LoadPreferences();
            UpdateUsage();
        }

        private void AddLabel(string text, int left, int top, int width, int height, float size, bool bold)
        {
            var l = new Label
            {
                Text = text,
                Font = new Font(Font.FontFamily, size * 4 / 3, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel)
            };
            l.SetBounds(left, top, width, height);
            Controls.Add(l);
        }

        private void AddButton(string text, int left, int top, int width, int height, EventHandler click)
        {
            var b = new Button
            {
                Text = text
            };
            b.SetBounds(left, top, width, height);
            b.Click += click;
            Controls.Add(b);
        }

        private void SaveCoordinates()
        {
            if (!updating)
            {
                app.SetCoordinates((int)x.Value, (int)y.Value);
            }
        }

        public void LoadPreferences()
        {
            updating = true;
            bool side = app.Prefs.Position == "sidebar";
            sidebar.Checked = side;
            title.Checked = !side;
            x.Value = Math.Max(0, Math.Min(4000, side ? app.Prefs.SidebarX : app.Prefs.TitleBelowX));
            y.Value = Math.Max(0, Math.Min(3000, side ? app.Prefs.SidebarY : app.Prefs.TitleBelowY));
            vertical.Text = side ? "세로: 창 아래에서" : "세로: 창 위에서";
            updating = false;
        }

        public void UpdateUsage()
        {
            summary.Text = app.MainText;
            detail.Text = app.Details;
        }
    }
}
