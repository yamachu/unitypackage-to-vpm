using System.Runtime.InteropServices;

namespace UnityPackageToVpm.Tests;

/// <summary>
/// Mirrors UnityMetaGenerator.FindUnityExecutable's probing (UNITY_PATH env var, then the
/// Unity Hub's default install location) so tests can tell whether a real Unity Editor is
/// reachable on this machine, without depending on the main project's internals.
/// Most environments (CI runners) won't have Unity installed; tests that would otherwise
/// require it should assert the "Unity not found" failure path instead of success there.
/// </summary>
internal static class UnityAvailability
{
    public static readonly bool IsAvailable = Probe();

    private static bool Probe()
    {
        var fromEnv = Environment.GetEnvironmentVariable("UNITY_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            const string hubEditorsRoot = @"C:\Program Files\Unity\Hub\Editor";
            return Directory.Exists(hubEditorsRoot) &&
                Directory.GetDirectories(hubEditorsRoot).Any(v => File.Exists(Path.Combine(v, "Editor", "Unity.exe")));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            const string hubEditorsRoot = "/Applications/Unity/Hub/Editor";
            return Directory.Exists(hubEditorsRoot) &&
                Directory.GetDirectories(hubEditorsRoot).Any(v => File.Exists(Path.Combine(v, "Unity.app", "Contents", "MacOS", "Unity")));
        }

        return false;
    }
}
