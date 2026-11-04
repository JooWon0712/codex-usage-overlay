using System;
using System.Collections.Generic;

namespace PersonalUsageOverlay
{
    internal static class BadgeLayoutChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            var defaults = new BadgeLayoutSettings();
            checks["badge_layout_default_canvas_and_five_elements"] = defaults.Width == 320 && defaults.Height == 82
                && defaults.Elements.Count == 5 && Fits(defaults);

            var absent = new BadgeLayoutSettings { Width = Int32.MinValue, Height = Int32.MaxValue, Elements = null };
            absent.Normalize();
            checks["badge_layout_missing_list_restores_known_elements_with_canvas_limits"] = absent.Width == 180
                && absent.Height == 300 && absent.Elements.Count == 5 && Fits(absent);

            var damaged = new BadgeLayoutSettings
            {
                Width = Int32.MaxValue,
                Height = Int32.MinValue,
                Elements = new List<BadgeElement>
                {
                    null,
                    new BadgeElement { Key = "unknown", Width = 900, Visible = true },
                    new BadgeElement { Key = "remaining", X = Int32.MaxValue, Y = Int32.MaxValue, Width = Int32.MaxValue,
                        FontSize = Single.NaN, Visible = false },
                    new BadgeElement { Key = "remaining", X = 1, Y = 2, Width = 3, FontSize = 9, Visible = true },
                    new BadgeElement { Key = "updated", X = Int32.MinValue, Y = Int32.MinValue, Width = -1,
                        FontSize = Single.PositiveInfinity, Visible = false },
                    new BadgeElement { Key = "weekly-reset", X = 900, Y = 900, Width = 900, FontSize = Single.NegativeInfinity },
                    new BadgeElement { Key = "countdown", X = -20, Y = -20, Width = 0, FontSize = 1000 },
                    new BadgeElement { Key = "indicator", X = -20, Y = 900, Width = 18, FontSize = -100 }
                }
            };
            damaged.Normalize();
            checks["badge_layout_unknown_null_and_duplicate_entries_removed"] = damaged.Elements.Count == 5
                && Find(damaged, "remaining").Width == 600;
            checks["badge_layout_required_status_fields_cannot_be_hidden"] = Find(damaged, "remaining").Visible
                && Find(damaged, "updated").Visible;
            checks["badge_layout_nonfinite_fonts_use_known_defaults"] = Find(damaged, "remaining").FontSize == 14f
                && Find(damaged, "updated").FontSize == 11f && Find(damaged, "weekly-reset").FontSize == 11f;
            checks["badge_layout_fonts_and_extreme_coordinates_stay_inside_canvas"] = damaged.Width == 600
                && damaged.Height == 60 && Find(damaged, "indicator").FontSize == 9f
                && Find(damaged, "countdown").FontSize == 28f && Fits(damaged);
            checks["badge_layout_optional_visibility_is_preserved"] = !Find(damaged, "weekly-reset").Visible
                && !Find(damaged, "countdown").Visible;
            checks["badge_layout_existing_z_order_survives_normalization"] = damaged.Elements[0].Key == "remaining"
                && damaged.Elements[1].Key == "updated" && damaged.Elements[2].Key == "weekly-reset"
                && damaged.Elements[3].Key == "countdown" && damaged.Elements[4].Key == "indicator";

            var partial = new BadgeLayoutSettings
            {
                Elements = new List<BadgeElement>
                {
                    new BadgeElement { Key = "updated", X = 12, Y = 14, Width = 99, FontSize = 10, Visible = true }
                }
            };
            partial.Normalize();
            checks["badge_layout_missing_known_fields_restored_without_losing_existing_field"] = partial.Elements.Count == 5
                && Find(partial, "updated").X == 12 && Find(partial, "updated").Y == 14
                && Find(partial, "updated").Width == 99 && Find(partial, "remaining").Bold && Fits(partial);
            checks["badge_layout_missing_elements_append_after_existing_z_order"] = partial.Elements[0].Key == "updated"
                && partial.Elements[1].Key == "indicator" && partial.Elements[2].Key == "remaining";

            var copy = partial.Copy();
            copy.Width = 200;
            Find(copy, "updated").X = 77;
            copy.Elements.RemoveAt(0);
            checks["badge_layout_copy_has_no_shared_list_or_element_mutation"] = partial.Width == 320
                && partial.Elements.Count == 5 && Find(partial, "updated").X == 12
                && !Object.ReferenceEquals(partial.Elements, copy.Elements);
            var nullListCopy = new BadgeLayoutSettings { Elements = null }.Copy();
            nullListCopy.Normalize();
            checks["badge_layout_copy_accepts_missing_json_list"] = nullListCopy.Elements.Count == 5 && Fits(nullListCopy);

            checks["badge_layout_height_handles_invalid_fonts_without_overflow"] = BadgeLayoutSettings.ElementHeight(
                new BadgeElement { Key = "indicator", FontSize = Single.NaN }) == 18
                && BadgeLayoutSettings.ElementHeight(new BadgeElement { Key = "remaining", FontSize = Single.PositiveInfinity }) == 22
                && BadgeLayoutSettings.ElementHeight(new BadgeElement { Key = "remaining", FontSize = 28 }) == 49;
        }

        private static BadgeElement Find(BadgeLayoutSettings layout, string key)
        {
            return layout.Elements.Find(element => element.Key == key);
        }

        private static bool Fits(BadgeLayoutSettings layout)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (BadgeElement element in layout.Elements)
            {
                if (element == null || !keys.Add(element.Key) || element.Width < 1 || element.FontSize < 9f
                    || element.FontSize > 28f || Single.IsNaN(element.FontSize) || Single.IsInfinity(element.FontSize)
                    || element.X < 0 || element.Y < 0 || element.X + element.Width > layout.Width
                    || element.Y + BadgeLayoutSettings.ElementHeight(element) > layout.Height)
                    return false;
            }

            return true;
        }
    }
}
