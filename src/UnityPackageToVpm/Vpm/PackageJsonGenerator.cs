using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UnityPackageToVpm.Logging;
using UnityPackageToVpm.Merging;

namespace UnityPackageToVpm.Vpm;

/// <summary>
/// Generates a minimal VPM-compatible package.json at the root of the output directory.
/// The generated fields are placeholders (name, version, etc.) meant to be edited by the
/// package author afterwards.
/// </summary>
internal static class PackageJsonGenerator
{
    public static void Generate(string outputDirectory, IReadOnlyList<string> legacyFolderRelativePaths)
    {
        var packageName = SanitizePackageName(Path.GetFileName(Path.TrimEndingDirectorySeparator(outputDirectory)));

        var legacyFolders = BuildLegacyFolders(packageName, legacyFolderRelativePaths);

        var packageJson = new PackageJson(
            Name: packageName,
            DisplayName: packageName,
            Version: "0.1.0",
            Description: "",
            Author: new PackageAuthor(Name: "", Email: ""),
            VpmDependencies: new Dictionary<string, string>(),
            LegacyFolders: legacyFolders);

        var json = JsonSerializer.Serialize(packageJson, PackageJsonSerializerContext.Default.PackageJson);

        File.WriteAllText(Path.Combine(outputDirectory, "package.json"), json);
    }

    /// <summary>
    /// Update-mode variant: carries over the previous package.json's fields (name,
    /// displayName, description, author, vpmDependencies, and any fields this tool
    /// doesn't know about) unchanged, only refreshing "version" and "legacyFolders".
    /// Parsed as a JsonNode tree (rather than the strongly-typed PackageJson record) so
    /// unrecognized/user-added fields round-trip untouched.
    /// </summary>
    public static bool GenerateForUpdate(
        string outputDirectory,
        string previousPackageJsonPath,
        IReadOnlyList<string> legacyFolderRelativePaths,
        string? versionOverride)
    {
        var root = JsonNode.Parse(File.ReadAllText(previousPackageJsonPath))?.AsObject();
        if (root is null)
        {
            Log.Error($"Could not parse previous package.json at '{previousPackageJsonPath}'.");
            return false;
        }

        var packageName = root["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(packageName))
        {
            Log.Error("Previous package.json has no 'name' field.");
            return false;
        }

        string newVersion;
        if (versionOverride is not null)
        {
            newVersion = versionOverride;
        }
        else
        {
            var previousVersion = root["version"]?.GetValue<string>();
            if (!TryIncrementPatch(previousVersion, out newVersion))
            {
                Log.Error($"Previous package.json's version ('{previousVersion}') isn't a plain major.minor.patch value; pass --version <semver> explicitly.");
                return false;
            }
        }

        Log.Info($"Carrying over package.json from previous version ({root["version"]?.GetValue<string>() ?? "unknown"} -> {newVersion}).");

        root["version"] = newVersion;

        var previousLegacyFolders = root["legacyFolders"]?.AsObject();
        var mergedLegacyFolders = new JsonObject();
        if (previousLegacyFolders is not null)
        {
            foreach (var kvp in previousLegacyFolders)
            {
                mergedLegacyFolders[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        foreach (var kvp in BuildLegacyFolders(packageName, legacyFolderRelativePaths))
        {
            mergedLegacyFolders[kvp.Key] = kvp.Value;
        }

        root["legacyFolders"] = mergedLegacyFolders;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(outputDirectory, "package.json"), root.ToJsonString(options));
        return true;
    }

    private static Dictionary<string, string> BuildLegacyFolders(string packageName, IReadOnlyList<string> legacyFolderRelativePaths) =>
        legacyFolderRelativePaths.ToDictionary(
            relativePath => $"Assets/{relativePath}",
            relativePath => $"Packages/{packageName}/{AssetMerger.RuntimeFolderName}/{relativePath}");

    private static bool TryIncrementPatch(string? version, out string newVersion)
    {
        newVersion = "";
        if (string.IsNullOrWhiteSpace(version)) return false;

        var parts = version.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        if (!int.TryParse(parts[2], out var patch)) return false;

        newVersion = $"{major}.{minor}.{patch + 1}";
        return true;
    }

    private static string SanitizePackageName(string name)
    {
        var lowered = name.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray();
        return new string(chars);
    }
}

internal sealed record PackageJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("author")] PackageAuthor Author,
    [property: JsonPropertyName("vpmDependencies")] Dictionary<string, string> VpmDependencies,
    [property: JsonPropertyName("legacyFolders")] Dictionary<string, string> LegacyFolders);

internal sealed record PackageAuthor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PackageJson))]
internal partial class PackageJsonSerializerContext : JsonSerializerContext
{
}
