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
            BadgeLayoutSettings originalPreferences = host.Prefs.BadgeLayout;
            using (var window = new SettingsWindow(host, 1f))
            {
                BadgePreview preview = Field<BadgePreview>(window, "preview");
                CheckBox indicator = Field<CheckBox>(window, "indicatorChoice");
                CheckBox reset = Field<CheckBox>(window, "resetChoice");
                CheckBox countdown = Field<CheckBox>(window, "countdownChoice");
                checks["settings_binding_fixture_stays_hidden_and_previews_only_its_fixture_state"] = !window.Visible && host.IsSample
                    && Object.ReferenceEquals(preview.Presentation, host.Presentation)
                    && !Object.ReferenceEquals(preview.LayoutSettings, host.Prefs.BadgeLayout);
                BadgeLayoutSettings beforeLoad = host.Prefs.BadgeLayout;
                window.LoadPreferences();
                checks["settings_binding_loading_choices_does_not_replace_saved_preferences"] = Object.ReferenceEquals(originalPreferences, beforeLoad)
                    && Object.ReferenceEquals(beforeLoad, host.Prefs.BadgeLayout)
                    && indicator.Checked && reset.Checked && countdown.Checked;

                indicator.Checked = false;
                checks["settings_binding_indicator_off_updates_preview_and_saved_choice"] = OptionsMatch(preview, host, false, true, true);
                indicator.Checked = true;
                checks["settings_binding_indicator_on_updates_preview_and_saved_choice"] = OptionsMatch(preview, host, true, true, true);
                reset.Checked = false;
                checks["settings_binding_reset_date_off_keeps_inline_countdown_at_compact_height"] = OptionsMatch(preview, host, true, false, true)
                    && preview.LayoutSettings.Height == 58 && host.Prefs.BadgeLayout.Height == 58
                    && Find(preview.LayoutSettings, "countdown").Y == 6;
                reset.Checked = true;
                checks["settings_binding_reset_date_on_updates_preview_and_saved_choice"] = OptionsMatch(preview, host, true, true, true);
                countdown.Checked = false;
                checks["settings_binding_countdown_off_keeps_reset_date_and_full_height"] = OptionsMatch(preview, host, true, true, false)
                    && preview.LayoutSettings.Height == 82;
                countdown.Checked = true;
                checks["settings_binding_countdown_on_updates_preview_and_saved_choice"] = OptionsMatch(preview, host, true, true, true);
                indicator.Checked = reset.Checked = countdown.Checked = false;
                checks["settings_binding_all_optional_fields_off_use_compact_height"] = OptionsMatch(preview, host, false, false, false)
                    && preview.LayoutSettings.Height == 58 && host.Prefs.BadgeLayout.Height == 58;
                indicator.Checked = true;
                checks["settings_binding_indicator_alone_keeps_compact_height"] = OptionsMatch(preview, host, true, false, false)
                    && preview.LayoutSettings.Height == 58;
                reset.Checked = true;
                checks["settings_binding_any_reset_date_restores_full_height"] = OptionsMatch(preview, host, true, true, false)
                    && preview.LayoutSettings.Height == 82;
                reset.Checked = false;
                countdown.Checked = true;
                checks["settings_binding_countdown_alone_keeps_compact_height"] = OptionsMatch(preview, host, true, false, true)
                    && preview.LayoutSettings.Height == 58 && host.Prefs.BadgeLayout.Height == 58;
                Find(preview.LayoutSettings, "remaining").Visible = false;
                Find(preview.LayoutSettings, "updated").Visible = false;
                indicator.Checked = false;
                checks["settings_binding_required_limit_and_status_are_restored_by_option_save"] = OptionsMatch(preview, host, false, false, true)
                    && RequiredVisible(preview.LayoutSettings) && RequiredVisible(host.Prefs.BadgeLayout);
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
                checks["settings_binding_position_change_preserves_optional_choices_and_legacy_coordinates"] = json.Serialize(host.Prefs.BadgeLayout) == priorLayout
                    && json.Serialize(preview.LayoutSettings) == priorLayout && host.Prefs.SidebarY == 98 && host.Prefs.SidebarBottomGap == 52
                    && !indicator.Checked && !reset.Checked && countdown.Checked
                    && host.Prefs.TitleOffsetX == 12 && host.Prefs.TitleGap == 10;
                position.SelectedIndex = 1;
                checks["settings_binding_return_to_title_restores_title_coordinates_and_preserves_options"] = host.Prefs.Position == Preferences.BelowTitle
                    && anchorX.Value == 12 && anchorY.Value == 10 && horizontal.Text.Contains("제목") && vertical.Text.Contains("제목")
                    && json.Serialize(preview.LayoutSettings) == priorLayout && !window.Visible;
                ComboBox themes = Field<ComboBox>(window, "themeChoice");
                themes.SelectedIndex = 2;
                window.LoadPreferences();
                checks["settings_binding_theme_choice_and_reload_preserve_optional_choices"] = host.Prefs.ThemeMode == "light"
                    && themes.SelectedIndex == 2 && OptionsMatch(preview, host, false, false, true)
                    && json.Serialize(host.Prefs.BadgeLayout) == priorLayout;
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
            remaining.X = 37; remaining.Y = 11; remaining.Width = 210; remaining.FontSize = 17.5f; remaining.Bold = false; remaining.Visible = false;
            BadgeElement updated = Find(prefs.BadgeLayout, "updated");
            updated.X = 23; updated.Y = 52; updated.Width = 250; updated.FontSize = 10f; updated.Bold = true; updated.Visible = false;
            BadgeElement reset = Find(prefs.BadgeLayout, "weekly-reset");
            reset.X = 18; reset.Y = 104; reset.Width = 300; reset.FontSize = 12.5f;
            BadgeElement countdown = Find(prefs.BadgeLayout, "countdown");
            countdown.X = 232; countdown.Y = 53; countdown.Width = 78; countdown.Visible = false;
            BadgeElement indicator = Find(prefs.BadgeLayout, "indicator");
            indicator.X = 8; indicator.Y = 12; indicator.Visible = false;
            prefs.BadgeLayout.Elements = new List<BadgeElement> { reset, indicator, countdown, updated, remaining };
            var serializer = new JavaScriptSerializer();
            Preferences restored = serializer.Deserialize<Preferences>(serializer.Serialize(prefs));
            restored.NormalizePosition();
            checks["settings_json_roundtrip_preserves_position_theme_and_both_position_coordinates"] = restored.Position == "sidebar"
                && restored.ThemeMode == "light" && restored.SidebarX == 111 && restored.SidebarY == 98 && restored.SidebarBottomGap == 52
                && restored.TitleOffsetX == -15 && restored.TitleGap == 12;
            BadgeElement fixedRemaining = Find(restored.BadgeLayout, "remaining"), fixedUpdated = Find(restored.BadgeLayout, "updated");
            checks["settings_json_old_drag_geometry_and_style_migrate_to_fixed_layout"] = restored.BadgeLayout.Width == 320 && restored.BadgeLayout.Height == 82
                && fixedRemaining.X == 10 && fixedRemaining.Y == 6 && fixedRemaining.Width == 300 && fixedRemaining.FontSize == 14f && fixedRemaining.Bold
                && fixedUpdated.X == 10 && fixedUpdated.Y == 31 && fixedUpdated.Width == 300 && fixedUpdated.FontSize == 11f && !fixedUpdated.Bold;
            checks["settings_json_only_optional_visibility_migrates_and_required_fields_stay_visible"] = !Find(restored.BadgeLayout, "indicator").Visible
                && Find(restored.BadgeLayout, "weekly-reset").Visible && !Find(restored.BadgeLayout, "countdown").Visible && RequiredVisible(restored.BadgeLayout);
            checks["settings_json_prior_bottom_row_countdown_uses_inline_reserved_column"] = Find(restored.BadgeLayout, "countdown").X == 232
                && Find(restored.BadgeLayout, "countdown").Y == 6 && Find(restored.BadgeLayout, "countdown").Width == 78
                && BadgeLayoutSettings.ElementHeight(Find(restored.BadgeLayout, "countdown")) == 25
                && Find(restored.BadgeLayout, "weekly-reset").Width == 300;
            checks["settings_json_old_z_order_migrates_to_fixed_order"] = restored.BadgeLayout.Elements.Count == 5
                && restored.BadgeLayout.Elements[0].Key == "indicator" && restored.BadgeLayout.Elements[1].Key == "remaining"
                && restored.BadgeLayout.Elements[2].Key == "updated" && restored.BadgeLayout.Elements[3].Key == "weekly-reset"
                && restored.BadgeLayout.Elements[4].Key == "countdown";
            Preferences secondRead = serializer.Deserialize<Preferences>(serializer.Serialize(restored));
            secondRead.NormalizePosition();
            checks["settings_json_fixed_layout_and_optional_choices_are_stable_after_roundtrip"] = LayoutsEqual(restored.BadgeLayout, secondRead.BadgeLayout);
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

        private static bool OptionsMatch(BadgePreview preview, SettingsPreviewHost host, bool indicator, bool reset, bool countdown)
        {
            return OptionalVisible(preview.LayoutSettings, indicator, reset, countdown)
                && OptionalVisible(host.Prefs.BadgeLayout, indicator, reset, countdown)
                && RequiredVisible(preview.LayoutSettings) && RequiredVisible(host.Prefs.BadgeLayout);
        }

        private static bool OptionalVisible(BadgeLayoutSettings layout, bool indicator, bool reset, bool countdown)
        {
            return Find(layout, "indicator").Visible == indicator && Find(layout, "weekly-reset").Visible == reset
                && Find(layout, "countdown").Visible == countdown;
        }

        private static bool RequiredVisible(BadgeLayoutSettings layout)
        {
            return Find(layout, "remaining").Visible && Find(layout, "updated").Visible;
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
    }
}
