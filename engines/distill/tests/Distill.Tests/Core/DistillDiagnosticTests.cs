using Distill.Core.Diagnostics;

namespace Distill.Tests.Core;

public class DistillDiagnosticTests
{
    [Fact]
    public void Create_WithValidConfidence_Succeeds()
    {
        var diagnostic = DistillDiagnostic.Create(
            id: "test-1",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Error,
            source: "msbuild",
            code: "CS0246",
            message: "Type not found",
            provenance: DiagnosticProvenance.MsBuildBinaryLog,
            confidence: 1.0);

        Assert.Equal("CS0246", diagnostic.Code);
        Assert.Equal(DiagnosticProvenance.MsBuildBinaryLog, diagnostic.Provenance);
    }

    [Fact]
    public void Create_WithInvalidConfidence_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DistillDiagnostic.Create(
                id: "test-1",
                kind: DiagnosticKind.Build,
                severity: DiagnosticSeverity.Error,
                source: "msbuild",
                code: "CS0246",
                message: "Type not found",
                confidence: 1.5));
    }

    [Fact]
    public void Records_WithSameValues_AreEqual()
    {
        var left = DistillDiagnostic.Create(
            id: "test-1",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Error,
            source: "msbuild",
            code: "CS0246",
            message: "Type not found");

        var right = DistillDiagnostic.Create(
            id: "test-1",
            kind: DiagnosticKind.Build,
            severity: DiagnosticSeverity.Error,
            source: "msbuild",
            code: "CS0246",
            message: "Type not found");

        Assert.Equal(left, right);
    }
}
