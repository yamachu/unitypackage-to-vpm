using System.Diagnostics;

namespace UnityPackageToVpm.Tests;

internal sealed record CliResult(int ExitCode, string StdOut, string StdErr)
{
    public string Combined => StdOut + StdErr;
}

/// <summary>
/// Runs unitypackage-to-vpm as a black-box CLI (its build output is copied next to the
/// test assembly via the ProjectReference), so tests exercise the real Program.cs entry
/// point and argument parsing rather than reaching into internals.
/// </summary>
internal static class CliRunner
{
    private static readonly string DllPath = Path.Combine(AppContext.BaseDirectory, "unitypackage-to-vpm.dll");

    public static CliResult Run(params string[] args)
    {
        if (!File.Exists(DllPath))
        {
            throw new FileNotFoundException($"Expected the tool's build output at '{DllPath}'; did the ProjectReference build run?", DllPath);
        }

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(DllPath);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new CliResult(process.ExitCode, stdOut, stdErr);
    }
}

/// <summary>
/// Thin wrapper over the `vpm` CLI (vrchat.vpm.cli) for end-to-end validation of
/// generated package.json. Tests that use this should tolerate `vpm` being absent from
/// PATH (e.g. in CI) via <see cref="IsAvailable"/>.
/// </summary>
internal static class VpmCli
{
    public static readonly bool IsAvailable = CheckAvailable();

    private static bool CheckAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("vpm", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    public static CliResult CheckPackage(string packageDirectory)
    {
        var psi = new ProcessStartInfo("vpm")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("check");
        psi.ArgumentList.Add("package");
        psi.ArgumentList.Add(packageDirectory);

        using var process = Process.Start(psi)!;
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new CliResult(process.ExitCode, stdOut, stdErr);
    }
}
