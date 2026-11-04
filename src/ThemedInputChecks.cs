using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class ThemedInputChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            checks["themed_inputs_keep_native_combo_and_numeric_base_types"] = typeof(ThemedComboBox).BaseType == typeof(ComboBox)
                && typeof(ThemedNumericUpDown).BaseType == typeof(NumericUpDown);
            CheckSelection(checks);
            CheckNumericBehavior(checks);
            foreach (VisualTheme theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                    CheckSettingsFields(checks, theme, scale);
        }

        private static void CheckSelection(Dictionary<string, bool> checks)
        {
            using (var choice = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed })
            {
                choice.Items.AddRange(new object[] { "사이드바", "세션 제목 아래" });
                int changed = 0;
                choice.SelectedIndexChanged += delegate { changed++; };
                choice.SelectedIndex = 0;
                choice.SelectedIndex = 1;
                choice.SelectedIndex = 1;
                checks["themed_combo_selection_retains_native_event_and_text_behavior"] = changed == 2
                    && choice.SelectedIndex == 1 && choice.Text == "세션 제목 아래" && choice.Items.Count == 2;
                choice.SelectedIndex = -1;
                checks["themed_combo_selection_can_be_cleared_without_replacing_native_items"] = changed == 3
                    && choice.SelectedIndex == -1 && choice.SelectedItem == null && choice.Items[0].ToString() == "사이드바";
            }
        }

        private static void CheckNumericBehavior(Dictionary<string, bool> checks)
        {
            using (var number = new ThemedNumericUpDown { Minimum = -4000, Maximum = 4000, Increment = 1, Value = 10 })
            {
                int changed = 0;
                number.ValueChanged += delegate { changed++; };
                number.Text = "35";
                Validate(number);
                int parsed = changed;
                checks["themed_numeric_text_validation_retains_native_value_changed"] = number.Value == 35 && parsed > 0;
                number.UpButton();
                bool up = number.Value == 36 && changed == parsed + 1;
                number.DownButton();
                checks["themed_numeric_native_up_down_buttons_keep_increment_and_events"] = up && number.Value == 35 && changed == parsed + 2;
                number.Value = number.Maximum;
                int atMaximum = changed;
                number.UpButton();
                bool upperBound = number.Value == number.Maximum && changed == atMaximum;
                number.Value = number.Minimum;
                int atMinimum = changed;
                number.DownButton();
                checks["themed_numeric_minimum_maximum_block_extra_steps_and_events"] = upperBound
                    && number.Value == number.Minimum && changed == atMinimum;
                number.Text = "999999";
                Validate(number);
                bool clamped = number.Value == number.Maximum;
                number.Text = "-999999";
                Validate(number);
                checks["themed_numeric_out_of_range_text_uses_native_clamping"] = clamped && number.Value == number.Minimum;
                number.Text = "invalid-number";
                Validate(number);
                checks["themed_numeric_invalid_edit_restores_last_valid_value"] = number.Value == number.Minimum
                    && number.Text.IndexOf("invalid", StringComparison.Ordinal) < 0;
            }
        }

        private static void CheckSettingsFields(Dictionary<string, bool> checks, VisualTheme theme, float scale)
        {
            // The fixture has no worker or file storage. Never show its window or
            // open the native dropdown; bitmap messages target only its controls.
            using (var window = new SettingsWindow(new SettingsPreviewHost(theme), scale))
            {
                window.SelectPreviewPage(1);
                ThemedNumericUpDown first = Field<ThemedNumericUpDown>(window, "x"), second = Field<ThemedNumericUpDown>(window, "y");
                ThemedComboBox choice = Field<ThemedComboBox>(window, "positionChoice");
                var firstFrame = first.Parent as ThemedNumberField;
                var secondFrame = second.Parent as ThemedNumberField;
                string name = "themed_inputs_" + (theme.IsDark ? "dark_" : "light_") + (int)(scale * 100) + "_";
                checks[name + "native_numeric_is_centered_in_scaled_padded_outer_frame"] = FrameFits(first, firstFrame, scale)
                    && FrameFits(second, secondFrame, scale);
                checks[name + "fonts_are_scaled_once_and_native_edit_spinner_stay_inside"] = NativeChildrenFit(first, scale)
                    && NativeChildrenFit(second, scale) && Near(choice.Font.Size, 12 * scale)
                    && choice.Font.Unit == GraphicsUnit.Pixel;
                checks[name + "closed_combo_and_numeric_frames_have_no_native_white_strips"] = NoWhite(choice)
                    && firstFrame != null && secondFrame != null && NoWhite(firstFrame) && NoWhite(secondFrame) && !window.Visible;
            }
        }

        private static bool FrameFits(ThemedNumericUpDown number, ThemedNumberField frame, float scale)
        {
            if (frame == null) return false;
            frame.PerformLayout();
            return frame.ClientRectangle.Contains(number.Bounds) && number.Width > 0 && number.Height == number.PreferredHeight
                && frame.Height == (int)Math.Round(32 * scale) && number.Left >= (int)Math.Round(10 * scale)
                && frame.ClientSize.Width - number.Right >= (int)Math.Round(5 * scale)
                && Math.Abs(number.Top - (frame.Height - number.Height) / 2) <= 1;
        }

        private static bool NativeChildrenFit(ThemedNumericUpDown number, float scale)
        {
            number.PerformLayout();
            bool edit = false, spin = false;
            foreach (Control child in number.Controls)
            {
                if (!number.ClientRectangle.Contains(child.Bounds) || !Near(child.Font.Size, number.Font.Size)) return false;
                if (child is TextBoxBase) edit = child.Width > 0 && child.Height > 0;
                else spin = child.Width >= 12 && child.Height >= 8;
            }
            return edit && spin && number.BorderStyle == BorderStyle.None && Near(number.Font.Size, 12 * scale)
                && number.Font.Unit == GraphicsUnit.Pixel;
        }

        private static bool NoWhite(Control control)
        {
            EnsureOwnHandles(control);
            using (var bitmap = new Bitmap(control.Width, control.Height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.White);
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color color = bitmap.GetPixel(x, y);
                        if (color.R == 255 && color.G == 255 && color.B == 255) return false;
                    }
                return true;
            }
        }

        private static void EnsureOwnHandles(Control control)
        {
            if (control.Handle == IntPtr.Zero) throw new InvalidOperationException("Fixture control handle was not created.");
            foreach (Control child in control.Controls) EnsureOwnHandles(child);
        }

        private static void Validate(NumericUpDown number)
        {
            typeof(NumericUpDown).GetMethod("ValidateEditText", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(number, new object[0]);
        }

        private static T Field<T>(SettingsWindow window, string name) where T : class
        {
            return (T)typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        }

        private static bool Near(float first, float second)
        {
            return Math.Abs(first - second) < 0.01f;
        }
    }
}
