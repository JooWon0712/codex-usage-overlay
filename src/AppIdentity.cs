using System;
using System.IO;
using System.Threading;

namespace PersonalUsageOverlay
{
    internal static class AppIdentity
    {
        // Keep these names across releases, including compatibility with the first prototype.
        public const string SingleInstance = "Local\\PersonalCodexUsageOverlayPrototype";
        public const string OpenSettings = "Local\\PersonalCodexUsageOverlayOpenSettings";
        public const string Shutdown = "Local\\PersonalCodexUsageOverlayShutdown";
        public const string UpdateLock = "Local\\PersonalCodexUsageOverlayUpdateLock";
        public static string InstallDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexUsageOverlay");
            }
        }

        public static string Executable
        {
            get
            {
                return Path.Combine(InstallDirectory, "UsageOverlay.exe");
            }
        }

        public static string DataDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageOverlay");
            }
        }

        public static string SettingsFile
        {
            get
            {
                return Path.Combine(DataDirectory, "settings.json");
            }
        }

        public static bool IsInstalledPath(string path)
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(Executable), StringComparison.OrdinalIgnoreCase);
        }

        public static bool Acquire(Mutex mutex, int timeout)
        {
            try
            {
                return mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
    }
}
