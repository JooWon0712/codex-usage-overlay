using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class BadgeFormatButtonChecks
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        internal static void Run(Dictionary<string, bool> checks)
        {
            CheckPlacement(checks);
            CheckChevron(checks);
            CheckWindow(checks);
        }

        private static void CheckPlacement(Dictionary<string, bool> checks)
        {
            foreach (bool compact in new[] { false, true })
            foreach (bool showClaude in new[] { false, true })
            {
                var layout = new BadgeLayoutSettings { Compact = compact, ShowClaude = showClaude };
                layout.Normalize();
                foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                {
                    string suffix = (compact ? "_compact" : "_detailed") + (showClaude ? "_with_claude_" : "_") + (int)(100 * scale);
                    int hitSize = (int)Math.Round(24 * scale);
                    int inset = (int)Math.Round((showClaude && !compact ? 12 : 8) * scale);
                    int headerHeight = (int)Math.Round((showClaude && !compact ? 28 : 24) * scale);
                    var badge = new Rectangle(-1220, -640, (int)Math.Round(layout.Width * scale), (int)Math.Round(layout.Height * scale));
                    Rectangle hit = BadgeFormatButton.CalculateBounds(badge, layout, scale);
                    var header = new Rectangle(badge.Location, new Size(badge.Width, headerHeight));
                    checks["badge_format_button_hit_area_fits_inline_in_the_header" + suffix] =
                        header.Contains(hit) && hit.Size == new Size(hitSize, hitSize) && hit.Right == header.Right - inset
                        && Math.Abs((hit.Top - header.Top) - (header.Bottom - hit.Bottom)) <= 1;
                }
            }
        }

        private static void CheckChevron(Dictionary<string, bool> checks)
        {
            foreach (bool compact in new[] { false, true })
            {
                using (var bitmap = new Bitmap(48, 48))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    BadgeFormatButton.DrawChevron(g, new Rectangle(0, 0, 48, 48), 2f, Color.White, compact);
                    int middleAlpha = 0, middleY = 0, outerAlpha = 0, outerY = 0;
                    for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int alpha = bitmap.GetPixel(x, y).A;
                        if (x >= 21 && x <= 26)
                        {
                            middleAlpha += alpha;
                            middleY += y * alpha;
                        }
                        else
                        {
                            outerAlpha += alpha;
                            outerY += y * alpha;
                        }
                    }
                    bool matches = middleAlpha > 0 && outerAlpha > 0 &&
                        (compact ? (double)middleY / middleAlpha > (double)outerY / outerAlpha
                            : (double)middleY / middleAlpha < (double)outerY / outerAlpha);
                    checks[compact ? "badge_format_button_points_down_to_expand" : "badge_format_button_points_up_to_collapse"] = matches;
                }
            }
        }

        private static void CheckWindow(Dictionary<string, bool> checks)
        {
            // This fixture never enters a Codex window or moves the real pointer.
            using (var button = new BadgeFormatButton())
            using (var badge = new Badge())
            {
                button.StartPosition = FormStartPosition.Manual;
                button.Bounds = new Rectangle(-32000, -32000, 24, 24);
                button.SetAppearance(VisualTheme.Dark, false);
                IntPtr foreground = Native.GetForegroundWindow();
                bool prepared = button.Present(1f);
                button.Show();
                IntPtr window = button.Handle;
                checks["badge_format_button_presents_the_layered_hit_area"] = prepared && button.Present(1f)
                    && (Native.GetWindowLongPtr(window, -20).ToInt64() & 0x80020L) == 0x80000L;
                checks["badge_format_button_does_not_activate_or_enter_taskbar"] =
                    (Native.GetWindowLongPtr(window, -20).ToInt64() & 0x080000a0L) == 0x08000080L
                    && !button.ShowInTaskbar && Native.GetForegroundWindow() == foreground
                    && SendMessage(window, 0x21, window, new IntPtr(1 | (0x201 << 16))).ToInt64() == 3;
                checks["badge_format_button_leaves_existing_badge_click_through"] =
                    (Native.GetWindowLongPtr(badge.Handle, -20).ToInt64() & 0x80020L) == 0x80020L;
                bool toCompact = button.AccessibleName == "간결형으로 전환";
                button.SetAppearance(VisualTheme.Light, true);
                checks["badge_format_button_names_the_next_format_accessibly"] =
                    toCompact && button.AccessibleName == "상세형으로 전환";

                int requests = 0;
                button.ToggleRequested += delegate { requests++; };
                SendLeftButton(window, 0x202, 10, 10);
                SendLeftButton(window, 0x201, 10, 10);
                SendLeftButton(window, 0x202, 60, 10);
                checks["badge_format_button_does_not_switch_on_unpaired_or_outside_release"] = requests == 0;
                SendLeftButton(window, 0x201, 10, 10);
                SendLeftButton(window, 0x202, 10, 10);
                checks["badge_format_button_switches_once_on_down_and_inside_release"] =
                    requests == 1 && !button.Capture && Native.GetForegroundWindow() == foreground;
                SendLeftButton(window, 0x201, 10, 10);
                button.Capture = false;
                SendLeftButton(window, 0x202, 10, 10);
                checks["badge_format_button_cancels_switch_if_it_loses_capture"] = requests == 1;
                button.Hide();
            }
        }

        private static void SendLeftButton(IntPtr window, int message, int x, int y)
        {
            int coordinates = (x & 0xffff) | ((y & 0xffff) << 16);
            SendMessage(window, message, new IntPtr(message == 0x201 ? 1 : 0), new IntPtr(coordinates));
        }
    }
}
