using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PersonalUsageOverlay
{
    internal static class ThemeReaderChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            Func<int?> light = delegate { return 1; };
            Func<int?> dark = delegate { return 0; };
            Func<int?> unavailable = delegate { return null; };
            int calls = 0;
            ThemeSnapshot explicitDark = ThemeReader.Parse("[desktop]\nappearanceTheme = \"dark\" # preference\n", delegate { calls++; return 1; });
            checks["theme_explicit_mode_ignores_windows"] = explicitDark.IsDark && explicitDark.Source == "config" && calls == 0;
            ThemeSnapshot systemLight = ThemeReader.Parse("[desktop]\nappearanceTheme = 'system'\n", light);
            checks["theme_system_resolves_windows"] = !systemLight.IsDark && systemLight.Source == "config+windows";
            ThemeSnapshot missing = ThemeReader.Parse("[other]\nappearanceTheme = 'dark'\n", light);
            checks["theme_missing_desktop_key_uses_verified_system_default"] = !missing.IsDark && missing.Source == "default+windows";
            ThemeSnapshot quoted = ThemeReader.Parse("[\"desktop\"]\n'appearanceTheme' = 'light'\n", dark);
            ThemeSnapshot dotted = ThemeReader.Parse("desktop.\"appearanceTheme\" = \"light\"\n", dark);
            checks["theme_quoted_and_dotted_keys_are_not_missed"] = !quoted.IsDark && quoted.Source == "config" && !dotted.IsDark && dotted.Source == "config";
            ThemeSnapshot comments = ThemeReader.Parse("# [desktop]\n# appearanceTheme='light'\nvalue = '# [desktop] appearanceTheme = light'\n[desktop]\nappearanceTheme='dark'\n", light);
            checks["theme_comments_and_string_contents_cannot_spoof_preference"] = comments.IsDark && comments.Source == "config";
            ThemeSnapshot triple = ThemeReader.Parse("value = '''\n[desktop]\nappearanceTheme='light'\n'''\n", dark);
            checks["theme_unsupported_multiline_string_is_explicit_fallback"] = triple.IsDark && triple.Source == "fallback+windows";
            string[] malformed = {
                "[desktop]\nappearanceTheme='dark'\nappearanceTheme='light'\n",
                "[desktop]\nappearanceTheme='dark'\n[desktop]\n",
                "[desktop]\nappearanceTheme='sepia'\n",
                "[desktop]\nappearanceTheme=true\n",
                "[desktop]\nappearanceTheme='light\n",
                "[desktop]\nappearanceTheme='light' garbage\n",
                "desktop = { appearanceTheme = 'light' }\n",
                "[desktop.appearanceTheme]\nvalue='light'\n",
                "[desktop.appearanceTheme]\n",
                "[desktop]\nappearanceTheme = \"light\"\ninvalid = word\n"
            };
            bool rejected = true;
            foreach (string fixture in malformed)
                rejected &= ThemeReader.Parse(fixture, dark).Source == "fallback+windows";
            checks["theme_ambiguous_damaged_and_unknown_values_do_not_guess"] = rejected;
            ThemeSnapshot composite = ThemeReader.Parse("\uFEFFenabled=true\nvalues = [\n 'one', # ignored\n [1, 2,],\n]\nother = { enabled = true, count = 2 }\n[desktop]\nappearanceTheme='light'\n", dark);
            checks["theme_bom_comments_and_unrelated_composite_values_are_supported"] = !composite.IsDark && composite.Source == "config";
            checks["theme_missing_windows_preference_is_disclosed"] = ThemeReader.Parse("", unavailable).Source == "default+default"
                && ThemeReader.Parse("invalid", unavailable).Source == "fallback+default"
                && ThemeReader.SystemSnapshot(unavailable).Status.Contains("확인 불가");
            checks["theme_windows_failure_or_invalid_value_does_not_escape"] = ThemeReader.SystemSnapshot(delegate { throw new IOException(); }).Source == "default"
                && ThemeReader.SystemSnapshot(delegate { return 2; }).Source == "default";
            checks["theme_system_only_uses_injected_os_preference"] = !ThemeReader.SystemSnapshot(light).IsDark && ThemeReader.SystemSnapshot(dark).IsDark;
            checks["theme_relative_codex_home_is_not_resolved_using_overlay_cwd"] = ThemeReader.ResolveConfigPath("relative", @"C:\Users\fixture") == null
                && ThemeReader.ResolveConfigPath(@"C:relative", @"C:\Users\fixture") == null
                && ThemeReader.ResolveConfigPath(@"\root-relative", @"C:\Users\fixture") == null;
            checks["theme_codex_home_and_default_user_profile_paths"] = ThemeReader.ResolveConfigPath(@"C:\custom home", @"C:\Users\fixture") == @"C:\custom home\config.toml"
                && ThemeReader.ResolveConfigPath(null, @"C:\Users\fixture") == @"C:\Users\fixture\.codex\config.toml";

            string folder = Path.Combine(Path.GetTempPath(), "CodexUsageOverlay-theme-check-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(folder, "config.toml");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, "[desktop]\nappearanceTheme='light'\n", new UTF8Encoding(true));
                using (var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                    checks["theme_shared_read_does_not_require_settings_writer_to_close"] = !ThemeReader.ReadConfiguration(file, dark).IsDark;
                File.WriteAllBytes(file, new byte[] { 0xFF, 0xFE, 0x00 });
                checks["theme_invalid_utf8_is_explicit_fallback"] = ThemeReader.ReadConfiguration(file, dark).Source == "fallback+windows";
                File.WriteAllText(file, new String(' ', ThemeReader.MaxConfigBytes + 1));
                checks["theme_oversized_configuration_is_bounded"] = ThemeReader.ReadConfiguration(file, dark).Source == "fallback+windows";
                File.Delete(file);
                checks["theme_missing_file_is_not_reported_as_success"] = ThemeReader.ReadConfiguration(file, light).Source == "fallback+windows";
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
                if (Directory.Exists(folder)) Directory.Delete(folder, false);
            }
        }
    }
}
