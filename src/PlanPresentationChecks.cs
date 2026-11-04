using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class PlanPresentationChecks
    {
        internal static void Run(Dictionary<string, bool> checks)
        {
            var now = DateTimeOffset.Parse("2026-11-05T00:00:00Z", CultureInfo.InvariantCulture);
            var state = new UsageState { status = "ok", observedAt = now.ToString("o"), planType = "plus" };
            state.windows.Add(new LimitWindow { limitId = "codex", window = "primary", remainingPercent = 76, windowDurationMins = 10080 });
            var presentation = UsagePresentation.Create(state, now, TimeZoneInfo.Utc);
            checks["plan_fresh_codex_observation_replaces_generic_account_suffix_and_can_be_hidden"] = presentation.PlanText == "Plus"
                && presentation.BadgeDetailText(true) == "갱신 00:00:00 · Plus"
                && presentation.BadgeDetailText(false) == "갱신 00:00:00 · Codex 계정";
            state.planType = "pro";
            var changed = UsagePresentation.Create(state, now, TimeZoneInfo.Utc);
            state.planType = null;
            var cleared = UsagePresentation.Create(state, now, TimeZoneInfo.Utc);
            checks["plan_only_change_or_clear_triggers_live_badge_presentation_replacement"] = presentation.MainText == changed.MainText
                && presentation.DetailText == changed.DetailText && !changed.HasSameDisplayAs(presentation)
                && !cleared.HasSameDisplayAs(changed) && cleared.HasSameDisplayAs(UsagePresentation.Create(state, now, TimeZoneInfo.Utc))
                && !presentation.HasSameDisplayAs(null);
            var names = new Dictionary<string, string> {
                {"free","Free"},{"go","Go"},{"plus","Plus"},{"pro","Pro"},
                {"team","Team"},{"business","Business"},{"enterprise","Enterprise"},{"edu","Edu"}
            };
            bool labels = true;
            foreach (var item in names)
            {
                state.planType = item.Key;
                labels &= UsagePresentation.Create(state, now, TimeZoneInfo.Utc).PlanText == item.Value;
            }
            checks["plan_common_labels_follow_verified_wire_type_without_price_or_paid_inference"] = labels;
            bool specialized = true;
            foreach (string value in new[] {"prolite","promax","self_serve_business_prolite","self_serve_business_usage_based","ent26",
                "enterprise_cbp_automation","enterprise_cbp_usage_based","edu_plus","edu_pro","sci","sci_plus"})
            {
                state.planType = value;
                specialized &= UsagePresentation.Create(state, now, TimeZoneInfo.Utc).PlanText == "요금제: " + value;
            }
            checks["plan_specialized_types_keep_verified_identifier_instead_of_guessing_a_retail_name"] = specialized;
            bool safe = true;
            foreach (string value in new[] {null,"unknown","","PLUS","new_future_plan","plus\nprivate@example.com","<token>"})
            {
                state.planType = value;
                var missing = UsagePresentation.Create(state, now, TimeZoneInfo.Utc);
                safe &= missing.PlanText == "요금제 확인 불가" && !missing.BadgeDetailText(true).Contains("private@example.com");
            }
            checks["plan_missing_unknown_unsupported_and_untrusted_types_are_unavailable_not_guessed"] = safe;
            state.planType = "pro";
            state.status = "stale"; state.reason = "request_timeout";
            var failed = UsagePresentation.Create(state, now, TimeZoneInfo.Utc);
            checks["plan_failed_refresh_keeps_previous_label_and_failure_time_visible"] = failed.PlanText == "이전 Pro"
                && failed.BadgeDetailText(true).Contains("갱신 실패") && failed.BadgeDetailText(true).Contains("00:00:00") && failed.HasQueryFailure;
            state.status = "loading"; state.reason = null;
            checks["plan_refreshing_snapshot_labels_retained_value_as_previous"] = UsagePresentation.Create(state, now, TimeZoneInfo.Utc).PlanText == "이전 Pro";
            state.status = "ok";
            checks["plan_expired_observation_does_not_claim_current_subscription"] = UsagePresentation.Create(state, now.AddMinutes(3), TimeZoneInfo.Utc).PlanText == "이전 Pro";
            state.reason = "account_changed";
            checks["plan_account_change_does_not_leak_previous_account_even_with_malformed_retained_data"] = UsagePresentation.Create(state, now, TimeZoneInfo.Utc).PlanText == "요금제 확인 불가";
            state.reason = null; state.observedAt = now.AddSeconds(1).ToString("o");
            checks["plan_future_or_missing_observation_cannot_be_used_as_verified_account"] = UsagePresentation.Create(state, now, TimeZoneInfo.Utc).PlanText == "요금제 확인 불가";
            state.observedAt = null; state.status = "loading";
            checks["plan_initial_connection_reports_loading_without_inventing_a_plan"] = UsagePresentation.Create(state, now, TimeZoneInfo.Utc).PlanText == "요금제 확인 중";
            state.status = "ok"; state.observedAt = now.ToString("o"); state.windows[0].limitId = "other";
            checks["plan_unrelated_bucket_cannot_supply_the_codex_account_plan"] = UsagePresentation.Create(state, now, TimeZoneInfo.Utc).PlanText == "요금제 확인 불가";
            var example = UsagePresentation.CreatePreview(now, TimeZoneInfo.Utc);
            checks["plan_settings_preview_remains_explicit_launch_time_example_not_live_account_data"] = example.PlanText == "Plus"
                && example.BadgeDetailText(true) == "갱신 00:00:00 · 예시 · Plus" && !example.HasQueryFailure;
            CheckSettings(checks);
        }

        private static void CheckSettings(Dictionary<string, bool> checks)
        {
            var json = new JavaScriptSerializer();
            var old = json.Deserialize<BadgeLayoutSettings>("{\"BackgroundTransparency\":50}"); old.Normalize();
            var off = old.Copy(); off.ShowPlan = false; off.Normalize();
            var restored = json.Deserialize<BadgeLayoutSettings>(json.Serialize(off)); restored.Normalize();
            checks["plan_display_defaults_on_for_old_settings_and_off_survives_copy_normalize_json"] = old.ShowPlan && !off.ShowPlan
                && !off.Copy().ShowPlan && !restored.ShowPlan && restored.BackgroundTransparency == 50 && restored.Height == old.Height;
            var host = new SettingsPreviewHost(VisualTheme.Dark);
            using (var window = new SettingsWindow(host, 1.5f))
            {
                var choice = (ThemedCheckBox)typeof(SettingsWindow).GetField("planChoice", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                var date = (ThemedCheckBox)typeof(SettingsWindow).GetField("resetChoice", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                var preview = (BadgePreview)typeof(SettingsWindow).GetField("preview", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                var cached = preview.Presentation;
                checks["plan_display_checkbox_is_scaled_and_does_not_overlap_date_choice"] = choice.Checked && choice.AccessibleName == "구독 요금제"
                    && !choice.Bounds.IntersectsWith(date.Bounds) && choice.ScaleFactor == 1.5f && choice.Right <= choice.Parent.ClientSize.Width;
                choice.Checked = false;
                bool disabled = !host.Prefs.BadgeLayout.ShowPlan && !preview.LayoutSettings.ShowPlan && Object.ReferenceEquals(cached, preview.Presentation);
                window.LoadPreferences();
                bool loaded = !choice.Checked && !preview.LayoutSettings.ShowPlan;
                choice.Checked = true;
                checks["plan_display_checkbox_updates_only_layout_and_preview_without_replacing_example"] = disabled && loaded
                    && host.Prefs.BadgeLayout.ShowPlan && preview.LayoutSettings.ShowPlan && Object.ReferenceEquals(cached, preview.Presentation);
                var layout = preview.LayoutSettings.Copy();
                using (var visible = BadgeRenderer.CreateGlyphBitmap(new Size(480,123), 1.5f, VisualTheme.Dark, cached, layout))
                {
                    layout.ShowPlan = false;
                    using (var hidden = BadgeRenderer.CreateGlyphBitmap(new Size(480,123), 1.5f, VisualTheme.Dark, cached, layout))
                    {
                        bool heading = true, changedMetadata = false;
                        for (int y = 0; y < visible.Height; y++)
                            for (int x = 0; x < visible.Width; x++)
                            {
                                bool differs = visible.GetPixel(x,y) != hidden.GetPixel(x,y);
                                if (y < 45) heading &= !differs;
                                else if (differs) changedMetadata = true;
                            }
                        checks["plan_display_toggle_changes_rendered_metadata_without_moving_heading_or_countdown"] = heading && changedMetadata;
                    }
                }
            }
        }
    }
}
