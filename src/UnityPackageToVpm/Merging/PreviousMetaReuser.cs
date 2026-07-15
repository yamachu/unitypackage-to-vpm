using UnityPackageToVpm.Logging;

namespace UnityPackageToVpm.Merging;

/// <summary>
/// Update mode: fills in .meta files still missing after merging (package.json.meta,
/// the Runtime wrapper folder's meta, intermediate folder metas, and any asset whose
/// .meta was missing from the source .unitypackage) by copying the sibling .meta from
/// a previous version's output, at the same package-root-relative path. A previous
/// .unitypackage-supplied .meta always wins and is never overwritten by this step;
/// a candidate is skipped (with a warning) if its GUID is already claimed elsewhere,
/// so Unity mints a fresh GUID for it instead of introducing a duplicate.
/// </summary>
internal static class PreviousMetaReuser
{
    public static int Reuse(string outputDirectory, string previousRootDirectory, IReadOnlyCollection<string> knownGuids)
    {
        var claimedGuids = new HashSet<string>(knownGuids, StringComparer.Ordinal);
        var reused = 0;

        foreach (var entry in Directory.EnumerateFileSystemEntries(outputDirectory, "*", SearchOption.AllDirectories))
        {
            if (entry.EndsWith(".meta", StringComparison.Ordinal)) continue;
            if (File.Exists(entry + ".meta")) continue;

            var relativePath = Path.GetRelativePath(outputDirectory, entry);
            var previousMetaPath = Path.Combine(previousRootDirectory, relativePath + ".meta");
            if (!File.Exists(previousMetaPath)) continue;

            var guid = TryParseGuid(previousMetaPath);
            if (guid is null)
            {
                Log.Warn($"Skipping reuse of '{previousMetaPath}': couldn't find a 'guid:' line.");
                continue;
            }

            if (!claimedGuids.Add(guid))
            {
                Log.Warn($"Skipping reuse of '{previousMetaPath}': guid {guid} is already used elsewhere in the new package; Unity will mint a fresh guid for '{relativePath}' instead.");
                continue;
            }

            File.Copy(previousMetaPath, entry + ".meta");
            reused++;
        }

        return reused;
    }

    private static string? TryParseGuid(string metaPath)
    {
        foreach (var line in File.ReadLines(metaPath))
        {
            var trimmed = line.AsSpan().TrimStart();
            if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
            {
                return trimmed["guid:".Length..].Trim().ToString();
            }
        }

        return null;
    }
}
