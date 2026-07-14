using UnityPackageToVpm.Extraction;
using UnityPackageToVpm.Logging;

namespace UnityPackageToVpm.Merging;

/// <summary>
/// Writes extracted assets into the output directory, tracking GUIDs across packages
/// so later packages can overwrite earlier ones in a controlled, order-dependent way.
/// </summary>
internal sealed class AssetMerger(string outputDirectory)
{
    public const string RuntimeFolderName = "Runtime";

    private readonly Dictionary<string, GuidRegistryEntry> _registry = new();

    public void Merge(string packageName, IReadOnlyList<ExtractedAsset> assets)
    {
        foreach (var asset in assets)
        {
            if (_registry.TryGetValue(asset.Guid, out var previous))
            {
                if (previous.RelativePath == asset.RelativePath)
                {
                    Log.Info($"Overwrite merge: '{asset.RelativePath}' (guid {asset.Guid}) overwritten by '{packageName}' (previously from '{previous.PackageName}').");
                }
                else
                {
                    Log.Warn($"⚠️ GUID Conflict Detected! guid {asset.Guid} moved from '{previous.RelativePath}' ({previous.PackageName}) to '{asset.RelativePath}' ({packageName}). Removing the previous location to avoid a stale duplicate.");
                    RemoveFromOutput(previous.RelativePath);
                }
            }

            _registry[asset.Guid] = new GuidRegistryEntry(packageName, asset.RelativePath);
            WriteToOutput(asset);
        }
    }

    private void WriteToOutput(ExtractedAsset asset)
    {
        var targetPath = ResolveTargetPath(asset.RelativePath);

        if (asset.IsFolder)
        {
            Directory.CreateDirectory(targetPath);
        }
        else
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(targetPath, asset.AssetBytes!);
        }

        if (asset.MetaBytes is not null)
        {
            File.WriteAllBytes(targetPath + ".meta", asset.MetaBytes);
        }
    }

    private void RemoveFromOutput(string relativePath)
    {
        var targetPath = ResolveTargetPath(relativePath);
        var metaPath = targetPath + ".meta";

        if (File.Exists(targetPath)) File.Delete(targetPath);
        if (File.Exists(metaPath)) File.Delete(metaPath);
        if (Directory.Exists(targetPath) && Directory.GetFileSystemEntries(targetPath).Length == 0)
        {
            Directory.Delete(targetPath);
        }
    }

    private string ResolveTargetPath(string relativePath) =>
        Path.Combine(outputDirectory, RuntimeFolderName, relativePath);

    /// <summary>
    /// Topmost folder-type assets that were explicitly present (with their own GUID) in
    /// a source package, keyed by their original "Assets/..." relative path. Ancestor
    /// directories that were only ever implied (never their own GUID entry) are excluded,
    /// since claiming them under legacyFolders could wrongly redirect content this tool
    /// never actually placed there. Used to populate package.json's "legacyFolders".
    /// </summary>
    public IReadOnlyList<string> GetTopLevelFolderRelativePaths()
    {
        var folderPaths = _registry.Values
            .Select(entry => entry.RelativePath)
            .Where(relativePath => Directory.Exists(ResolveTargetPath(relativePath)))
            .Distinct()
            .OrderBy(relativePath => relativePath.Length)
            .ToList();

        var roots = new List<string>();
        foreach (var path in folderPaths)
        {
            var hasAncestorRoot = roots.Any(root => path.StartsWith(root + "/", StringComparison.Ordinal));
            if (!hasAncestorRoot) roots.Add(path);
        }

        return roots;
    }

    private sealed record GuidRegistryEntry(string PackageName, string RelativePath);
}
