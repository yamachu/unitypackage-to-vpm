using UnityPackageToVpm.Extraction;
using UnityPackageToVpm.Logging;

namespace UnityPackageToVpm.Vpm;

/// <summary>
/// A source .unitypackage that already contains a fully-formed VPM package: every asset
/// lives under a single top-level folder (e.g. "dev.yamachu.kurogo.ft/") whose own
/// "package.json" is a real VPM manifest, not a placeholder this tool needs to invent.
/// </summary>
internal sealed record EmbeddedVpmPackage(string RootFolderName, string? RootFolderGuid);

/// <summary>
/// Detects whether extracted assets already form a self-contained VPM package layout,
/// so the caller can adopt it (write its contents straight to the output root) instead
/// of wrapping everything under Runtime/ and synthesizing package.json.
/// </summary>
internal static class EmbeddedVpmPackageDetector
{
    public static bool TryDetect(string packageName, IReadOnlyList<ExtractedAsset> assets, out EmbeddedVpmPackage? embedded)
    {
        embedded = null;

        var candidates = assets
            .Where(a => !a.IsFolder && a.RelativePath.EndsWith("/package.json", StringComparison.Ordinal))
            .Select(a => a.RelativePath[..^"/package.json".Length])
            .Where(root => !root.Contains('/', StringComparison.Ordinal))
            .Distinct()
            .ToList();

        if (candidates.Count == 0) return true;

        if (candidates.Count > 1)
        {
            Log.Error($"'{packageName}': found multiple candidate embedded VPM package roots ({string.Join(", ", candidates)}); can't determine which one to adopt.");
            return false;
        }

        var root = candidates[0];
        var prefix = root + "/";
        var offenders = assets
            .Where(a => a.RelativePath != root && !a.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
            .Select(a => a.RelativePath)
            .Take(5)
            .ToList();

        if (offenders.Count > 0)
        {
            Log.Error($"'{packageName}': detected an embedded VPM package under 'Assets/{root}/' but found assets outside it (e.g. {string.Join(", ", offenders)}); refusing to guess a layout.");
            return false;
        }

        var rootGuid = assets.FirstOrDefault(a => a.RelativePath == root)?.Guid;
        Log.Info($"'{packageName}': detected embedded VPM package layout under 'Assets/{root}/' (package.json present); adopting its contents as the output root.");

        embedded = new EmbeddedVpmPackage(root, rootGuid);
        return true;
    }

    public static List<ExtractedAsset> Rebase(IReadOnlyList<ExtractedAsset> assets, string rootFolderName)
    {
        var prefix = rootFolderName + "/";
        var rebased = new List<ExtractedAsset>(assets.Count);

        foreach (var asset in assets)
        {
            if (asset.RelativePath == rootFolderName) continue;

            rebased.Add(new ExtractedAsset
            {
                Guid = asset.Guid,
                RelativePath = asset.RelativePath[prefix.Length..],
                AssetBytes = asset.AssetBytes,
                MetaBytes = asset.MetaBytes,
            });
        }

        return rebased;
    }
}
