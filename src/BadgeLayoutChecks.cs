using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace PersonalUsageOverlay
{
    internal static class BadgeLayoutChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            var serializer = new JavaScriptSerializer();
            var defaults = new BadgeLayoutSettings();
            checks["badge_layout_default_fixed_geometry_and_order"] = defaults.Width == 320 && defaults.Height == 82
                && defaults.Elements.Count == 4
                && Is(defaults.Elements[0], "remaining", 10, 6, 214, 14f, true, true)
                && Is(defaults.Elements[1], "updated", 10, 31, 300, 11f, false, true)
                && Is(defaults.Elements[2], "weekly-reset", 10, 53, 300, 11f, false, true)
                && Is(defaults.Elements[3], "countdown", 232, 6, 78, 12f, true, true);

            var absent = new BadgeLayoutSettings { Width = Int32.MinValue, Height = Int32.MaxValue, Elements = null };
            absent.Normalize();
            checks["badge_layout_missing_list_restores_fixed_defaults"] = serializer.Serialize(absent) == serializer.Serialize(defaults);
            var partial = new BadgeLayoutSettings
            {
                Elements = new List<BadgeElement> { new BadgeElement { Key = "updated", Visible = false } }
            };
            partial.Normalize();
            checks["badge_layout_missing_optional_entries_default_to_visible"] = serializer.Serialize(partial) == serializer.Serialize(defaults);

            var remaining = new BadgeElement { Key = "remaining", X = Int32.MaxValue, Y = Int32.MinValue,
                Width = Int32.MaxValue, FontSize = Single.NaN, Bold = false, Visible = false };
            var oldElements = new List<BadgeElement>
            {
                null,
                new BadgeElement { Key = "unknown", Visible = false },
                new BadgeElement { Key = "countdown", X = 900, Y = 800, Width = 700, FontSize = 28, Visible = false },
                new BadgeElement { Key = "countdown", Visible = true },
                remaining,
                new BadgeElement { Key = "weekly-reset", X = -4, Y = 50, Width = 19, FontSize = Single.PositiveInfinity, Bold = true, Visible = true },
                new BadgeElement { Key = "indicator", X = 100, Y = 200, Width = 500, FontSize = -100, Bold = true, Visible = false },
                new BadgeElement { Key = "indicator", Visible = true },
                new BadgeElement { Key = "updated", X = 3, Y = 200, Width = 99, FontSize = 22, Bold = true, Visible = false }
            };
            var legacy = new BadgeLayoutSettings { Width = 600, Height = 300, Elements = oldElements };
            legacy.Normalize();
            checks["badge_layout_first_known_option_wins_and_duplicates_unknowns_are_discarded"] = legacy.Elements.Count == 4
                && !legacy.Elements.Exists(element => element.Key == "indicator")
                && Find(legacy, "weekly-reset").Visible && !Find(legacy, "countdown").Visible;
            checks["badge_layout_drag_coordinates_fonts_canvas_and_z_order_are_retired"] = legacy.Width == 320 && legacy.Height == 82
                && Is(legacy.Elements[0], "remaining", 10, 6, 300, 14f, true, true)
                && Is(legacy.Elements[1], "updated", 10, 31, 300, 11f, false, true)
                && Is(legacy.Elements[2], "weekly-reset", 10, 53, 300, 11f, false, true)
                && Is(legacy.Elements[3], "countdown", 232, 6, 78, 12f, true, false);
            checks["badge_layout_normalize_does_not_mutate_retained_input_list_or_elements"] = oldElements.Count == 9
                && oldElements[0] == null && remaining.X == Int32.MaxValue && remaining.Y == Int32.MinValue
                && remaining.Width == Int32.MaxValue && Single.IsNaN(remaining.FontSize) && !remaining.Visible
                && !Object.ReferenceEquals(oldElements, legacy.Elements) && !Object.ReferenceEquals(remaining, Find(legacy, "remaining"));

            bool fit = true, nonoverlap = true, size = true, required = true, expanded = true, inline = true, idempotent = true;
            for (int mask = 0; mask < 4; mask++)
            {
                var layout = new BadgeLayoutSettings();
                Find(layout, "weekly-reset").Visible = (mask & 1) != 0;
                Find(layout, "countdown").Visible = (mask & 2) != 0;
                Find(layout, "remaining").Visible = false;
                Find(layout, "updated").Visible = false;
                layout.Normalize();
                fit &= VisibleElementsFit(layout);
                nonoverlap &= VisibleElementsDoNotOverlap(layout);
                size &= layout.Width == 320 && layout.Height == ((mask & 1) == 0 ? 58 : 82)
                    && layout.Elements.Count == 4 && !layout.Elements.Exists(element => element.Key == "indicator");
                required &= Find(layout, "remaining").Visible && Find(layout, "updated").Visible;
                expanded &= Find(layout, "remaining").X == 10
                    && Find(layout, "remaining").X + Find(layout, "remaining").Width == ((mask & 2) == 0 ? 310 : 224)
                    && Find(layout, "weekly-reset").X == 10 && Find(layout, "weekly-reset").Width == 300;
                BadgeElement remainingInline = Find(layout, "remaining"), countdownInline = Find(layout, "countdown");
                inline &= countdownInline.X == 232 && countdownInline.Y == remainingInline.Y && countdownInline.Width == 78
                    && BadgeLayoutSettings.ElementHeight(countdownInline) == BadgeLayoutSettings.ElementHeight(remainingInline)
                    && ((mask & 2) == 0 || countdownInline.X - remainingInline.X - remainingInline.Width == 8);
                string first = serializer.Serialize(layout);
                layout.Normalize();
                idempotent &= first == serializer.Serialize(layout);
            }
            checks["badge_layout_all_four_toggle_combinations_fit_without_an_icon_element"] = fit && size;
            checks["badge_layout_all_four_toggle_combinations_have_no_overlapping_elements"] = nonoverlap;
            checks["badge_layout_remaining_and_updated_cannot_be_hidden"] = required;
            checks["badge_layout_main_reserves_countdown_column_only_when_visible_and_date_stays_full_width"] = expanded;
            checks["badge_layout_countdown_shares_heading_center_and_keeps_reserved_gap_in_all_options"] = inline;
            checks["badge_layout_normalization_is_idempotent_for_all_options"] = idempotent;

            BadgeLayoutSettings fromJson = serializer.Deserialize<BadgeLayoutSettings>("{\"Width\":599,\"Height\":299,\"Elements\":[{\"Key\":\"countdown\",\"Visible\":false},{\"Key\":\"indicator\",\"Visible\":false},{\"Key\":\"weekly-reset\",\"Visible\":true},{\"Key\":\"remaining\",\"Visible\":false}]} ");
            fromJson.Normalize();
            BadgeLayoutSettings roundTrip = serializer.Deserialize<BadgeLayoutSettings>(serializer.Serialize(fromJson));
            roundTrip.Normalize();
            checks["badge_layout_json_preserves_only_optional_visibility_through_migration_and_roundtrip"] = serializer.Serialize(fromJson) == serializer.Serialize(legacy)
                && serializer.Serialize(roundTrip) == serializer.Serialize(fromJson);
            BadgeLayoutSettings formerBottomRow = serializer.Deserialize<BadgeLayoutSettings>("{\"Height\":82,\"Elements\":[{\"Key\":\"indicator\",\"Visible\":false},{\"Key\":\"weekly-reset\",\"Visible\":false},{\"Key\":\"countdown\",\"X\":10,\"Y\":53,\"Width\":300,\"Visible\":true}]}");
            formerBottomRow.Normalize();
            checks["badge_layout_json_bottom_row_countdown_migrates_inline_without_adding_height"] = formerBottomRow.Height == 58
                && Is(Find(formerBottomRow, "remaining"), "remaining", 10, 6, 214, 14f, true, true)
                && Is(Find(formerBottomRow, "countdown"), "countdown", 232, 6, 78, 12f, true, true)
                && !Find(formerBottomRow, "weekly-reset").Visible;
            bool ignoredIcons = true;
            foreach (bool oldVisible in new[] { true, false })
            {
                var oldIcon = new BadgeLayoutSettings();
                oldIcon.Elements.Insert(0, new BadgeElement { Key = "indicator", X = 10, Width = 18, Visible = oldVisible });
                oldIcon.Normalize();
                ignoredIcons &= oldIcon.Elements.Count == 4 && !oldIcon.Elements.Exists(element => element.Key == "indicator")
                    && Is(Find(oldIcon, "remaining"), "remaining", 10, 6, 214, 14f, true, true);
            }
            checks["badge_layout_legacy_visible_or_hidden_icons_are_removed_without_reserved_space"] = ignoredIcons;

            var source = new BadgeLayoutSettings
            {
                Width = 555, Height = 222,
                Elements = new List<BadgeElement>
                {
                    new BadgeElement { Key = "countdown", X = 44, Y = 55, Width = 66, FontSize = 23, Bold = false, Visible = false },
                    null
                }
            };
            string beforeCopy = serializer.Serialize(source);
            BadgeLayoutSettings copy = source.Copy();
            bool rawFieldsCopied = serializer.Serialize(copy) == beforeCopy && !Object.ReferenceEquals(source.Elements, copy.Elements)
                && !Object.ReferenceEquals(source.Elements[0], copy.Elements[0]);
            copy.Elements[0].X = 99;
            copy.Normalize();
            checks["badge_layout_copy_deep_copies_all_raw_fields_before_fixed_normalization"] = rawFieldsCopied
                && serializer.Serialize(source) == beforeCopy && copy.Width == 320 && copy.Height == 82
                && !Find(copy, "countdown").Visible && VisibleElementsFit(copy);
            var missingList = new BadgeLayoutSettings { Width = 555, Height = 222, Elements = null };
            BadgeLayoutSettings missingCopy = missingList.Copy();
            bool retainedNull = missingCopy.Elements == null && missingCopy.Width == 555 && missingCopy.Height == 222;
            missingCopy.Normalize();
            checks["badge_layout_copy_preserves_null_list_until_normalized"] = retainedNull && missingList.Elements == null
                && serializer.Serialize(missingCopy) == serializer.Serialize(defaults);
            checks["badge_layout_element_height_stays_finite_and_keeps_fixed_rows_separate"] = BadgeLayoutSettings.ElementHeight(
                new BadgeElement { Key = "countdown", FontSize = Single.NaN }) == 25
                && BadgeLayoutSettings.ElementHeight(null) == 21
                && BadgeLayoutSettings.ElementHeight(new BadgeElement { FontSize = Single.PositiveInfinity }) == 21
                && BadgeLayoutSettings.ElementHeight(new BadgeElement { FontSize = Single.MinValue }) > 0
                && BadgeLayoutSettings.ElementHeight(new BadgeElement { FontSize = Single.MaxValue }) == 48
                && 6 + BadgeLayoutSettings.ElementHeight(Find(defaults, "remaining")) <= Find(defaults, "updated").Y;
            CheckTransparency(checks, serializer);
        }

        private static void CheckTransparency(Dictionary<string, bool> checks, JavaScriptSerializer serializer)
        {
            bool accepted = new BadgeLayoutSettings().BackgroundTransparency == 0;
            foreach (int value in new[] { 0, 50, 100 })
            {
                var layout = new BadgeLayoutSettings { BackgroundTransparency = value };
                layout.Normalize();
                accepted &= layout.BackgroundTransparency == value;
            }
            checks["badge_background_transparency_defaults_opaque_and_accepts_zero_half_full"] = accepted;

            bool clamped = true;
            foreach (int value in new[] { Int32.MinValue, -1, 101, Int32.MaxValue })
            {
                var layout = new BadgeLayoutSettings { BackgroundTransparency = value };
                layout.Normalize();
                clamped &= layout.BackgroundTransparency == (value < 0 ? 0 : 100);
            }
            checks["badge_background_transparency_clamps_invalid_extremes_without_overflow"] = clamped;

            int[] legacyValues = { 0, 4, 5, 9, 14, 15, 54, 55, 82, 84, 85, 94, 95, 100 };
            int[] expectedValues = { 0, 0, 10, 10, 10, 20, 50, 60, 80, 80, 90, 90, 100, 100 };
            bool rounded = true;
            for (int index = 0; index < legacyValues.Length; index++)
            {
                var layout = new BadgeLayoutSettings { BackgroundTransparency = legacyValues[index] };
                layout.Normalize();
                rounded &= layout.BackgroundTransparency == expectedValues[index];
                layout.Normalize();
                rounded &= layout.BackgroundTransparency == expectedValues[index];
            }
            checks["badge_background_transparency_rounds_to_tens_with_midpoints_up_and_is_idempotent"] = rounded;

            var source = new BadgeLayoutSettings { BackgroundTransparency = Int32.MaxValue };
            BadgeLayoutSettings copy = source.Copy();
            bool rawPreserved = copy.BackgroundTransparency == Int32.MaxValue;
            copy.Normalize();
            var absentElements = new BadgeLayoutSettings { BackgroundTransparency = -20, Elements = null };
            BadgeLayoutSettings absentCopy = absentElements.Copy();
            checks["badge_background_transparency_copy_preserves_raw_value_before_normalization"] = rawPreserved
                && source.BackgroundTransparency == Int32.MaxValue && copy.BackgroundTransparency == 100
                && absentCopy.BackgroundTransparency == -20 && absentCopy.Elements == null;

            BadgeLayoutSettings legacy = serializer.Deserialize<BadgeLayoutSettings>("{\"Elements\":[{\"Key\":\"weekly-reset\",\"Visible\":false},{\"Key\":\"countdown\",\"Visible\":true}]}");
            legacy.Normalize();
            checks["badge_background_transparency_missing_legacy_json_stays_opaque_and_keeps_choices"] = legacy.BackgroundTransparency == 0
                && !Find(legacy, "weekly-reset").Visible && Find(legacy, "countdown").Visible && legacy.Height == 58;
            bool oldPercentages = true;
            foreach (int value in new[] { 82, 85 })
            {
                BadgeLayoutSettings restored = serializer.Deserialize<BadgeLayoutSettings>("{\"BackgroundTransparency\":" + value
                    + ",\"Elements\":[{\"Key\":\"weekly-reset\",\"Visible\":false},{\"Key\":\"countdown\",\"Visible\":true}]}");
                restored.Normalize();
                oldPercentages &= restored.BackgroundTransparency == (value == 82 ? 80 : 90)
                    && !Find(restored, "weekly-reset").Visible && Find(restored, "countdown").Visible;
            }
            checks["badge_background_transparency_legacy_json_82_and_85_migrate_without_changing_options"] = oldPercentages;

            bool roundTrips = true;
            for (int mask = 0; mask < 4; mask++)
            {
                var layout = new BadgeLayoutSettings { BackgroundTransparency = 50 };
                Find(layout, "weekly-reset").Visible = (mask & 1) != 0;
                Find(layout, "countdown").Visible = (mask & 2) != 0;
                BadgeLayoutSettings restored = serializer.Deserialize<BadgeLayoutSettings>(serializer.Serialize(layout));
                restored.Normalize();
                roundTrips &= restored.BackgroundTransparency == 50
                    && Find(restored, "weekly-reset").Visible == ((mask & 1) != 0)
                    && Find(restored, "countdown").Visible == ((mask & 2) != 0);
            }
            checks["badge_background_transparency_roundtrips_with_all_date_countdown_choices"] = roundTrips;
        }

        private static BadgeElement Find(BadgeLayoutSettings layout, string key)
        {
            return layout.Elements.Find(element => element != null && element.Key == key);
        }

        private static bool Is(BadgeElement item, string key, int x, int y, int width, float fontSize, bool bold, bool visible)
        {
            return item != null && item.Key == key && item.X == x && item.Y == y && item.Width == width
                && item.FontSize == fontSize && item.Bold == bold && item.Visible == visible;
        }

        private static bool VisibleElementsFit(BadgeLayoutSettings layout)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (BadgeElement element in layout.Elements)
            {
                if (element == null || !keys.Add(element.Key)) return false;
                if (!element.Visible) continue;
                if (element.X < 0 || element.Y < 0 || element.Width < 1 || element.X + element.Width > layout.Width
                    || element.Y + BadgeLayoutSettings.ElementHeight(element) > layout.Height
                    || Single.IsNaN(element.FontSize) || Single.IsInfinity(element.FontSize)) return false;
            }
            return keys.Count == 4 && !keys.Contains("indicator");
        }

        private static bool VisibleElementsDoNotOverlap(BadgeLayoutSettings layout)
        {
            for (int i = 0; i < layout.Elements.Count; i++)
            {
                BadgeElement first = layout.Elements[i];
                if (!first.Visible) continue;
                for (int j = i + 1; j < layout.Elements.Count; j++)
                {
                    BadgeElement second = layout.Elements[j];
                    if (second.Visible && first.X < second.X + second.Width && second.X < first.X + first.Width
                        && first.Y < second.Y + BadgeLayoutSettings.ElementHeight(second)
                        && second.Y < first.Y + BadgeLayoutSettings.ElementHeight(first)) return false;
                }
            }
            return true;
        }
    }
}
