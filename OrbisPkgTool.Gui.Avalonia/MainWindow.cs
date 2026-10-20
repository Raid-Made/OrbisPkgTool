using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OrbisPkgTool.Gui;

public sealed class MainWindow : Window
{
    private readonly PackageBar _packageBar = new();
    private readonly OperationsPane _pkgOps = new();
    private readonly OperationsPane _toolOps = new();
    private readonly OutputConsole _output = new();
    private readonly TabControl _tabs = new();
    private readonly TextBlock _pkgStatus = new() { Text = "No package selected.", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { Text = "Ready." };
    private readonly TextBlock _lastRun = new();

    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _closeConfirmed;
    private readonly GuiSettings _settings = GuiSettings.Load();

    public MainWindow()
    {
        Title = "OrbisPkgTool";
        Width = _settings.Width > 0 ? _settings.Width : 1180;
        Height = _settings.Height > 0 ? _settings.Height : 820;
        MinWidth = 820;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _tabs.Items.Add(new TabItem { Header = "Package", Content = _pkgOps, FontSize = 16 });
        _tabs.Items.Add(new TabItem { Header = "Build & Tools", Content = _toolOps, FontSize = 16 });

        // Tabs on top, output console below, draggable splitter between.
        var body = new Grid { RowDefinitions = new RowDefinitions("*,4,260") };
        Grid.SetRow(_tabs, 0);
        body.Children.Add(_tabs);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(splitter, 1);
        body.Children.Add(splitter);
        Grid.SetRow(_output, 2);
        body.Children.Add(_output);

        var statusBar = new Border
        {
            Padding = new Thickness(10, 4),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(_lastRun, Dock.Right),
                    Docked(new Border { Width = 24 }, Dock.Right),
                    Docked(_status, Dock.Right),
                    _pkgStatus,
                },
            },
        };

        var root = new DockPanel();
        DockPanel.SetDock(_packageBar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(_packageBar);
        root.Children.Add(statusBar);
        root.Children.Add(body);
        Content = root;

        _pkgOps.SetCommands(CommandRegistry.PackageOps);
        _toolOps.SetCommands(CommandRegistry.ToolOps);
        WireEvents();

        _packageBar.SetPasscode(_settings.Passcode);
        _packageBar.SetRecents(_settings.Recents);
        if (_settings.Package.Length > 0) _packageBar.SetPackagePath(_settings.Package);
        if (_settings.Tab is >= 0 and < 2) _tabs.SelectedIndex = _settings.Tab;
    }

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private OperationsPane ActivePane => _tabs.SelectedIndex == 1 ? _toolOps : _pkgOps;

    private void WireEvents()
    {
        _packageBar.PackageChanged += (_, _) => UpdatePackageStatus();
        foreach (var pane in new[] { _pkgOps, _toolOps })
        {
            var p = pane;
            p.RunRequested += async (cmd, values) => await RunAsync(p, cmd, values);
            p.CancelRequested += () => _cts?.Cancel();
            p.SelectionChanged += cmd => _status.Text = cmd != null ? $"Selected: {cmd.CliWord}" : "Ready.";
        }
        KeyDown += (_, e) =>
        {
            if (ActivePane.HandleKey(e.Key)) e.Handled = true;
        };
    }

    private void UpdatePackageStatus()
    {
        string path = _packageBar.PackagePath;
        _pkgStatus.Text = path.Length > 0
            ? $"PKG: {Path.GetFileName(path)}    Passcode: {_packageBar.Passcode}"
            : "No package selected.";
    }

    // ------------------------------------------------------------------
    // Run pipeline (shared by both tabs)
    // ------------------------------------------------------------------
    private async Task RunAsync(OperationsPane pane, CommandDef cmd, IReadOnlyDictionary<string, string> values)
    {
        if (_running) return;

        var cli = CliRunner.Resolve();
        if (cli == null)
        {
            await Dialogs.ShowMessage(this, "CLI not found",
                "The OrbisPkgTool command-line tool was not found next to the GUI.\n" +
                "Build the OrbisPkgTool project, or set ORBISPKGTOOL_CLI to its path.");
            return;
        }

        // Package-tab ops get pkg + passcode from the package bar.
        var effective = new Dictionary<string, string>(values);
        bool isPkgOp = pane == _pkgOps;
        if (isPkgOp)
        {
            effective["pkg"] = _packageBar.PackagePath;
            effective["passcode"] = _packageBar.Passcode;
            if (string.IsNullOrWhiteSpace(effective["pkg"]))
            {
                await Dialogs.ShowMessage(this, "No package", "Select a PKG file in the package bar first.");
                return;
            }
        }

        string[] args;
        try
        {
            args = [.. cmd.CliWord.Split(' ', StringSplitOptions.RemoveEmptyEntries), .. cmd.BuildArgs(effective)];
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessage(this, "Argument error", $"Cannot build arguments: {ex.Message}");
            return;
        }

        SetRunning(true);
        var page = _output.StartRun(cmd.Name);
        LastRunPage = page;
        _cts = new CancellationTokenSource();
        _status.Text = $"Running: {cmd.CliWord}";
        _output.AppendLine(page, $"> {cli.DisplayName} {string.Join(' ', args.Select(Quote))}");
        _output.AppendLine(page, "");

        var sw = Stopwatch.StartNew();
        try
        {
            int exit = await CliRunner.RunAsync(cli, args, line => _output.AppendLine(page, line), _cts.Token);
            sw.Stop();
            bool success = exit == 0;
            _output.FinishRun(page, success, sw.Elapsed.TotalSeconds);
            _status.Text = success ? $"Done in {sw.Elapsed.TotalSeconds:F1}s" : $"Failed (exit {exit})";
            _lastRun.Text = $"Last run: {cmd.Name} {(success ? "✓" : "✗")} {sw.Elapsed.TotalSeconds:F1}s  {DateTime.Now:HH:mm}";
            if (isPkgOp && success)
                _packageBar.AddRecent(_packageBar.PackagePath);
        }
        catch (OperationCanceledException)
        {
            _output.CancelRun(page);
            _status.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            _output.ErrorRun(page, ex.Message);
            _status.Text = "Error";
        }
        finally
        {
            SetRunning(false);
        }
    }

    private static string Quote(string a) => a.Length == 0 || a.Contains(' ') ? $"\"{a}\"" : a;

    private void SetRunning(bool running)
    {
        _running = running;
        _pkgOps.SetRunning(running);
        _toolOps.SetRunning(running);
    }

    // ------------------------------------------------------------------
    // Close: confirm if a run is active, persist settings
    // ------------------------------------------------------------------
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_running && !_closeConfirmed)
        {
            e.Cancel = true;
            if (await Dialogs.Confirm(this, "Exit", "A command is still running. Kill it and exit?"))
            {
                _cts?.Cancel();
                _closeConfirmed = true;
                Close();
            }
            return;
        }

        _settings.Package = _packageBar.PackagePath;
        _settings.Passcode = _packageBar.Passcode;
        _settings.Recents.Clear();
        _settings.Recents.AddRange(_packageBar.GetRecents());
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Tab = _tabs.SelectedIndex;
        _settings.Save();
        base.OnClosing(e);
    }

    // Test hooks
    internal OperationsPane PackageOps => _pkgOps;
    internal OperationsPane ToolOps => _toolOps;
    internal PackageBar PackageBarControl => _packageBar;
    internal TabControl Tabs => _tabs;
    internal OutputConsole.RunPage? LastRunPage { get; private set; }
}
