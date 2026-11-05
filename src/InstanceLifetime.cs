using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace PersonalUsageOverlay
{
    // A per-user/session identity endpoint, not an instruction or data channel.
    // The installer captures a real process handle before requesting shutdown.
    internal sealed class InstanceLifetime : IDisposable
    {
        private readonly object gate = new object();
        private NamedPipeServerStream listener;
        private bool stopped;
        private static InstanceLifetime current;
        internal bool WorkDrained { get; private set; }

        internal static string PipeName
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                using (var process = Process.GetCurrentProcess())
                    return "PersonalCodexUsageOverlayLifetime-" + identity.User.Value + "-" + process.SessionId;
            }
        }

        internal InstanceLifetime()
        {
            listener = CreatePipe();
            current = this;
            new Thread(Serve) { IsBackground = true, Name = "Usage Overlay lifetime identity" }.Start();
        }

        private static NamedPipeServerStream CreatePipe()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            using (var identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 512, 512, security);
        }

        private void Serve()
        {
            while (true)
            {
                NamedPipeServerStream pipe;
                lock (gate) { if (stopped) return; pipe = listener; }
                try
                {
                    pipe.WaitForConnection();
                    using (var process = Process.GetCurrentProcess())
                    {
                        string packet = "CUO-LIFETIME-1|" + process.Id.ToString(CultureInfo.InvariantCulture) + "|"
                            + ShutdownCoordinator.ProcessCreationTime(process.Handle).ToString(CultureInfo.InvariantCulture) + "|drain\n";
                        byte[] bytes = Encoding.ASCII.GetBytes(packet);
                        pipe.Write(bytes, 0, bytes.Length);
                        // An acknowledgement keeps Disconnect from discarding unread identity bytes.
                        byte[] ack = new byte[1];
                        IAsyncResult read = pipe.BeginRead(ack, 0, 1, null, null);
                        using (read.AsyncWaitHandle)
                            if (read.AsyncWaitHandle.WaitOne(1000)) pipe.EndRead(read);
                    }
                }
                catch (Exception) { }
                finally { pipe.Dispose(); }
                lock (gate)
                {
                    if (stopped) return;
                    try { listener = CreatePipe(); }
                    catch { return; } // Missing identity must fail closed in an installer.
                }
            }
        }

        internal static void ConfirmOwnedWorkDrained(bool verified)
        {
            if (current != null) current.WorkDrained = verified;
            if (!verified) Environment.ExitCode = 5;
        }

        public void Dispose()
        {
            lock (gate)
            {
                stopped = true;
                if (listener != null) listener.Dispose();
                if (ReferenceEquals(current, this)) current = null;
            }
        }
    }
}
