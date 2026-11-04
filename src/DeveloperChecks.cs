using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class DeveloperChecks
    {
        public static void SelfTest(string output)
        {
            var checks = new Dictionary<string, bool>();
            CheckTitleLayout(checks);
            TitleAnchorTrackerChecks.Run(checks);
            CheckPreferences(checks);
            RuntimeResolverChecks.Run(checks);
            ThemeReaderChecks.Run(checks);
            ThemeChecks.Run(checks);
            BadgeLayoutChecks.Run(checks);
            UsagePresentationChecks.Run(checks);
            BadgeEditorChecks.Run(checks);
            SettingsBindingChecks.Run(checks);
            var now = DateTimeOffset.Parse("2020-01-01T00:03:00Z", CultureInfo.InvariantCulture);
            var quota = new UsageState
            {
                status = "ok",
                observedAt = now.ToString("o")
            };
            var weekly = new LimitWindow
            {
                limitId = "codex",
                window = "primary",
                windowDurationMins = 10080,
                remainingPercent = 14
            };
            quota.windows.Add(weekly);
            checks["weekly_fourteen_percent_warns"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            weekly.remainingPercent = 15;
            checks["exactly_fifteen_does_not_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.remainingPercent = 15.001;
            checks["above_fifteen_does_not_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.remainingPercent = 14.999;
            checks["unrounded_value_controls_warning"] = WeeklyQuotaWarning.Remaining(quota, now) == 14.999 && weekly.remainingPercent.ToString("0.#", CultureInfo.InvariantCulture) == "15";
            weekly.remainingPercent = 0;
            checks["zero_is_exhausted_not_unavailable"] = WeeklyQuotaWarning.Remaining(quota, now) == 0;
            weekly.remainingPercent = 14;
            weekly.window = "secondary";
            checks["weekly_secondary_is_supported"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            weekly.windowDurationMins = 300;
            checks["five_hour_is_not_weekly_warning"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.windowDurationMins = null;
            checks["missing_period_is_not_guessed"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.windowDurationMins = 10080;
            weekly.limitId = "other";
            checks["other_account_bucket_does_not_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            weekly.limitId = "codex";
            foreach (var status in new[] { "loading", "stale", "unavailable" })
            {
                quota.status = status;
                checks["no_live_warning_when_" + status] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            }

            quota.status = "ok";
            quota.observedAt = now.AddMinutes(-3).ToString("o");
            checks["three_minute_boundary_is_stale"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = now.AddMinutes(-3).AddMilliseconds(1).ToString("o");
            checks["fresh_last_millisecond_can_warn"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            quota.observedAt = null;
            checks["missing_success_time_cannot_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = "invalid";
            checks["invalid_success_time_cannot_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = now.AddMinutes(1).ToString("o");
            checks["future_success_time_cannot_warn"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            quota.observedAt = now.ToString("o");
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, 101 })
            {
                weekly.remainingPercent = value;
                checks["invalid_remaining_" + value.ToString(CultureInfo.InvariantCulture)] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            }

            weekly.remainingPercent = 14;
            checks["successful_recovery_restores_warning"] = WeeklyQuotaWarning.Remaining(quota, now) == 14;
            weekly.remainingPercent = 80;
            checks["quota_recovery_clears_warning"] = !WeeklyQuotaWarning.Remaining(quota, now).HasValue;
            bool passed = true;
            foreach (bool result in checks.Values)
                passed &= result;
            var text = new JavaScriptSerializer().Serialize(new { passed = passed, checks = checks });
            if (output != null)
                File.WriteAllText(output, text, new UTF8Encoding(false));
            Environment.ExitCode = passed ? 0 : 1;
        }

        private static void CheckPreferences(Dictionary<string, bool> checks)
        {
            var json = new JavaScriptSerializer();
            var legacy = json.Deserialize<Preferences>("{\"Position\":\"title\",\"SidebarX\":88,\"SidebarY\":94,\"TitleX\":560,\"TitleY\":48}");
            legacy.NormalizePosition();
            checks["legacy_title_moves_below_without_old_offsets"] = legacy.Position == Preferences.BelowTitle && legacy.TitleOffsetX == 0 && legacy.TitleGap == 6;
            checks["legacy_title_keeps_sidebar_coordinates"] = legacy.SidebarX == 88 && legacy.SidebarY == 94;
            var sidebar = json.Deserialize<Preferences>("{\"Position\":\"sidebar\",\"SidebarX\":112,\"SidebarY\":98,\"TitleX\":560,\"TitleY\":48}");
            sidebar.NormalizePosition();
            checks["legacy_sidebar_selection_and_offsets_preserved"] = sidebar.Position == "sidebar" && sidebar.SidebarX == 112 && sidebar.SidebarY == 98;
            checks["legacy_sidebar_gets_new_below_title_defaults"] = sidebar.TitleOffsetX == 0 && sidebar.TitleGap == 6;
            var oldBelow = json.Deserialize<Preferences>("{\"Position\":\"title-below\",\"TitleBelowX\":48,\"TitleBelowY\":56}");
            oldBelow.NormalizePosition();
            checks["old_client_offsets_are_not_title_offsets"] = oldBelow.Position == Preferences.BelowTitle && oldBelow.TitleOffsetX == 0 && oldBelow.TitleGap == 6;
            var custom = new Preferences { TitleOffsetX = -12, TitleGap = 10 };
            var restored = json.Deserialize<Preferences>(json.Serialize(custom));
            restored.NormalizePosition();
            checks["below_title_custom_coordinates_round_trip"] = restored.Position == Preferences.BelowTitle && restored.TitleOffsetX == -12 && restored.TitleGap == 10;
            restored.ResetCurrentPosition();
            checks["below_title_reset_uses_new_defaults"] = restored.TitleOffsetX == 0 && restored.TitleGap == 6;
            var invalid = new Preferences { Position = null };
            invalid.NormalizePosition();
            checks["unknown_position_defaults_below_title"] = invalid.Position == Preferences.BelowTitle;
            var excessive = new Preferences { TitleOffsetX = int.MaxValue, TitleGap = int.MaxValue };
            excessive.NormalizePosition();
            checks["stored_title_offsets_stay_within_supported_range"] = excessive.TitleOffsetX == 4000 && excessive.TitleGap == 3000;
            excessive.TitleOffsetX = int.MinValue;
            excessive.TitleGap = int.MinValue;
            excessive.NormalizePosition();
            checks["stored_title_offsets_reject_negative_gap"] = excessive.TitleOffsetX == -4000 && excessive.TitleGap == 0;
        }

        private static void CheckTitleLayout(Dictionary<string, bool> checks)
        {
            var preferences = new Preferences();
            checks["below_title_is_default"] = preferences.Position == Preferences.BelowTitle;
            var sidebar = new Preferences { Position = "sidebar" };
            checks["sidebar_preserves_legacy_bottom_gap"] = OverlayLayout.CalculateBounds(new Rectangle(100,200,1200,800),1,sidebar) == new Rectangle(164,882,320,82);
            checks["sidebar_negative_monitor_at_150_percent"] = OverlayLayout.CalculateBounds(new Rectangle(-2560,-200,2000,1200),1.5f,sidebar) == new Rectangle(-2464,823,480,123);
            checks["tiny_sidebar_hides_full_badge_instead_of_clipping"] = OverlayLayout.CalculateBounds(new Rectangle(0,0,100,30),1,sidebar).IsEmpty;
            var client = new Rectangle(59,104,2944,1877);
            var anchor = new TitleAnchorObservation { Status="ok",TitleBounds=new Rectangle(585,195,199,28),HeaderBounds=new Rectangle(507,170,2491,78) };
            checks["unavailable_title_never_falls_back_over_sidebar"] = OverlayLayout.CalculateBounds(client,1.5f,preferences).IsEmpty;
            checks["real_header_fixture_at_150_percent"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor) == new Rectangle(585,257,480,123);
            checks["physical_anchor_not_rescaled_at_100_percent"] = OverlayLayout.CalculateBounds(client,1,preferences,anchor) == new Rectangle(585,254,320,82);
            checks["physical_anchor_not_rescaled_at_200_percent"] = OverlayLayout.CalculateBounds(client,2,preferences,anchor) == new Rectangle(585,260,640,164);
            preferences.TitleOffsetX=12; preferences.TitleGap=10;
            checks["title_relative_manual_offsets"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor) == new Rectangle(603,263,480,123);
            preferences.ResetCurrentPosition();
            anchor.TitleBounds=new Rectangle(2900,195,80,28);
            checks["right_edge_clamps_within_content"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor) == new Rectangle(2518,257,480,123);
            anchor.TitleBounds=new Rectangle(585,195,199,28); preferences.TitleOffsetX=-4000;
            checks["negative_offset_cannot_enter_sidebar"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor).Left == 507;
            preferences.ResetCurrentPosition();
            anchor.HeaderBounds=new Rectangle(107,170,2891,78); anchor.TitleBounds=new Rectangle(185,195,199,28);
            checks["sidebar_layout_changes_without_client_resize"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor) == new Rectangle(185,257,480,123);
            anchor.HeaderBounds=new Rectangle(-2112,-134,1547,78); anchor.TitleBounds=new Rectangle(-2034,-109,199,28);
            checks["anchored_negative_monitor_coordinates"] = OverlayLayout.CalculateBounds(new Rectangle(-2560,-200,2000,1200),1.5f,preferences,anchor) == new Rectangle(-2034,-47,480,123);
            anchor.HeaderBounds=new Rectangle(507,170,70,78); anchor.TitleBounds=new Rectangle(517,195,40,28);
            checks["insufficient_content_width_hides_badge"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor).IsEmpty;
            anchor.HeaderBounds=new Rectangle(507,170,2491,78); anchor.TitleBounds=new Rectangle(585,195,199,28);
            checks["insufficient_space_below_header_hides_badge"] = OverlayLayout.CalculateBounds(new Rectangle(59,104,2944,150),1.5f,preferences,anchor).IsEmpty;
            checks["partial_height_does_not_cut_reset_or_countdown"] = OverlayLayout.CalculateBounds(new Rectangle(59,104,2944,250),1.5f,preferences,anchor).IsEmpty;
            var large = new Preferences { BadgeLayout = new BadgeLayoutSettings { Width=600,Height=300 } };
            checks["custom_canvas_dimensions_drive_actual_overlay_bounds"] = OverlayLayout.CalculateBounds(client,1.5f,large,anchor) == new Rectangle(585,257,900,450);
            anchor.Status="ambiguous";
            checks["ambiguous_title_hides_badge"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor).IsEmpty;
            anchor.Status="ok"; anchor.TitleBounds=new Rectangle(80,195,199,28);
            checks["title_outside_content_header_rejected"] = OverlayLayout.CalculateBounds(client,1.5f,preferences,anchor).IsEmpty;
            DateTime now=new DateTime(2020,1,1,0,0,10,DateTimeKind.Utc);
            checks["fresh_anchor_before_expiry"] = TitleAnchorTracker.IsFresh(now.AddMilliseconds(-1999),now);
            checks["anchor_expires_at_two_seconds"] = !TitleAnchorTracker.IsFresh(now.AddSeconds(-2),now);
            checks["future_anchor_rejected"] = !TitleAnchorTracker.IsFresh(now.AddSeconds(1),now);
            checks["old_anchor_rejected"] = !TitleAnchorTracker.IsFresh(now.AddMinutes(-1),now);
        }

        public static void RenderSamples(string directory)
        {
            Directory.CreateDirectory(directory);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var samples = new[] {
                new { Name="normal", Value=SettingsPreviewHost.CreateSample(96,"ok") },
                new { Name="weekly-low", Value=SettingsPreviewHost.CreateSample(14,"ok") },
                new { Name="weekly-exhausted", Value=SettingsPreviewHost.CreateSample(0,"ok") },
                new { Name="stale", Value=SettingsPreviewHost.CreateSample(14,"stale") },
                new { Name="loading", Value=SettingsPreviewHost.CreateSample(0,"loading") },
                new { Name="reset-unknown", Value=SettingsPreviewHost.CreateSample(96,"ok",false) }
            };
            foreach (var theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (var sample in samples)
                    foreach (float scale in new[] {1f,1.5f,2f})
                    {
                        var layout = new BadgeLayoutSettings();
                        using (var badge = new Badge { ClientSize=new Size((int)(layout.Width*scale),(int)(layout.Height*scale)),ScaleFactor=scale,Theme=theme,LayoutSettings=layout,Presentation=sample.Value })
                        using (var bitmap = new Bitmap(badge.Width,badge.Height))
                        {
                            badge.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                            bitmap.Save(Path.Combine(directory,(theme.IsDark?"dark-":"light-")+sample.Name+"-"+(int)(scale*100)+".png"),System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
            foreach (var theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                foreach (string position in new[] {"sidebar",Preferences.BelowTitle})
                    foreach (float scale in new[] {1f,1.5f,2f})
                        using (var window = new SettingsWindow(new SettingsPreviewHost(theme,position),scale))
                        using (var bitmap = new Bitmap(window.ClientSize.Width,window.ClientSize.Height))
                        {
                            window.FormBorderStyle=FormBorderStyle.None;window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-32000,-32000);window.Opacity=0;window.Show();
                            bitmap.SetResolution(96*scale,96*scale);
                            for(int page=0;page<3;page++)
                            {
                                window.SelectPreviewPage(page);window.PerformLayout();Application.DoEvents();
                                window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,window.ClientSize));
                                bitmap.Save(Path.Combine(directory,(theme.IsDark?"dark-":"light-")+"settings-"+position+"-page"+page+"-"+(int)(scale*100)+".png"),System.Drawing.Imaging.ImageFormat.Png);
                            }
                            window.Hide();
                        }
        }
    }
}
