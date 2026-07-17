namespace UnityPackageToVpm.Tests;

/// <summary>
/// A scratch directory for one test's input .unitypackage files and output directory,
/// deleted on dispose so tests don't leak temp files.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    public string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), $"unitypackage-to-vpm-test-{Guid.NewGuid():N}");

    private readonly List<string> _packageFiles = new();

    public string OutputDirectory { get; }

    public TestWorkspace()
    {
        Directory.CreateDirectory(RootDirectory);
        OutputDirectory = Path.Combine(RootDirectory, "out");
    }

    public string AddPackage(TestUnityPackageBuilder builder)
    {
        var path = builder.BuildToTempFile();
        _packageFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _packageFiles)
        {
            if (File.Exists(file)) File.Delete(file);
        }

        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
