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

AssetMerger? merger = null;
EmbeddedVpmPackage? adopted = null;
var adoptedDecided = false;

foreach (var packagePath in inputPackages)
{
    var packageName = Path.GetFileName(packagePath);
    Log.Info($"Extracting '{packageName}'...");

    var assets = UnityPackageExtractor.Extract(packagePath);
    CompatibilityScanner.Scan(packageName, assets);

    if (!EmbeddedVpmPackageDetector.TryDetect(packageName, assets, out var embedded))
    {
        return 1;
    }

    if (!adoptedDecided)
    {
        adopted = embedded;
        adoptedDecided = true;
        merger = new AssetMerger(outputDirectory, adopted is null ? AssetMerger.RuntimeFolderName : "");
    }
    else if (embedded?.RootFolderName != adopted?.RootFolderName)
    {
        Log.Error($"'{packageName}' disagrees with earlier input(s) on embedded VPM package layout ('{embedded?.RootFolderName ?? "none"}' vs '{adopted?.RootFolderName ?? "none"}'); can't merge these into a single output.");
        return 1;
    }

    if (adopted is not null)
    {
        assets = EmbeddedVpmPackageDetector.Rebase(assets, adopted.RootFolderName);
    }

    merger!.Merge(packageName, assets);
}

if (adopted is null && versionOverride is not null && previousPackage is null)
{
    Log.Error("--version can only be used together with --previous.");
    return 1;
}

Log.Info("Generating package.json...");
if (adopted is not null)
{
    if (previousPackage is not null)
    {
        Log.Info("Input already contains an embedded VPM package.json; ignoring --previous's package.json (only reusing its .meta files).");
    }

    if (!PackageJsonGenerator.FinalizeAdopted(outputDirectory, adopted.RootFolderName, adopted.RootFolderGuid, versionOverride))
    {
        return 1;
    }

    if (previousPackage is not null)
    {
        var reusedCount = PreviousMetaReuser.Reuse(outputDirectory, previousPackage.RootDirectory, merger!.KnownGuids);
        Log.Info($"Reused {reusedCount} .meta file(s) from the previous version.");
    }
}
else if (previousPackage is not null)
{
    var previousPackageJsonPath = Path.Combine(previousPackage.RootDirectory, "package.json");
    if (!PackageJsonGenerator.GenerateForUpdate(outputDirectory, previousPackageJsonPath, merger!.GetTopLevelFolderRelativePaths(), versionOverride))
    {
        return 1;
    }

    var reusedCount = PreviousMetaReuser.Reuse(outputDirectory, previousPackage.RootDirectory, merger.KnownGuids);
    Log.Info($"Reused {reusedCount} .meta file(s) from the previous version.");
}
else
{
    PackageJsonGenerator.Generate(outputDirectory, merger!.GetTopLevelFolderRelativePaths());
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
