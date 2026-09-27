using Distill.Core.Config;
using Distill.Core.Planning;
using Proof.Adapters.Distill;
using Proof.Core;

namespace Proof.Tests;

public sealed class ImportedCheckTests
{
    [Fact]
    public void TryResolve_AllChecksCoveredWithMatchingProvenance_Succeeds()
    {
        var root = TempRoot();
        try
        {
            var buildArtifact = WriteArtifact(root, ".proof/import/build.binlog", "binlog");
            var trxArtifact = WriteArtifact(root, ".proof/import/unit.trx", TrxXml);
            var build = Check("build", "dotnet build App.slnx", "restore");
            var test = Check("test", "dotnet test App.Tests.csproj");
            var planned = new[]
            {
                new PlannedCheck("build", build, build.DependsOn),
                new PlannedCheck("unit", test, test.DependsOn)
            };
            var manifest = new ImportedCheckManifest("src", new Dictionary<string, ImportedCheckEntry>
            {
                ["build"] = Entry(build, ".proof/import/build.binlog", Sha256(buildArtifact)),
                ["unit"] = Entry(test, ".proof/import/unit.trx", Sha256(trxArtifact))
            });

            var resolved = ImportedCheckResolver.TryResolve(
                new ProofPlan([], SourceDigest: "src"), planned, root, manifest, out var results);

            Assert.True(resolved);
            Assert.Equal(["build", "unit"], results.Select(item => item.CheckId));
            Assert.All(results, item => Assert.Equal("imported", item.SourceId));
            Assert.NotNull(results[1].ExecutedCases);
            Assert.Equal("App.Tests.Foo.Bar", Assert.Single(results[1].ExecutedCases!).FullyQualifiedName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_StaleSourceDigest_Declines()
    {
        var root = TempRoot();
        try
        {
            var artifact = WriteArtifact(root, "build.binlog", "x");
            var check = Check("build", "dotnet build App.slnx");
            var planned = new[] { new PlannedCheck("build", check, check.DependsOn) };
            var manifest = new ImportedCheckManifest("other-digest", new Dictionary<string, ImportedCheckEntry>
            {
                ["build"] = Entry(check, "build.binlog", Sha256(artifact))
            });

            Assert.False(ImportedCheckResolver.TryResolve(
                new ProofPlan([], SourceDigest: "src"), planned, root, manifest, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_ChangedCommandDigest_Declines()
    {
        var root = TempRoot();
        try
        {
            var artifact = WriteArtifact(root, "build.binlog", "x");
            var check = Check("build", "dotnet build App.slnx");
            var planned = new[] { new PlannedCheck("build", check, check.DependsOn) };
            var staleEntry = Entry(check, "build.binlog", Sha256(artifact)) with { CommandDigest = new string('a', 64) };
            var manifest = new ImportedCheckManifest("src", new Dictionary<string, ImportedCheckEntry>
            {
                ["build"] = staleEntry
            });

            Assert.False(ImportedCheckResolver.TryResolve(
                new ProofPlan([], SourceDigest: "src"), planned, root, manifest, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_ArtifactShaMismatch_Declines()
    {
        var root = TempRoot();
        try
        {
            var artifact = WriteArtifact(root, "build.binlog", "x");
            var check = Check("build", "dotnet build App.slnx");
            var planned = new[] { new PlannedCheck("build", check, check.DependsOn) };
            var manifest = new ImportedCheckManifest("src", new Dictionary<string, ImportedCheckEntry>
            {
                ["build"] = Entry(check, "build.binlog", new string('a', 64))
            });

            Assert.False(ImportedCheckResolver.TryResolve(
                new ProofPlan([], SourceDigest: "src"), planned, root, manifest, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_PartialManifest_Declines()
    {
        var root = TempRoot();
        try
        {
            var artifact = WriteArtifact(root, "build.binlog", "x");
            var build = Check("build", "dotnet build App.slnx");
            var test = Check("test", "dotnet test App.Tests.csproj");
            var planned = new[]
            {
                new PlannedCheck("build", build, build.DependsOn),
                new PlannedCheck("unit", test, test.DependsOn)
            };
            var manifest = new ImportedCheckManifest("src", new Dictionary<string, ImportedCheckEntry>
            {
                ["build"] = Entry(build, "build.binlog", Sha256(artifact))
            });

            Assert.False(ImportedCheckResolver.TryResolve(
                new ProofPlan([], SourceDigest: "src"), planned, root, manifest, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManifestLoader_MalformedJson_ReturnsNull()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "manifest.json");
            File.WriteAllText(path, "{ not json");

            Assert.Null(ImportedCheckManifestLoader.TryLoad(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManifestLoader_RoundTrips()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "manifest.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceDigest = "src",
                checks = new Dictionary<string, object>
                {
                    ["build"] = new
                    {
                        artifact = ".proof/import/build.binlog",
                        sha256 = new string('a', 64),
                        commandDigest = new string('b', 64),
                        kind = "build",
                        status = "pass",
                        exitCode = 0
                    }
                }
            }));

            var manifest = ImportedCheckManifestLoader.TryLoad(path);

            Assert.NotNull(manifest);
            Assert.Equal("src", manifest!.SourceDigest);
            Assert.Equal("pass", manifest.Checks!["build"].Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runner_ValidImport_SkipsExecutionAndYieldsImportedEvidence()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "distill.yml"), """
                version: 1
                workspace:
                  solution: App.slnx
                  runDir: .distill/runs
                profiles:
                  quick:
                    checks:
                      - build
                checks:
                  build:
                    kind: build
                    command: dotnet build definitely-missing.slnx
                    source: auto
                    timeout: 60
                    stopOnFailure: true
                    dependsOn: []
                """);

            var configPath = Path.Combine(root, "distill.yml");
            var config = DistillConfigLoader.Load(configPath);
            var definition = config.Checks["build"];

            var artifact = WriteArtifact(root, ".proof/import/build.binlog", "binlog");
            var manifest = new ImportedCheckManifest("src", new Dictionary<string, ImportedCheckEntry>
            {
                ["build"] = new(
                    ".proof/import/build.binlog",
                    Sha256(artifact),
                    ImportedCheckResolver.ComputeCommandDigest(definition),
                    "build",
                    "pass",
                    0)
            });
            var manifestPath = Path.Combine(root, ".proof", "import", "manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(
                manifestPath,
                System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));

            var verificationPlan = new VerificationPlan(
                [new PlannedVerificationCheck("build", "build", definition.Command)],
                [],
                "quick");
            var runner = new DistillVerificationRunner(
                configPath,
                cacheEnabled: false,
                importManifestPath: ".proof/import/manifest.json");

            var result = await runner.VerifyAsync(
                new ProofPlan([], SourceDigest: "src"),
                verificationPlan,
                root,
                CancellationToken.None);

            var evidence = Assert.Single(result.Evidence.Evidence);
            Assert.Equal(EvidenceKind.Build, evidence.Kind);
            Assert.Equal(EvidenceStatus.Pass, evidence.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CheckConfig Check(string kind, string command, params string[] dependsOn)
        => new()
        {
            Kind = kind,
            Command = command,
            DependsOn = [.. dependsOn]
        };

    private static ImportedCheckEntry Entry(CheckConfig check, string artifact, string sha256)
        => new(artifact, sha256, ImportedCheckResolver.ComputeCommandDigest(check), check.Kind, "pass", 0);

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "proof-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteArtifact(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256Hex.HashStream(stream);
    }

    private const string TrxXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="1" testName="App.Tests.Foo.Bar" outcome="Passed" duration="00:00:01" />
          </Results>
          <TestDefinitions>
            <UnitTest id="1" name="App.Tests.Foo.Bar" />
          </TestDefinitions>
        </TestRun>
        """;
}
