using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace OrbisPkgTool.Gui;

/// <summary>Message boxes and native file/folder pickers.</summary>
public static class Dialogs
{
    public static Task ShowMessage(Window owner, string title, string message) =>
        ShowButtons(owner, title, message, ["OK"]);

    public static async Task<bool> Confirm(Window owner, string title, string message) =>
        await ShowButtons(owner, title, message, ["Yes", "No"]) == "Yes";

    private static async Task<string?> ShowButtons(Window owner, string title, string message, string[] buttons)
    {
        string? result = null;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 320,
            MaxWidth = 560,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        foreach (var b in buttons)
        {
            var btn = new Button { Content = b, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            btn.Click += (_, _) => { result = b; dialog.Close(); };
            row.Children.Add(btn);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                row,
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> PickOpenFile(TopLevel top, string title, string winFormsFilter)
    {
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(winFormsFilter),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickSaveFile(TopLevel top, string title, string winFormsFilter)
    {
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = ParseFilter(winFormsFilter),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> PickFolder(TopLevel top, string title)
    {
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Converts the command registry's Windows Forms filter strings
    /// ("PS4 Packages (*.pkg)|*.pkg|All files (*.*)|*.*") to picker file types.
    /// </summary>
    public static List<FilePickerFileType> ParseFilter(string filter)
    {
        var types = new List<FilePickerFileType>();
        if (string.IsNullOrWhiteSpace(filter))
            filter = "All files (*.*)|*.*";
        var parts = filter.Split('|');
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                // "*.*" means everything; on Linux files often have no extension.
                .Select(p => p == "*.*" ? "*" : p)
                .ToList();
            types.Add(new FilePickerFileType(parts[i]) { Patterns = patterns });
        }
        return types;
    }
}
