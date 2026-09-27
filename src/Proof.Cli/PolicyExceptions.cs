using System.Text.Json;
using Proof.Core;
using Proof.Engine;

namespace Proof.Cli;

internal sealed record LoadedPolicyException(string Path, PolicyException? Value);

internal static class PolicyExceptionStore
{
    internal static IReadOnlyList<LoadedPolicyException> Load(string workspaceRoot)
    {
        var directory = Path.Combine(workspaceRoot, ".proof", "exceptions");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<LoadedPolicyException>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            PolicyException? value = null;
            try
            {
                value = JsonSerializer.Deserialize<PolicyException>(File.ReadAllText(file), ProofJson.WireOptions);
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }

            result.Add(new LoadedPolicyException(file, value));
        }

        return result;
    }
}

internal sealed record ExceptionCoverageView(
    string RuleId,
    string SubjectId,
    string? ExceptionFile,
    bool Covered,
    string? Note);

internal sealed record ExceptionEvaluationReport(
    string CertificatePath,
    string Verdict,
    string? SourceDigest,
    int RequiredOpenObligations,
    int Covered,
    int Uncovered,
    int BlockingConstraints,
    IReadOnlyList<ExceptionCoverageView> Coverage);

internal static class PolicyExceptionEvaluator
{
    internal static ExceptionEvaluationReport Evaluate(
        ChangeCertificate certificate,
        string certificatePath,
        IReadOnlyList<LoadedPolicyException> exceptions)
    {
        var now = DateTimeOffset.UtcNow;
        var valid = exceptions
            .Where(item => item.Value is not null
                && PolicyExceptionGovernance.EvaluateState(item.Value, now) == PolicyExceptionState.Valid)
            .ToArray();

        var open = certificate.Evaluation.Obligations
            .Where(item => item.Obligation.Required && item.Status != ObligationStatus.Proven)
            .ToArray();

        var coverage = new List<ExceptionCoverageView>();
        var covered = 0;
        foreach (var evaluated in open)
        {
            var ruleId = evaluated.Obligation.RuleId;
            var subjectId = evaluated.Obligation.SubjectId;
            var match = valid.FirstOrDefault(item =>
                PolicyExceptionGovernance.Covers(item.Value!, ruleId, subjectId, certificate.SourceDigest));
            var isCovered = match is not null;
            if (isCovered)
            {
                covered++;
            }

            coverage.Add(new ExceptionCoverageView(
                ruleId,
                subjectId,
                match is null ? null : Path.GetFileName(match.Path),
                isCovered,
                isCovered ? null : "no valid exception"));
        }

        return new ExceptionEvaluationReport(
            certificatePath,
            JsonNamingPolicy.SnakeCaseUpper.ConvertName(certificate.Verdict.ToString()),
            certificate.SourceDigest,
            open.Length,
            covered,
            open.Length - covered,
            (certificate.Constraints ?? [])
                .Count(item => string.Equals(item.Severity, "blocking", StringComparison.OrdinalIgnoreCase)),
            coverage);
    }

    /// <summary>
    /// merge-block은 그 블록이 의무가 이끄는 것이고 열린 필수 의무가
    /// 모두 덮였을 때만 예외로 해제될 수 있다. 의무가 적어도
    /// 하나는 실제로 예외되어야 하고, blocking constraint
    /// (영향 절단, 무결성 코드, ...)가 남아 있으면 안 된다. 예외가
    /// 의무가 아닌 차단을 조용히 해제하지 못하게 한다.
    /// </summary>
    internal static bool CanClearMergeBlock(ExceptionEvaluationReport report)
        => report.RequiredOpenObligations > 0
           && report.Uncovered == 0
           && report.BlockingConstraints == 0;

    internal static string Render(ExceptionEvaluationReport report)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Policy exception evaluation");
        builder.AppendLine();
        builder.AppendLine($"Certificate: {report.CertificatePath}");
        builder.AppendLine($"Verdict: {report.Verdict} (unchanged; exceptions never change the verdict)");
        builder.AppendLine($"Source: {report.SourceDigest ?? "unknown"}");
        builder.AppendLine($"Open required obligations: {report.RequiredOpenObligations} (covered {report.Covered}, uncovered {report.Uncovered})");
        builder.AppendLine($"Blocking constraints: {report.BlockingConstraints}");
        builder.AppendLine();
        foreach (var item in report.Coverage)
        {
            var state = item.Covered ? "COVERED  " : "UNCOVERED";
            var via = item.ExceptionFile is null ? string.Empty : $" via {item.ExceptionFile}";
            builder.AppendLine($"- {state} {item.RuleId} {item.SubjectId}{via}");
        }

        if (report.Coverage.Count == 0)
        {
            builder.AppendLine("- (no open required obligations)");
        }

        return builder.ToString().TrimEnd();
    }
}
