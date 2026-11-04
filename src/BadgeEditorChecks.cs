using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class BadgeEditorChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            CheckSelectionAndMovement(checks);
            CheckHitOrder(checks);
            CheckWidthResize(checks);
            CheckKeyboard(checks);
            CheckReentry(checks);
            CheckSavedCopies(checks);
            CheckCaptureCancellation(checks);
            CheckScaleAndNulls(checks);
        }

        // Only hidden controls owned by this test process receive these calls.
        // No OS input, account host, worker, settings write or app window is used.
        private static BadgeEditor CreateEditor()
        {
            var editor = new BadgeEditor
            {
                Visible = false,
                Size = new Size(470, 250),
                UiScale = 1f,
                Layout = FixtureLayout()
            };
            IntPtr ownHandle = editor.Handle;
            return editor;
        }

        private static BadgeLayoutSettings FixtureLayout()
        {
            var layout = new BadgeLayoutSettings { Width = 320, Height = 120 };
            BadgeElement main = Find(layout, "remaining");
            main.X = 40; main.Y = 10; main.Width = 100;
            BadgeElement updated = Find(layout, "updated");
            updated.X = 10; updated.Y = 70; updated.Width = 100;
            Find(layout, "weekly-reset").Visible = false;
            Find(layout, "countdown").Visible = false;
            Find(layout, "indicator").Visible = false;
            layout.Normalize();
            return layout;
        }

        private static void CheckSelectionAndMovement(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                BadgeLayoutSettings model = editor.Layout;
                BadgeElement main = Find(model, "remaining");
                int selections = 0, changes = 0;
                editor.SelectionChanged += delegate { selections++; };
                editor.LayoutChanged += delegate { changes++; };
                Point start = Inside(editor, main);
                Mouse(editor, "OnMouseDown", start);
                checks["badge_editor_pointer_selects_and_captures_owned_control"] = editor.SelectedKey == "remaining"
                    && selections == 1 && editor.Capture && Object.ReferenceEquals(editor.Layout, model);
                editor.SelectedKey = "remaining";
                Mouse(editor, "OnMouseMove", start);
                checks["badge_editor_identical_selection_and_pointer_are_noops"] = selections == 1 && changes == 0;
                Mouse(editor, "OnMouseMove", Offset(start, 14, 7));
                checks["badge_editor_drag_uses_logical_xy_and_preserves_size"] = main.X == 54 && main.Y == 17
                    && main.Width == 100 && changes == 1;
                Mouse(editor, "OnMouseMove", Offset(start, 14, 7));
                checks["badge_editor_repeated_drag_coordinate_does_not_repeat_change_event"] = changes == 1;
                Mouse(editor, "OnMouseUp", Offset(start, 14, 7));
                checks["badge_editor_mouse_up_releases_capture"] = !editor.Capture;

                start = Inside(editor, main);
                Mouse(editor, "OnMouseDown", start);
                Mouse(editor, "OnMouseMove", new Point(-5000, -5000));
                Mouse(editor, "OnMouseUp", new Point(-5000, -5000));
                checks["badge_editor_drag_outside_top_left_clamps_inside_badge"] = main.X == 0 && main.Y == 0;

                start = Inside(editor, main);
                Mouse(editor, "OnMouseDown", start);
                Mouse(editor, "OnMouseMove", new Point(5000, 5000));
                Mouse(editor, "OnMouseUp", new Point(5000, 5000));
                checks["badge_editor_drag_outside_bottom_right_clamps_inside_badge"] = main.X == model.Width - main.Width
                    && main.Y == model.Height - BadgeLayoutSettings.ElementHeight(main) && !editor.Capture;

                editor.SelectedKey = "unknown";
                int cleared = selections;
                editor.SelectedKey = null;
                editor.SelectedKey = "unknown";
                checks["badge_editor_unknown_selection_clears_once"] = editor.SelectedKey == null
                    && cleared == 2 && selections == cleared;
            }
        }

        private static void CheckHitOrder(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                BadgeLayoutSettings model = editor.Layout;
                BadgeElement main = Find(model, "remaining"), updated = Find(model, "updated");
                updated.X = main.X; updated.Y = main.Y; updated.Width = main.Width;
                BadgeElement hidden = Find(model, "countdown");
                hidden.X = main.X; hidden.Y = main.Y; hidden.Width = main.Width;
                hidden.Visible = false;
                model.Normalize();
                Point overlap = Inside(editor, main);
                Mouse(editor, "OnMouseDown", overlap);
                Mouse(editor, "OnMouseUp", overlap);
                checks["badge_editor_hit_test_prefers_last_visible_element_and_skips_hidden"] = editor.SelectedKey == "updated";
                model.Elements.Remove(updated);
                model.Elements.Insert(0, updated);
                model.Normalize();
                Mouse(editor, "OnMouseDown", overlap);
                Mouse(editor, "OnMouseUp", overlap);
                checks["badge_editor_hit_test_follows_reordered_element_list"] = editor.SelectedKey == "remaining";
                Mouse(editor, "OnMouseDown", new Point(2, 2));
                Mouse(editor, "OnMouseUp", new Point(2, 2));
                checks["badge_editor_empty_canvas_clears_selection_without_capture"] = editor.SelectedKey == null && !editor.Capture;
            }
        }

        private static void CheckWidthResize(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                BadgeElement main = Find(editor.Layout, "remaining");
                editor.SelectedKey = main.Key;
                int changes = 0;
                editor.LayoutChanged += delegate { changes++; };
                Point handle = BottomRight(editor, main);
                Mouse(editor, "OnMouseDown", handle);
                Mouse(editor, "OnMouseMove", Offset(handle, 25, 15));
                Mouse(editor, "OnMouseUp", Offset(handle, 25, 15));
                checks["badge_editor_resize_handle_changes_only_width"] = main.Width == 125 && main.X == 40 && main.Y == 10
                    && changes == 1;
                handle = BottomRight(editor, main);
                Mouse(editor, "OnMouseDown", handle);
                Mouse(editor, "OnMouseMove", new Point(5000, handle.Y));
                Mouse(editor, "OnMouseUp", new Point(5000, handle.Y));
                checks["badge_editor_resize_preserves_left_and_clamps_at_right_edge"] = main.X == 40
                    && main.Width == editor.Layout.Width - main.X;
                handle = BottomRight(editor, main);
                Mouse(editor, "OnMouseDown", handle);
                Mouse(editor, "OnMouseMove", new Point(-5000, handle.Y));
                int atMinimum = changes;
                Mouse(editor, "OnMouseMove", new Point(-5000, handle.Y));
                Mouse(editor, "OnMouseUp", new Point(-5000, handle.Y));
                checks["badge_editor_resize_minimum_is_normalized_and_repeated_edge_is_noop"] = main.Width == 18
                    && main.X == 40 && changes == atMinimum;

                BadgeElement indicator = Find(editor.Layout, "indicator");
                indicator.Visible = true; indicator.X = 180; indicator.Y = 60;
                editor.Layout.Normalize();
                editor.SelectedKey = indicator.Key;
                Point inside = Inside(editor, indicator);
                Mouse(editor, "OnMouseDown", inside);
                Mouse(editor, "OnMouseMove", Offset(inside, 18, 8));
                Mouse(editor, "OnMouseUp", Offset(inside, 18, 8));
                checks["badge_editor_indicator_moves_without_resizing"] = indicator.Width == 18
                    && indicator.X == 198 && indicator.Y == 68;
            }
        }

        private static void CheckKeyboard(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                BadgeElement main = Find(editor.Layout, "remaining");
                editor.SelectedKey = main.Key;
                int changes = 0;
                editor.LayoutChanged += delegate { changes++; };
                KeyEventArgs right = Key(editor, Keys.Right);
                Key(editor, Keys.Shift | Keys.Down);
                checks["badge_editor_arrow_moves_one_and_shift_arrow_moves_ten_dip"] = main.X == 41 && main.Y == 20
                    && changes == 2 && right.Handled && right.SuppressKeyPress;
                Key(editor, Keys.Left);
                Key(editor, Keys.Shift | Keys.Up);
                checks["badge_editor_arrows_preserve_element_size"] = main.X == 40 && main.Y == 10 && main.Width == 100;
                int before = changes;
                Key(editor, Keys.Control | Keys.Right);
                Key(editor, Keys.Alt | Keys.Down);
                checks["badge_editor_modified_shortcuts_are_not_layout_moves"] = changes == before && main.X == 40 && main.Y == 10;
                main.X = 0; main.Y = 0;
                Key(editor, Keys.Left);
                Key(editor, Keys.Up);
                checks["badge_editor_arrow_at_boundary_is_noop"] = main.X == 0 && main.Y == 0 && changes == before;
                editor.SelectedKey = null;
                Key(editor, Keys.Right);
                checks["badge_editor_arrow_without_selection_does_not_edit"] = changes == before;
                checks["badge_editor_arrow_is_input_key_and_tab_keeps_normal_focus_navigation"] = (bool)Invoke(editor, "IsInputKey", Keys.Right)
                    && !(bool)Invoke(editor, "IsInputKey", Keys.Tab);
            }
        }

        private static void CheckReentry(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                BadgeLayoutSettings original = editor.Layout, replacement = FixtureLayout();
                Find(replacement, "remaining").X = 70;
                int changes = 0, selections = 0;
                editor.LayoutChanged += delegate { changes++; };
                editor.SelectionChanged += delegate
                {
                    selections++;
                    editor.Layout = replacement;
                };
                Point start = Inside(editor, Find(original, "remaining"));
                Mouse(editor, "OnMouseDown", start);
                Mouse(editor, "OnMouseMove", Offset(start, 30, 20));
                Mouse(editor, "OnMouseUp", Offset(start, 30, 20));
                checks["badge_editor_selection_handler_model_replacement_cancels_stale_drag"] = Object.ReferenceEquals(editor.Layout, replacement)
                    && Find(original, "remaining").X == 40 && Find(replacement, "remaining").X == 70
                    && selections == 1 && changes == 0 && !editor.Capture;
            }

            using (var editor = CreateEditor())
            {
                BadgeLayoutSettings original = editor.Layout, replacement = FixtureLayout();
                editor.SelectedKey = "remaining";
                editor.LayoutChanged += delegate { editor.Layout = replacement; };
                Point start = Inside(editor, Find(original, "remaining"));
                Mouse(editor, "OnMouseDown", start);
                Mouse(editor, "OnMouseMove", Offset(start, 5, 4));
                Mouse(editor, "OnMouseMove", Offset(start, 30, 20));
                Mouse(editor, "OnMouseUp", Offset(start, 30, 20));
                checks["badge_editor_layout_handler_replacement_stops_further_drag_without_touching_new_model"] = Object.ReferenceEquals(editor.Layout, replacement)
                    && Find(original, "remaining").X == 45 && Find(replacement, "remaining").X == 40 && !editor.Capture;
            }
        }

        private static void CheckSavedCopies(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                BadgeLayoutSettings live = editor.Layout, saved = null;
                int changes = 0;
                editor.LayoutChanged += delegate { changes++; saved = editor.Layout.Copy(); };
                Point start = Inside(editor, Find(live, "remaining"));
                Mouse(editor, "OnMouseDown", start);
                Mouse(editor, "OnMouseMove", Offset(start, 4, 2));
                bool keptCapture = editor.Capture;
                Mouse(editor, "OnMouseMove", Offset(start, 9, 5));
                Mouse(editor, "OnMouseUp", Offset(start, 9, 5));
                checks["badge_editor_copy_saving_handler_keeps_drag_model_and_continues_movement"] = keptCapture
                    && Object.ReferenceEquals(editor.Layout, live) && changes == 2
                    && Find(live, "remaining").X == 49 && Find(live, "remaining").Y == 15;
                checks["badge_editor_event_saved_copy_is_independent_and_matches_final_layout"] = saved != null
                    && !Object.ReferenceEquals(saved, live) && !Object.ReferenceEquals(Find(saved, "remaining"), Find(live, "remaining"))
                    && Find(saved, "remaining").X == 49 && Find(saved, "remaining").Y == 15;
            }
        }

        private static void CheckCaptureCancellation(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                BadgeElement main = Find(editor.Layout, "remaining");
                Point start = Inside(editor, main);
                Mouse(editor, "OnMouseDown", start);
                Invoke(editor, "OnLostFocus", EventArgs.Empty);
                Mouse(editor, "OnMouseMove", Offset(start, 20, 10));
                checks["badge_editor_focus_loss_releases_capture_and_stops_drag"] = !editor.Capture && main.X == 40 && main.Y == 10;

                start = Inside(editor, main);
                Mouse(editor, "OnMouseDown", start);
                editor.Size = new Size(480, 260);
                Mouse(editor, "OnMouseMove", Offset(start, 20, 10));
                checks["badge_editor_canvas_resize_cancels_drag_without_changing_badge_layout"] = !editor.Capture
                    && main.X == 40 && main.Y == 10 && editor.Layout.Width == 320 && editor.Layout.Height == 120;

                start = Inside(editor, main);
                Mouse(editor, "OnMouseDown", start);
                editor.Enabled = false;
                Mouse(editor, "OnMouseMove", Offset(start, 20, 10));
                checks["badge_editor_disabling_releases_capture_and_stops_drag"] = !editor.Capture && main.X == 40 && main.Y == 10;
            }
        }

        private static void CheckScaleAndNulls(Dictionary<string, bool> checks)
        {
            using (var editor = CreateEditor())
            {
                editor.UiScale = 1.5f;
                editor.Size = new Size(705, 375);
                checks["badge_editor_explicit_ui_dpi_controls_full_size_render_scale"] = Near(editor.RenderScale, 1.5f);
                BadgeElement main = Find(editor.Layout, "remaining");
                Point start = Inside(editor, main);
                Mouse(editor, "OnMouseDown", start);
                Mouse(editor, "OnMouseMove", Offset(start, 15, 15));
                Mouse(editor, "OnMouseUp", Offset(start, 15, 15));
                checks["badge_editor_scaled_pointer_delta_is_converted_back_to_dip"] = main.X == 50 && main.Y == 20;
                editor.UiScale = 1f;
                editor.Size = new Size(180, 90);
                checks["badge_editor_fit_to_view_keeps_badge_inside_padded_small_canvas"] = editor.RenderScale > 0f
                    && editor.RenderScale < 1f && Bounds(editor).Left >= 16 && Bounds(editor).Top >= 16
                    && Bounds(editor).Right <= editor.ClientSize.Width - 16 && Bounds(editor).Bottom <= editor.ClientSize.Height - 16;
                editor.FitToView = false;
                checks["badge_editor_non_fit_mode_keeps_logical_badge_size"] = Near(editor.RenderScale, 1f)
                    && editor.Layout.Width == 320 && editor.Layout.Height == 120;
                editor.FitToView = true;
                editor.Size = new Size(20, 20);
                checks["badge_editor_canvas_without_padding_space_has_no_render_area"] = editor.RenderScale == 0f && Bounds(editor).IsEmpty;

                editor.Layout = null;
                editor.Theme = null;
                editor.Presentation = null;
                checks["badge_editor_null_inputs_are_safe_and_use_default_layout_theme"] = editor.Layout != null
                    && editor.Layout.Width == 320 && editor.Layout.Height == 82 && editor.Theme == VisualTheme.Dark
                    && editor.Presentation == null;
                editor.UiScale = Single.NaN;
                bool nanAuto = editor.UiScale == 0f;
                editor.UiScale = Single.PositiveInfinity;
                bool infinityAuto = editor.UiScale == 0f;
                editor.UiScale = -1f;
                checks["badge_editor_invalid_explicit_ui_scales_fall_back_to_auto"] = nanAuto && infinityAuto && editor.UiScale == 0f;
            }
        }

        private static BadgeElement Find(BadgeLayoutSettings layout, string key)
        {
            return layout.Elements.Find(element => element.Key == key);
        }

        private static Point Inside(BadgeEditor editor, BadgeElement element)
        {
            Rectangle bounds = Bounds(editor);
            float scale = editor.RenderScale;
            return new Point((int)Math.Round(bounds.Left + (element.X + Math.Min(8, element.Width / 2f)) * scale),
                (int)Math.Round(bounds.Top + (element.Y + 5) * scale));
        }

        private static Point BottomRight(BadgeEditor editor, BadgeElement element)
        {
            Rectangle bounds = Bounds(editor);
            RectangleF logical = BadgeRenderer.ElementBounds(element);
            return new Point((int)Math.Round(bounds.Left + logical.Right * editor.RenderScale),
                (int)Math.Round(bounds.Top + logical.Bottom * editor.RenderScale));
        }

        private static Rectangle Bounds(BadgeEditor editor)
        {
            object[] args = { 0f };
            return (Rectangle)typeof(BadgeEditor).GetMethod("GetBadgeBounds", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, args);
        }

        private static Point Offset(Point point, int x, int y)
        {
            return new Point(point.X + x, point.Y + y);
        }

        private static void Mouse(BadgeEditor editor, string method, Point point)
        {
            Invoke(editor, method, new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        }

        private static KeyEventArgs Key(BadgeEditor editor, Keys keys)
        {
            var args = new KeyEventArgs(keys);
            Invoke(editor, "OnKeyDown", args);
            return args;
        }

        private static object Invoke(BadgeEditor editor, string method, object argument)
        {
            return typeof(BadgeEditor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(editor, new[] { argument });
        }

        private static bool Near(float actual, float expected)
        {
            return Math.Abs(actual - expected) < 0.001f;
        }
    }
}
