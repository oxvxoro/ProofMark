using Proof.Core;

namespace Proof.Engine.Binding;

internal static class BindingIdentity
{
    internal static bool TestIdentityMatches(ProofEvidence evidence, ProofObligation obligation, bool exact)
    {
        if (evidence.Scope?.SubjectRefs is { Count: > 0 } && exact)
        {
            return evidence.Scope.SubjectRefs.Any(reference => MatchesSubjectRef(reference, obligation));
        }

        var needles = new List<string> { obligation.SubjectId };
        if (!string.IsNullOrWhiteSpace(obligation.Subject?.DisplayName))
        {
            needles.Add(obligation.Subject.DisplayName);
            needles.Add(SubjectIdentityMatcher.TrimSignature(obligation.Subject.DisplayName));
        }

        var haystacks = new List<string>();
        if (evidence.Scope?.Subjects is not null)
        {
            haystacks.AddRange(evidence.Scope.Subjects);
        }

        if (!string.IsNullOrWhiteSpace(evidence.Subject)
            && !string.Equals(evidence.Subject, evidence.Provenance.CheckId, StringComparison.Ordinal))
        {
            haystacks.Add(evidence.Subject);
        }

        foreach (var haystack in haystacks)
        {
            foreach (var needle in needles)
            {
                if (string.IsNullOrWhiteSpace(needle) || string.IsNullOrWhiteSpace(haystack))
                {
                    continue;
                }

                if (string.Equals(haystack, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        SubjectIdentityMatcher.TrimSignature(haystack),
                        SubjectIdentityMatcher.TrimSignature(needle),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (exact)
                {
                    continue;
                }

                if (SubjectIdentityMatcher.HeuristicNameMatch(haystack, needle))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool ApiCompatibilityProjectMatches(ProofEvidence evidence, string? project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return false;
        }

        return ProjectMatches(evidence, project, exact: true);
    }

    internal static bool ApiCompatibilitySymbolMatches(ProofEvidence evidence, ProofObligation obligation)
    {
        if (string.Equals(evidence.Subject, obligation.SubjectId, StringComparison.Ordinal))
        {
            return true;
        }

        return evidence.Scope?.SubjectRefs?.Any(reference =>
            string.Equals(reference.Id, obligation.SubjectId, StringComparison.Ordinal)) == true;
    }

    internal static bool StaticAnalysisProjectMatches(ProofEvidence evidence, ProofObligation obligation)
    {
        if (string.IsNullOrWhiteSpace(obligation.SubjectId))
        {
            return false;
        }

        if (evidence.Scope?.SubjectRefs is { Count: > 0 })
        {
            return evidence.Scope.SubjectRefs.Any(reference =>
                string.Equals(reference.Project ?? reference.Id, obligation.SubjectId, StringComparison.OrdinalIgnoreCase));
        }

        return string.Equals(evidence.Subject, obligation.SubjectId, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TestMappingSubjectMatches(ProofEvidence evidence, ProofObligation obligation)
    {
        if (string.IsNullOrWhiteSpace(obligation.SubjectId))
        {
            return false;
        }

        if (evidence.Scope?.SubjectRefs is { Count: > 0 })
        {
            return evidence.Scope.SubjectRefs.Any(reference =>
                string.Equals(reference.Id, obligation.SubjectId, StringComparison.Ordinal));
        }

        return string.Equals(evidence.Subject, obligation.SubjectId, StringComparison.Ordinal);
    }

    internal static bool IsSourceBound(EvidenceKind kind)
        => kind is EvidenceKind.Build or EvidenceKind.TestRun or EvidenceKind.TestCase
            or EvidenceKind.ApiCompatibility or EvidenceKind.StaticAnalysis or EvidenceKind.TestMapping
            or EvidenceKind.RuntimeCoverage or EvidenceKind.ManualReview or EvidenceKind.Architecture;

    internal static bool IsRepositoryWide(string? commandTarget)
    {
        if (string.IsNullOrWhiteSpace(commandTarget))
        {
            return true;
        }

        var normalized = commandTarget.Replace('\\', '/');
        return normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ProjectMatches(ProofEvidence evidence, string subjectId, bool exact)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return false;
        }

        if (subjectId.StartsWith("scip:", StringComparison.OrdinalIgnoreCase) && exact)
        {
            return evidence.Scope?.SubjectRefs?.Any(reference =>
                string.Equals(reference.Project, subjectId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(reference.Id, subjectId, StringComparison.OrdinalIgnoreCase)) == true
                || evidence.Scope?.Subjects?.Any(item =>
                    string.Equals(item, subjectId, StringComparison.OrdinalIgnoreCase)) == true;
        }

        if (evidence.Scope?.SubjectRefs is { Count: > 0 } && exact)
        {
            return evidence.Scope.SubjectRefs.Any(reference =>
                string.Equals(reference.Project, subjectId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(reference.Id, subjectId, StringComparison.OrdinalIgnoreCase));
        }

        if (evidence.Scope?.Subjects is not null)
        {
            foreach (var item in evidence.Scope.Subjects)
            {
                if (string.Equals(item, subjectId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileNameWithoutExtension(item), subjectId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!exact && (item.Contains(subjectId, StringComparison.OrdinalIgnoreCase)
                               || subjectId.Contains(item, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return CommandTargetMatchesProject(evidence, subjectId, exact);
    }

    internal static bool CommandTargetMatchesProject(ProofEvidence evidence, string? project, bool exact = true)
    {
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(evidence.Scope?.CommandTarget))
        {
            return false;
        }

        var target = evidence.Scope.CommandTarget.Replace('\\', '/');
        if (target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(target);
        if (string.Equals(name, project, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !exact && (target.Contains(project, StringComparison.OrdinalIgnoreCase)
                          || project.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesSubjectRef(EvidenceSubjectRef reference, ProofObligation obligation)
    {
        var expected = new SubjectIdentity(
            obligation.Subject?.Id ?? obligation.SubjectId,
            obligation.Subject?.DisplayName,
            obligation.Subject?.DisplayName,
            obligation.Subject?.Project,
            obligation.Subject?.TargetFramework);
        var actual = new SubjectIdentity(
            reference.Id,
            reference.DisplayName,
            reference.FullyQualifiedName,
            reference.Project,
            reference.TargetFramework);
        return SubjectIdentityMatcher.Match(expected, actual) == IdentityMatchKind.Exact;
    }
}
