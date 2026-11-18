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
            CheckFormatBindings(checks);
            CheckPreviewCanvasBindings(checks);
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
                checks["settings_binding_reset_date_off_keeps_inline_countdown_at_fixed_detailed_height"] = OptionsMatch(preview, host, false, true)
                    && preview.LayoutSettings.Height == BadgeLayoutSettings.DetailedHeight
                    && host.Prefs.BadgeLayout.Height == BadgeLayoutSettings.DetailedHeight
                    && Find(preview.LayoutSettings, "countdown").Y == Find(preview.LayoutSettings, "remaining").Y;
                reset.Checked = true;
                checks["settings_binding_reset_date_on_keeps_countdown_inline_in_preview_and_preferences"] = OptionsMatch(preview, host, true, true)
                    && Find(preview.LayoutSettings, "countdown").Y == Find(preview.LayoutSettings, "weekly-reset").Y
                    && Find(host.Prefs.BadgeLayout, "countdown").Y == Find(host.Prefs.BadgeLayout, "weekly-reset").Y;
                countdown.Checked = false;
                checks["settings_binding_countdown_off_keeps_reset_date_and_fixed_height"] = OptionsMatch(preview, host, true, false)
                    && preview.LayoutSettings.Height == BadgeLayoutSettings.DetailedHeight;
                countdown.Checked = true;
                checks["settings_binding_countdown_on_updates_preview_and_saved_choice"] = OptionsMatch(preview, host, true, true);
                reset.Checked = countdown.Checked = false;
                checks["settings_binding_all_optional_fields_off_keep_fixed_detailed_height"] = OptionsMatch(preview, host, false, false)
                    && preview.LayoutSettings.Height == BadgeLayoutSettings.DetailedHeight
                    && host.Prefs.BadgeLayout.Height == BadgeLayoutSettings.DetailedHeight;
                reset.Checked = true;
                checks["settings_binding_reset_date_alone_keeps_fixed_height"] = OptionsMatch(preview, host, true, false)
                    && preview.LayoutSettings.Height == BadgeLayoutSettings.DetailedHeight;
                reset.Checked = false;
                countdown.Checked = true;
                checks["settings_binding_countdown_alone_keeps_fixed_detailed_height"] = OptionsMatch(preview, host, false, true)
                    && preview.LayoutSettings.Height == BadgeLayoutSettings.DetailedHeight
                    && host.Prefs.BadgeLayout.Height == BadgeLayoutSettings.DetailedHeight;
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
                    && anchorX.Value == 12 && anchorY.Value == 10 && horizontal.Text.Contains("기본 위치") && vertical.Text.Contains("제목")
                    && json.Serialize(preview.LayoutSettings) == priorLayout && !window.Visible;
                ComboBox themes = Field<ComboBox>(window, "themeChoice");
                themes.SelectedIndex = 2;
                window.LoadPreferences();
                checks["settings_binding_theme_choice_and_reload_preserve_optional_choices"] = host.Prefs.ThemeMode == "light"
                    && themes.SelectedIndex == 2 && OptionsMatch(preview, host, false, true)
                    && json.Serialize(host.Prefs.BadgeLayout) == priorLayout;
                checks["settings_binding_options_position_theme_and_reload_preserve_cached_example"] = PreviewUnchanged(preview,
                    cachedPreview, cachedText, previewLabel, cachedLabel);
                var claude = Field<CheckBox>(window, "claudeChoice");
                bool defaultOff = !claude.Checked && !preview.LayoutSettings.ShowClaude;
                claude.Checked = true;
                window.LoadPreferences();
                checks["settings_binding_claude_toggle_updates_saved_choice_and_example"] = defaultOff && claude.Checked
                    && host.Prefs.BadgeLayout.ShowClaude && preview.LayoutSettings.ShowClaude
                    && preview.LayoutSettings.Height == BadgeLayoutSettings.StackedCodexHeight + BadgeLayoutSettings.DetailedClaudeHeight
                    && preview.Presentation.Claude.Status.StartsWith("예시", StringComparison.Ordinal)
                    && Field<Label>(window, "claudeStatus").Text == "실제 Claude Code · 한도 수신 대기";
                var intro = Field<Label>(window, "claudeIntro");
                var status = Field<Label>(window, "claudeStatus");
                var setupHint = Field<Label>(window, "claudeSetupHint");
                string introText = intro.Text;
                var now = host.StartedAt;
                bool setupHintsMatch = setupHint.Text.Length == 0;
                foreach (string reason in new[] { "missing-cli", "auth-required" })
                {
                    var snapshot = new ClaudeUsageSnapshot { status = reason, attemptedAt = now.ToString("o", CultureInfo.InvariantCulture) };
                    ClaudeUsageDisplay actual = ClaudeUsageDisplay.FromAutomatic(snapshot, now, TimeZoneInfo.Utc);
                    host.SetPresentation(UsagePresentation.Create(new UsageState { status = "loading" }, now, TimeZoneInfo.Utc, actual));
                    window.UpdateUsage();
                    setupHintsMatch &= status.Text == "실제 Claude Code · " + actual.Status
                        && !String.IsNullOrEmpty(actual.SetupHint) && setupHint.Text == actual.SetupHint;
                }
                claude.Checked = false;
                checks["settings_binding_claude_toggle_off_restores_codex_only_example"] = !host.Prefs.BadgeLayout.ShowClaude
                    && !preview.LayoutSettings.ShowClaude && preview.LayoutSettings.Height == BadgeLayoutSettings.DetailedHeight
                    && status.Text.Length == 0;
                checks["settings_binding_claude_onboarding_persists_and_only_enabled_lookup_shows_setup_hint"] =
                    intro.Parent == claude.Parent && intro.Top >= claude.Bottom && intro.Bottom <= status.Top
                    && status.Bottom <= setupHint.Top && intro.Text == introText
                    && introText.Contains("CLI") && introText.Contains("15분") && setupHint.Text.Length == 0
                    && setupHintsMatch && ReferenceEquals(preview.Presentation, cachedPreview);
            }
        }

        private static void CheckFormatBindings(Dictionary<string, bool> checks)
        {
            var host = new SettingsPreviewHost(VisualTheme.Dark);
            using (var window = new SettingsWindow(host, 1f))
            {
                var preview = Field<BadgePreview>(window, "preview");
                var format = Field<ThemedComboBox>(window, "formatChoice");
                var reset = Field<CheckBox>(window, "resetChoice");
                var countdown = Field<CheckBox>(window, "countdownChoice");
                var plan = Field<CheckBox>(window, "planChoice");
                var claude = Field<CheckBox>(window, "claudeChoice");
                var hint = Field<Label>(window, "formatHint");
                UsagePresentation example = preview.Presentation;
                checks["settings_badge_format_defaults_to_detailed_and_is_on_the_preview_page"] = format.SelectedIndex == 0
                    && format.Items.Count == 2 && format.Items[0].ToString() == "상세형" && format.Items[1].ToString() == "간결형"
                    && format.AccessibleName == "배지 형식" && format.Parent == preview.Parent
                    && !preview.LayoutSettings.Compact && !host.Prefs.BadgeLayout.Compact;

                countdown.Checked = false;
                claude.Checked = true;
                int detailedHeight = preview.LayoutSettings.Height;
                format.SelectedIndex = 1;
                var saved = host.Prefs.BadgeLayout;
                window.LoadPreferences();
                checks["settings_badge_format_selection_updates_saved_layout_and_example_without_resetting_choices"] = preview.LayoutSettings.Compact
                    && saved.Compact && ReferenceEquals(saved, host.Prefs.BadgeLayout) && format.SelectedIndex == 1
                    && preview.LayoutSettings.Height < detailedHeight && ReferenceEquals(example, preview.Presentation)
                    && reset.Checked && !countdown.Checked && plan.Checked && claude.Checked
                    && reset.Enabled && !countdown.Enabled && plan.Enabled && claude.Enabled
                    && hint.Text == "디데이는 상세형에서 표시";

                var json = new JavaScriptSerializer();
                var roundTrip = json.Deserialize<BadgeLayoutSettings>(json.Serialize(saved));
                roundTrip.Normalize();
                checks["settings_badge_format_round_trip_preserves_compact_and_hidden_detailed_choices"] = roundTrip.Compact
                    && roundTrip.ShowPlan && roundTrip.ShowClaude && Find(roundTrip, "weekly-reset").Visible && !Find(roundTrip, "countdown").Visible;
                reset.Checked = false;
                checks["settings_compact_reset_choice_updates_preview_and_preferences"] = preview.LayoutSettings.Compact
                    && !Find(preview.LayoutSettings, "weekly-reset").Visible && !Find(host.Prefs.BadgeLayout, "weekly-reset").Visible
                    && preview.LayoutSettings.ShowClaude && ReferenceEquals(example, preview.Presentation);
                reset.Checked = true;
                format.SelectedIndex = 0;
                checks["settings_badge_format_returning_to_detailed_restores_original_display_options"] = !preview.LayoutSettings.Compact
                    && !host.Prefs.BadgeLayout.Compact && preview.LayoutSettings.Height == detailedHeight
                    && reset.Checked && !countdown.Checked && plan.Checked && claude.Checked
                    && reset.Enabled && countdown.Enabled && plan.Enabled && hint.Text.Length == 0;
            }
        }

        private static void CheckPreviewCanvasBindings(Dictionary<string, bool> checks)
        {
            bool canvas = true, label = true, unchanged = true;
            foreach (VisualTheme theme in new[] { VisualTheme.Dark, VisualTheme.Light })
            {
                var host = new SettingsPreviewHost(theme);
                host.Prefs.BadgeLayout.BackgroundTransparency = 50;
                using (var window = new SettingsWindow(host, 1f))
                {
                    BadgePreview preview = Field<BadgePreview>(window, "preview");
                    Label caption = Field<Label>(window, "previewStatus");
                    UsagePresentation example = preview.Presentation;
                    string text = PreviewText(example), expectedLabel = "예시 데이터 · "
                        + (SystemInformation.HighContrast ? "고대비 배경" : "다크 배경") + " · 프로그램 시작 시각 기준";
                    canvas &= preview.CanvasColor == (SystemInformation.HighContrast ? theme.Background : VisualTheme.Dark.Background)
                        && preview.LayoutSettings.BackgroundTransparency == 50;
                    label &= caption.Text == expectedLabel && IsExamplePreview(example, host.StartedAt);
                    window.SelectPreviewPage(1);
                    window.SelectPreviewPage(0);
                    window.LoadPreferences();
                    unchanged &= PreviewUnchanged(preview, example, text, caption, expectedLabel)
                        && !Object.ReferenceEquals(example, host.Presentation) && !window.Visible;
                }
            }
            checks["settings_preview_canvas_is_explicit_dark_example_for_both_selected_themes"] = canvas;
            checks["settings_preview_label_declares_example_canvas_and_launch_clock"] = label;
            checks["settings_preview_canvas_page_selection_and_reload_keep_example_immutable"] = unchanged;
        }

        private static void CheckTransparencyBindings(Dictionary<string, bool> checks)
        {
            var host = new SettingsPreviewHost(VisualTheme.Dark);
            host.Prefs.NormalizePosition();
            host.Prefs.BadgeLayout.BackgroundTransparency = 82;
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
                    && transparency.Minimum == 0 && transparency.Maximum == 10 && transparency.Value == 8
                    && transparencyLabel.Text.Contains("80%") && preview.LayoutSettings.BackgroundTransparency == 80
                    && host.Prefs.BadgeLayout.BackgroundTransparency == 80
                    && Object.ReferenceEquals(saved, host.Prefs.BadgeLayout) && OptionsMatch(preview, host, true, true);

                reset.Checked = countdown.Checked = false;
                transparency.Value = 5;
                checks["settings_background_transparency_slider_updates_independent_preview_and_preferences"] = transparency.Value == 5
                    && preview.LayoutSettings.BackgroundTransparency == 50 && host.Prefs.BadgeLayout.BackgroundTransparency == 50
                    && !Object.ReferenceEquals(preview.LayoutSettings, host.Prefs.BadgeLayout) && transparencyLabel.Text.Contains("50")
                    && OptionsMatch(preview, host, false, false) && preview.LayoutSettings.Height == BadgeLayoutSettings.DetailedHeight
                    && PreviewUnchanged(preview, cachedPreview, cachedText, previewLabel, cachedLabel);

                transparency.Value = 0;
                bool opaque = preview.LayoutSettings.BackgroundTransparency == 0 && host.Prefs.BadgeLayout.BackgroundTransparency == 0;
                transparency.Value = 10;
                checks["settings_background_transparency_endpoints_preserve_choices_and_example"] = opaque
                    && preview.LayoutSettings.BackgroundTransparency == 100 && host.Prefs.BadgeLayout.BackgroundTransparency == 100
                    && transparencyLabel.Text.Contains("100") && OptionsMatch(preview, host, false, false)
                    && PreviewUnchanged(preview, cachedPreview, cachedText, previewLabel, cachedLabel) && !window.Visible;

                BadgeLayoutSettings external = host.Prefs.BadgeLayout.Copy();
                external.BackgroundTransparency = 85;
                host.SetBadgeLayout(external);
                BadgeLayoutSettings beforeReload = host.Prefs.BadgeLayout;
                window.LoadPreferences();
                bool loaded = transparency.Value == 9 && preview.LayoutSettings.BackgroundTransparency == 90
                    && host.Prefs.BadgeLayout.BackgroundTransparency == 90 && transparencyLabel.Text.Contains("90%")
                    && external.BackgroundTransparency == 85 && Object.ReferenceEquals(beforeReload, host.Prefs.BadgeLayout);
                transparency.Value = 2;
                checks["settings_background_transparency_model_reload_and_slider_save_work_both_directions"] = loaded
                    && preview.LayoutSettings.BackgroundTransparency == 20 && host.Prefs.BadgeLayout.BackgroundTransparency == 20
                    && transparencyLabel.Text.Contains("20%") && OptionsMatch(preview, host, false, false)
                    && PreviewUnchanged(preview, cachedPreview, cachedText, previewLabel, cachedLabel) && !window.Visible;
                bool elevenSteps = transparency.Minimum == 0 && transparency.Maximum == 10
                    && transparency.SmallChange == 1 && transparency.LargeChange == 1;
                for (int step = 0; step <= 10; step++)
                {
                    transparency.Value = step;
                    int percent = step * 10;
                    elevenSteps &= preview.LayoutSettings.BackgroundTransparency == percent && host.Prefs.BadgeLayout.BackgroundTransparency == percent
                        && transparencyLabel.Text.Contains(percent.ToString(CultureInfo.InvariantCulture) + "%")
                        && OptionsMatch(preview, host, false, false);
                }
                checks["settings_background_transparency_native_keyboard_and_mouse_range_has_only_eleven_percent_steps"] = elevenSteps
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

            Label codexOptions = null, claudeOptions = null;
            foreach (Control control in pages[0].Controls)
            {
                if (control.Text == "Codex 표시 옵션") codexOptions = control as Label;
                if (control.Text == "Claude Code") claudeOptions = control as Label;
            }
            var claude = Field<ThemedCheckBox>(window, "claudeChoice");
            var claudeStatus = Field<Label>(window, "claudeStatus");
            bool codexBeforeClaude = codexOptions != null && claudeOptions != null;
            foreach (var name in new[] { "resetChoice", "planChoice", "countdownChoice" })
            {
                var choice = Field<ThemedCheckBox>(window, name);
                codexBeforeClaude = codexBeforeClaude && choice.Parent == pages[0]
                    && choice.Top >= codexOptions.Bottom && choice.Bottom < claudeOptions.Top;
            }
            checks["settings_overlay_groups_codex_options_and_claude_toggle_with_its_status"] = codexBeforeClaude
                && claude.Parent == pages[0] && claude.Top >= claudeOptions.Bottom
                && claude.AccessibleName == "Claude Code 한도 표시" && claude.Text == "한도 표시"
                && claudeStatus.Parent == pages[0] && claudeStatus.Left == claude.Left && claudeStatus.Top >= claude.Bottom;
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
            checks["settings_json_old_drag_geometry_and_style_migrate_to_fixed_layout"] = restored.BadgeLayout.Width == BadgeLayoutSettings.DetailedWidth
                && restored.BadgeLayout.Height == BadgeLayoutSettings.DetailedHeight && !restored.BadgeLayout.Compact
                && restored.BadgeLayout.BackgroundTransparency == 50
                && fixedRemaining.X == BadgeLayoutSettings.DetailedPadding && fixedRemaining.Y == 44
                && fixedRemaining.Width == 292 && fixedRemaining.FontSize == 14f && fixedRemaining.Bold
                && BadgeLayoutSettings.ElementHeight(fixedRemaining) == 27
                && fixedUpdated.X == BadgeLayoutSettings.DetailedPadding && fixedUpdated.Y == 24
                && fixedUpdated.Width == 292 && fixedUpdated.FontSize == 11f && !fixedUpdated.Bold;
            checks["settings_json_only_date_countdown_choices_migrate_and_legacy_icon_is_removed"] = !restored.BadgeLayout.Elements.Exists(element => element.Key == "indicator")
                && Find(restored.BadgeLayout, "weekly-reset").Visible && !Find(restored.BadgeLayout, "countdown").Visible && RequiredVisible(restored.BadgeLayout);
            var fixedCountdown = Find(restored.BadgeLayout, "countdown");
            var fixedReset = Find(restored.BadgeLayout, "weekly-reset");
            checks["settings_json_prior_reset_and_countdown_positions_normalize_to_the_quota_row"] =
                BadgeRenderer.ElementBounds(fixedCountdown) == BadgeRenderer.ElementBounds(fixedRemaining)
                && BadgeRenderer.ElementBounds(fixedReset) == BadgeRenderer.ElementBounds(fixedRemaining);
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
                    && Find(layout, "remaining").X == layout.ContentPadding;
        }

        private static bool RequiredVisible(BadgeLayoutSettings layout)
        {
            return Find(layout, "remaining").Visible && Find(layout, "updated").Visible;
        }

        private static bool LayoutsEqual(BadgeLayoutSettings first, BadgeLayoutSettings second)
        {
            if (first.Width != second.Width || first.Height != second.Height || first.Compact != second.Compact
                || first.BackgroundTransparency != second.BackgroundTransparency || first.ShowPlan != second.ShowPlan || first.ShowClaude != second.ShowClaude
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
