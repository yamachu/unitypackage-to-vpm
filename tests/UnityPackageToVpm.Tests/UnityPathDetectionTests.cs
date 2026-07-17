using Xunit;

namespace UnityPackageToVpm.Tests;

public class UnityPathDetectionTests
{
    [Fact]
    public void LinuxUnityHubAutoDetection_FindsUnityUnderHomeDirectory()
    {
        if (!OperatingSystem.IsLinux())
        {
            // This auto-detection branch only runs under RuntimeInformation.IsOSPlatform(Linux);
            // nothing to verify on other host OSes.
            return;
        }

        using var workspace = new TestWorkspace();

        var fakeHome = Path.Combine(workspace.RootDirectory, "fake-home");
        var editorDir = Path.Combine(fakeHome, "Unity", "Hub", "Editor", "2022.3.99f1", "Editor");
        Directory.CreateDirectory(editorDir);

        // Stand in for a real Unity Editor executable: just prove it got invoked.
        var invokedMarker = Path.Combine(workspace.RootDirectory, "unity-invoked.marker");
        var fakeUnityPath = Path.Combine(editorDir, "Unity");
        File.WriteAllText(fakeUnityPath, "#!/bin/sh\n" + $"touch \"{invokedMarker}\"\n" + "exit 0\n");
        File.SetUnixFileMode(fakeUnityPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var builder = new TestUnityPackageBuilder();
        builder.AddFile("Scripts/Foo.cs", "public class Foo {}"); // no folder entry -> forces a missing .meta, requiring Unity
        var package = workspace.AddPackage(builder);

        var result = CliRunner.RunWithEnvironment(
            new Dictionary<string, string?> { ["HOME"] = fakeHome, ["UNITY_PATH"] = null },
            workspace.OutputDirectory, package);

        Assert.DoesNotContain("Could not locate a Unity Editor executable", result.Combined);
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(invokedMarker), "expected the fake Unity Hub executable under $HOME/Unity/Hub/Editor to have been auto-detected and invoked");
    }
}
