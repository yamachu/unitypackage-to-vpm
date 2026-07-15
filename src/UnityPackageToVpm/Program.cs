using UnityPackageToVpm.Extraction;
using UnityPackageToVpm.Logging;
using UnityPackageToVpm.Merging;
using UnityPackageToVpm.Unity;
using UnityPackageToVpm.Validation;
using UnityPackageToVpm.Vpm;

string? unityPath = null;
string? previousPath = null;
string? versionOverride = null;
var positionalArgs = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--unity-path" or "-u")
    {
        if (i + 1 >= args.Length)
        {
            Log.Error($"Missing value for '{args[i]}'.");
            return 1;
        }

        unityPath = args[++i];
        continue;
    }

    if (args[i].StartsWith("--unity-path=", StringComparison.Ordinal))
    {
        unityPath = args[i]["--unity-path=".Length..];
        continue;
    }

    if (args[i] is "--previous" or "-p")
    {
        if (i + 1 >= args.Length)
        {
            Log.Error($"Missing value for '{args[i]}'.");
            return 1;
        }

        previousPath = args[++i];
        continue;
    }

    if (args[i].StartsWith("--previous=", StringComparison.Ordinal))
    {
        previousPath = args[i]["--previous=".Length..];
        continue;
    }

    if (args[i] == "--version")
    {
        if (i + 1 >= args.Length)
        {
            Log.Error($"Missing value for '{args[i]}'.");
            return 1;
        }

        versionOverride = args[++i];
        continue;
    }

    if (args[i].StartsWith("--version=", StringComparison.Ordinal))
    {
        versionOverride = args[i]["--version=".Length..];
        continue;
    }

    positionalArgs.Add(args[i]);
}

if (positionalArgs.Count < 2)
{
    Log.Error("Usage: unitypackage-to-vpm [--unity-path <path>] [--previous <dir-or-zip>] [--version <semver>] <output-vpm-directory> <input1.unitypackage> [input2.unitypackage ...]");
    return 1;
}

if (versionOverride is not null && previousPath is null)
{
    Log.Error("--version can only be used together with --previous.");
    return 1;
}

var outputDirectory = Path.GetFullPath(positionalArgs[0]);
var inputPackages = positionalArgs[1..].ToArray();

foreach (var package in inputPackages)
{
    if (!File.Exists(package))
    {
        Log.Error($"Input package not found: {package}");
        return 1;
    }
}

using var previousPackage = previousPath is not null ? PreviousPackage.Load(previousPath) : null;
if (previousPath is not null && previousPackage is null)
{
    return 1;
}

if (previousPackage is not null && Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
{
    Log.Warn($"Output directory '{outputDirectory}' is not empty; pre-existing files there won't be tracked and may be left stale.");
}

Directory.CreateDirectory(outputDirectory);

var merger = new AssetMerger(outputDirectory);

foreach (var packagePath in inputPackages)
{
    var packageName = Path.GetFileName(packagePath);
    Log.Info($"Extracting '{packageName}'...");

    var assets = UnityPackageExtractor.Extract(packagePath);
    CompatibilityScanner.Scan(packageName, assets);
    merger.Merge(packageName, assets);
}

Log.Info("Generating package.json...");
if (previousPackage is not null)
{
    var previousPackageJsonPath = Path.Combine(previousPackage.RootDirectory, "package.json");
    if (!PackageJsonGenerator.GenerateForUpdate(outputDirectory, previousPackageJsonPath, merger.GetTopLevelFolderRelativePaths(), versionOverride))
    {
        return 1;
    }

    var reusedCount = PreviousMetaReuser.Reuse(outputDirectory, previousPackage.RootDirectory, merger.KnownGuids);
    Log.Info($"Reused {reusedCount} .meta file(s) from the previous version.");
}
else
{
    PackageJsonGenerator.Generate(outputDirectory, merger.GetTopLevelFolderRelativePaths());
}

if (MetaCompletenessChecker.HasMissingMeta(outputDirectory))
{
    Log.Info("Some assets or folders are missing .meta files; invoking Unity to generate them.");
    if (!UnityMetaGenerator.Run(outputDirectory, unityPath))
    {
        Log.Error("Unity meta generation failed.");
        return 1;
    }
}
else
{
    Log.Info("All assets already had .meta files; skipping Unity invocation.");
}

Log.Info($"Done. VPM package written to '{outputDirectory}'.");
return 0;
