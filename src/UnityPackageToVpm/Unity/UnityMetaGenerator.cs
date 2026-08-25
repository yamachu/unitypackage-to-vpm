using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UnityPackageToVpm.Logging;

namespace UnityPackageToVpm.Unity;

/// <summary>
/// Silently launches the local Unity Editor to generate any missing .meta files.
///
/// Unity's batchmode refuses a -projectPath that has no "Assets" folder ("Couldn't
/// set project path to: "), so the output directory (which holds package content
/// directly at its root, VPM-style) can't be passed in as-is. Instead this spins up
/// a throwaway project whose Assets folder is a symlink back to the output directory:
/// Unity happily imports through the symlink and writes .meta files onto the real
/// files, and afterwards only the throwaway project (Library/ProjectSettings/the
/// symlink itself) needs cleaning up — the output directory is never polluted.
/// </summary>
internal static class UnityMetaGenerator
{
    public static bool Run(string outputDirectory, string? unityPathOverride = null)
    {
        var unityPath = FindUnityExecutable(unityPathOverride);
        if (unityPath is null)
        {
            Log.Error("Could not locate a Unity Editor executable. Pass --unity-path <path> (or set the UNITY_PATH environment variable) to a Unity executable and re-run, or add the missing .meta files manually.");
            return false;
        }

        var tempProjectDir = Path.Combine(Path.GetTempPath(), $"unitypackage-to-vpm-{Guid.NewGuid():N}");
        var assetsLink = Path.Combine(tempProjectDir, "Assets");
        Directory.CreateDirectory(tempProjectDir);
        Directory.CreateSymbolicLink(assetsLink, outputDirectory);
        CreateDefaultManifest(tempProjectDir);

        try
        {
            var logFile = Path.Combine(tempProjectDir, "unity_meta_gen.log");
            var arguments = $"-batchmode -nographics -quit -projectPath \"{tempProjectDir}\" -logFile \"{logFile}\"";

            Log.Info($"Launching Unity to generate missing .meta files: {unityPath} {arguments}");

            var startInfo = new ProcessStartInfo(unityPath, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Log.Error("Failed to start the Unity process.");
                return false;
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Log.Error($"Unity exited with code {process.ExitCode}. See '{logFile}' for details (copied to the output directory).");
                var preservedLog = Path.Combine(outputDirectory, "unity_meta_gen.log");
                if (File.Exists(logFile)) File.Copy(logFile, preservedLog, overwrite: true);
            }

            return process.ExitCode == 0;
        }
        finally
        {
            CleanupTempProject(tempProjectDir, assetsLink);
        }
    }

    /// <summary>
    /// Writes a minimal Packages/manifest.json so the throwaway project has the same
    /// commonly-used, non-built-in packages that a normal Unity project (especially
    /// ones used for VRChat avatars/worlds) would have. Without this, Unity only
    /// registers the handful of truly built-in modules, so any imported script that
    /// references e.g. UI/EventSystems, TextMeshPro, or Timeline
    /// (all extremely common in VRChat-oriented .unitypackages) fails to compile in
    /// the temp project even though it compiles fine in a real project.
    /// </summary>
    private static void CreateDefaultManifest(string tempProjectDir)
    {
        var packagesDir = Path.Combine(tempProjectDir, "Packages");
        Directory.CreateDirectory(packagesDir);

        const string manifest = """
            {
              "dependencies": {
                "com.unity.ugui": "1.0.0",
                "com.unity.textmeshpro": "3.0.6",
                "com.unity.timeline": "1.7.5"
              }
            }
            """;

        File.WriteAllText(Path.Combine(packagesDir, "manifest.json"), manifest);
    }

    private static void CleanupTempProject(string tempProjectDir, string assetsLink)
    {
        // Delete the symlink itself, not its target, before removing the rest of the throwaway project.
        TryDeleteDirectory(assetsLink, recursive: false);
        TryDeleteDirectory(tempProjectDir, recursive: true);
    }

    private static void TryDeleteDirectory(string path, bool recursive)
    {
        const int maxAttempts = 8;
        const int retryDelayMs = 250;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return;

                Directory.Delete(path, recursive);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(retryDelayMs);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(retryDelayMs);
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to clean up temporary directory '{path}': {ex.Message}");
                return;
            }
        }

        Log.Warn($"Failed to clean up temporary directory '{path}' after multiple attempts. You can remove it manually.");
    }

    private static string? FindUnityExecutable(string? unityPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(unityPathOverride))
        {
            if (File.Exists(unityPathOverride)) return unityPathOverride;
            Log.Error($"--unity-path was set to '{unityPathOverride}', but no file exists there.");
            return null;
        }

        var fromEnv = Environment.GetEnvironmentVariable("UNITY_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            const string hubEditorsRoot = @"C:\Program Files\Unity\Hub\Editor";
            if (!Directory.Exists(hubEditorsRoot)) return null;

            return Directory.GetDirectories(hubEditorsRoot)
                .OrderDescending(StringComparer.Ordinal)
                .Select(versionDir => Path.Combine(versionDir, "Editor", "Unity.exe"))
                .FirstOrDefault(File.Exists);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            const string hubEditorsRoot = "/Applications/Unity/Hub/Editor";
            if (!Directory.Exists(hubEditorsRoot)) return null;

            return Directory.GetDirectories(hubEditorsRoot)
                .OrderDescending(StringComparer.Ordinal)
                .Select(versionDir => Path.Combine(versionDir, "Unity.app", "Contents", "MacOS", "Unity"))
                .FirstOrDefault(File.Exists);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Unlike the Windows/macOS Hub, Linux installs aren't consistently rooted at
            // one path (manual tarball extracts, snap-style installs, etc.), so this is a
            // best-effort guess at the Unity Hub's own default location; prefer
            // --unity-path/UNITY_PATH explicitly if this doesn't find your install.
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home)) return null;

            var hubEditorsRoot = Path.Combine(home, "Unity", "Hub", "Editor");
            if (!Directory.Exists(hubEditorsRoot)) return null;

            return Directory.GetDirectories(hubEditorsRoot)
                .OrderDescending(StringComparer.Ordinal)
                .Select(versionDir => Path.Combine(versionDir, "Editor", "Unity"))
                .FirstOrDefault(File.Exists);
        }

        return null;
    }
}
