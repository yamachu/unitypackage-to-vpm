using System.Text.Json.Nodes;
using Xunit;

namespace UnityPackageToVpm.Tests;

public class EmbeddedVpmPackageAdoptionTests
{
    private const string PackageId = "dev.yamachu.kurogo.ft";

    private const string EmbeddedManifest = """
        {
          "name": "dev.yamachu.kurogo.ft",
          "displayName": "Kurogo FT",
          "version": "0.1.0",
          "unity": "2022.3",
          "description": "Test fixture package.",
          "author": { "name": "yamachu" },
          "vpmDependencies": {
            "dev.yamachu.kurogo.core": ">=0.1.0 <1.0.0-a"
          }
        }
        """;

    private static TestUnityPackageBuilder KurogoFtLikePackage(string manifest = EmbeddedManifest, bool includeRootFolderEntry = true)
    {
        var builder = new TestUnityPackageBuilder();
        if (includeRootFolderEntry) builder.AddFolder(PackageId);
        builder.AddFile($"{PackageId}/package.json", manifest);
        builder.AddFolder($"{PackageId}/Runtime");
        builder.AddFile($"{PackageId}/Runtime/KurogoFTConfiguration.cs", "public class KurogoFTConfiguration {}");
        builder.AddFolder($"{PackageId}/Editor");
        builder.AddFile($"{PackageId}/Editor/KurogoFTPlugin.cs", "public class KurogoFTPlugin {}");
        return builder;
    }

    [Fact]
    public void AdoptsEmbeddedVpmPackage_WritesContentsToOutputRoot()
    {
        using var workspace = new TestWorkspace();
        var package = workspace.AddPackage(KurogoFtLikePackage());

        var result = CliRunner.Run(workspace.OutputDirectory, package);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("detected embedded VPM package layout", result.Combined);

        // Contents land directly at the output root, not wrapped in Runtime/<id>/...
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "package.json")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "Runtime", "KurogoFTConfiguration.cs")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "Editor", "KurogoFTPlugin.cs")));
        Assert.False(Directory.Exists(Path.Combine(workspace.OutputDirectory, PackageId)));
        Assert.False(File.Exists(Path.Combine(workspace.OutputDirectory, $"{PackageId}.meta")));

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace.OutputDirectory, "package.json")))!.AsObject();
        Assert.Equal(PackageId, manifest["name"]!.GetValue<string>());
        Assert.Equal("0.1.0", manifest["version"]!.GetValue<string>());
        // The embedded semver range must round-trip without HTML-escaping (">=" etc).
        Assert.Equal(">=0.1.0 <1.0.0-a", manifest["vpmDependencies"]!["dev.yamachu.kurogo.core"]!.GetValue<string>());
    }

    [Fact]
    public void AdoptsEmbeddedVpmPackage_InjectsLegacyFoldersEntryForOldAssetsPath()
    {
        using var workspace = new TestWorkspace();
        var package = workspace.AddPackage(KurogoFtLikePackage());

        var result = CliRunner.Run(workspace.OutputDirectory, package);

        Assert.Equal(0, result.ExitCode);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace.OutputDirectory, "package.json")))!.AsObject();
        var legacyFolders = manifest["legacyFolders"]!.AsObject();
        Assert.True(legacyFolders.ContainsKey($"Assets/{PackageId}"));
    }

    [Fact]
    public void AdoptsEmbeddedVpmPackage_DoesNotOverwriteExistingLegacyFoldersEntry()
    {
        var manifestWithLegacyFolders = """
            {
              "name": "dev.yamachu.kurogo.ft",
              "displayName": "Kurogo FT",
              "version": "0.1.0",
              "legacyFolders": { "Assets/dev.yamachu.kurogo.ft": "deadbeefdeadbeefdeadbeefdeadbeef" }
            }
            """;

        using var workspace = new TestWorkspace();
        var package = workspace.AddPackage(KurogoFtLikePackage(manifestWithLegacyFolders));

        var result = CliRunner.Run(workspace.OutputDirectory, package);

        Assert.Equal(0, result.ExitCode);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace.OutputDirectory, "package.json")))!.AsObject();
        Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeef", manifest["legacyFolders"]!["Assets/dev.yamachu.kurogo.ft"]!.GetValue<string>());
    }

    [Fact]
    public void AdoptsEmbeddedVpmPackage_WhenRootFolderHasNoOwnGuidEntry_SkipsLegacyFoldersInjectionWithWarning()
    {
        using var workspace = new TestWorkspace();
        var package = workspace.AddPackage(KurogoFtLikePackage(includeRootFolderEntry: false));

        var result = CliRunner.Run(workspace.OutputDirectory, package);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("skipping automatic legacyFolders entry", result.Combined);

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace.OutputDirectory, "package.json")))!.AsObject();
        Assert.Null(manifest["legacyFolders"]);
    }

    [Fact]
    public void AdoptsEmbeddedVpmPackage_VersionOverrideAppliesWithoutPrevious()
    {
        using var workspace = new TestWorkspace();
        var package = workspace.AddPackage(KurogoFtLikePackage());

        var result = CliRunner.Run("--version", "9.9.9", workspace.OutputDirectory, package);

        Assert.Equal(0, result.ExitCode);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace.OutputDirectory, "package.json")))!.AsObject();
        Assert.Equal("9.9.9", manifest["version"]!.GetValue<string>());
    }

    [Fact]
    public void RejectsPackage_WhenAssetsExistOutsideTheEmbeddedPackageRoot()
    {
        using var workspace = new TestWorkspace();
        var builder = KurogoFtLikePackage();
        builder.AddFile("SomeOtherFile.txt", "stray");
        var package = workspace.AddPackage(builder);

        var result = CliRunner.Run(workspace.OutputDirectory, package);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("found assets outside it", result.Combined);
    }

    [Fact]
    public void RejectsPackage_WhenMultipleCandidateRootsExist()
    {
        using var workspace = new TestWorkspace();
        var builder = new TestUnityPackageBuilder();
        builder.AddFile("dev.yamachu.kurogo.ft/package.json", EmbeddedManifest);
        builder.AddFile("dev.yamachu.kurogo.core/package.json", EmbeddedManifest);
        var package = workspace.AddPackage(builder);

        var result = CliRunner.Run(workspace.OutputDirectory, package);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("multiple candidate embedded VPM package roots", result.Combined);
    }

    [Fact]
    public void MergesSuccessfully_WhenMultipleInputsAgreeOnTheSameEmbeddedRoot()
    {
        using var workspace = new TestWorkspace();
        var packageA = workspace.AddPackage(KurogoFtLikePackage());
        var packageB = workspace.AddPackage(KurogoFtLikePackage());

        var result = CliRunner.Run(workspace.OutputDirectory, packageA, packageB);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "package.json")));
    }

    [Fact]
    public void RejectsInputs_WhenOneIsAdoptedAndAnotherIsNot()
    {
        using var workspace = new TestWorkspace();
        var adoptPackage = workspace.AddPackage(KurogoFtLikePackage());

        var normalBuilder = new TestUnityPackageBuilder();
        normalBuilder.AddFile("Scripts/Foo.cs", "public class Foo {}");
        var normalPackage = workspace.AddPackage(normalBuilder);

        var result = CliRunner.Run(workspace.OutputDirectory, adoptPackage, normalPackage);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("disagrees with earlier input(s)", result.Combined);
    }

    [Fact]
    public void NormalLayoutWithoutEmbeddedPackageJson_GeneratesPlaceholderUnderRuntimeFolder()
    {
        using var workspace = new TestWorkspace();
        var builder = new TestUnityPackageBuilder();
        builder.AddFile("Scripts/Foo.cs", "public class Foo {}");
        var package = workspace.AddPackage(builder);

        var result = CliRunner.Run(workspace.OutputDirectory, package);

        Assert.DoesNotContain("detected embedded VPM package layout", result.Combined);

        // The "Runtime" wrapper folder this tool synthesizes never has its own .meta from
        // the source package, so a normal-mode conversion always needs Unity to mint one.
        // CI runners don't have Unity installed, so only assert full success (and inspect
        // the generated files) where Unity is actually reachable; elsewhere just confirm
        // the tool fails the way it's supposed to when Unity can't be found.
        if (!UnityAvailability.IsAvailable)
        {
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Could not locate a Unity Editor executable", result.Combined);
            return;
        }

        Assert.True(result.ExitCode == 0, $"Expected success but got exit code {result.ExitCode}. Tool output:\n{result.Combined}");
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "Runtime", "Scripts", "Foo.cs")));

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace.OutputDirectory, "package.json")))!.AsObject();
        Assert.Equal("0.1.0", manifest["version"]!.GetValue<string>());
    }

    [Fact]
    public void AdoptedPackage_PassesVpmCliValidation()
    {
        if (!VpmCli.IsAvailable)
        {
            // No SkippableFact package available offline; a plain pass keeps this
            // environment-dependent check from failing CI/machines without `vpm` on PATH.
            return;
        }

        using var workspace = new TestWorkspace();
        var package = workspace.AddPackage(KurogoFtLikePackage());

        var toolResult = CliRunner.Run(workspace.OutputDirectory, package);
        Assert.Equal(0, toolResult.ExitCode);

        var vpmResult = VpmCli.CheckPackage(workspace.OutputDirectory);
        Assert.Equal(0, vpmResult.ExitCode);
        Assert.Contains(PackageId, vpmResult.Combined);
    }
}
