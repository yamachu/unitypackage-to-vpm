using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace UnityPackageToVpm.Tests;

/// <summary>
/// Builds a synthetic .unitypackage (gzip-compressed tar, one GUID-named folder per
/// asset containing "pathname"/"asset"/"asset.meta") for tests, without needing a real
/// Unity Editor or export.
/// </summary>
internal sealed class TestUnityPackageBuilder
{
    private readonly List<(string Guid, string Pathname, byte[]? Content, byte[]? Meta)> _entries = new();
    private int _nextGuid = 1;

    private string NextGuid() => (_nextGuid++).ToString("x32");

    public TestUnityPackageBuilder AddFile(string assetsRelativePath, string? content = "content", string? guid = null, bool includeMeta = true)
    {
        guid ??= NextGuid();
        var contentBytes = content is null ? null : Encoding.UTF8.GetBytes(content);
        var metaBytes = includeMeta ? MakeMeta(guid) : null;
        _entries.Add((guid, assetsRelativePath, contentBytes ?? [], metaBytes));
        return this;
    }

    public TestUnityPackageBuilder AddFolder(string assetsRelativePath, string? guid = null, bool includeMeta = true)
    {
        guid ??= NextGuid();
        var metaBytes = includeMeta ? MakeMeta(guid) : null;
        _entries.Add((guid, assetsRelativePath, null, metaBytes));
        return this;
    }

    public static byte[] MakeMeta(string guid) =>
        Encoding.UTF8.GetBytes($"fileFormatVersion: 2\nguid: {guid}\n");

    public string BuildToTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unitypackage-to-vpm-test-{Guid.NewGuid():N}.unitypackage");

        using var fileStream = File.Create(path);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest);
        using var tarWriter = new TarWriter(gzipStream);

        foreach (var (guid, pathname, content, meta) in _entries)
        {
            AddTextEntry(tarWriter, $"{guid}/pathname", $"Assets/{pathname}");
            if (content is not null)
            {
                AddBinaryEntry(tarWriter, $"{guid}/asset", content);
            }
            if (meta is not null)
            {
                AddBinaryEntry(tarWriter, $"{guid}/asset.meta", meta);
            }
        }

        return path;
    }

    private static void AddTextEntry(TarWriter writer, string name, string text) =>
        AddBinaryEntry(writer, name, Encoding.UTF8.GetBytes(text));

    private static void AddBinaryEntry(TarWriter writer, string name, byte[] data)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(data),
        };
        writer.WriteEntry(entry);
    }
}
