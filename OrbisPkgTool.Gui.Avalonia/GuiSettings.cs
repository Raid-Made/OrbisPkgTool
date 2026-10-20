namespace OrbisPkgTool.Gui;

/// <summary>
/// Window/package state persisted between runs as simple key=value lines in
/// the per-user config folder (~/.config/OrbisPkgTool on Linux,
/// %APPDATA%\OrbisPkgTool on Windows).
/// </summary>
public sealed class GuiSettings
{
    public string Package { get; set; } = "";
    public string Passcode { get; set; } = CommandDef.DefaultPasscode;
    public List<string> Recents { get; } = [];
    public double Width { get; set; }
    public double Height { get; set; }
    public int Tab { get; set; }

    public static string FilePath =>
        // Create: GetFolderPath returns "" when the folder does not exist yet
        // (e.g. no ~/.config on a fresh Linux account).
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
            "OrbisPkgTool", "gui-settings.txt");

    public static GuiSettings Load()
    {
        var s = new GuiSettings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq], value = line[(eq + 1)..];
                switch (key)
                {
                    case "pkg": s.Package = value; break;
                    case "passcode" when value.Length == 32: s.Passcode = value; break;
                    case "recent": s.Recents.Add(value); break;
                    case "width" when double.TryParse(value, out var w): s.Width = w; break;
                    case "height" when double.TryParse(value, out var h): s.Height = h; break;
                    case "tab" when int.TryParse(value, out var t): s.Tab = t; break;
                }
            }
        }
        catch
        {
            // Unreadable settings: start fresh.
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var lines = new List<string>
            {
                $"pkg={Package}",
                $"passcode={Passcode}",
                $"width={Width}",
                $"height={Height}",
                $"tab={Tab}",
            };
            lines.AddRange(Recents.Select(r => $"recent={r}"));
            File.WriteAllLines(FilePath, lines);
        }
        catch
        {
            // Settings are a convenience; never fail the app over them.
        }
    }
}
