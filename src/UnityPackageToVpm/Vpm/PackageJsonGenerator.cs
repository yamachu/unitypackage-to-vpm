using System.Text.Json;
using System.Text.Json.Serialization;
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

        var legacyFolders = legacyFolderRelativePaths.ToDictionary(
            relativePath => $"Assets/{relativePath}",
            relativePath => $"Packages/{packageName}/{AssetMerger.RuntimeFolderName}/{relativePath}");

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
