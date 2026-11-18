using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PersonalUsageOverlay
{
    internal static class InstalledCodex
    {
        private const string PackageName = "OpenAI.Codex";
        private const string PublisherId = "2p2nqsd0c76g0";
        private const int InsufficientBuffer = 122;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackagesByPackageFamily(string familyName, ref uint count,
            [Out] IntPtr[] fullNames, ref uint bufferLength, IntPtr buffer);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackagePathByFullName(string fullName, ref uint pathLength, StringBuilder path);

        public static string FindCli()
        {
            string directory = FindPackageDirectory();
            if (directory == null) return null;
            string cli = Path.Combine(directory, "app", "resources", "codex.exe");
            return File.Exists(cli) ? cli : null;
        }

        internal static string FindPackageDirectory()
        {
            return FindPackageDirectory(PackageName, PublisherId);
        }

        internal static string FindPackageDirectory(string packageName, string publisherId)
        {
            // A Store update changes the package full name, but not its family.
            // Windows returns only registrations for the current user.
            string familyName = packageName + "_" + publisherId;
            uint count = 0, length = 0;
            if (GetPackagesByPackageFamily(familyName, ref count, null, ref length, IntPtr.Zero) != InsufficientBuffer
                || count == 0 || count > 1024 || length == 0 || length > 262144)
                return null;

            var names = new IntPtr[count];
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length * sizeof(char)));
            try
            {
                if (GetPackagesByPackageFamily(familyName, ref count, names, ref length, buffer) != 0)
                    return null;

                string latest = null;
                Version latestVersion = null;
                for (int i = 0; i < count; i++)
                {
                    string name = Marshal.PtrToStringUni(names[i]);
                    if (name == null)
                        continue;
                    string[] parts = name.Split('_');
                    Version version;
                    if (parts.Length != 5 || !parts[0].Equals(packageName, StringComparison.OrdinalIgnoreCase)
                        || !parts[2].Equals("x64", StringComparison.OrdinalIgnoreCase) || parts[3].Length != 0
                        || !parts[4].Equals(publisherId, StringComparison.OrdinalIgnoreCase)
                        || !Version.TryParse(parts[1], out version) || version.Revision < 0)
                        continue;
                    if (latestVersion == null || version > latestVersion)
                    {
                        latest = name;
                        latestVersion = version;
                    }
                }

                return latest == null ? null : GetPackageDirectory(latest);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string GetPackageDirectory(string fullName)
        {
            uint length = 0;
            if (GetPackagePathByFullName(fullName, ref length, null) != InsufficientBuffer
                || length == 0 || length > 32768)
                return null;
            var path = new StringBuilder((int)length);
            if (GetPackagePathByFullName(fullName, ref length, path) != 0)
                return null;

            return path.ToString();
        }
    }
}
