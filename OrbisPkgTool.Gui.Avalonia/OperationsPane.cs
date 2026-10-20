using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OrbisPkgTool.Gui;

/// <summary>
/// Reusable operations pane: a grouped tree of operations on the left and a
/// dynamic options panel (per selected operation) on the right, with
/// Run/Cancel buttons at the bottom. Raises <see cref="RunRequested"/> when the
/// user triggers a run (F5, double-click, or the Run button).
/// </summary>
public sealed class OperationsPane : Grid
{
    private readonly TreeView _tree = new();
    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _desc = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly StackPanel _fields = new() { Spacing = 8 };
    private readonly Button _runButton = new() { Content = "Run (F5)", MinWidth = 110, IsEnabled = false, Classes = { "accent" } };
    private readonly Button _cancelButton = new() { Content = "Cancel (Esc)", MinWidth = 110, IsEnabled = false };

    private readonly Dictionary<string, Func<string>> _fieldValues = new();
    private CommandDef? _current;
    private bool _running;

    public event Action<CommandDef, IReadOnlyDictionary<string, string>>? RunRequested;
    public event Action? CancelRequested;
    public event Action<CommandDef?>? SelectionChanged;

    public OperationsPane()
    {
        ColumnDefinitions = new ColumnDefinitions("230,4,*");

        _tree.SelectionChanged += (_, _) => SelectCommand(_tree.SelectedItem as TreeViewItem);
        _tree.DoubleTapped += (_, _) => { if (_current != null) RaiseRun(); };
        SetColumn(_tree, 0);
        Children.Add(_tree);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        SetColumn(splitter, 1);
        Children.Add(splitter);

        _runButton.Click += (_, _) => RaiseRun();
        _cancelButton.Click += (_, _) => CancelRequested?.Invoke();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { _runButton, _cancelButton },
        };

        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 10), Children = { _title, _desc } };
        var right = new DockPanel { Margin = new Thickness(12, 8, 8, 8) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(header);
        right.Children.Add(buttons);
        // Right padding keeps the vertical scrollbar clear of the browse buttons.
        _fields.Margin = new Thickness(0, 0, 16, 0);
        right.Children.Add(new ScrollViewer { Content = _fields });
        SetColumn(right, 2);
        Children.Add(right);

        _title.Text = "Select an operation on the left.";
    }

    public void SetCommands(IReadOnlyList<CommandDef> commands)
    {
        var groups = new List<TreeViewItem>();
        foreach (var g in commands.GroupBy(c => c.Group).OrderBy(g => g.Key))
        {
            var groupItem = new TreeViewItem { Header = g.Key, IsExpanded = true, FontWeight = FontWeight.SemiBold };
            groupItem.ItemsSource = g.OrderBy(c => c.Name)
                .Select(c => new TreeViewItem { Header = c.Name, Tag = c, FontWeight = FontWeight.Normal })
                .ToList();
            groups.Add(groupItem);
        }
        _tree.ItemsSource = groups;
        if (groups.Count > 0 && groups[0].ItemsSource is List<TreeViewItem> { Count: > 0 } first)
            _tree.SelectedItem = first[0];
    }

    public void SetRunning(bool running)
    {
        _running = running;
        _runButton.IsEnabled = !running && _current != null;
        _cancelButton.IsEnabled = running;
    }

    /// <summary>F5 / Esc handling, forwarded from the window for the active tab.</summary>
    public bool HandleKey(Key key)
    {
        if (key == Key.F5 && !_running) { RaiseRun(); return true; }
        if (key == Key.Escape && _running) { CancelRequested?.Invoke(); return true; }
        return false;
    }

    // ------------------------------------------------------------------
    // Selection + dynamic fields
    // ------------------------------------------------------------------
    private void SelectCommand(TreeViewItem? item)
    {
        if (item?.Tag is not CommandDef cmd) return;
        _current = cmd;
        _title.Text = cmd.Title;
        _desc.Text = cmd.Description;
        BuildFields(cmd);
        _runButton.IsEnabled = !_running;
        SelectionChanged?.Invoke(cmd);
    }

    private void BuildFields(CommandDef cmd)
    {
        _fields.Children.Clear();
        _fieldValues.Clear();

        if (cmd.Fields.Length == 0)
        {
            _fields.Children.Add(new TextBlock
            {
                Text = "This operation has no extra options.\n" +
                       (cmd.Group is "Inspect" or "Diagnose" or "Extract"
                           ? "It uses the PKG and passcode from the package bar."
                           : "Click Run to execute."),
                Opacity = 0.6,
            });
            return;
        }

        foreach (var f in cmd.Fields)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*") };
            var label = new TextBlock { Text = f.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            row.Children.Add(label);

            Control input = f.Kind switch
            {
                FieldKind.File => MakePathRow(f, PathKind.OpenFile),
                FieldKind.SaveFile => MakePathRow(f, PathKind.SaveFile),
                FieldKind.Folder => MakePathRow(f, PathKind.Folder),
                FieldKind.Combo => MakeCombo(f),
                FieldKind.Check => MakeCheck(f),
                _ => MakeText(f),
            };
            SetColumn(input, 1);
            row.Children.Add(input);
            _fields.Children.Add(row);
        }
    }

    private enum PathKind { OpenFile, SaveFile, Folder }

    private Control MakeText(CommandField f)
    {
        var box = new TextBox { Text = f.Default, Watermark = f.Default.Length == 0 ? f.Hint : null };
        _fieldValues[f.Id] = () => (box.Text ?? "").Trim();
        return box;
    }

    private Control MakeCombo(CommandField f)
    {
        var items = f.Choices.Select((val, i) =>
        {
            string rem = f.ChoiceRemarks is { } r && i < r.Length ? r[i] : "";
            return rem.Length > 0 ? $"{val} — {rem}" : val;
        }).ToList();
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = items.Count > 0 ? Math.Max(0, Array.IndexOf(f.Choices, f.Default)) : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (f.Hint.Length > 0) ToolTip.SetTip(combo, f.Hint);
        _fieldValues[f.Id] = () =>
            combo.SelectedIndex >= 0 && combo.SelectedIndex < f.Choices.Length ? f.Choices[combo.SelectedIndex] : "";
        return combo;
    }

    private Control MakeCheck(CommandField f)
    {
        var cb = new CheckBox { Content = f.Hint, IsChecked = f.Default == "1" };
        _fieldValues[f.Id] = () => cb.IsChecked == true ? "1" : "0";
        return cb;
    }

    private Control MakePathRow(CommandField f, PathKind kind)
    {
        var box = new TextBox { Text = f.Default, Watermark = f.Default.Length == 0 ? f.Hint : null };
        var btn = new Button { Content = "…", Width = 36, HorizontalContentAlignment = HorizontalAlignment.Center };
        btn.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            string? path = kind switch
            {
                PathKind.OpenFile => await Dialogs.PickOpenFile(top, f.Label, f.Filter),
                PathKind.SaveFile => await Dialogs.PickSaveFile(top, f.Label, f.Filter),
                _ => await Dialogs.PickFolder(top, f.Label),
            };
            if (path != null) box.Text = path;
        };
        var dock = new DockPanel();
        DockPanel.SetDock(btn, Dock.Right);
        btn.Margin = new Thickness(6, 0, 0, 0);
        dock.Children.Add(btn);
        dock.Children.Add(box);
        _fieldValues[f.Id] = () => (box.Text ?? "").Trim();
        return dock;
    }

    private void RaiseRun()
    {
        if (_current == null) return;
        var values = _fieldValues.ToDictionary(kv => kv.Key, kv => kv.Value());
        RunRequested?.Invoke(_current, values);
    }

    // Test hooks
    internal bool Select(string name)
    {
        foreach (var group in (_tree.ItemsSource as IEnumerable<TreeViewItem>) ?? [])
            foreach (var item in (group.ItemsSource as IEnumerable<TreeViewItem>) ?? [])
                if (item.Tag is CommandDef c && c.Name == name)
                {
                    _tree.SelectedItem = item;
                    return true;
                }
        return false;
    }

    internal void RunSelected() => RaiseRun();
}
