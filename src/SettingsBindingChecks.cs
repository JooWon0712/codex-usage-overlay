using System;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class SettingsBindingChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            CheckInMemorySettingsBindings(checks);
            CheckJsonRoundTrip(checks);
            CheckSidebarMigration(checks);
        }

        private static void CheckInMemorySettingsBindings(Dictionary<string, bool> checks)
        {
            // SettingsPreviewHost only holds an in-memory fixture. Constructing
            // the preview form does not show it or create an OverlayContext.
            var host = new SettingsPreviewHost(VisualTheme.Dark);
            host.Prefs.TitleOffsetX = 12;
            host.Prefs.TitleGap = 10;
            host.Prefs.SidebarX = 112;
            host.Prefs.SidebarY = 98;
            host.Prefs.SidebarBottomGap = null;
            host.Prefs.NormalizePosition();
            using (var window = new SettingsWindow(host, 1f))
            {
                BadgeEditor editor = Field<BadgeEditor>(window, "editor");
                ComboBox elements = Field<ComboBox>(window, "elementChoice");
                NumericUpDown x = Field<NumericUpDown>(window, "elementX");
                NumericUpDown y = Field<NumericUpDown>(window, "elementY");
                NumericUpDown width = Field<NumericUpDown>(window, "elementWidth");
                NumericUpDown font = Field<NumericUpDown>(window, "elementFont");
                CheckBox visible = Field<CheckBox>(window, "visibleChoice");
                checks["settings_binding_fixture_stays_hidden_and_uses_an_independent_edit_model"] = !window.Visible
                    && host.IsSample && !Object.ReferenceEquals(editor.Layout, host.Prefs.BadgeLayout);

                editor.SelectedKey = null;
                checks["settings_binding_blank_selection_clears_choice_and_disables_element_numbers"] = elements.SelectedIndex == -1
                    && !x.Enabled && !y.Enabled && !width.Enabled && !font.Enabled;
                elements.SelectedIndex = 0;
                checks["settings_binding_choice_reselects_remaining_after_blank_canvas"] = editor.SelectedKey == "remaining"
                    && x.Enabled && y.Enabled && width.Enabled && font.Enabled;
                x.Value = 35;
                checks["settings_binding_number_edits_update_editor_and_in_memory_saved_copy"] = Find(editor.Layout, "remaining").X == 35
                    && Find(host.Prefs.BadgeLayout, "remaining").X == 35
                    && !Object.ReferenceEquals(Find(editor.Layout, "remaining"), Find(host.Prefs.BadgeLayout, "remaining"));

                elements.SelectedIndex = 3;
                bool optionalEnabled = visible.Enabled;
                visible.Checked = false;
                checks["settings_binding_optional_visibility_updates_editor_and_saved_copy"] = optionalEnabled
                    && editor.SelectedKey == "countdown" && !Find(editor.Layout, "countdown").Visible
                    && !Find(host.Prefs.BadgeLayout, "countdown").Visible;

                elements.SelectedIndex = 0;
                bool remainingDisabled = !visible.Enabled;
                visible.Checked = false;
                checks["settings_binding_remaining_status_cannot_be_hidden_even_by_programmatic_change"] = remainingDisabled
                    && visible.Checked && Find(editor.Layout, "remaining").Visible && Find(host.Prefs.BadgeLayout, "remaining").Visible;
                elements.SelectedIndex = 1;
                bool updatedDisabled = !visible.Enabled;
                visible.Checked = false;
                checks["settings_binding_updated_status_cannot_be_hidden_even_by_programmatic_change"] = updatedDisabled
                    && visible.Checked && Find(editor.Layout, "updated").Visible && Find(host.Prefs.BadgeLayout, "updated").Visible;

                elements.SelectedIndex = 0;
                width.Value = 160;
                font.Value = 15.5m;
                y.Value = 10;
                Invoke(window, "ChangeZOrder", true);
                checks["settings_binding_width_font_and_order_edits_are_saved_together"] = Find(host.Prefs.BadgeLayout, "remaining").X == 35
                    && Find(host.Prefs.BadgeLayout, "remaining").Y == 10
                    && Find(host.Prefs.BadgeLayout, "remaining").Width == 160
                    && Find(host.Prefs.BadgeLayout, "remaining").FontSize == 15.5f
                    && host.Prefs.BadgeLayout.Elements[host.Prefs.BadgeLayout.Elements.Count - 1].Key == "remaining";

                var json = new JavaScriptSerializer();
                string priorLayout = json.Serialize(host.Prefs.BadgeLayout);
                ComboBox position = Field<ComboBox>(window, "positionChoice");
                NumericUpDown anchorX = Field<NumericUpDown>(window, "x");
                NumericUpDown anchorY = Field<NumericUpDown>(window, "y");
                Label horizontal = Field<Label>(window, "horizontal");
                Label vertical = Field<Label>(window, "vertical");
                bool beganBelowTitle = host.Prefs.Position == Preferences.BelowTitle && position.SelectedIndex == 1
                    && anchorX.Value == 12 && anchorY.Value == 10;
                position.SelectedIndex = 0;
                checks["settings_binding_sidebar_choice_updates_mode_coordinate_values_and_labels"] = beganBelowTitle
                    && host.Prefs.Position == "sidebar" && position.SelectedIndex == 0
                    && anchorX.Value == 112 && anchorY.Value == 52
                    && horizontal.Text.Contains("창 왼쪽") && vertical.Text.Contains("창 아래");
                checks["settings_binding_position_change_preserves_edited_layout_and_legacy_coordinates"] = json.Serialize(host.Prefs.BadgeLayout) == priorLayout
                    && json.Serialize(editor.Layout) == priorLayout && host.Prefs.SidebarY == 98 && host.Prefs.SidebarBottomGap == 52
                    && host.Prefs.TitleOffsetX == 12 && host.Prefs.TitleGap == 10;
                position.SelectedIndex = 1;
                checks["settings_binding_return_to_title_restores_title_coordinates_and_preserves_layout"] = host.Prefs.Position == Preferences.BelowTitle
                    && anchorX.Value == 12 && anchorY.Value == 10 && horizontal.Text.Contains("제목") && vertical.Text.Contains("제목")
                    && json.Serialize(editor.Layout) == priorLayout && !window.Visible;
            }
        }

        private static void CheckJsonRoundTrip(Dictionary<string, bool> checks)
        {
            var prefs = new Preferences
            {
                Position = "sidebar", ThemeMode = "light", SidebarX = 111, SidebarY = 98,
                SidebarBottomGap = 52, TitleOffsetX = -15, TitleGap = 12,
                BadgeLayout = new BadgeLayoutSettings { Width = 420, Height = 160 }
            };
            BadgeElement remaining = Find(prefs.BadgeLayout, "remaining");
            remaining.X = 37; remaining.Y = 11; remaining.Width = 210; remaining.FontSize = 17.5f; remaining.Bold = false;
            BadgeElement updated = Find(prefs.BadgeLayout, "updated");
            updated.X = 23; updated.Y = 52; updated.Width = 250; updated.FontSize = 10f; updated.Bold = true;
            BadgeElement reset = Find(prefs.BadgeLayout, "weekly-reset");
            reset.X = 18; reset.Y = 104; reset.Width = 300; reset.FontSize = 12.5f;
            BadgeElement countdown = Find(prefs.BadgeLayout, "countdown");
            countdown.Visible = false;
            BadgeElement indicator = Find(prefs.BadgeLayout, "indicator");
            indicator.X = 8; indicator.Y = 12;
            prefs.BadgeLayout.Elements = new List<BadgeElement> { reset, indicator, countdown, updated, remaining };
            prefs.NormalizePosition();
            var serializer = new JavaScriptSerializer();
            Preferences restored = serializer.Deserialize<Preferences>(serializer.Serialize(prefs));
            restored.NormalizePosition();
            checks["settings_json_roundtrip_preserves_position_theme_and_both_position_coordinates"] = restored.Position == "sidebar"
                && restored.ThemeMode == "light" && restored.SidebarX == 111 && restored.SidebarY == 98 && restored.SidebarBottomGap == 52
                && restored.TitleOffsetX == -15 && restored.TitleGap == 12;
            checks["settings_json_roundtrip_preserves_badge_canvas_coordinates_width_font_bold_and_visibility"] = LayoutsEqual(prefs.BadgeLayout, restored.BadgeLayout)
                && !Find(restored.BadgeLayout, "countdown").Visible && Find(restored.BadgeLayout, "remaining").Visible
                && Find(restored.BadgeLayout, "updated").Visible;
            checks["settings_json_roundtrip_preserves_element_z_order"] = restored.BadgeLayout.Elements.Count == 5
                && restored.BadgeLayout.Elements[0].Key == "weekly-reset" && restored.BadgeLayout.Elements[1].Key == "indicator"
                && restored.BadgeLayout.Elements[2].Key == "countdown" && restored.BadgeLayout.Elements[3].Key == "updated"
                && restored.BadgeLayout.Elements[4].Key == "remaining";
            restored.SidebarY = 500;
            restored.NormalizePosition();
            checks["settings_json_explicit_bottom_gap_survives_later_normalization"] = restored.SidebarBottomGap == 52 && restored.SidebarY == 500;
        }

        private static void CheckSidebarMigration(Dictionary<string, bool> checks)
        {
            var serializer = new JavaScriptSerializer();
            var legacy = serializer.Deserialize<Preferences>("{\"Position\":\"sidebar\",\"SidebarX\":112,\"SidebarY\":98}");
            legacy.NormalizePosition();
            checks["settings_legacy_sidebar_offset_migrates_to_bottom_gap_without_rewriting_old_coordinate"] = legacy.Position == "sidebar"
                && legacy.SidebarX == 112 && legacy.SidebarY == 98 && legacy.SidebarBottomGap == 52;
            var minimum = serializer.Deserialize<Preferences>("{\"Position\":\"sidebar\",\"SidebarY\":-2147483648}");
            minimum.NormalizePosition();
            checks["settings_legacy_minimum_sidebar_coordinate_migrates_without_overflow"] = minimum.SidebarY == Int32.MinValue
                && minimum.SidebarBottomGap == 0;
            var maximum = serializer.Deserialize<Preferences>("{\"Position\":\"sidebar\",\"SidebarY\":2147483647}");
            maximum.NormalizePosition();
            checks["settings_legacy_maximum_sidebar_coordinate_clamps_gap_without_overflow"] = maximum.SidebarY == Int32.MaxValue
                && maximum.SidebarBottomGap == 3000;
            var explicitGap = serializer.Deserialize<Preferences>("{\"Position\":\"sidebar\",\"SidebarY\":98,\"SidebarBottomGap\":21}");
            explicitGap.NormalizePosition();
            explicitGap.SidebarY = Int32.MinValue;
            explicitGap.NormalizePosition();
            checks["settings_new_bottom_gap_is_not_remigrated_from_legacy_y"] = explicitGap.SidebarBottomGap == 21
                && explicitGap.SidebarY == Int32.MinValue;
        }

        private static BadgeElement Find(BadgeLayoutSettings layout, string key)
        {
            return layout.Elements.Find(element => element.Key == key);
        }

        private static bool LayoutsEqual(BadgeLayoutSettings first, BadgeLayoutSettings second)
        {
            if (first.Width != second.Width || first.Height != second.Height || first.Elements.Count != second.Elements.Count)
                return false;
            for (int i = 0; i < first.Elements.Count; i++)
            {
                BadgeElement a = first.Elements[i], b = second.Elements[i];
                if (a.Key != b.Key || a.X != b.X || a.Y != b.Y || a.Width != b.Width || a.FontSize != b.FontSize
                    || a.Bold != b.Bold || a.Visible != b.Visible)
                    return false;
            }
            return true;
        }

        private static T Field<T>(SettingsWindow window, string name) where T : class
        {
            return (T)typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        }

        private static void Invoke(SettingsWindow window, string method, object argument)
        {
            typeof(SettingsWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new[] { argument });
        }
    }
}
