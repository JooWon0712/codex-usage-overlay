using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PersonalUsageOverlay
{
    internal sealed class TitleAnchorObservation
    {
        public string Status;
        public string SearchLimit;
        public Rectangle TitleBounds, HeaderBounds, TitleMenuBounds;
        public DateTime ObservedAtUtc;
        public long ElapsedMilliseconds;
    }

    // Read-only UIA access. Call on the single background MTA worker, never the UI thread.
    internal sealed class TitleAnchorReader
    {
        private const int MaxNodes = 600, MaxDepth = 28, SearchBudgetMilliseconds = 1500;
        private const int SearchTopDips = 240;
        private const string HeaderOffsetClass = "top-[var(--app-shell-page-header-offset,0px)]";
        private const string ShellToolbarClass = "@container/app-shell-header-toolbar";
        private static readonly char[] ClassSeparators = { ' ', '\t', '\r', '\n' };
        private IntPtr cachedWindow;
        private Candidate cached;
        private DateTime cachedAtUtc;

        internal sealed class Snapshot
        {
            public ControlType Type;
            public string Name, Id, ClassName;
            public bool Offscreen;
            public Rectangle Bounds;
            public bool HasBounds;
        }

        private enum HeaderKind { None, Legacy, UnifiedMain, UnifiedShell }

        private sealed class Visit
        {
            public AutomationElement Element, Document, Header, Toolbar, TitleMenu;
            public Snapshot DocumentInfo, HeaderInfo, ToolbarInfo;
            public HeaderKind Kind;
            public int Depth;
        }

        private sealed class Candidate
        {
            public AutomationElement Title, Document, Header, Toolbar, TitleMenu;
            public Rectangle TitleBounds, HeaderBounds, TitleMenuBounds;
        }

        private sealed class SearchLimitException : Exception
        {
            public SearchLimitException(string limit) : base(limit) { }
        }

        public TitleAnchorObservation Query(IntPtr hwnd, Rectangle clientBounds, float scale)
        {
            var clock = Stopwatch.StartNew();
            if (hwnd != cachedWindow)
            {
                cached = null;
                cachedWindow = hwnd;
            }
            if (hwnd == IntPtr.Zero || clientBounds.Width <= 0 || clientBounds.Height <= 0
                || float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
            {
                cached = null;
                return Result("header_unavailable", null, clock);
            }
            try
            {
                var request = CreateRequest();
                // Reacquire periodically so a newly introduced second header is not cached away.
                if (cached != null && DateTime.UtcNow - cachedAtUtc < TimeSpan.FromSeconds(5))
                {
                    try
                    {
                        if (Refresh(cached, request, clientBounds, scale, clock))
                            return Result("ok", cached, clock);
                    }
                    catch (Exception error)
                    {
                        if (!IsProviderFailure(error)) throw;
                    }
                }
                cached = null;
                bool ambiguous;
                var found = Search(hwnd, request, clientBounds, scale, clock, out ambiguous);
                if (ambiguous) return Result("ambiguous", null, clock);
                if (found == null || !Refresh(found, request, clientBounds, scale, clock))
                    return Result("header_unavailable", null, clock);
                cached = found;
                cachedAtUtc = DateTime.UtcNow;
                return Result("ok", found, clock);
            }
            catch (SearchLimitException error)
            {
                cached = null;
                var result = Result("search_timeout", null, clock);
                result.SearchLimit = error.Message;
                return result;
            }
            catch (Exception error)
            {
                cached = null;
                if (!IsProviderFailure(error)) throw;
                return Result("provider_error", null, clock);
            }
        }

        private static CacheRequest CreateRequest()
        {
            var request = new CacheRequest { TreeScope = TreeScope.Element, TreeFilter = Automation.RawViewCondition };
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.IsOffscreenProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            return request;
        }

        private static Snapshot Read(AutomationElement element, CacheRequest request, Stopwatch clock)
        {
            CheckBudget(clock);
            var snapshot = ReadCached(element.GetUpdatedCache(request));
            CheckBudget(clock);
            return snapshot;
        }

        private static Snapshot ReadCached(AutomationElement element)
        {
            var info = element.Cached;
            var snapshot = new Snapshot
            {
                Type = info.ControlType,
                Name = (info.Name ?? "").Trim(),
                Id = info.AutomationId ?? "",
                ClassName = info.ClassName ?? "",
                Offscreen = info.IsOffscreen
            };
            Rectangle bounds;
            snapshot.HasBounds = TryRectangle(info.BoundingRectangle, out bounds);
            snapshot.Bounds = bounds;
            return snapshot;
        }

        private static Candidate Search(IntPtr hwnd, CacheRequest request, Rectangle client, float scale,
            Stopwatch clock, out bool ambiguous)
        {
            ambiguous = false;
            Candidate found = null;
            var walker = TreeWalker.RawViewWalker;
            var queue = new Queue<Visit>();
            CheckBudget(clock);
            var root = AutomationElement.FromHandle(hwnd).GetUpdatedCache(request);
            queue.Enqueue(new Visit { Element = root });
            int scheduled = 1;
            var top = new Rectangle(client.Left, client.Top, client.Width,
                Math.Min(client.Height, (int)Math.Ceiling(SearchTopDips * scale)));
            while (queue.Count > 0)
            {
                CheckBudget(clock);
                var visit = queue.Dequeue();
                var info = ReadCached(visit.Element);
                if (info.Offscreen || IsSidebar(info.ClassName) || IsConversationBody(info.ClassName)
                    || (info.HasBounds && !info.Bounds.IntersectsWith(top))) continue;

                // Nested web documents belong to embedded panels. The session header lives in the outer document.
                if (info.Type == ControlType.Document && visit.Document != null) continue;
                if (info.Type == ControlType.Document && info.Id == "RootWebArea")
                {
                    visit.Document = visit.Element;
                    visit.DocumentInfo = info;
                    visit.Header = null; visit.HeaderInfo = null;
                    visit.Kind = HeaderKind.None;
                    visit.Toolbar = null; visit.ToolbarInfo = null;
                    visit.TitleMenu = null;
                }
                var headerKind = visit.Document == null ? HeaderKind.None : ClassifyHeader(info.ClassName);
                if (headerKind != HeaderKind.None)
                {
                    visit.Header = visit.Element;
                    visit.HeaderInfo = info;
                    visit.Kind = headerKind;
                    visit.Toolbar = null; visit.ToolbarInfo = null;
                    visit.TitleMenu = null;
                }
                if (IsToolbar(info, visit.Kind))
                {
                    visit.Toolbar = visit.Element;
                    visit.ToolbarInfo = info;
                    visit.TitleMenu = null;
                }
                if ((visit.Kind == HeaderKind.UnifiedMain || visit.Toolbar != null) && IsTitleMenu(info))
                    visit.TitleMenu = visit.Element;
                if (visit.HeaderInfo != null
                    && Valid(info, visit.DocumentInfo, visit.HeaderInfo, visit.ToolbarInfo, client, scale))
                {
                    // Prefer the text rectangle if UIA exposes the label inside the same menu button.
                    bool labelOfFoundButton = found != null && found.Title == visit.TitleMenu
                        && info.Type == ControlType.Text && found.TitleBounds.Contains(info.Bounds);
                    if (found != null && !labelOfFoundButton
                        && (found.TitleBounds != info.Bounds || found.HeaderBounds != visit.HeaderInfo.Bounds))
                    {
                        ambiguous = true;
                        return null;
                    }
                    found = new Candidate { Title = visit.Element, Document = visit.Document, Header = visit.Header,
                        Toolbar = visit.Toolbar, TitleMenu = visit.TitleMenu,
                        TitleBounds = info.Bounds, HeaderBounds = visit.HeaderInfo.Bounds };
                }

                // A deep branch must not discard an otherwise valid title in a different branch.
                if (info.Type == ControlType.Text || visit.Depth >= MaxDepth) continue;
                CheckBudget(clock);
                var child = walker.GetFirstChild(visit.Element, request);
                while (child != null)
                {
                    CheckBudget(clock);
                    if (scheduled++ >= MaxNodes) throw new SearchLimitException("nodes");
                    queue.Enqueue(new Visit { Element = child, Depth = visit.Depth + 1, Document = visit.Document,
                        DocumentInfo = visit.DocumentInfo, Header = visit.Header, HeaderInfo = visit.HeaderInfo,
                        Kind = visit.Kind, Toolbar = visit.Toolbar, ToolbarInfo = visit.ToolbarInfo,
                        TitleMenu = visit.TitleMenu });
                    child = walker.GetNextSibling(child, request);
                }
            }
            return found;
        }

        private static bool Refresh(Candidate candidate, CacheRequest request, Rectangle client, float scale, Stopwatch clock)
        {
            var document = Read(candidate.Document, request, clock);
            var header = Read(candidate.Header, request, clock);
            var toolbar = candidate.Toolbar == null ? null : Read(candidate.Toolbar, request, clock);
            var title = Read(candidate.Title, request, clock);
            var menu = candidate.TitleMenu == null ? null
                : candidate.TitleMenu == candidate.Title ? title : Read(candidate.TitleMenu, request, clock);
            if (!Valid(title, document, header, toolbar, client, scale)) return false;
            candidate.TitleBounds = title.Bounds;
            candidate.HeaderBounds = header.Bounds;
            candidate.TitleMenuBounds = IsTitleInMenu(title, menu, toolbar ?? header) ? menu.Bounds : Rectangle.Empty;
            return true;
        }

        internal static bool Valid(Snapshot title, Snapshot document, Snapshot header, Snapshot toolbar,
            Rectangle client, float scale)
        {
            var kind = ClassifyHeader(header.ClassName);
            bool validHeader = document.Type == ControlType.Document && document.Id == "RootWebArea" && kind != HeaderKind.None
                && !title.Offscreen && !document.Offscreen && !header.Offscreen
                && title.Name.Length > 0 && string.Equals(title.Name, document.Name, StringComparison.Ordinal)
                && title.HasBounds && header.HasBounds
                && client.Contains(header.Bounds) && client.Contains(title.Bounds)
                && header.Bounds.Contains(title.Bounds)
                && header.Bounds.Height <= Math.Ceiling(160 * scale)
                && header.Bounds.Bottom <= client.Top + Math.Ceiling(SearchTopDips * scale);
            if (!validHeader) return false;
            if (kind == HeaderKind.UnifiedMain && toolbar == null)
                return title.Type == ControlType.Text || IsTitleMenu(title);

            return (title.Type == ControlType.Text || IsTitleMenu(title))
                && toolbar != null && IsToolbar(toolbar, kind) && !toolbar.Offscreen && toolbar.HasBounds
                && header.Bounds.Contains(toolbar.Bounds) && toolbar.Bounds.Contains(title.Bounds);
        }

        private static bool IsTitleInMenu(Snapshot title, Snapshot menu, Snapshot region)
        {
            return menu != null && IsTitleMenu(menu) && !menu.Offscreen && menu.HasBounds
                && region.Bounds.Contains(menu.Bounds) && menu.Bounds.Contains(title.Bounds);
        }

        private static HeaderKind ClassifyHeader(string classes)
        {
            if (HasClass(classes, "MainTitlebar-", true) || HasClass(classes, "ApplicationMenuPageHeader-", true)
                || (HasClass(classes, "group/titlebar") && HasClass(classes, HeaderOffsetClass))) return HeaderKind.Legacy;
            if (HasClass(classes, HeaderOffsetClass) && HasClass(classes, "[contain:layout_style]")
                && HasClass(classes, "h-[var(--app-shell-header-height,var(--height-toolbar))]")) return HeaderKind.UnifiedMain;
            if (HasClass(classes, "[anchor-scope:--titlebar-end-controls]")
                && HasClass(classes, "h-[var(--app-shell-titlebar-height,var(--height-toolbar))]")) return HeaderKind.UnifiedShell;
            return HeaderKind.None;
        }

        private static bool IsToolbar(Snapshot info, HeaderKind kind)
        {
            if (kind == HeaderKind.None) return false;
            if (kind == HeaderKind.Legacy && info.Type == ControlType.ToolBar) return true;
            if (!HasClass(info.ClassName, ShellToolbarClass)) return false;
            // The shell also hosts unrelated toolbars. Only its thread title toolbar can serve as an anchor.
            return kind != HeaderKind.UnifiedShell || HasClass(info.ClassName, "TabStripFlushStart-", true);
        }

        private static bool IsTitleMenu(Snapshot info)
        {
            return info.Type == ControlType.Button && (HasClass(info.ClassName, "text-conversation-title")
                || (HasClass(info.ClassName, "conversation-header-control")
                    && HasClass(info.ClassName, "px-conversation-title-inline")));
        }

        private static bool IsSidebar(string classes)
        {
            return HasClass(classes, "app-shell-left-panel", true) || HasClass(classes, "ConversationSidebar-", true);
        }

        internal static bool IsConversationBody(string classes)
        {
            // In the observed Windows tree these transcript surfaces are siblings of the header.
            return HasClass(classes, "MainContentSurface-", true) || HasClass(classes, "MainContentViewport-", true)
                || HasClass(classes, "[--thread-content-responsive-max-width:var(--thread-content-expanded-max-width,inherit)]");
        }

        private static bool HasClass(string classes, string name, bool matchPrefix = false)
        {
            foreach (string token in (classes ?? "").Split(ClassSeparators, StringSplitOptions.RemoveEmptyEntries))
                if (matchPrefix ? token.StartsWith(name, StringComparison.Ordinal) : token == name) return true;
            return false;
        }

        private static bool TryRectangle(System.Windows.Rect value, out Rectangle rectangle)
        {
            rectangle = Rectangle.Empty;
            if (value.IsEmpty || !Finite(value.Left) || !Finite(value.Top) || !Finite(value.Right) || !Finite(value.Bottom)
                || value.Width <= 0 || value.Height <= 0) return false;
            double left = Math.Floor(value.Left), top = Math.Floor(value.Top);
            double right = Math.Ceiling(value.Right), bottom = Math.Ceiling(value.Bottom);
            if (left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue
                || right - left > int.MaxValue || bottom - top > int.MaxValue) return false;
            rectangle = Rectangle.FromLTRB((int)left, (int)top, (int)right, (int)bottom);
            return true;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static void CheckBudget(Stopwatch clock)
        {
            if (clock.ElapsedMilliseconds >= SearchBudgetMilliseconds) throw new SearchLimitException("elapsed");
        }

        private static bool IsProviderFailure(Exception error)
        {
            return error is ElementNotAvailableException || error is InvalidOperationException || error is COMException
                || error is ArgumentException || error is UnauthorizedAccessException || error is System.Security.SecurityException;
        }

        private static TitleAnchorObservation Result(string status, Candidate candidate, Stopwatch clock)
        {
            return new TitleAnchorObservation { Status = status,
                TitleBounds = candidate == null ? Rectangle.Empty : candidate.TitleBounds,
                HeaderBounds = candidate == null ? Rectangle.Empty : candidate.HeaderBounds,
                TitleMenuBounds = candidate == null ? Rectangle.Empty : candidate.TitleMenuBounds,
                ObservedAtUtc = DateTime.UtcNow, ElapsedMilliseconds = clock.ElapsedMilliseconds };
        }
    }
}
