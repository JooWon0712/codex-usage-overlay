using System;
using System.IO;

namespace PersonalUsageOverlay
{
    internal static class RuntimeResolver
    {
        public static RuntimePaths Resolve(RuntimePaths configured, string applicationDirectory, string searchPath)
        {
            configured = configured ?? new RuntimePaths();
            return new RuntimePaths
            {
                Node = ResolveExecutable(configured.Node, "node.exe", applicationDirectory, searchPath),
                Codex = ResolveExecutable(configured.Codex, "codex.exe", applicationDirectory, searchPath)
            };
        }

        private static string ResolveExecutable(string configured, string executableName, string applicationDirectory, string searchPath)
        {
            string resolved = ExistingExecutable(configured, applicationDirectory);
            if (resolved != null)
                return resolved;
            foreach (string entry in (searchPath ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string directory = Unquote(entry);
                    // Empty or relative PATH entries must not depend on the caller's working directory.
                    if (!IsAbsolute(directory))
                        continue;
                    resolved = ExistingExecutable(Path.Combine(directory, executableName), applicationDirectory);
                    if (resolved != null)
                        return resolved;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
            return null;
        }

        private static string ExistingExecutable(string value, string applicationDirectory)
        {
            try
            {
                string path = Unquote(value);
                if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (Path.IsPathRooted(path) && !IsAbsolute(path))
                    return null;
                string fullPath = Path.GetFullPath(IsAbsolute(path) ? path : Path.Combine(applicationDirectory, path));
                return File.Exists(fullPath) ? fullPath : null;
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        private static string Unquote(string value)
        {
            string result = (value ?? "").Trim();
            return result.Length >= 2 && result[0] == '"' && result[result.Length - 1] == '"'
                ? result.Substring(1, result.Length - 2) : result;
        }

        private static bool IsAbsolute(string path)
        {
            string root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && (root.StartsWith("\\\\", StringComparison.Ordinal)
                || (root.Length >= 3 && root[1] == ':' && (root[2] == '\\' || root[2] == '/')));
        }
    }
}
