using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class ThemedPaintChecks
    {
        private static readonly Color ParentBackground = Color.FromArgb(41, 59, 73);

        public static void Run(Dictionary<string, bool> checks)
        {
            using (var button = new ThemedButton())
            using (var choice = new ThemedCheckBox())
            {
                checks["themed_paint_target_framework_button_and_checkbox_are_opaque"] = Style(button, ControlStyles.Opaque)
                    && Style(choice, ControlStyles.Opaque) && !button.IsHandleCreated && !choice.IsHandleCreated;
                checks["themed_paint_controls_preserve_owned_paint_and_buffer_styles"] = PaintStyles(button) && PaintStyles(choice);
            }
            foreach (VisualTheme theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                {
                    string name = "themed_paint_" + (theme.IsDark ? "dark_" : "light_") + (int)(scale * 100) + "_";
                    bool buttons = true, choices = true;
                    foreach (bool enabled in new[] { true, false })
                        foreach (bool state in new[] { true, false })
                        {
                            buttons &= ButtonPaintsWholeClient(theme, scale, enabled, state);
                            choices &= CheckBoxPaintsLabelBackground(theme, scale, enabled, state);
                        }
                    checks[name + "button_corners_are_painted_without_background_callback"] = buttons;
                    checks[name + "checkbox_label_has_no_unpainted_black_buffer"] = choices;
                    checks[name + "selected_button_has_hover_fill_and_preserves_button_properties"] = SelectedButton(theme, scale);
                    checks[name + "partial_paint_preserves_clipping_for_background_and_text"] = ClipIsPreserved(theme, scale, false)
                        && ClipIsPreserved(theme, scale, true);
                    checks[name + "translated_paint_keeps_text_inside_its_control"] = TranslationIsPreserved(theme, scale, false)
                        && TranslationIsPreserved(theme, scale, true);
                    checks[name + "unparented_controls_use_theme_background"] = UnparentedBackground(theme, scale);
                }
        }

        private static bool ButtonPaintsWholeClient(VisualTheme theme, float scale, bool enabled, bool selected)
        {
            using (var font = FontFor(scale))
            using (var parent = new Panel { BackColor = ParentBackground, Visible = false })
            using (var button = new ThemedButton
            {
                Theme = theme, ScaleFactor = scale, Enabled = enabled, Selected = selected,
                Text = "한도 표시", Font = font, Size = SizeFor(180, 32, scale)
            })
            {
                parent.Controls.Add(button);
                using (Bitmap bitmap = PaintOnly(button, scale, null, Point.Empty))
                    return CornersEqual(bitmap, ParentBackground) && NoBlack(bitmap, button.ClientRectangle)
                        && !button.IsHandleCreated && !parent.IsHandleCreated;
            }
        }

        private static bool CheckBoxPaintsLabelBackground(VisualTheme theme, float scale, bool enabled, bool check)
        {
            using (var font = FontFor(scale))
            using (var parent = new Panel { BackColor = ParentBackground, Visible = false })
            using (var choice = new ThemedCheckBox
            {
                Theme = theme, ScaleFactor = scale, Enabled = enabled, Checked = check,
                Text = "상태 아이콘 표시", Font = font, Size = SizeFor(240, 26, scale)
            })
            {
                parent.Controls.Add(choice);
                using (Bitmap bitmap = PaintOnly(choice, scale, null, Point.Empty))
                {
                    // High contrast may legitimately use a black tick. Inspect
                    // the label area separately from the checkbox glyph.
                    int labelLeft = (int)Math.Ceiling(20 * scale);
                    var label = new Rectangle(labelLeft, 0, choice.Width - labelLeft, choice.Height);
                    return CornersEqual(bitmap, ParentBackground) && NoBlack(bitmap, label)
                        && !choice.IsHandleCreated && !parent.IsHandleCreated;
                }
            }
        }

        private static bool SelectedButton(VisualTheme theme, float scale)
        {
            using (var parent = new Panel { BackColor = ParentBackground, Visible = false })
            using (var button = new ThemedButton
            {
                Theme = theme, ScaleFactor = scale, Size = SizeFor(180, 32, scale),
                Text = "", DialogResult = DialogResult.Cancel, TabStop = true, UseMnemonic = false
            })
            {
                parent.Controls.Add(button);
                int x = button.Width / 2, y = button.Height - (int)Math.Ceiling(5 * scale);
                using (Bitmap normal = PaintOnly(button, scale, null, Point.Empty))
                {
                    button.Selected = true;
                    using (Bitmap selected = PaintOnly(button, scale, null, Point.Empty))
                    {
                        bool fill = Same(normal.GetPixel(x, y), theme.Surface) && Same(selected.GetPixel(x, y), theme.Hover);
                        bool properties = button.Selected && button.Enabled && button.TabStop && !button.UseMnemonic
                            && button.DialogResult == DialogResult.Cancel;
                        button.Selected = false;
                        return fill && properties && !button.Selected && !button.IsHandleCreated;
                    }
                }
            }
        }

        private static bool ClipIsPreserved(VisualTheme theme, float scale, bool checkbox)
        {
            using (var font = FontFor(scale))
            using (var parent = new Panel { BackColor = ParentBackground, Visible = false })
            using (Control control = PaintControl(theme, scale, checkbox, font))
            {
                parent.Controls.Add(control);
                int left = checkbox ? (int)Math.Round(30 * scale) : control.Width / 2 - (int)Math.Round(10 * scale);
                var clip = new Rectangle(left, 0, Math.Max(1, (int)Math.Round(20 * scale)), control.Height);
                using (Bitmap bitmap = PaintOnly(control, scale, clip, Point.Empty))
                    return OutsideIsBlack(bitmap, clip) && NoBlack(bitmap, clip) && !control.IsHandleCreated;
            }
        }

        private static bool TranslationIsPreserved(VisualTheme theme, float scale, bool checkbox)
        {
            using (var font = FontFor(scale))
            using (var parent = new Panel { BackColor = ParentBackground, Visible = false })
            using (Control control = PaintControl(theme, scale, checkbox, font))
            {
                parent.Controls.Add(control);
                var offset = new Point(control.Width + 4, 6);
                var translated = new Rectangle(offset, control.Size);
                using (Bitmap text = PaintOnly(control, scale, control.ClientRectangle, offset))
                {
                    control.Text = "";
                    using (Bitmap empty = PaintOnly(control, scale, control.ClientRectangle, offset))
                        return OutsideIsBlack(text, translated) && TextChangedPixels(text, empty, translated)
                            && !control.IsHandleCreated;
                }
            }
        }

        private static bool UnparentedBackground(VisualTheme theme, float scale)
        {
            using (var font = FontFor(scale))
            using (Control button = PaintControl(theme, scale, false, font))
            using (Control choice = PaintControl(theme, scale, true, font))
            using (Bitmap first = PaintOnly(button, scale, null, Point.Empty))
            using (Bitmap second = PaintOnly(choice, scale, null, Point.Empty))
                return CornersEqual(first, theme.Background) && CornersEqual(second, theme.Background)
                    && !button.IsHandleCreated && !choice.IsHandleCreated;
        }

        private static Control PaintControl(VisualTheme theme, float scale, bool checkbox, Font font)
        {
            if (checkbox)
                return new ThemedCheckBox { Theme = theme, ScaleFactor = scale, Font = font, Text = "상태 아이콘 표시", Size = SizeFor(180, 32, scale) };
            return new ThemedButton { Theme = theme, ScaleFactor = scale, Font = font, Text = "한도 표시", Size = SizeFor(180, 32, scale) };
        }

        private static Bitmap PaintOnly(Control control, float scale, Rectangle? clip, Point offset)
        {
            var bitmap = new Bitmap(control.Width + offset.X + 4, control.Height + offset.Y + 4, PixelFormat.Format32bppArgb);
            bitmap.SetResolution(96 * scale, 96 * scale);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Black);
                    graphics.TranslateTransform(offset.X, offset.Y);
                    if (clip.HasValue) graphics.SetClip(clip.Value);
                    using (var args = new PaintEventArgs(graphics, control.ClientRectangle))
                        control.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(control, new object[] { args });
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static Font FontFor(float scale)
        {
            return new Font("Segoe UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private static Size SizeFor(int width, int height, float scale)
        {
            return new Size((int)Math.Round(width * scale), (int)Math.Round(height * scale));
        }

        private static bool CornersEqual(Bitmap bitmap, Color expected)
        {
            // PaintOnly leaves a four-pixel black guard outside the control.
            int right = bitmap.Width - 5, bottom = bitmap.Height - 5;
            return Same(bitmap.GetPixel(0, 0), expected) && Same(bitmap.GetPixel(right, 0), expected)
                && Same(bitmap.GetPixel(0, bottom), expected) && Same(bitmap.GetPixel(right, bottom), expected);
        }

        private static bool NoBlack(Bitmap bitmap, Rectangle area)
        {
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (Same(bitmap.GetPixel(x, y), Color.Black)) return false;
            return true;
        }

        private static bool OutsideIsBlack(Bitmap bitmap, Rectangle area)
        {
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (!area.Contains(x, y) && !Same(bitmap.GetPixel(x, y), Color.Black)) return false;
            return true;
        }

        private static bool TextChangedPixels(Bitmap first, Bitmap second, Rectangle area)
        {
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (!Same(first.GetPixel(x, y), second.GetPixel(x, y))) return true;
            return false;
        }

        private static bool Same(Color first, Color second)
        {
            return first.R == second.R && first.G == second.G && first.B == second.B;
        }

        private static bool Style(Control control, ControlStyles style)
        {
            return (bool)typeof(Control).GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { style });
        }

        private static bool PaintStyles(Control control)
        {
            return Style(control, ControlStyles.UserPaint) && Style(control, ControlStyles.AllPaintingInWmPaint)
                && Style(control, ControlStyles.OptimizedDoubleBuffer);
        }
    }
}
