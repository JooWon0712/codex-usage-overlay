using System;
using System.Collections.Generic;
using System.IO;

namespace PersonalUsageOverlay
{
    internal static class RuntimeResolverChecks
    {
        public static void Run(Dictionary<string, bool> checks)
        {
            string root = Path.Combine(Path.GetTempPath(), "CodexUsageOverlay-check-" + Guid.NewGuid().ToString("N"));
            string app = Path.Combine(root, "app");
            string moved = Path.Combine(root, "moved");
            string search = Path.Combine(root, "runtime 경로");
            var folders = new[] { root, app, moved, search };
            var files = new List<string>();
            try
            {
                foreach (string folder in folders)
                    Directory.CreateDirectory(folder);
                foreach (string folder in new[] { app, moved, search })
                    foreach (string name in new[] { "node.exe", "codex.exe" })
                    {
                        string file = Path.Combine(folder, name);
                        File.WriteAllText(file, "test fixture; never executed");
                        files.Add(file);
                    }
                var absolute = RuntimeResolver.Resolve(new RuntimePaths { Node = files[0], Codex = files[1] }, moved, search);
                checks["configured_absolute_runtime_wins_path"] = absolute.Node == files[0] && absolute.Codex == files[1];
                var relative = RuntimeResolver.Resolve(new RuntimePaths { Node = "node.exe", Codex = "codex.exe" }, app, search);
                checks["relative_runtime_uses_executable_folder"] = relative.Node == files[0] && relative.Codex == files[1];
                var relocated = RuntimeResolver.Resolve(new RuntimePaths { Node = "node.exe", Codex = "codex.exe" }, moved, search);
                checks["relative_runtime_follows_moved_package"] = relocated.Node == files[2] && relocated.Codex == files[3];
                var partial = RuntimeResolver.Resolve(new RuntimePaths { Node = files[0], Codex = "missing.exe" }, app, search);
                checks["stale_runtime_field_falls_back_independently"] = partial.Node == files[0] && partial.Codex == files[5];
                var missing = RuntimeResolver.Resolve(null, app, "\"" + search + "\"");
                checks["missing_config_uses_quoted_unicode_path"] = missing.Node == files[4] && missing.Codex == files[5];
                var skipped = RuntimeResolver.Resolve(null, app, ";relative-path;Z:relative;bad\0path;" + search);
                checks["invalid_path_entries_do_not_block_valid_entry"] = skipped.Node == files[4] && skipped.Codex == files[5];
                var empty = RuntimeResolver.Resolve(null, app, ";;relative-path");
                checks["empty_and_relative_path_do_not_search_current_folder"] = empty.Node == null && empty.Codex == null;
                var malformed = RuntimeResolver.Resolve(new RuntimePaths { Node = "bad\0node.exe", Codex = "Z:codex.exe" }, app, search);
                checks["invalid_config_uses_path_fallback"] = malformed.Node == files[4] && malformed.Codex == files[5];
                string wrapper = Path.Combine(app, "codex.cmd");
                File.WriteAllText(wrapper, "not executable");
                files.Add(wrapper);
                var unsupported = RuntimeResolver.Resolve(new RuntimePaths { Codex = wrapper }, app, null);
                checks["shell_wrappers_are_not_spawned_as_executables"] = unsupported.Codex == null;
            }
            finally
            {
                foreach (string file in files)
                    File.Delete(file);
                for (int i = folders.Length - 1; i >= 0; i--)
                    if (Directory.Exists(folders[i]))
                        Directory.Delete(folders[i], false);
            }
        }
    }
}
