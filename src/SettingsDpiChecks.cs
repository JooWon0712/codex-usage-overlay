using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class SettingsDpiChecks
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        internal static object Observation;

        internal static void Run(Dictionary<string, bool> checks)
        {
            IntPtr foreground = Native.GetForegroundWindow();
            var observations = new List<object>();
            var host = new DpiPreviewHost();
            IntPtr handle = IntPtr.Zero;
            bool noActivation = true, sameHandle = true, preferences = true, example = true;
            int maxBoundsDrift = 0;
            using (var window = new SettingsWindow(host, 1f))
            {
                window.ShowInTaskbar = false;
                window.Opacity = 0;
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-16000, -16000);
                window.Show();
                handle = window.Handle;
                // Seed the fixture explicitly. These are real messages to our
                // own HWND, not claims of physical-monitor DPI transitions.
                SendDpi(window, 96, 96);
                Settle(window);
                window.SelectPreviewPage(1);
                var baseline = CaptureControls(window);
                string saved = Json(host.Prefs);
                int calls = host.MutationCalls + host.QueryCalls + host.WindowCalls;
                var preview = Field<BadgePreview>(window, "preview");
                UsagePresentation cached = preview.Presentation;
                string cachedText = PreviewText(cached);
                var position = Field<ThemedComboBox>(window, "positionChoice");
                var theme = Field<ThemedComboBox>(window, "themeChoice");
                var x = Field<ThemedNumericUpDown>(window, "x");
                var y = Field<ThemedNumericUpDown>(window, "y");
                int selectedPosition = position.SelectedIndex, selectedTheme = theme.SelectedIndex;
                decimal left = x.Value, vertical = y.Value;
                int previousDpi = 96;
                foreach (int dpi in new[] { 96, 144, 192, 96 })
                {
                    SendDpi(window, previousDpi, dpi);
                    previousDpi = dpi;
                    Settle(window);
                    float scale = dpi / 96f;
                    string suffix = "_" + dpi + (observations.Count == 3 ? "_returned" : "");
                    var content = Content(window);
                    var page = Field<Panel>(window, "pageHost");
                    var firstFrame = x.Parent as ThemedNumberField;
                    var secondFrame = y.Parent as ThemedNumberField;
                    bool viewport = content != null
                        && content.Bounds == new Rectangle(0, window.CaptionHeight, (int)Math.Round(600 * scale), (int)Math.Round(630 * scale))
                        && window.ClientSize == new Size(content.Width, content.Height + window.CaptionHeight)
                        && window.CaptionHeight == (window.HasCustomCaption ? (int)Math.Round(44 * scale) : 0);
                    checks["settings_dpi_viewport_and_caption_match_message_scale" + suffix] = viewport;
                    checks["settings_dpi_main_content_and_preview_coordinates_are_scaled_once" + suffix] =
                        page.Bounds == Scaled(20, 130, 560, 428, scale)
                        && preview.Bounds == Scaled(16, 42, 520, 116, scale)
                        && Near(Field<float>(window, "uiScale"), scale)
                        && Near(Field<float>(window, "layoutScale"), scale) && Near(preview.UiScale, scale);
                    checks["settings_dpi_native_choices_keep_selection_font_item_height_and_scale" + suffix] =
                        ChoiceFits(position, 24, 52, 400, scale) && ChoiceFits(theme, 24, 294, 400, scale)
                        && position.SelectedIndex == selectedPosition && theme.SelectedIndex == selectedTheme;
                    checks["settings_dpi_number_frames_fonts_and_native_children_are_scaled_once" + suffix] =
                        FrameFits(x, firstFrame, 24, 158, 180, scale) && FrameFits(y, secondFrame, 240, 158, 184, scale)
                        && x.Value == left && y.Value == vertical;
                    checks["settings_dpi_buttons_checks_and_slider_use_new_scale" + suffix] =
                        OtherInputsFit(window, scale);
                    sameHandle &= window.Handle == handle && !window.IsDisposed;
                    noActivation &= Native.GetForegroundWindow() == foreground && !window.ShowInTaskbar;
                    preferences &= saved == Json(host.Prefs)
                        && host.MutationCalls + host.QueryCalls + host.WindowCalls == calls;
                    example &= ReferenceEquals(preview.Presentation, cached) && PreviewText(preview.Presentation) == cachedText
                        && Field<Label>(window, "previewStatus").Text.Contains("예시");
                    observations.Add(new {
                        dpi = dpi, scale = scale, captionHeight = window.CaptionHeight,
                        client = SizeValue(window.ClientSize), content = Bounds(content.Bounds), page = Bounds(page.Bounds),
                        preview = Bounds(preview.Bounds), previewScale = preview.UiScale,
                        choiceFont = position.Font.Size, choiceScale = position.ScaleFactor, choiceItemHeight = position.ItemHeight,
                        numberFont = x.Font.Size, numberScale = x.ScaleFactor,
                        numberFrame = firstFrame == null ? null : Bounds(firstFrame.Bounds),
                        viewportMatches = viewport, foregroundUnchanged = Native.GetForegroundWindow() == foreground,
                        sameWindow = window.Handle == handle, hostMutationCalls = host.MutationCalls,
                        hostQueryCalls = host.QueryCalls, hostWindowCalls = host.WindowCalls
                    });
                }
                bool returnFonts = true, returnBounds = true;
                List<ControlState> final = CaptureControls(window);
                returnBounds &= final.Count == baseline.Count;
                for (int index = 0; index < Math.Min(final.Count, baseline.Count); index++)
                {
                    int drift = BoundsDrift(final[index].Bounds, baseline[index].Bounds);
                    maxBoundsDrift = Math.Max(maxBoundsDrift, drift);
                    returnBounds &= drift <= 1;
                    returnFonts &= Near(final[index].FontSize, baseline[index].FontSize)
                        && final[index].FontUnit == baseline[index].FontUnit;
                }
                checks["settings_dpi_round_trip_returns_all_created_control_bounds_within_one_physical_pixel"] = returnBounds;
                checks["settings_dpi_round_trip_returns_all_created_control_fonts_without_double_scaling"] = returnFonts;
                checks["settings_dpi_round_trip_keeps_same_hwnd_and_does_not_activate_or_create_taskbar_entry"] = sameHandle && noActivation;
                checks["settings_dpi_messages_do_not_save_preferences_query_or_activate_target"] = preferences;
                checks["settings_dpi_messages_preserve_cached_launch_time_example"] = example;
                window.Close();
            }
            checks["settings_dpi_fixture_releases_its_own_window_after_round_trip"] = handle != IntPtr.Zero && !Native.IsWindow(handle)
                && Native.GetForegroundWindow() == foreground;
            Observation = new { mechanism = "WM_DPICHANGED sent only to an offscreen nonactivating preview fixture",
                physicalMonitorMoveTested = false, maxRoundTripBoundsDrift = maxBoundsDrift,
                hostMutationCalls = host.MutationCalls, hostQueryCalls = host.QueryCalls, hostWindowCalls = host.WindowCalls,
                samples = observations };
        }

        private static void SendDpi(SettingsWindow window, int oldDpi, int newDpi)
        {
            double ratio = (double)newDpi / oldDpi;
            var suggested = new Native.RECT { Left = -16000, Top = -16000,
                Right = -16000 + (int)Math.Round(window.Width * ratio),
                Bottom = -16000 + (int)Math.Round(window.Height * ratio) };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.RECT)));
            try
            {
                Marshal.StructureToPtr(suggested, pointer, false);
                SendMessage(window.Handle, 0x2e0, new IntPtr((newDpi << 16) | newDpi), pointer);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private static void Settle(SettingsWindow window)
        {
            window.PerformLayout();
            foreach (var key in new[] { "x", "y" })
            {
                var number = Field<ThemedNumericUpDown>(window, key);
                if (number.Parent != null) number.Parent.PerformLayout();
                number.PerformLayout();
            }
            window.Update();
        }

        private static bool ChoiceFits(ThemedComboBox choice, int left, int top, int width, float scale)
        {
            return choice.Left == (int)Math.Round(left * scale) && choice.Top == (int)Math.Round(top * scale)
                && choice.Width == (int)Math.Round(width * scale) && Near(choice.Font.Size, 12 * scale)
                && choice.Font.Unit == GraphicsUnit.Pixel && Near(choice.ScaleFactor, scale)
                && choice.ItemHeight == (int)Math.Round(26 * scale) && choice.DropDownStyle == ComboBoxStyle.DropDownList;
        }

        private static bool FrameFits(ThemedNumericUpDown number, ThemedNumberField frame, int left, int top, int width, float scale)
        {
            if (frame == null || frame.Bounds != Scaled(left, top, width, 32, scale)
                || !Near(frame.ScaleFactor, scale) || !Near(number.ScaleFactor, scale)
                || !Near(frame.Font.Size, 12 * scale) || !Near(number.Font.Size, 12 * scale)
                || number.Font.Unit != GraphicsUnit.Pixel || !frame.ClientRectangle.Contains(number.Bounds)
                || number.Left != (int)Math.Round(12 * scale) || number.Height != number.PreferredHeight) return false;
            bool edit = false, spin = false;
            foreach (Control child in number.Controls)
            {
                if (!number.ClientRectangle.Contains(child.Bounds) || !Near(child.Font.Size, 12 * scale)) return false;
                if (child is TextBoxBase) edit = child.Width > 0 && child.Height > 0;
                else spin = child.Width >= 12 && child.Height >= 8;
            }
            return edit && spin;
        }

        private static bool OtherInputsFit(SettingsWindow window, float scale)
        {
            var buttons = Field<ThemedButton[]>(window, "pageButtons");
            bool result = buttons.Length == 2;
            for (int index = 0; index < buttons.Length; index++)
                result &= buttons[index].Bounds == Scaled(20 + index * 284, 96, 276, 30, scale)
                    && Near(buttons[index].ScaleFactor, scale) && Near(buttons[index].Font.Size, 12 * scale);
            foreach (var key in new[] { "resetChoice", "countdownChoice", "planChoice" })
            {
                var choice = Field<ThemedCheckBox>(window, key);
                result &= Near(choice.ScaleFactor, scale) && Near(choice.Font.Size, 14 * scale);
            }
            return result && Near(Field<ThemedTrackBar>(window, "transparencyChoice").ScaleFactor, scale);
        }

        private sealed class ControlState
        {
            internal Rectangle Bounds;
            internal float FontSize;
            internal GraphicsUnit FontUnit;
        }
        private static List<ControlState> CaptureControls(SettingsWindow window)
        {
            var result = new List<ControlState>();
            foreach (Control control in Field<List<Control>>(window, "uiControls"))
                result.Add(new ControlState { Bounds = control.Bounds, FontSize = control.Font.Size, FontUnit = control.Font.Unit });
            return result;
        }
        private static Control Content(SettingsWindow window)
        { return (Control)typeof(SettingsFrame).GetField("ContentHost", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        private static T Field<T>(SettingsWindow window, string name)
        { return (T)typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        private static Rectangle Scaled(int left, int top, int width, int height, float scale)
        { return new Rectangle((int)Math.Round(left * scale), (int)Math.Round(top * scale), (int)Math.Round(width * scale), (int)Math.Round(height * scale)); }
        private static int BoundsDrift(Rectangle first, Rectangle second)
        { return Math.Max(Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y)), Math.Max(Math.Abs(first.Width - second.Width), Math.Abs(first.Height - second.Height))); }
        private static bool Near(float first, float second) { return Math.Abs(first - second) <= 0.02f; }
        private static string Json(Preferences prefs) { return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(prefs); }
        private static string PreviewText(UsagePresentation value)
        { return value.MainText + "\n" + value.DetailText + "\n" + value.ResetDateText + "\n" + value.CountdownText; }
        private static object Bounds(Rectangle rect) { return new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height }; }
        private static object SizeValue(Size size) { return new { width = size.Width, height = size.Height }; }

        // Delegate to the same in-memory renderer fixture, counting effects so
        // a DPI message cannot silently become a query or preference write.
        private sealed class DpiPreviewHost : ISettingsHost
        {
            private readonly SettingsPreviewHost inner = new SettingsPreviewHost(VisualTheme.Dark);
            internal int MutationCalls, QueryCalls, WindowCalls;
            public Preferences Prefs { get { return inner.Prefs; } }
            public string MainText { get { return inner.MainText; } }
            public string Details { get { return inner.Details; } }
            public string PositionStatus { get { return inner.PositionStatus; } }
            public string ThemeStatus { get { return inner.ThemeStatus; } }
            public VisualTheme Theme { get { return inner.Theme; } }
            public UsagePresentation Presentation { get { return inner.Presentation; } }
            public DateTimeOffset StartedAt { get { return inner.StartedAt; } }
            public void SetPosition(string value) { MutationCalls++; inner.SetPosition(value); }
            public void SetCoordinates(int left, int vertical) { MutationCalls++; inner.SetCoordinates(left, vertical); }
            public void ResetPosition() { MutationCalls++; inner.ResetPosition(); }
            public void SetTheme(string value) { MutationCalls++; inner.SetTheme(value); }
            public void SetBadgeLayout(BadgeLayoutSettings value) { MutationCalls++; inner.SetBadgeLayout(value); }
            public void ManualRefresh() { QueryCalls++; inner.ManualRefresh(); }
            public void ActivateTarget() { WindowCalls++; inner.ActivateTarget(); }
            public void Quit() { WindowCalls++; inner.Quit(); }
        }
    }
}
