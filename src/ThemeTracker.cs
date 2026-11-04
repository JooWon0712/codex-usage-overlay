using System;
using System.Threading;

namespace PersonalUsageOverlay
{
    internal sealed class ThemeTracker : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private ThemeSnapshot snapshot;
        private DateTime observedAt = DateTime.MinValue, nextRead = DateTime.MinValue;
        private bool stopped, useCodex, pending;
        private long generation;

        public ThemeTracker()
        {
            var thread = new Thread(ReadLoop) { IsBackground = true, Name = "Usage Overlay theme" };
            thread.Start();
        }

        public ThemeSnapshot GetOrRequest(bool codex)
        {
            lock (gate)
            {
                if (!stopped && (useCodex != codex || DateTime.UtcNow >= nextRead))
                {
                    if (useCodex != codex) { generation++; snapshot = null; }
                    useCodex = codex;
                    pending = true;
                    nextRead = DateTime.UtcNow.AddSeconds(2);
                    wake.Set();
                }
                if (snapshot == null)
                    return new ThemeSnapshot { IsDark = true, Status = "테마 확인 중 · 기본 다크", Source = "pending" };
                if (DateTime.UtcNow - observedAt >= TimeSpan.FromSeconds(6))
                    return new ThemeSnapshot { IsDark = snapshot.IsDark, Status = "테마 조회 지연 · 이전 스타일 유지", Source = "stale" };
                return snapshot;
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (true)
                {
                    wake.WaitOne();
                    bool codex;
                    long requested;
                    lock (gate)
                    {
                        if (stopped) return;
                        if (!pending) continue;
                        pending = false;
                        codex = useCodex;
                        requested = generation;
                    }
                    ThemeSnapshot result;
                    try { result = codex ? ThemeReader.Read() : ThemeReader.ReadSystem(); }
                    catch { result = new ThemeSnapshot { IsDark = true, Status = "테마 확인 불가 · 기본 다크", Source = "unavailable" }; }
                    lock (gate)
                    {
                        if (!stopped && requested == generation)
                        {
                            snapshot = result;
                            observedAt = DateTime.UtcNow;
                        }
                    }
                }
            }
            finally { wake.Dispose(); }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                pending = false;
                wake.Set();
            }
            // Never make the UI or X wait for a file/registry read.
        }
    }
}
