using Distill.Core.Config;
using Distill.Core.Planning;
using Distill.Git;
using Distill.Reporting;

namespace Distill.Tests;

public class DotnetCommandParserTests
{
    [Fact]
    public void Parse_StripsDotnetPrefixAndTarget()
    {
        var parsed = DotnetCommandParser.Parse("dotnet build MyApp.sln --no-restore");

        Assert.Equal("build", parsed.Verb);
        Assert.Equal("MyApp.sln", parsed.Target);
        Assert.Equal(["--no-restore"], parsed.Arguments);
    }

    [Fact]
    public void ToArgumentList_RebuildsCommandTokens()
    {
        var parsed = new DotnetCommandParser.ParsedDotnetCommand("test", "tests/Unit/Unit.csproj", ["--no-build"]);
        var args = DotnetCommandParser.ToArgumentList(parsed);

        Assert.Equal(["test", "tests/Unit/Unit.csproj", "--no-build"], args);
    }
}

public class DistillConfigLoaderTests
{
    [Fact]
    public void LoadFromYaml_ParsesProfilesAndChecks()
    {
        const string yaml = """
            version: 1
            profiles:
              quick:
                checks: [build, unit]
            checks:
              build:
                kind: build
                command: dotnet build App.sln
                source: msbuild-binlog
              unit:
                kind: test
                command: dotnet test tests/Unit/Unit.csproj
                dependsOn: [build]
            """;

        var config = DistillConfigLoader.LoadFromYaml(yaml);

        Assert.Equal(["build", "unit"], config.Profiles["quick"].Checks);
        Assert.Equal("build", config.Checks["build"].Kind);
        Assert.Equal(["build"], config.Checks["unit"].DependsOn);
    }
}

public class DiffHunkParserTests
{
    [Fact]
    public void Parse_ExtractsSingleHunk()
    {
        const string patch = """
            diff --git a/src/Foo.cs b/src/Foo.cs
            --- a/src/Foo.cs
            +++ b/src/Foo.cs
            @@ -10,3 +10,4 @@
             line
            +added
             line
            """;

        var hunks = DiffHunkParser.Parse(patch);

        Assert.Single(hunks);
        Assert.Equal("src/Foo.cs", hunks[0].File);
        Assert.True(DiffHunkParser.ContainsLine(hunks[0], 11));
    }

    [Fact]
    public void Parse_DeletedFile_UsesOldPathFromMinusHeader()
    {
        const string patch = """
            diff --git a/src/Keep.cs b/src/Keep.cs
            --- a/src/Keep.cs
            +++ b/src/Keep.cs
            @@ -1,1 +1,1 @@
             keep
            diff --git a/src/Gone.cs b/src/Gone.cs
            --- a/src/Gone.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -deleted
            -lines
            """;

        var hunks = DiffHunkParser.Parse(patch);
        Assert.Contains(hunks, hunk => hunk.File == "src/Gone.cs" && hunk.OldLength == 2);
        Assert.Contains(hunks, hunk => hunk.File == "src/Keep.cs");
    }
}

public class EvidenceCorrelatorTests
{
    [Fact]
    public void Rank_PrefersDiagnosticsInsideChangedHunks()
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation("src/Foo.cs", 11, 5));

        var hunk = new ChangedHunk("src/Foo.cs", 10, 3, 10, 4, "@@ -10,3 +10,4 @@");
        var ranked = EvidenceCorrelator.Rank([diagnostic], [hunk], ["src/Foo.cs"]);

        Assert.NotEmpty(ranked);
        Assert.True(ranked[0].Score >= CorrelationScore.DiagnosticLineInsideChangedHunk);
        Assert.Contains("line-in-hunk", ranked[0].Reasons);
    }

    [Fact]
    public void Rank_AddsFailedTestFileChangedScore()
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "test-1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Test,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "vstest",
            code: "TEST_FAILED",
            message: "assertion failed",
            testName: "Tests.Fails",
            frames:
            [
                new Distill.Core.Diagnostics.StackFrameEvidence(
                    "tests/Tests.cs",
                    15,
                    "Tests.Fails",
                    false)
            ],
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.VSTestLoggerEvent);

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic],
            Array.Empty<ChangedHunk>(),
            ["tests/Tests.cs"]);

        Assert.Contains("test-file-changed", ranked[0].Reasons);
        Assert.True(ranked[0].Score >= CorrelationScore.FailedTestFileChanged);
    }

    [Fact]
    public void Rank_MatchesAbsoluteDiagnosticPathToRelativeGitPath()
    {
        var workspace = @"C:\repo";
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation(@"C:\repo\src\Foo.cs", 11, 5));

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic],
            Array.Empty<ChangedHunk>(),
            ["src/Foo.cs"],
            workspace);

        Assert.Contains("changed-file", ranked[0].Reasons);
    }

    [Fact]
    public void Rank_TreatsDotSlashPrefixSameAsRelativePathForChangedFileMatch()
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation("./src/Foo.cs", 1, 1));

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic],
            Array.Empty<ChangedHunk>(),
            ["src/Foo.cs"]);

        Assert.Contains("changed-file", ranked[0].Reasons);
    }

    [Fact]
    public void Rank_MatchesLeadingDotDirectoryChangedFile()
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Analysis,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Warning,
            source: "analysis",
            code: "YML001",
            message: "workflow issue",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.Sarif,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation(".github/workflows/ci.yml", 3, 1));

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic],
            Array.Empty<ChangedHunk>(),
            [".github/workflows/ci.yml"]);

        Assert.Contains("changed-file", ranked[0].Reasons);
    }

    [Theory]
    [InlineData("obj/Debug/Foo.g.cs")]
    [InlineData("bin/Debug/Foo.dll.cs")]
    [InlineData("src/obj/Debug/Foo.cs")]
    [InlineData("src/bin/Debug/Foo.cs")]
    public void Rank_ScoresGeneratedFileForRootAndNestedObjBin(string file)
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS0000",
            message: "generated file issue",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation(file, 1, 1));

        var ranked = EvidenceCorrelator.Rank([diagnostic], Array.Empty<ChangedHunk>(), Array.Empty<string>());

        Assert.Contains("generated-file", ranked[0].Reasons);
    }

    [Fact]
    public void Rank_OutsideWorkspaceAbsolutePath_DoesNotMatchSameNameWorkspaceRelativeChangedFile()
    {
        // 워크스페이스 밖을 가리키는 진단(예: Unix 절대 경로
        // /outside/src/Foo.cs)은, 앞쪽 슬래시를 단순하게 자른 뒤에 상대 꼬리가
        // 같은 워크스페이스 안 변경 파일과 절대 같은 파일로 보면 안 된다
        // (outside/src/Foo.cs).
        var workspace = "/repo";
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation("/outside/src/Foo.cs", 11, 5));

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic],
            Array.Empty<ChangedHunk>(),
            ["outside/src/Foo.cs"],
            workspace);

        Assert.DoesNotContain("changed-file", ranked[0].Reasons);
    }

    [Fact]
    public void Rank_DedupesSameDiagnosticAcrossAbsoluteAndRelativePath()
    {
        var workspace = @"C:\repo";
        var absolute = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d-abs",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation(@"C:\repo\src\Foo.cs", 11, 5));

        var relative = absolute with
        {
            Id = "d-rel",
            Location = new Distill.Core.Diagnostics.SourceLocation("src/Foo.cs", 11, 5)
        };

        var ranked = EvidenceCorrelator.Rank(
            [absolute, relative],
            Array.Empty<ChangedHunk>(),
            Array.Empty<string>(),
            workspace);

        Assert.Single(ranked);
        Assert.Equal("d-abs", ranked[0].Diagnostic.Id);
    }

    [Fact]
    public void Rank_DuplicateKey_CollapsesToSingleEntry()
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation("src/Foo.cs", 11, 5));
        var duplicate = diagnostic with { Id = "d2" };

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic, duplicate],
            Array.Empty<ChangedHunk>(),
            Array.Empty<string>());

        Assert.Single(ranked);
        Assert.Equal("d1", ranked[0].Diagnostic.Id);
    }

    [Fact]
    public void Rank_SameKeyExceptLine_RemainsDistinct()
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation("src/Foo.cs", 11, 5));
        var differentLine = diagnostic with
        {
            Id = "d2",
            Location = new Distill.Core.Diagnostics.SourceLocation("src/Foo.cs", 12, 5)
        };

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic, differentLine],
            Array.Empty<ChangedHunk>(),
            Array.Empty<string>());

        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Rank_SameKeyExceptTestName_RemainsDistinct()
    {
        var diagnostic = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d1",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Test,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "vstest",
            code: "TEST_FAILED",
            message: "assertion failed",
            testName: "Tests.First",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.VSTestLoggerEvent);
        var differentTest = diagnostic with { Id = "d2", TestName = "Tests.Second" };

        var ranked = EvidenceCorrelator.Rank(
            [diagnostic, differentTest],
            Array.Empty<ChangedHunk>(),
            Array.Empty<string>());

        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Rank_DuplicateKeyWithDifferentScores_RetainsHighestRankedFirstOccurrence()
    {
        // 두 진단은 같은 중복 키(Kind|Code|Message|TestName|File|Line)를 공유한다.
        // Frames는 그 키에 들어 있지 않다. 하나는 frame-in-changed-file로 점수가
        // 더 붙으므로, 먼저 정렬되어 유지되는 대표여야 한다.
        var plain = Distill.Core.Diagnostics.DistillDiagnostic.Create(
            id: "d-plain",
            kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
            severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
            source: "build",
            code: "CS1002",
            message: "Missing semicolon",
            provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0,
            location: new Distill.Core.Diagnostics.SourceLocation("src/Foo.cs", 11, 5));

        var withScoringFrame = plain with
        {
            Id = "d-framed",
            Frames =
            [
                new Distill.Core.Diagnostics.StackFrameEvidence("src/Frame.cs", 5, "Method", false)
            ]
        };

        var ranked = EvidenceCorrelator.Rank(
            [plain, withScoringFrame],
            Array.Empty<ChangedHunk>(),
            ["src/Frame.cs"]);

        Assert.Single(ranked);
        Assert.Equal("d-framed", ranked[0].Diagnostic.Id);
    }

    [Fact]
    public void Rank_MoreThanEightDistinctErrors_AllRemainInRankedList()
    {
        var diagnostics = Enumerable.Range(0, 9)
            .Select(index => Distill.Core.Diagnostics.DistillDiagnostic.Create(
                id: $"d{index}",
                kind: Distill.Core.Diagnostics.DiagnosticKind.Build,
                severity: Distill.Core.Diagnostics.DiagnosticSeverity.Error,
                source: "build",
                code: $"CS{1000 + index}",
                message: $"error {index}",
                provenance: Distill.Core.Diagnostics.DiagnosticProvenance.MsBuildBinaryLog,
                confidence: 1.0,
                location: new Distill.Core.Diagnostics.SourceLocation($"src/File{index}.cs", 1, 1)))
            .ToArray();

        var ranked = EvidenceCorrelator.Rank(
            diagnostics,
            Array.Empty<ChangedHunk>(),
            Array.Empty<string>());

        Assert.Equal(9, ranked.Count);
    }
}

public class CheckPlannerTests
{
    [Fact]
    public void Plan_OrdersDependenciesBeforeDependents()
    {
        var config = new DistillConfig
        {
            Profiles =
            {
                ["quick"] = new ProfileDefinition { Checks = ["unit"] }
            },
            Checks =
            {
                ["build"] = new CheckConfig
                {
                    Kind = "build",
                    Command = "dotnet build App.sln"
                },
                ["unit"] = new CheckConfig
                {
                    Kind = "test",
                    Command = "dotnet test tests/Unit/Unit.csproj",
                    DependsOn = ["build"]
                }
            }
        };

        var planned = CheckPlanner.Plan(config, "quick");

        Assert.Equal(["build", "unit"], planned.Select(check => check.Id));
    }
}
