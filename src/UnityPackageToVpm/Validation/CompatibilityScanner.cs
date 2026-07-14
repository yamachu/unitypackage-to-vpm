using System.Text;
using System.Text.RegularExpressions;
using UnityPackageToVpm.Extraction;
using UnityPackageToVpm.Logging;

namespace UnityPackageToVpm.Validation;

/// <summary>
/// Lightweight static scan over extracted assets for patterns known to break once an
/// asset moves from Assets/ into a VPM package under Packages/ — see
/// https://vcc.docs.vrchat.com/guides/convert-unitypackage/. This can only flag
/// suspicious patterns, not prove breakage; it exists so the warning shows up here
/// instead of as a confusing runtime failure after the package is installed.
/// </summary>
internal static class CompatibilityScanner
{
    private static readonly Regex HardcodedAssetsPathPattern = new("[\"']\\s*Assets/", RegexOptions.Compiled);

    public static void Scan(string packageName, IReadOnlyList<ExtractedAsset> assets)
    {
        foreach (var asset in assets)
        {
            if (asset.AssetBytes is null) continue;

            if (asset.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                ScanCSharpSource(packageName, asset);
            }
            else if (asset.RelativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"ℹ️ Pre-compiled DLL detected: '{asset.RelativePath}' (from '{packageName}'). Verify the assembly doesn't hardcode Assets-relative paths or otherwise assume it lives outside Packages/.");
            }
        }
    }

    private static void ScanCSharpSource(string packageName, ExtractedAsset asset)
    {
        var source = Encoding.UTF8.GetString(asset.AssetBytes!);

        if (HardcodedAssetsPathPattern.IsMatch(source))
        {
            Log.Warn($"⚠️ Hardcoded Assets path detected in '{asset.RelativePath}' (from '{packageName}'). This path will break once the asset moves under Packages/.");
        }

        if (source.Contains("Application.dataPath", StringComparison.Ordinal))
        {
            Log.Warn($"⚠️ 'Application.dataPath' usage detected in '{asset.RelativePath}' (from '{packageName}'). File I/O built on an Assets-relative path is likely to break for a VPM package.");
        }
    }
}
