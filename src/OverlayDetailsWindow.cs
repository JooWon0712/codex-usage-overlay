using System;
using System.Drawing;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal sealed class OverlayDetailsWindow : SettingsFrame
    {
        private readonly TextBox details = new TextBox();
        private readonly ThemedButton closeButton = new ThemedButton();
        private Font contentFont;

        public OverlayDetailsWindow(string title, string text, VisualTheme theme, float scale, Size workArea)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;

            theme = theme ?? VisualTheme.Dark;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            SetFrameTheme(theme);

            details.AccessibleName = title + " 내용";
            details.TabIndex = 0;
            details.Multiline = true;
            details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Vertical;
            details.BackColor = theme.Surface;
            details.ForeColor = theme.Text;
            details.Text = (text ?? String.Empty).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            details.KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.Modifiers != Keys.Control || e.KeyCode != Keys.A) return;
                details.SelectAll();
                e.SuppressKeyPress = true;
            };
            ContentHost.Controls.Add(details);

            closeButton.Text = "닫기";
            closeButton.AccessibleName = "닫기";
            closeButton.TabIndex = 1;
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.Theme = theme;
            ContentHost.Controls.Add(closeButton);
            CancelButton = closeButton;
            LayoutDetails(scale, workArea);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            details.Select(0, 0);
            details.ScrollToCaret();
        }

        protected override void OnFrameDpiChanged(float scale)
        {
            LayoutDetails(scale, Screen.FromHandle(Handle).WorkingArea.Size);
        }

        private void LayoutDetails(float scale, Size workArea)
        {
            SetFrameScale(scale);
            int margin = (int)Math.Round(16 * scale);
            int buttonWidth = (int)Math.Round(88 * scale);
            int buttonHeight = (int)Math.Round(32 * scale);
            int width = Math.Min((int)Math.Round(560 * scale), workArea.Width - margin * 2);
            int height = Math.Min((int)Math.Round(400 * scale), workArea.Height - CaptionHeight - margin * 2);
            SetContentSize(width, height);

            // WinForms keeps the currently assigned Font when the new value is equal.
            // Reuse it at the same DPI so controls never paint with a disposed font.
            float fontSize = 13 * scale;
            if (contentFont == null || contentFont.Size != fontSize)
            {
                Font previous = contentFont;
                contentFont = new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                details.Font = closeButton.Font = contentFont;
                if (previous != null) previous.Dispose();
            }
            closeButton.ScaleFactor = scale;
            details.SetBounds(margin, margin, width - margin * 2, height - buttonHeight - margin * 3);
            closeButton.SetBounds(width - buttonWidth - margin, height - buttonHeight - margin,
                buttonWidth, buttonHeight);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && contentFont != null) contentFont.Dispose();
        }
    }
}
