using System.Formats.Tar;
using System.IO.Compression;

namespace UnityPackageToVpm.Extraction;

/// <summary>
/// Parses a .unitypackage (gzip-compressed tar) without depending on the Unity Editor.
/// </summary>
internal static class UnityPackageExtractor
{
    public static List<ExtractedAsset> Extract(string unityPackagePath)
    {
        var groups = new Dictionary<string, GuidGroupBuilder>();

        using (var fileStream = File.OpenRead(unityPackagePath))
        using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
        using (var tarReader = new TarReader(gzipStream))
        {
            TarEntry? entry;
            while ((entry = tarReader.GetNextEntry()) is not null)
            {
                if (entry.EntryType == TarEntryType.Directory) continue;

                var normalized = entry.Name.Replace('\\', '/').TrimStart('.', '/');
                var segments = normalized.Split('/', 2);
                if (segments.Length < 2 || segments[1].Length == 0) continue;

                var guid = segments[0];
                var fileName = segments[1];

                if (!groups.TryGetValue(guid, out var builder))
                {
                    builder = new GuidGroupBuilder(guid);
                    groups[guid] = builder;
                }

                switch (fileName)
                {
                    case "pathname":
                        builder.Pathname = ReadTextEntry(entry);
                        break;
                    case "asset":
                        builder.AssetBytes = ReadBinaryEntry(entry);
                        break;
                    case "asset.meta":
                        builder.MetaBytes = ReadBinaryEntry(entry);
                        break;
                }
            }
        }

        var results = new List<ExtractedAsset>();
        foreach (var builder in groups.Values)
        {
            if (builder.Pathname is null) continue;

            var trimmed = TrimAssetsPrefix(builder.Pathname.Trim());
            if (trimmed.Length == 0) continue;

            results.Add(new ExtractedAsset
            {
                Guid = builder.Guid,
                RelativePath = trimmed,
                AssetBytes = builder.AssetBytes,
                MetaBytes = builder.MetaBytes,
            });
        }

        return results;
    }

    private static string TrimAssetsPrefix(string pathname)
    {
        var normalized = pathname.Replace('\\', '/');
        const string prefix = "Assets/";
        return normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : normalized;
    }

    private static string ReadTextEntry(TarEntry entry)
    {
        // Do not dispose entry.DataStream: TarReader owns it and advances/disposes it
        // internally on the next GetNextEntry() call.
        var stream = entry.DataStream;
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBinaryEntry(TarEntry entry)
    {
        var stream = entry.DataStream;
        if (stream is null) return [];
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class GuidGroupBuilder(string guid)
    {
        public string Guid { get; } = guid;
        public string? Pathname { get; set; }
        public byte[]? AssetBytes { get; set; }
        public byte[]? MetaBytes { get; set; }
    }
}
