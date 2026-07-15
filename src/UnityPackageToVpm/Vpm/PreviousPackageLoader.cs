using System.IO.Compression;
using UnityPackageToVpm.Logging;

namespace UnityPackageToVpm.Vpm;

/// <summary>
/// Loads a previously converted VPM package (a directory or a VPM .zip with package.json
/// at its root) so update mode can reuse its .meta files and package.json fields. A .zip
/// input is extracted to a temp directory that <see cref="Dispose"/> cleans up.
/// </summary>
internal sealed class PreviousPackage : IDisposable
{
    private readonly string? _temporaryDirectory;

    private PreviousPackage(string rootDirectory, string? temporaryDirectory)
    {
        RootDirectory = rootDirectory;
        _temporaryDirectory = temporaryDirectory;
    }

    public string RootDirectory { get; }

    public static PreviousPackage? Load(string path)
    {
        if (Directory.Exists(path))
        {
            var rootDirectory = Path.GetFullPath(path);
            if (!File.Exists(Path.Combine(rootDirectory, "package.json")))
            {
                Log.Error($"--previous directory '{rootDirectory}' has no package.json at its root.");
                return null;
            }

            return new PreviousPackage(rootDirectory, temporaryDirectory: null);
        }

        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"unitypackage-to-vpm-prev-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);

            try
            {
                ZipFile.ExtractToDirectory(path, temporaryDirectory);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to extract --previous zip '{path}': {ex.Message}");
                TryDeleteDirectory(temporaryDirectory);
                return null;
            }

            if (!File.Exists(Path.Combine(temporaryDirectory, "package.json")))
            {
                Log.Error($"--previous zip '{path}' has no package.json at its root.");
                TryDeleteDirectory(temporaryDirectory);
                return null;
            }

            return new PreviousPackage(temporaryDirectory, temporaryDirectory);
        }

        Log.Error($"--previous path '{path}' is neither an existing directory nor a .zip file.");
        return null;
    }

    public void Dispose()
    {
        if (_temporaryDirectory is not null) TryDeleteDirectory(_temporaryDirectory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to clean up temporary directory '{path}': {ex.Message}");
        }
    }
}
