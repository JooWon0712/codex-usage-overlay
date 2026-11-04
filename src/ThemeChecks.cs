using System;
using System.Collections.Generic;
using System.Drawing;

namespace PersonalUsageOverlay
{
    internal static class ThemeChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
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
