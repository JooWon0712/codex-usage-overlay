using System;
using System.Collections.Generic;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class ThemeChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            checks["light_palette_matches_selected_neutral_ink_design"] = PaletteMatches(VisualTheme.Light, new[]
            {
                0xCFD3D8, 0xDBDEE2, 0xA7AFB9, 0x20262E, 0x444F5D, 0xC4CAD2,
                0xBAC2CD, 0x3F4C5C, 0x444F5D, 0x79510C, 0xA62B41, 0x92263A
            });
            checks["dark_palette_is_preserved_when_light_design_changes"] = PaletteMatches(VisualTheme.Dark, new[]
            {
                0x101318, 0x191D24, 0x30353E, 0xE6E8EB, 0x9DA4AE, 0x252B34,
                0x303843, 0xB4BDCA, 0x91C9B6, 0xE8B979, 0xED7482, 0xFFB3BF
            });
            foreach (var theme in new[] { VisualTheme.Dark, VisualTheme.Light })
            {
                string name = theme.IsDark ? "dark_" : "light_";
                foreach (var background in new[] { theme.Background, theme.Surface, theme.Hover, theme.Pressed })
                    checks[name + "body_text_contrast_" + background.ToArgb()] = Contrast(theme.Text, background) >= 4.5;
                checks[name + "muted_text_contrast"] = Contrast(theme.Muted, theme.Background) >= 4.5 && Contrast(theme.Muted, theme.Surface) >= 4.5;
                foreach (var state in new[] { theme.Success, theme.Attention, theme.Danger, theme.DangerText })
                    checks[name + "badge_state_contrast_" + state.ToArgb()] = Contrast(state, theme.Surface) >= 4.5;
                checks[name + "surface_keeps_transparency_key_free"] = theme.Surface.ToArgb() != Color.FromArgb(1, 1, 1).ToArgb();
            }
            var legacy = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Preferences>("{\"Position\":\"sidebar\"}");
            legacy.NormalizePosition();
            checks["old_preferences_default_to_codex_theme"] = legacy.ThemeMode == "codex" && legacy.Position == "sidebar";
            legacy.ThemeMode = "unknown";
            legacy.NormalizePosition();
            checks["invalid_theme_mode_defaults_to_codex"] = legacy.ThemeMode == "codex";
            foreach (string mode in new[] { "light", "dark", "system" })
            {
                legacy.ThemeMode = mode;
                legacy.NormalizePosition();
                checks["manual_theme_mode_preserved_" + mode] = legacy.ThemeMode == mode;
            }
        }

        private static bool PaletteMatches(VisualTheme theme, int[] expected)
        {
            Color[] actual = { theme.Background, theme.Surface, theme.Border, theme.Text, theme.Muted, theme.Hover,
                theme.Pressed, theme.Accent, theme.Success, theme.Attention, theme.Danger, theme.DangerText };
            if (actual.Length != expected.Length) return false;
            for (int index = 0; index < actual.Length; index++)
                if (actual[index].ToArgb() != Color.FromArgb((expected[index] >> 16) & 255,
                    (expected[index] >> 8) & 255, expected[index] & 255).ToArgb()) return false;
            return true;
        }

        private static double Contrast(Color first, Color second)
        {
            double a = Luminance(first), b = Luminance(second);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        private static double Luminance(Color color)
        {
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        private static double Linear(byte value)
        {
            double channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
    }
}
