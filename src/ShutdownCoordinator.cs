using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PersonalUsageOverlay
{
    internal static class ShutdownCoordinator
    {
        internal const int Busy = 2, Legacy = 3, InvalidArguments = 64;
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr job, int kind, out JobAccounting info, uint length, IntPtr returned);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(IntPtr job, uint code);
        [StructLayout(LayoutKind.Sequential)]
        private struct JobAccounting
        {
            public long User, Kernel, PeriodUser, PeriodKernel;
            public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
        }

        internal sealed class Owner : IDisposable
        {
            internal readonly IntPtr Handle;
            internal readonly uint Pid;
            internal readonly long Created;
            internal readonly string Path;
            internal Owner(uint pid)
            {
                Pid = pid;
                Handle = OpenProcess(0x100000 | 0x1000, false, pid); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
                if (Handle == IntPtr.Zero) throw new IOException("Cannot verify process identity.");
                try
                {
                    Created = ProcessCreationTime(Handle);
                    var path = new StringBuilder(32768); uint size = (uint)path.Capacity;
                    if (!QueryFullProcessImageName(Handle, 0, path, ref size)) throw new IOException("Cannot verify executable path.");
                    Path = System.IO.Path.GetFullPath(path.ToString());
                }
                catch { Dispose(); throw; }
            }
            internal bool Exited { get { return WaitForSingleObject(Handle, 0) == 0; } }
            internal bool Wait(int timeout) { return WaitForSingleObject(Handle, (uint)Math.Max(0, timeout)) == 0; }
            internal bool CleanExit
            {
                get { uint code; return Exited && GetExitCodeProcess(Handle, out code) && code == 0; }
            }
            public void Dispose() { Native.CloseHandle(Handle); }
        }

        internal static long ProcessCreationTime(IntPtr handle)
        {
            long created, exited, kernel, user;
            if (!GetProcessTimes(handle, out created, out exited, out kernel, out user)) throw new IOException("Cannot verify process creation time.");
            return created;
        }

        internal static bool TryPrepareArguments(string[] args, out string target, out int timeout, out bool noClose)
        {
            target = null; timeout = 0; noClose = false;
            if (args == null || args.Length != 5 && args.Length != 6 || args[0] != "--prepare-install" || args[1] != "--target"
                || args[3] != "--wait" || !Int32.TryParse(args[4], out timeout) || timeout < 1000 || timeout > 60000
                || args.Length == 6 && args[5] != "--no-close") return false;
            try
            {
                target = NormalizeTarget(args[2]); noClose = args.Length == 6; return true;
            }
            catch { return false; }
        }

        internal static string NormalizeTarget(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || !System.IO.Path.IsPathRooted(value)
                || value.Length < 3 || value[1] != ':' || (value[2] != '\\' && value[2] != '/'))
                throw new ArgumentException("An absolute local installation directory is required.");
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(value, "UsageOverlay.exe"));
        }

        internal static bool ValidIdentity(string packet, uint pid, long created)
        {
            return packet == "CUO-LIFETIME-1|" + pid.ToString(CultureInfo.InvariantCulture) + "|"
                + created.ToString(CultureInfo.InvariantCulture) + "|drain";
        }

        private static Owner ConnectOwner(int timeout)
        {
            var clock = Stopwatch.StartNew();
            using (var pipe = new NamedPipeClientStream(".", InstanceLifetime.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(Math.Min(1000, timeout));
                uint pid, session;
                using (var current = Process.GetCurrentProcess())
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out pid)
                        || !ProcessIdToSessionId(pid, out session) || session != current.SessionId)
                        throw new IOException("Cannot verify lifetime endpoint.");
                var owner = new Owner(pid);
                try
                {
                    if (!String.Equals(System.IO.Path.GetFileName(owner.Path), "UsageOverlay.exe", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Unexpected lifetime executable.");
                    byte[] bytes = new byte[128]; int count = 0;
                    while (count < bytes.Length)
                    {
                        IAsyncResult read = pipe.BeginRead(bytes, count, 1, null, null);
                        int obtained;
                        using (read.AsyncWaitHandle)
                        {
                            if (!read.AsyncWaitHandle.WaitOne(Math.Max(0, Math.Min(timeout, 1000) - (int)clock.ElapsedMilliseconds)))
                                throw new IOException("Lifetime identity timed out.");
                            obtained = pipe.EndRead(read);
                        }
                        if (obtained == 0) throw new IOException("Lifetime identity was incomplete.");
                        if (bytes[count++] == 10) break;
                    }
                    if (count == 0 || bytes[count - 1] != 10 || !ValidIdentity(Encoding.ASCII.GetString(bytes, 0, count - 1), pid, owner.Created))
                        throw new IOException("Lifetime identity did not match its process.");
                    pipe.WriteByte(1);
                    return owner;
                }
                catch { owner.Dispose(); throw; }
            }
        }

        private static bool InstanceBusy()
        {
            try
            {
                using (var mutex = Mutex.OpenExisting(AppIdentity.SingleInstance))
                {
                    if (!AppIdentity.Acquire(mutex, 0)) return true;
                    mutex.ReleaseMutex(); return false;
                }
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }

        private static List<Owner> InstalledProcesses(string target)
        {
            var found = new List<Owner>();
            try
            {
                using (var current = Process.GetCurrentProcess())
                foreach (var process in Process.GetProcessesByName("UsageOverlay"))
                using (process)
                {
                    if (process.Id == current.Id) continue;
                    Owner owner = null;
                    try
                    {
                        owner = new Owner((uint)process.Id);
                        if (String.Equals(owner.Path, target, StringComparison.OrdinalIgnoreCase)) { found.Add(owner); owner = null; }
                    }
                    catch (ArgumentException) { }
                    catch (IOException) { if (!process.HasExited && process.SessionId == current.SessionId) throw; }
                    finally { if (owner != null) owner.Dispose(); }
                }
                return found;
            }
            catch { foreach (var owner in found) owner.Dispose(); throw; }
        }

        // The setup owns UpdateLock while calling this and replacing files.
        // This bridge never kills another process or treats a released mutex as exit proof.
        internal static int Prepare(string target, int timeout, bool noClose)
        {
            var clock = Stopwatch.StartNew();
            var installed = new List<Owner>(); Owner owner = null;
            try
            {
                installed = InstalledProcesses(target);
                bool busy = InstanceBusy();
                if (noClose) return busy || installed.Exists(item => !item.Exited) ? Busy : 0;
                if (busy || installed.Exists(item => !item.Exited))
                {
                    try { owner = ConnectOwner(Remaining(clock, timeout)); }
                    catch { return Legacy; } // Unproved legacy owners require the user's normal X/Exit.
                    if (!owner.Exited)
                    {
                        try { using (var signal = EventWaitHandle.OpenExisting(AppIdentity.Shutdown)) signal.Set(); }
                        catch (WaitHandleCannotBeOpenedException)
                        {
                            // The verified owner may already be draining its final exit.
                            // Its process handle and exit code, not the missing event, decide success.
                        }
                    }
                    if (!owner.Wait(Remaining(clock, timeout))) return Busy;
                    if (!owner.CleanExit) return 5;
                }
                foreach (var process in installed)
                    if (!process.Wait(Remaining(clock, timeout))) return Busy;
                return InstanceBusy() ? Busy : 0;
            }
            catch { return 1; }
            finally { if (owner != null) owner.Dispose(); foreach (var process in installed) process.Dispose(); }
        }

        private static int Remaining(Stopwatch clock, int timeout)
        { return Math.Max(0, timeout - (int)clock.ElapsedMilliseconds); }

        internal static bool DrainOwnedJob(IntPtr job, int timeout)
        {
            if (job == IntPtr.Zero) return true;
            var clock = Stopwatch.StartNew(); bool terminated = false;
            do
            {
                JobAccounting info;
                if (!QueryInformationJobObject(job, 1, out info, (uint)Marshal.SizeOf(typeof(JobAccounting)), IntPtr.Zero)) return false;
                if (info.ActiveProcesses == 0) return true;
                if (!terminated)
                {
                    if (!TerminateJobObject(job, 1)) return false; // This handle is only our own worker job.
                    terminated = true;
                }
                Thread.Sleep(20);
            } while (clock.ElapsedMilliseconds < timeout);
            return false;
        }
    }
}
