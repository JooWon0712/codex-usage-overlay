using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace PersonalUsageOverlay
{
    // Disposable hosted-CI fixture only. It does not construct the GUI, read
    // settings, authenticate, or start Node/Codex. Its only child is itself.
    internal static class LifetimeFixture
    {
        internal static int Run(string[] args)
        {
            if (!AllowedEnvironment(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT"))) return 64;
            try
            {
                if (args[0] == "--lifetime-fixture-child")
                {
                    int delay;
                    if (args.Length != 5 || args[1] != "--event" || args[3] != "--wait"
                        || !Int32.TryParse(args[4], out delay) || delay < 0 || delay > 5000) return 64;
                    using (var signal = EventWaitHandle.OpenExisting(args[2]))
                    {
                        if (!signal.WaitOne(60000)) return 2;
                        Thread.Sleep(delay); return 0;
                    }
                }
                string ready; int childDelay, exitDelay; bool legacy;
                if (!Arguments(args, out ready, out childDelay, out exitDelay, out legacy)) return 64;
                return Parent(ready, childDelay, exitDelay, legacy);
            }
            catch { return 1; }
        }

        internal static bool AllowedEnvironment(string actions, string runner)
        { return actions == "true" && runner == "github-hosted"; }

        private static bool Arguments(string[] args, out string ready, out int childDelay, out int exitDelay, out bool legacy)
        {
            ready = null; childDelay = 500; exitDelay = 0; legacy = false;
            if (args.Length < 3 || args[1] != "--ready") return false;
            ready = Path.GetFullPath(args[2]);
            if (!Path.IsPathRooted(args[2]) || !Inside(ready, Environment.GetEnvironmentVariable("RUNNER_TEMP"))
                && !Inside(ready, Environment.GetEnvironmentVariable("GITHUB_WORKSPACE"))) return false;
            for (int index = 3; index < args.Length; index++)
            {
                if (args[index] == "--legacy") legacy = true;
                else
                {
                    string option = args[index]; int delay;
                    if (++index >= args.Length || !Int32.TryParse(args[index], out delay) || delay < 0 || delay > 5000) return false;
                    if (option == "--child-delay") childDelay = delay;
                    else if (option == "--exit-delay") exitDelay = delay;
                    else return false;
                }
            }
            return true;
        }

        private static bool Inside(string path, string directory)
        {
            return !String.IsNullOrEmpty(directory)
                && path.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static int Parent(string ready, int childDelay, int exitDelay, bool legacy)
        {
            using (var update = new Mutex(false, AppIdentity.UpdateLock))
            {
                if (!AppIdentity.Acquire(update, 0)) return 4;
                bool updateOwned = true;
                try
                {
                    using (var mutex = new Mutex(false, AppIdentity.SingleInstance))
                    {
                        if (!AppIdentity.Acquire(mutex, 0)) return 4;
                        bool instanceOwned = true;
                        InstanceLifetime lifetime = null;
                        IntPtr job = IntPtr.Zero; Process child = null;
                        string childEvent = "Local\\CodexUsageOverlayFixtureChild-" + Guid.NewGuid().ToString("N");
                        using (var shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, AppIdentity.Shutdown))
                        using (var stopChild = new EventWaitHandle(false, EventResetMode.AutoReset, childEvent))
                        try
                        {
                            if (!legacy) lifetime = new InstanceLifetime();
                            job = Native.CreateJobObject(IntPtr.Zero, null);
                            var limits = new Native.JOB_LIMITS(); limits.Basic.Flags = 0x2000;
                            if (job == IntPtr.Zero || !Native.SetInformationJobObject(job, 9, ref limits, (uint)System.Runtime.InteropServices.Marshal.SizeOf(limits))) return 5;
                            using (var current = Process.GetCurrentProcess())
                            {
                                child = Process.Start(new ProcessStartInfo(current.MainModule.FileName,
                                    "--lifetime-fixture-child --event " + childEvent + " --wait " + childDelay.ToString(CultureInfo.InvariantCulture))
                                    { UseShellExecute = false, CreateNoWindow = true });
                                if (child == null || !Native.AssignProcessToJobObject(job, child.Handle)) return 5;
                                Directory.CreateDirectory(Path.GetDirectoryName(ready));
                                string pendingReady = ready + "." + Guid.NewGuid().ToString("N") + ".tmp";
                                File.WriteAllText(pendingReady, new JavaScriptSerializer().Serialize(new {
                                    parentPid = current.Id, childPid = child.Id,
                                    parentStartUtc = current.StartTime.ToUniversalTime().ToString("o"),
                                    childStartUtc = child.StartTime.ToUniversalTime().ToString("o"), legacy = legacy
                                }));
                                File.Move(pendingReady, ready);
                            }
                            update.ReleaseMutex(); updateOwned = false;
                            if (!shutdown.WaitOne(60000)) return 2;
                            stopChild.Set();
                            bool childExited = child.WaitForExit(7000);
                            bool drained = ShutdownCoordinator.DrainOwnedJob(job, 5000);
                            InstanceLifetime.ConfirmOwnedWorkDrained(childExited && drained);
                            // Deliberately release the mutex before exiting, proving
                            // that the bridge waits the real captured process handle.
                            mutex.ReleaseMutex(); instanceOwned = false;
                            Thread.Sleep(exitDelay);
                            return childExited && drained ? 0 : 5;
                        }
                        finally
                        {
                            stopChild.Set();
                            if (job != IntPtr.Zero) { ShutdownCoordinator.DrainOwnedJob(job, 5000); Native.CloseHandle(job); }
                            if (child != null)
                            {
                                try
                                {
                                    if (!child.WaitForExit(1000)) { child.Kill(); child.WaitForExit(2000); }
                                }
                                catch { }
                                child.Dispose();
                            }
                            if (lifetime != null) lifetime.Dispose();
                            if (instanceOwned) mutex.ReleaseMutex();
                        }
                    }
                }
                finally { if (updateOwned) update.ReleaseMutex(); }
            }
        }
    }
}
