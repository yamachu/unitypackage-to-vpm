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

    public static CliResult Run(params string[] args) => RunWithEnvironment(null, args);

    /// <summary>
    /// Like <see cref="Run"/>, but with environment variable overrides applied to the
    /// child process first — e.g. to point HOME at a fake Unity Hub layout, or clear
    /// UNITY_PATH so auto-detection logic is actually exercised instead of shortcut.
    /// A null value removes the variable instead of setting it.
    /// </summary>
    public static CliResult RunWithEnvironment(IReadOnlyDictionary<string, string?>? environment, params string[] args)
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

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null) psi.EnvironmentVariables.Remove(key);
                else psi.EnvironmentVariables[key] = value;
            }
        }

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
