using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PersonalUsageOverlay
{
    internal sealed class ThemeSnapshot
    {
        public bool IsDark;
        public string Status, Source;
    }

    // This is a read-only observation of the installed app's local preference,
    // not a public contract for its merged or in-memory settings. Call off the UI thread.
    internal static class ThemeReader
    {
        internal const int MaxConfigBytes = 256 * 1024;

        public static ThemeSnapshot Read()
        {
            try
            {
                string path = ResolveConfigPath(Environment.GetEnvironmentVariable("CODEX_HOME"),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                return ReadConfiguration(path, ReadWindowsPreference);
            }
            catch (Exception) { return Fallback(ReadWindowsPreference); }
        }

        public static ThemeSnapshot ReadSystem()
        {
            return SystemSnapshot(ReadWindowsPreference);
        }

        internal static string ResolveConfigPath(string codexHome, string userProfile)
        {
            string home = String.IsNullOrEmpty(codexHome) ? userProfile : codexHome;
            if (String.IsNullOrEmpty(home) || !Path.IsPathRooted(home)) return null;
            string root = Path.GetPathRoot(home);
            // C:relative and \root-relative depend on a different process's cwd/drive.
            if (String.IsNullOrEmpty(root) || root == "\\" || root == "/" || root.EndsWith(":")) return null;
            string absolute = Path.GetFullPath(home);
            return Path.Combine(String.IsNullOrEmpty(codexHome) ? Path.Combine(absolute, ".codex") : absolute, "config.toml");
        }

        internal static ThemeSnapshot ReadConfiguration(string path, Func<int?> readWindows)
        {
            try
            {
                if (String.IsNullOrEmpty(path)) return Fallback(readWindows);
                byte[] data;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length > MaxConfigBytes) return Fallback(readWindows);
                    using (var bytes = new MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        int count;
                        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (bytes.Length + count > MaxConfigBytes) return Fallback(readWindows);
                            bytes.Write(buffer, 0, count);
                        }
                        data = bytes.ToArray();
                    }
                }
                return Parse(new UTF8Encoding(false, true).GetString(data), readWindows);
            }
            catch (Exception) { return Fallback(readWindows); }
        }

        internal static ThemeSnapshot Parse(string text, Func<int?> readWindows)
        {
            try
            {
                if (text == null || text.Length > MaxConfigBytes) return Fallback(readWindows);
                string mode = new PreferenceParser(text).Read();
                if (mode == "light" || mode == "dark")
                    return new ThemeSnapshot { IsDark = mode == "dark", Status = "Codex · " + Label(mode == "dark"), Source = "config" };
                int? light = SafeWindowsPreference(readWindows);
                bool dark = light != 1;
                return new ThemeSnapshot
                {
                    IsDark = dark,
                    Status = light.HasValue ? "Codex · 시스템(" + Label(dark) + ")" : "Codex · 시스템 확인 불가 · 기본 다크",
                    Source = (mode == null ? "default" : "config") + (light.HasValue ? "+windows" : "+default")
                };
            }
            catch (Exception) { return Fallback(readWindows); }
        }

        internal static ThemeSnapshot SystemSnapshot(Func<int?> readWindows)
        {
            int? light = SafeWindowsPreference(readWindows);
            return new ThemeSnapshot
            {
                IsDark = light != 1,
                Status = light.HasValue ? "Windows · " + Label(light != 1) : "Windows 확인 불가 · 기본 다크",
                Source = light.HasValue ? "windows" : "default"
            };
        }

        private static ThemeSnapshot Fallback(Func<int?> readWindows)
        {
            int? light = SafeWindowsPreference(readWindows);
            return new ThemeSnapshot
            {
                IsDark = light != 1,
                Status = light.HasValue ? "Codex 확인 불가 · Windows " + Label(light != 1) : "테마 확인 불가 · 기본 다크",
                Source = light.HasValue ? "fallback+windows" : "fallback+default"
            };
        }

        private static int? SafeWindowsPreference(Func<int?> provider)
        {
            try
            {
                int? value = provider == null ? null : provider();
                return value == 0 || value == 1 ? value : null;
            }
            catch (Exception) { return null; }
        }

        private static int? ReadWindowsPreference()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
            {
                object value = key == null ? null : key.GetValue("AppsUseLightTheme", null);
                return value is int ? (int?)value : null;
            }
        }

        private static string Label(bool dark) { return dark ? "다크" : "라이트"; }

        // A deliberately limited TOML reader. Unsupported constructs fail the whole
        // observation rather than making a hidden/ambiguous key look absent. No content is logged.
        private sealed class PreferenceParser
        {
            private readonly string text;
            private int at, values;
            private string mode;
            private List<string> section = new List<string>();
            private readonly HashSet<string> tables = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            private static readonly Regex Number = new Regex(@"^[+-]?(?:0|[1-9](?:_?[0-9])*)(?:\.[0-9](?:_?[0-9])*)?(?:[eE][+-]?[0-9](?:_?[0-9])*)?$|^0x[0-9A-Fa-f](?:_?[0-9A-Fa-f])*$|^0o[0-7](?:_?[0-7])*$|^0b[01](?:_?[01])*$|^[+-]?(?:inf|nan)$", RegexOptions.CultureInvariant);

            public PreferenceParser(string input) { text = input; }

            public string Read()
            {
                if (Peek() == '\uFEFF') at++;
                while (true)
                {
                    Space(true);
                    if (at == text.Length) return mode;
                    if (Peek() == '[') ReadTable();
                    else ReadAssignment();
                    Space(false);
                    if (at < text.Length && Peek() != '\r' && Peek() != '\n') Bad();
                }
            }

            private void ReadTable()
            {
                at++;
                if (Peek() == '[') Bad(); // Array tables are outside this reader's supported subset.
                List<string> path = KeyPath();
                Expect(']');
                string key = Join(path);
                if (tables.Count >= 2048 || !tables.Add(key) || Conflicts(keys, key)
                    || (path.Count >= 2 && path[0] == "desktop" && path[1] == "appearanceTheme")) Bad();
                section = path;
            }

            private void ReadAssignment()
            {
                List<string> path = new List<string>(section);
                path.AddRange(KeyPath());
                Expect('=');
                string key = Join(path);
                if (keys.Count >= 2048 || Conflicts(keys, key) || ContainsOrBelow(tables, key)) Bad();
                keys.Add(key);
                // Inline desktop tables or descendants of appearanceTheme cannot be
                // silently mistaken for the absence of the scalar appearance preference.
                if ((path.Count == 1 && path[0] == "desktop") || IsThemePrefix(path)) Bad();
                string value = Value(0);
                if (path.Count == 2 && path[0] == "desktop" && path[1] == "appearanceTheme")
                {
                    if (value != "light" && value != "dark" && value != "system") Bad();
                    mode = value;
                }
            }

            private List<string> KeyPath()
            {
                var path = new List<string>();
                do
                {
                    Space(false);
                    if (Peek() == '"' || Peek() == '\'') path.Add(StringValue());
                    else
                    {
                        int start = at;
                        while (at < text.Length && BareKey(Peek())) at++;
                        if (at == start) Bad();
                        path.Add(text.Substring(start, at - start));
                    }
                    Space(false);
                    if (Peek() != '.') return path;
                    at++;
                    if (path.Count > 32) Bad();
                } while (true);
            }

            private string Value(int depth)
            {
                if (depth > 32 || ++values > 16384) Bad();
                Space(false);
                if (Peek() == '"' || Peek() == '\'') return StringValue();
                if (Peek() == '[')
                {
                    at++;
                    Space(true);
                    while (Peek() != ']')
                    {
                        Value(depth + 1);
                        Space(true);
                        if (Peek() != ',') break;
                        at++;
                        Space(true);
                    }
                    Expect(']');
                    return null;
                }
                if (Peek() == '{')
                {
                    at++;
                    Space(false);
                    var inline = new HashSet<string>(StringComparer.Ordinal);
                    while (Peek() != '}')
                    {
                        string key = Join(KeyPath());
                        if (Conflicts(inline, key)) Bad();
                        inline.Add(key);
                        Expect('=');
                        Value(depth + 1);
                        Space(false);
                        if (Peek() != ',') break;
                        at++;
                        Space(false);
                        if (Peek() == '}') Bad();
                    }
                    Expect('}');
                    return null;
                }
                int start = at;
                while (at < text.Length && " \t\r\n#,]}".IndexOf(Peek()) < 0) at++;
                string token = text.Substring(start, at - start);
                if (token != "true" && token != "false" && !Number.IsMatch(token)) Bad();
                return null; // Dates and other unsupported scalar forms cause an explicit fallback.
            }

            private string StringValue()
            {
                char quote = Peek();
                at++;
                if (Peek() == quote && at + 1 < text.Length && text[at + 1] == quote) Bad();
                var result = new StringBuilder();
                while (at < text.Length)
                {
                    char c = text[at++];
                    if (c == quote) return result.ToString();
                    if ((c < 32 && c != '\t') || c == 127) Bad();
                    if (c != '\\' || quote == '\'') { result.Append(c); continue; }
                    if (at >= text.Length) Bad();
                    char escape = text[at++];
                    switch (escape)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case 'b': result.Append('\b'); break;
                        case 't': result.Append('\t'); break;
                        case 'n': result.Append('\n'); break;
                        case 'f': result.Append('\f'); break;
                        case 'r': result.Append('\r'); break;
                        case 'u': case 'U':
                            int digits = escape == 'u' ? 4 : 8;
                            if (text.Length - at < digits) Bad();
                            uint code;
                            if (!UInt32.TryParse(text.Substring(at, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)
                                || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) Bad();
                            at += digits;
                            result.Append(Char.ConvertFromUtf32((int)code));
                            break;
                        default: Bad(); break;
                    }
                }
                Bad();
                return null;
            }

            private void Space(bool lines)
            {
                while (at < text.Length)
                {
                    char c = Peek();
                    if (c == ' ' || c == '\t' || (lines && (c == '\r' || c == '\n'))) { at++; continue; }
                    if (c != '#') return;
                    while (at < text.Length && Peek() != '\r' && Peek() != '\n')
                    {
                        if ((Peek() < 32 && Peek() != '\t') || Peek() == 127) Bad();
                        at++;
                    }
                    if (!lines) return;
                }
            }

            private void Expect(char c) { Space(false); if (Peek() != c) Bad(); at++; }
            private char Peek() { return at < text.Length ? text[at] : '\0'; }
            private static bool BareKey(char c) { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-'; }
            private static string Join(List<string> path) { return String.Join("\u001F", path.ToArray()); }
            private static bool IsThemePrefix(List<string> path) { return path.Count >= 2 && path[0] == "desktop" && path[1] == "appearanceTheme" && path.Count != 2; }
            private static bool ContainsOrBelow(HashSet<string> items, string key)
            {
                foreach (string item in items) if (item == key || item.StartsWith(key + "\u001F", StringComparison.Ordinal)) return true;
                return false;
            }
            private static bool Conflicts(HashSet<string> items, string key)
            {
                foreach (string item in items)
                    if (item == key || item.StartsWith(key + "\u001F", StringComparison.Ordinal) || key.StartsWith(item + "\u001F", StringComparison.Ordinal)) return true;
                return false;
            }
            private static void Bad() { throw new FormatException("Unsupported or invalid theme configuration."); }
        }
    }
}
