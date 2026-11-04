using System;
using System.Collections.Generic;
using System.Globalization;
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
            CheckTransparencyBindings(checks);
            CheckFailureDisclosure(checks);
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
                Label previewLabel = Field<Label>(window, "previewStatus");
                UsagePresentation cachedPreview = preview.Presentation;
                string cachedText = PreviewText(cachedPreview), cachedLabel = previewLabel.Text;
                CheckBox reset = Field<CheckBox>(window, "resetChoice");
                CheckBox countdown = Field<CheckBox>(window, "countdownChoice");
                checks["settings_binding_hidden_preview_uses_independent_example_and_process_start_clock"] = !window.Visible
                    && !Object.ReferenceEquals(preview.Presentation, host.Presentation)
                    && IsExamplePreview(preview.Presentation, host.StartedAt) && previewLabel.Text.Contains("예시")
                    && !Object.ReferenceEquals(preview.LayoutSettings, host.Prefs.BadgeLayout);
                BadgeLayoutSettings beforeLoad = host.Prefs.BadgeLayout;
                window.LoadPreferences();
                checks["settings_binding_loading_choices_does_not_replace_saved_preferences"] = Object.ReferenceEquals(originalPreferences, beforeLoad)
                    && Object.ReferenceEquals(beforeLoad, host.Prefs.BadgeLayout)
                    && reset.Checked && countdown.Checked;
                checks["settings_binding_only_overlay_and_position_theme_pages_remain"] = Field<ThemedButton[]>(window, "pageButtons").Length == 2
                    && Field<Panel[]>(window, "pages").Length == 2
                    && typeof(SettingsWindow).GetField("indicatorChoice", BindingFlags.Instance | BindingFlags.NonPublic) == null;
                CheckCommonSettingsDisplay(checks, window, host);

                reset.Checked = false;
                checks["settings_binding_reset_date_off_keeps_inline_countdown_at_compact_height"] = OptionsMatch(preview, host, false, true)
                    && preview.LayoutSettings.Height == 58 && host.Prefs.BadgeLayout.Height == 58
                    && Find(preview.LayoutSettings, "countdown").Y == 6;
                reset.Checked = true;
                checks["settings_binding_reset_date_on_updates_preview_and_saved_choice"] = OptionsMatch(preview, host, true, true);
                countdown.Checked = false;
                checks["settings_binding_countdown_off_keeps_reset_date_and_full_height"] = OptionsMatch(preview, host, true, false)
                    && preview.LayoutSettings.Height == 82;
                countdown.Checked = true;
                checks["settings_binding_countdown_on_updates_preview_and_saved_choice"] = OptionsMatch(preview, host, true, true);
                reset.Checked = countdown.Checked = false;
                checks["settings_binding_all_optional_fields_off_use_compact_height"] = OptionsMatch(preview, host, false, false)
                    && preview.LayoutSettings.Height == 58 && host.Prefs.BadgeLayout.Height == 58;
                reset.Checked = true;
                checks["settings_binding_any_reset_date_restores_full_height"] = OptionsMatch(preview, host, true, false)
                    && preview.LayoutSettings.Height == 82;
                reset.Checked = false;
                countdown.Checked = true;
                checks["settings_binding_countdown_alone_keeps_compact_height"] = OptionsMatch(preview, host, false, true)
                    && preview.LayoutSettings.Height == 58 && host.Prefs.BadgeLayout.Height == 58;
                Find(preview.LayoutSettings, "remaining").Visible = false;
                Find(preview.LayoutSettings, "updated").Visible = false;
                reset.Checked = true;
                checks["settings_binding_required_limit_and_status_are_restored_by_option_save"] = OptionsMatch(preview, host, true, true)
                    && RequiredVisible(preview.LayoutSettings) && RequiredVisible(host.Prefs.BadgeLayout);
                reset.Checked = false;
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
                    && !reset.Checked && countdown.Checked
                    && host.Prefs.TitleOffsetX == 12 && host.Prefs.TitleGap == 10;
                position.SelectedIndex = 1;
                checks["settings_binding_return_to_title_restores_title_coordinates_and_preserves_options"] = host.Prefs.Position == Preferences.BelowTitle
                    && anchorX.Value == 12 && anchorY.Value == 10 && horizontal.Text.Contains("제목") && vertical.Text.Contains("제목")
                    && json.Serialize(preview.LayoutSettings) == priorLayout && !window.Visible;
                ComboBox themes = Field<ComboBox>(window, "themeChoice");
                themes.SelectedIndex = 2;
                window.LoadPreferences();
                checks["settings_binding_theme_choice_and_reload_preserve_optional_choices"] = host.Prefs.ThemeMode == "light"
                    && themes.SelectedIndex == 2 && OptionsMatch(preview, host, false, true)
                    && json.Serialize(host.Prefs.BadgeLayout) == priorLayout;
                checks["settings_binding_options_position_theme_and_reload_preserve_cached_example"] = PreviewUnchanged(preview,
                    cachedPreview, cachedText, previewLabel, cachedLabel);
            }
        }

        private static void CheckTransparencyBindings(Dictionary<string, bool> checks)
        {
            var host = new SettingsPreviewHost(VisualTheme.Dark);
            host.Prefs.BadgeLayout.BackgroundTransparency = 37;
            host.Prefs.NormalizePosition();
            BadgeLayoutSettings saved = host.Prefs.BadgeLayout;
            using (var window = new SettingsWindow(host, 1f))
            {
                BadgePreview preview = Field<BadgePreview>(window, "preview");
                Label previewLabel = Field<Label>(window, "previewStatus");
                TrackBar transparency = Field<TrackBar>(window, "transparencyChoice");
                Label transparencyLabel = Field<Label>(window, "transparencyStatus");
                CheckBox reset = Field<CheckBox>(window, "resetChoice");
                CheckBox countdown = Field<CheckBox>(window, "countdownChoice");
                UsagePresentation cachedPreview = preview.Presentation;
                string cachedText = PreviewText(cachedPreview), cachedLabel = previewLabel.Text;
                window.LoadPreferences();
                checks["settings_background_transparency_loads_saved_value_without_saving_or_showing"] = !window.Visible
                    && transparency.Minimum == 0 && transparency.Maximum == 100 && transparency.Value == 37
                    && transparencyLabel.Text.Contains("37") && preview.LayoutSettings.BackgroundTransparency == 37
                    && Object.ReferenceEquals(saved, host.Prefs.BadgeLayout) && OptionsMatch(preview, host, true, true);

                reset.Checked = countdown.Checked = false;
                transparency.Value = 50;
                checks["settings_background_transparency_slider_updates_independent_preview_and_preferences"] = transparency.Value == 50
                    && preview.LayoutSettings.BackgroundTransparency == 50 && host.Prefs.BadgeLayout.BackgroundTransparency == 50
                    && !Object.ReferenceEquals(preview.LayoutSettings, host.Prefs.BadgeLayout) && transparencyLabel.Text.Contains("50")
                    && OptionsMatch(preview, host, false, false) && preview.LayoutSettings.Height == 58
                    && PreviewUnchanged(preview, cachedPreview, cachedText, previewLabel, cachedLabel);

                transparency.Value = 0;
                bool opaque = preview.LayoutSettings.BackgroundTransparency == 0 && host.Prefs.BadgeLayout.BackgroundTransparency == 0;
                transparency.Value = 100;
                checks["settings_background_transparency_endpoints_preserve_choices_and_example"] = opaque
                    && preview.LayoutSettings.BackgroundTransparency == 100 && host.Prefs.BadgeLayout.BackgroundTransparency == 100
                    && transparencyLabel.Text.Contains("100") && OptionsMatch(preview, host, false, false)
                    && PreviewUnchanged(preview, cachedPreview, cachedText, previewLabel, cachedLabel) && !window.Visible;

                BadgeLayoutSettings external = host.Prefs.BadgeLayout.Copy();
                external.BackgroundTransparency = 17;
                host.SetBadgeLayout(external);
                BadgeLayoutSettings beforeReload = host.Prefs.BadgeLayout;
                window.LoadPreferences();
                bool loaded = transparency.Value == 17 && preview.LayoutSettings.BackgroundTransparency == 17
                    && host.Prefs.BadgeLayout.BackgroundTransparency == 17 && transparencyLabel.Text.Contains("17")
                    && Object.ReferenceEquals(beforeReload, host.Prefs.BadgeLayout);
                transparency.Value = 23;
                checks["settings_background_transparency_model_reload_and_slider_save_work_both_directions"] = loaded
                    && preview.LayoutSettings.BackgroundTransparency == 23 && host.Prefs.BadgeLayout.BackgroundTransparency == 23
                    && transparencyLabel.Text.Contains("23") && OptionsMatch(preview, host, false, false)
                    && PreviewUnchanged(preview, cachedPreview, cachedText, previewLabel, cachedLabel) && !window.Visible;
            }
        }

        private static void CheckCommonSettingsDisplay(Dictionary<string, bool> checks, SettingsWindow window, SettingsPreviewHost host)
        {
            ThemedButton[] buttons = Field<ThemedButton[]>(window, "pageButtons");
            Panel[] pages = Field<Panel[]>(window, "pages");
            BadgePreview preview = Field<BadgePreview>(window, "preview");
            Label label = Field<Label>(window, "previewStatus");
            UsagePresentation example = preview.Presentation;
            string text = PreviewText(example), caption = label.Text;
            BadgeLayoutSettings savedLayout = host.Prefs.BadgeLayout;
            var json = new JavaScriptSerializer();
            string preferences = json.Serialize(host.Prefs);

            window.SelectPreviewPage(0);
            bool overlay = buttons[0].Selected && !buttons[1].Selected && LocallyVisible(pages[0]) && !LocallyVisible(pages[1])
                && PositionAdjustmentPolicy.CanShow(false, true, true, true);
            window.SelectPreviewPage(1);
            bool position = !buttons[0].Selected && buttons[1].Selected && !LocallyVisible(pages[0]) && LocallyVisible(pages[1])
                && PositionAdjustmentPolicy.CanShow(false, true, true, true);
            window.SelectPreviewPage(0);
            checks["settings_both_tabs_use_common_foreground_target_display_policy"] = overlay && position
                && buttons[0].Selected && !buttons[1].Selected;
            checks["settings_tab_navigation_keeps_saved_options_and_cached_example_without_showing"] = !window.Visible
                && Object.ReferenceEquals(savedLayout, host.Prefs.BadgeLayout) && preferences == json.Serialize(host.Prefs)
                && PreviewUnchanged(preview, example, text, label, caption);
        }

        private static void CheckJsonRoundTrip(Dictionary<string, bool> checks)
        {
            var prefs = new Preferences
            {
                Position = "sidebar", ThemeMode = "light", SidebarX = 111, SidebarY = 98,
                SidebarBottomGap = 52, TitleOffsetX = -15, TitleGap = 12,
                BadgeLayout = new BadgeLayoutSettings { Width = 420, Height = 160, BackgroundTransparency = 50 }
            };
            BadgeElement remaining = Find(prefs.BadgeLayout, "remaining");
            remaining.X = 37; remaining.Y = 11; remaining.Width = 210; remaining.FontSize = 17.5f; remaining.Bold = false; remaining.Visible = false;
            BadgeElement updated = Find(prefs.BadgeLayout, "updated");
            updated.X = 23; updated.Y = 52; updated.Width = 250; updated.FontSize = 10f; updated.Bold = true; updated.Visible = false;
            BadgeElement reset = Find(prefs.BadgeLayout, "weekly-reset");
            reset.X = 18; reset.Y = 104; reset.Width = 300; reset.FontSize = 12.5f;
            BadgeElement countdown = Find(prefs.BadgeLayout, "countdown");
            countdown.X = 232; countdown.Y = 53; countdown.Width = 78; countdown.Visible = false;
            var indicator = new BadgeElement { Key = "indicator", X = 8, Y = 12, Width = 18, Visible = true };
            prefs.BadgeLayout.Elements = new List<BadgeElement> { reset, indicator, countdown, updated, remaining };
            var serializer = new JavaScriptSerializer();
            Preferences restored = serializer.Deserialize<Preferences>(serializer.Serialize(prefs));
            restored.NormalizePosition();
            checks["settings_json_roundtrip_preserves_position_theme_and_both_position_coordinates"] = restored.Position == "sidebar"
                && restored.ThemeMode == "light" && restored.SidebarX == 111 && restored.SidebarY == 98 && restored.SidebarBottomGap == 52
                && restored.TitleOffsetX == -15 && restored.TitleGap == 12;
            BadgeElement fixedRemaining = Find(restored.BadgeLayout, "remaining"), fixedUpdated = Find(restored.BadgeLayout, "updated");
            checks["settings_json_old_drag_geometry_and_style_migrate_to_fixed_layout"] = restored.BadgeLayout.Width == 320 && restored.BadgeLayout.Height == 82
                && restored.BadgeLayout.BackgroundTransparency == 50
                && fixedRemaining.X == 10 && fixedRemaining.Y == 6 && fixedRemaining.Width == 300 && fixedRemaining.FontSize == 14f && fixedRemaining.Bold
                && fixedUpdated.X == 10 && fixedUpdated.Y == 31 && fixedUpdated.Width == 300 && fixedUpdated.FontSize == 11f && !fixedUpdated.Bold;
            checks["settings_json_only_date_countdown_choices_migrate_and_legacy_icon_is_removed"] = !restored.BadgeLayout.Elements.Exists(element => element.Key == "indicator")
                && Find(restored.BadgeLayout, "weekly-reset").Visible && !Find(restored.BadgeLayout, "countdown").Visible && RequiredVisible(restored.BadgeLayout);
            checks["settings_json_prior_bottom_row_countdown_uses_inline_reserved_column"] = Find(restored.BadgeLayout, "countdown").X == 232
                && Find(restored.BadgeLayout, "countdown").Y == 6 && Find(restored.BadgeLayout, "countdown").Width == 78
                && BadgeLayoutSettings.ElementHeight(Find(restored.BadgeLayout, "countdown")) == 25
                && Find(restored.BadgeLayout, "weekly-reset").Width == 300;
            checks["settings_json_old_z_order_migrates_to_fixed_order"] = restored.BadgeLayout.Elements.Count == 4
                && restored.BadgeLayout.Elements[0].Key == "remaining" && restored.BadgeLayout.Elements[1].Key == "updated"
                && restored.BadgeLayout.Elements[2].Key == "weekly-reset" && restored.BadgeLayout.Elements[3].Key == "countdown";
            Preferences secondRead = serializer.Deserialize<Preferences>(serializer.Serialize(restored));
            secondRead.NormalizePosition();
            checks["settings_json_fixed_layout_and_optional_choices_are_stable_after_roundtrip"] = LayoutsEqual(restored.BadgeLayout, secondRead.BadgeLayout);
            restored.SidebarY = 500;
            restored.NormalizePosition();
            checks["settings_json_explicit_bottom_gap_survives_later_normalization"] = restored.SidebarBottomGap == 52 && restored.SidebarY == 500;
        }

        private static void CheckFailureDisclosure(Dictionary<string, bool> checks)
        {
            var healthy = SettingsPreviewHost.CreateSample(95, "ok");
            var failed = SettingsPreviewHost.CreateSample(14, "stale");
            var loading = SettingsPreviewHost.CreateSample(0, "loading");
            var host = new SettingsPreviewHost(VisualTheme.Dark, Preferences.BelowTitle, healthy);
            using (var window = new SettingsWindow(host, 1f))
            {
                ThemedButton toggle = Field<ThemedButton>(window, "problemToggle");
                TextBox details = Field<TextBox>(window, "problemDetails");
                BadgePreview preview = Field<BadgePreview>(window, "preview");
                Label previewLabel = Field<Label>(window, "previewStatus");
                UsagePresentation cachedPreview = preview.Presentation;
                string cachedText = PreviewText(cachedPreview), cachedLabel = previewLabel.Text;
                checks["settings_failure_details_are_absent_during_normal_usage"] = !healthy.HasQueryFailure
                    && !LocallyVisible(toggle) && !LocallyVisible(details) && !Value<bool>(window, "problemExpanded")
                    && details.Text == String.Empty && IsExamplePreview(cachedPreview, host.StartedAt)
                    && !Object.ReferenceEquals(cachedPreview, healthy);
                host.SetPresentation(failed);
                window.UpdateUsage();
                checks["settings_failure_shows_only_collapsed_problem_toggle"] = failed.HasQueryFailure
                    && LocallyVisible(toggle) && !LocallyVisible(details) && !Value<bool>(window, "problemExpanded");
                checks["settings_actual_query_failure_preserves_example_reference_text_and_label"] = PreviewUnchanged(preview,
                    cachedPreview, cachedText, previewLabel, cachedLabel) && !Object.ReferenceEquals(preview.Presentation, failed);
                ClickOwnControl(toggle);
                checks["settings_failure_toggle_opens_real_click_handler_and_expected_failure_text"] = Value<bool>(window, "problemExpanded")
                    && LocallyVisible(details) && Newlines(details.Text) == Newlines(failed.FailureDetails)
                    && details.Text.Length > 0 && !window.Visible;
                ClickOwnControl(toggle);
                checks["settings_failure_toggle_collapses_without_removing_failure_entry"] = !Value<bool>(window, "problemExpanded")
                    && !LocallyVisible(details) && LocallyVisible(toggle)
                    && PreviewUnchanged(preview, cachedPreview, cachedText, previewLabel, cachedLabel);
                ClickOwnControl(toggle);
                host.SetPresentation(healthy);
                window.UpdateUsage();
                checks["settings_failure_recovery_hides_clears_and_resets_expansion"] = !LocallyVisible(toggle) && !LocallyVisible(details)
                    && !Value<bool>(window, "problemExpanded") && details.Text == String.Empty;
                checks["settings_actual_query_recovery_preserves_example_reference_text_and_label"] = PreviewUnchanged(preview,
                    cachedPreview, cachedText, previewLabel, cachedLabel) && !Object.ReferenceEquals(preview.Presentation, healthy);
                host.SetPresentation(failed);
                window.UpdateUsage();
                checks["settings_new_failure_after_recovery_starts_collapsed"] = LocallyVisible(toggle) && !LocallyVisible(details)
                    && !Value<bool>(window, "problemExpanded");
                ClickOwnControl(toggle);
                host.SetPresentation(loading);
                window.UpdateUsage();
                checks["settings_loading_is_not_shown_as_a_failure_and_clears_old_problem_text"] = !loading.HasQueryFailure
                    && !LocallyVisible(toggle) && !LocallyVisible(details) && !Value<bool>(window, "problemExpanded")
                    && details.Text == String.Empty && !window.Visible;
                checks["settings_actual_query_loading_preserves_example_reference_text_and_label"] = PreviewUnchanged(preview,
                    cachedPreview, cachedText, previewLabel, cachedLabel) && !Object.ReferenceEquals(preview.Presentation, loading);
            }
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

        private static bool OptionsMatch(BadgePreview preview, SettingsPreviewHost host, bool reset, bool countdown)
        {
            return OptionalVisible(preview.LayoutSettings, reset, countdown)
                && OptionalVisible(host.Prefs.BadgeLayout, reset, countdown)
                && RequiredVisible(preview.LayoutSettings) && RequiredVisible(host.Prefs.BadgeLayout);
        }

        private static bool OptionalVisible(BadgeLayoutSettings layout, bool reset, bool countdown)
        {
            return layout.Elements.Count == 4 && !layout.Elements.Exists(element => element.Key == "indicator")
                && Find(layout, "weekly-reset").Visible == reset && Find(layout, "countdown").Visible == countdown
                && Find(layout, "remaining").X == 10;
        }

        private static bool RequiredVisible(BadgeLayoutSettings layout)
        {
            return Find(layout, "remaining").Visible && Find(layout, "updated").Visible;
        }

        private static bool LayoutsEqual(BadgeLayoutSettings first, BadgeLayoutSettings second)
        {
            if (first.Width != second.Width || first.Height != second.Height || first.BackgroundTransparency != second.BackgroundTransparency
                || first.Elements.Count != second.Elements.Count)
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

        private static T Value<T>(SettingsWindow window, string name)
        {
            return (T)typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        }

        private static bool LocallyVisible(Control control)
        {
            // Control.Visible also reads ancestor visibility. The fixture form
            // stays hidden, so inspect the framework's local visibility flag.
            object visible = typeof(Control).GetField("STATE_VISIBLE", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            return (bool)typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new[] { visible });
        }

        private static void ClickOwnControl(Control control)
        {
            // PerformClick is a no-op for a hidden form. Invoke only this own
            // control's real Click path; no window is shown and no OS input sent.
            control.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { EventArgs.Empty });
        }

        private static string Newlines(string value)
        {
            return (value ?? String.Empty).Replace("\r\n", "\n");
        }

        private static bool IsExamplePreview(UsagePresentation presentation, DateTimeOffset startedAt)
        {
            return presentation != null && presentation.MainText == "주간 96% 남음"
                && presentation.DetailText == "갱신 " + startedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " · 예시";
        }

        private static string PreviewText(UsagePresentation presentation)
        {
            return presentation == null ? String.Empty : presentation.MainText + "\n" + presentation.DetailText
                + "\n" + presentation.ResetDateText + "\n" + presentation.CountdownText;
        }

        private static bool PreviewUnchanged(BadgePreview preview, UsagePresentation cached, string text, Label label, string caption)
        {
            return cached != null && Object.ReferenceEquals(preview.Presentation, cached) && PreviewText(preview.Presentation) == text
                && label.Text == caption && caption.Contains("예시");
        }
    }
}
