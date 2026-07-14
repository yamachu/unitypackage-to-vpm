namespace UnityPackageToVpm.Merging;

/// <summary>
/// Detects assets left without a .meta file, including directories that only exist
/// implicitly (e.g. an intermediate path segment, or the "Runtime" wrapper folder)
/// and therefore never went through the per-asset write path that copies a source
/// package's own asset.meta.
/// </summary>
internal static class MetaCompletenessChecker
{
    public static bool HasMissingMeta(string outputDirectory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(outputDirectory, "*", SearchOption.AllDirectories))
        {
            if (entry.EndsWith(".meta", StringComparison.Ordinal)) continue;
            if (!File.Exists(entry + ".meta")) return true;
        }

        return false;
    }
}
