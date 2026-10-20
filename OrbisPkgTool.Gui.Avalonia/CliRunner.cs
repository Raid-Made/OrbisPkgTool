using System.Diagnostics;

namespace OrbisPkgTool.Gui;

/// <summary>
/// Locates the OrbisPkgTool CLI and runs it as a child process, streaming
/// stdout/stderr line by line. Every GUI operation goes through the CLI, so
/// the GUI and the terminal tool always behave the same.
/// </summary>
public static class CliRunner
{
    /// <summary>How to launch the CLI: an executable, plus any leading arguments.</summary>
    public sealed record CliCommand(string FileName, string[] PrefixArgs)
    {
        public string DisplayName => PrefixArgs.Length > 0 ? Path.GetFileName(PrefixArgs[0]) : Path.GetFileName(FileName);
    }

    /// <summary>
    /// Resolution order: ORBISPKGTOOL_CLI env var → native launcher next to the
    /// GUI (OrbisPkgTool / OrbisPkgTool.exe) → OrbisPkgTool.dll next to the GUI
    /// (run through the dotnet host) → the CLI project's bin folders in a
    /// source checkout.
    /// </summary>
    public static CliCommand? Resolve()
    {
        string? env = Environment.GetEnvironmentVariable("ORBISPKGTOOL_CLI");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
            return FromPath(env);

        foreach (var dir in CandidateDirs())
        {
            var cmd = FromDirectory(dir);
            if (cmd != null) return cmd;
        }
        return null;
    }

    private static IEnumerable<string> CandidateDirs()
    {
        yield return AppContext.BaseDirectory;

        // Source checkout: walk up to the repo root and look in the CLI's bin folders.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var cliBin = Path.Combine(dir, "OrbisPkgTool", "bin");
            if (Directory.Exists(cliBin))
            {
                foreach (var config in new[] { "Release", "Debug" })
                    yield return Path.Combine(cliBin, config, "net10.0");
            }
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

    private static CliCommand? FromDirectory(string dir)
    {
        string native = Path.Combine(dir, OperatingSystem.IsWindows() ? "OrbisPkgTool.exe" : "OrbisPkgTool");
        if (File.Exists(native)) return FromPath(native);
        string dll = Path.Combine(dir, "OrbisPkgTool.dll");
        if (File.Exists(dll)) return FromPath(dll);
        return null;
    }

    private static CliCommand FromPath(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new CliCommand(DotnetHost(), [path])
            : new CliCommand(path, []);

    /// <summary>The dotnet host running this GUI (framework-dependent), else "dotnet" from PATH.</summary>
    private static string DotnetHost()
    {
        var self = Environment.ProcessPath;
        if (self != null && Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return self;
        return "dotnet";
    }

    /// <summary>Runs the CLI with <paramref name="args"/>; each output line goes to <paramref name="onLine"/>.</summary>
    public static async Task<int> RunAsync(CliCommand cli, IEnumerable<string> args, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cli.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in cli.PrefixArgs) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };

        if (!proc.Start())
            throw new InvalidOperationException("Process failed to start.");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        // WaitForExitAsync returns before the async readers drain; this waits for EOF.
        proc.WaitForExit();
        return proc.ExitCode;
    }
}
