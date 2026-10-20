using System.Collections.Concurrent;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OrbisPkgTool.Gui;

/// <summary>
/// Bottom-of-window output console: one tab per run, live-streaming,
/// ✓/✗ exit markers, per-tab close, and a cap of 10 tabs (oldest finished
/// run is dropped first).
///
/// Output lines are BATCHED: worker threads enqueue into a concurrent queue
/// and a 100 ms UI timer flushes them in one append + scroll, so
/// progress-heavy stages (PFSC emits ~17k lines for a 1 GB game) cannot
/// starve the UI thread.
/// </summary>
public sealed class OutputConsole : DockPanel
{
    private const int MaxTabs = 10;
    private const int MaxTextChars = 50_000;
    private const int TrimKeepChars = 30_000;

    private static readonly IBrush ConsoleBackground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush ConsoleForeground = new SolidColorBrush(Color.FromRgb(220, 220, 220));

    private readonly TabControl _tabs = new();
    private readonly List<RunPage> _pages = [];
    private readonly ConcurrentQueue<(RunPage Page, string Text)> _pending = new();
    private readonly DispatcherTimer _flushTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    /// <summary>One run's tab: header text + output box + running flag.</summary>
    internal sealed class RunPage
    {
        internal required TabItem Tab { get; init; }
        internal required TextBlock Header { get; init; }
        internal required TextBox Box { get; init; }
        internal bool Running { get; set; } = true;
        /// <summary>Full captured output (also used by tests).</summary>
        public StringBuilder Log { get; } = new();
    }

    public OutputConsole()
    {
        var closeBtn = new Button { Content = "Close Tab" };
        closeBtn.Click += async (_, _) => await CloseSelected();
        var clearBtn = new Button { Content = "Clear All" };
        clearBtn.Click += (_, _) => ClearAll();
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 6),
            Children = { new TextBlock { Text = "Output", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, closeBtn, clearBtn },
        };
        SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);
        Children.Add(_tabs);

        var welcome = MakeBox();
        welcome.TextWrapping = TextWrapping.Wrap;
        welcome.Text =
            "OrbisPkgTool GUI\n\n" +
            "1.  Select a .pkg file in the package bar (top).\n" +
            "    Metadata loads instantly via in-process PkgReader.GetInfo().\n\n" +
            "2.  Pick an operation from the Package tab (left tree) or switch\n" +
            "    to the Build & Tools tab for non-PKG tools.\n\n" +
            "3.  Press Run (or F5). Each run opens a new tab here.\n\n" +
            "Shortcuts: F5 = Run · Esc = Cancel · Double-click op = Run\n";
        _tabs.Items.Add(new TabItem { Header = "welcome", Content = welcome, FontSize = 13 });

        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();
    }

    private static TextBox MakeBox() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = PackageBar.MonoFont,
        FontSize = 12,
        Background = ConsoleBackground,
        Foreground = ConsoleForeground,
        BorderThickness = new Thickness(0),
        CaretBrush = Brushes.Transparent,
    };

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------
    public bool AnyRunning => _pages.Any(p => p.Running);

    /// <summary>Creates a new tab for a run (titled "op · HH:mm"), selects it, returns it.</summary>
    internal RunPage StartRun(string opName)
    {
        if (_pages.Count >= MaxTabs && _pages.FirstOrDefault(p => !p.Running) is { } victim)
            RemovePage(victim);

        var header = new TextBlock { Text = $"{opName} · {DateTime.Now:HH:mm}" };
        var box = MakeBox();
        var tab = new TabItem { Header = header, Content = box, FontSize = 13 };
        ToolTip.SetTip(tab, opName);
        var page = new RunPage { Tab = tab, Header = header, Box = box };
        _pages.Add(page);
        _tabs.Items.Add(tab);
        _tabs.SelectedItem = tab;
        return page;
    }

    /// <summary>Streams one line into the given run's page (thread-safe, batched).</summary>
    internal void AppendLine(RunPage page, string text) => _pending.Enqueue((page, text));

    internal void FinishRun(RunPage page, bool success, double seconds) =>
        EndRun(page, success ? "✓" : "✗", success
            ? $"=== EXIT 0 in {seconds:F1}s ==="
            : $"=== FAILED (exit != 0) in {seconds:F1}s ===");

    internal void CancelRun(RunPage page) => EndRun(page, "✗", "=== CANCELLED ===");

    internal void ErrorRun(RunPage page, string message) => EndRun(page, "✗", $"=== ERROR: {message} ===");

    public void ClearAll()
    {
        foreach (var p in _pages.Where(p => !p.Running).ToList())
            RemovePage(p);
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------
    private void EndRun(RunPage page, string marker, string line)
    {
        page.Running = false;
        page.Header.Text = $"{marker} {page.Header.Text}";
        AppendLine(page, "");
        AppendLine(page, line);
        FlushPending(); // immediate flush so the final status is visible
    }

    /// <summary>Drains the batched output queue on the UI thread.</summary>
    private void FlushPending()
    {
        if (_pending.IsEmpty) return;
        var byPage = new Dictionary<RunPage, StringBuilder>();
        while (_pending.TryDequeue(out var item))
        {
            if (!byPage.TryGetValue(item.Page, out var sb))
                byPage[item.Page] = sb = new StringBuilder();
            sb.Append(item.Text).Append('\n');
        }

        foreach (var (page, sb) in byPage)
        {
            page.Log.Append(sb);
            string text = (page.Box.Text ?? "") + sb;
            // Trim the head when the buffer is too large, keeping recent output.
            if (text.Length > MaxTextChars)
            {
                int keepFrom = text.Length - TrimKeepChars;
                int nl = text.IndexOf('\n', keepFrom);
                text = text[(nl >= 0 ? nl + 1 : keepFrom)..];
            }
            page.Box.Text = text;
            page.Box.CaretIndex = text.Length;
        }
    }

    private async Task CloseSelected()
    {
        var page = _pages.FirstOrDefault(p => p.Tab == _tabs.SelectedItem);
        if (page == null)
        {
            if (_tabs.SelectedItem is TabItem other) _tabs.Items.Remove(other); // welcome tab
            return;
        }
        if (page.Running && TopLevel.GetTopLevel(this) is Window w &&
            !await Dialogs.Confirm(w, "Run in progress", "This run is still in progress. Close its tab anyway?"))
            return;
        RemovePage(page);
    }

    private void RemovePage(RunPage page)
    {
        _tabs.Items.Remove(page.Tab);
        _pages.Remove(page);
    }
}
