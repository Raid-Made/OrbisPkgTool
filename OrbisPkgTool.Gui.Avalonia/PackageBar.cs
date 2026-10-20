using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace OrbisPkgTool.Gui;

/// <summary>
/// Top-of-window bar that holds the PKG path + recents dropdown + passcode,
/// and renders an in-process metadata summary (via <see cref="PkgReader.GetInfo"/>).
/// The PKG handle is opened → read → disposed immediately on every load — never
/// held open, so in-place ops (fixdigests/resignpfs) still work on the file.
/// </summary>
public sealed class PackageBar : Border
{
    private readonly TextBox _pathBox = new() { Watermark = "Path to a .pkg file" };
    private readonly Button _browseBtn = new() { Content = "Browse…" };
    private readonly ComboBox _recentsCombo = new() { Width = 220, PlaceholderText = "Recent packages" };
    private readonly TextBox _passcodeBox = new() { MaxLength = 32, FontFamily = MonoFont };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badge = new() { Width = 84, CornerRadius = new CornerRadius(4), Padding = new Thickness(6) };
    private readonly TextBlock _badgeText = new()
    {
        FontWeight = FontWeight.Bold, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _warn = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    private readonly List<string> _recents = [];
    private CancellationTokenSource? _summaryCts;
    private bool _settingRecents;

    public static readonly FontFamily MonoFont = new("Cascadia Mono, DejaVu Sans Mono, Liberation Mono, monospace");

    public event EventHandler? PackageChanged;

    public PackageBar()
    {
        Padding = new Thickness(10, 8);
        BorderThickness = new Thickness(0, 0, 0, 1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        Background = new SolidColorBrush(Color.FromArgb(14, 128, 128, 128));

        _badge.Child = _badgeText;
        _passcodeBox.Text = CommandDef.DefaultPasscode;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 6,
        };

        AddLabel(grid, "Package:", 0);
        Grid.SetRow(_pathBox, 0); Grid.SetColumn(_pathBox, 1); grid.Children.Add(_pathBox);
        Grid.SetRow(_browseBtn, 0); Grid.SetColumn(_browseBtn, 2); grid.Children.Add(_browseBtn);
        Grid.SetRow(_recentsCombo, 0); Grid.SetColumn(_recentsCombo, 3); grid.Children.Add(_recentsCombo);

        AddLabel(grid, "Passcode:", 1);
        Grid.SetRow(_passcodeBox, 1); Grid.SetColumn(_passcodeBox, 1); Grid.SetColumnSpan(_passcodeBox, 3);
        grid.Children.Add(_passcodeBox);

        var summaryRow = new DockPanel { MinHeight = 44 };
        DockPanel.SetDock(_badge, Dock.Left);
        summaryRow.Children.Add(_badge);
        _summary.Margin = new Thickness(10, 0, 0, 0);
        summaryRow.Children.Add(_summary);
        Grid.SetRow(summaryRow, 2); Grid.SetColumnSpan(summaryRow, 4); grid.Children.Add(summaryRow);

        Grid.SetRow(_warn, 3); Grid.SetColumnSpan(_warn, 4); grid.Children.Add(_warn);

        Child = grid;

        _pathBox.TextChanged += (_, _) => OnPathChanged();
        _passcodeBox.TextChanged += (_, _) => PackageChanged?.Invoke(this, EventArgs.Empty);
        _browseBtn.Click += async (_, _) => await BrowseForPkg();
        _recentsCombo.SelectionChanged += (_, _) =>
        {
            if (_settingRecents) return;
            int i = _recentsCombo.SelectedIndex;
            if (i >= 0 && i < _recents.Count)
                _pathBox.Text = _recents[i];
        };

        ClearSummary();
    }

    private static void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------
    public string PackagePath => (_pathBox.Text ?? "").Trim();
    public string Passcode => (_passcodeBox.Text ?? "").Trim();

    public void SetPasscode(string passcode) => _passcodeBox.Text = passcode;

    public void SetPackagePath(string path)
    {
        if (_pathBox.Text == path) OnPathChanged();
        else _pathBox.Text = path;
    }

    public IReadOnlyList<string> GetRecents() => _recents;

    public void SetRecents(IEnumerable<string> paths)
    {
        // Snapshot first: callers may pass _recents itself.
        var items = paths.ToList();
        _recents.Clear();
        _recents.AddRange(items);
        _settingRecents = true;
        try
        {
            _recentsCombo.ItemsSource = items.Select(p => $"{Path.GetFileName(p)}  —  {p}").ToList();
            _recentsCombo.SelectedIndex = -1;
        }
        finally
        {
            _settingRecents = false;
        }
    }

    public void AddRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _recents.RemoveAll(p => p == path);
        _recents.Insert(0, path);
        if (_recents.Count > 12) _recents.RemoveRange(12, _recents.Count - 12);
        SetRecents(_recents);
    }

    // ------------------------------------------------------------------
    // Browse + summary
    // ------------------------------------------------------------------
    private async Task BrowseForPkg()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var path = await Dialogs.PickOpenFile(top, "Select PKG file", "PS4 Packages (*.pkg)|*.pkg|All files (*.*)|*.*");
        if (path != null) _pathBox.Text = path;
    }

    private void OnPathChanged()
    {
        PackageChanged?.Invoke(this, EventArgs.Empty);
        if (File.Exists(PackagePath))
            _ = LoadSummaryAsync(PackagePath);
        else
            ClearSummary();
    }

    private void ClearSummary()
    {
        _summaryCts?.Cancel();
        _summary.Text = "Select a .pkg to load its metadata summary.";
        SetBadge("—", Color.FromRgb(150, 150, 150));
        _warn.Text = "";
        _warn.IsVisible = false;
    }

    private void SetBadge(string text, Color color)
    {
        _badgeText.Text = text;
        _badge.Background = new SolidColorBrush(color);
    }

    private void SetWarning(string text)
    {
        _warn.Text = text;
        _warn.IsVisible = text.Length > 0;
    }

    private async Task LoadSummaryAsync(string pkgPath)
    {
        _summaryCts?.Cancel();
        _summaryCts = new CancellationTokenSource();
        var token = _summaryCts.Token;

        _summary.Text = "Loading metadata…";
        SummaryLoaded = false;
        SetWarning("");

        // PkgReader rejects passcodes != 32 chars. Fall back to the default so the
        // summary still loads; the run pipeline will surface the error.
        string pc = Passcode.Length == 32 ? Passcode : CommandDef.DefaultPasscode;

        try
        {
            var (info, passcodeStatus) = await Task.Run(() =>
            {
                using var reader = new PkgReader(pkgPath, pc);
                return (reader.GetInfo(), reader.PasscodeStatus);
            }, token);
            if (token.IsCancellationRequested) return;

            var size = new FileInfo(pkgPath).Length / 1024.0 / 1024.0;
            _summary.Text =
                $"Title: {info.Title}    Title ID: {info.TitleId}    Content ID: {info.ContentId}\n" +
                $"Category: {info.Category}    App Ver: {info.AppVersion}    System: {info.SystemVersion}    Size: {size:F1} MB    {passcodeStatus}";

            SetBadge(info.Type.ToString(), info.Type switch
            {
                PkgType.Game => Color.FromRgb(46, 125, 50),
                PkgType.Patch => Color.FromRgb(21, 101, 192),
                PkgType.Dlc => Color.FromRgb(123, 31, 162),
                PkgType.Theme => Color.FromRgb(255, 111, 0),
                PkgType.Avatar => Color.FromRgb(191, 54, 12),
                PkgType.Wallpaper => Color.FromRgb(173, 20, 87),
                _ => Color.FromRgb(96, 96, 96),
            });

            SummaryLoaded = true;
            if (passcodeStatus.StartsWith("passcode mismatch", StringComparison.Ordinal))
                SetWarning("⚠ Passcode mismatch — using RSA-recovered dk3 (read-only). In-place ops (fixdigests/resignpfs) will likely fail.");
        }
        catch (OperationCanceledException)
        {
            // A newer load superseded this one.
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            _summary.Text = "Failed to load metadata.";
            SummaryLoaded = true;
            SetBadge("!", Colors.Firebrick);
            SetWarning($"⚠ {ex.GetType().Name}: {ex.Message} — Run remains enabled; CLI diagnostics may still work on this file.");
        }
    }

    /// <summary>Test hook: true once the latest summary load finished (success or failure).</summary>
    internal bool SummaryLoaded { get; private set; }
}
