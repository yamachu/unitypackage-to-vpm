namespace UnityPackageToVpm.Extraction;

/// <summary>
/// One GUID-keyed entry extracted from a .unitypackage archive.
/// </summary>
internal sealed class ExtractedAsset
{
    public required string Guid { get; init; }

    /// <summary>Relative path with the leading "Assets/" prefix already trimmed.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Null when the entry describes a folder rather than a file.</summary>
    public byte[]? AssetBytes { get; init; }

    public byte[]? MetaBytes { get; init; }

    public bool IsFolder => AssetBytes is null;
}
