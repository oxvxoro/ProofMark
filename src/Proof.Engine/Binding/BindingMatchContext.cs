using Proof.Core;

namespace Proof.Engine.Binding;

internal readonly struct BindingMatchContext
{
    public bool IsBuild { get; init; }

    public bool IsTestCase { get; init; }

    public bool IsTest { get; init; }

    public bool IsApi { get; init; }

    public bool RepositoryWide { get; init; }

    public static BindingMatchContext From(ProofEvidence evidence)
    {
        var isTestCase = evidence.Kind == EvidenceKind.TestCase;
        return new BindingMatchContext
        {
            IsBuild = evidence.Kind == EvidenceKind.Build,
            IsTestCase = isTestCase,
            IsTest = isTestCase || evidence.Kind == EvidenceKind.TestRun,
            IsApi = evidence.Kind == EvidenceKind.ApiCompatibility,
            RepositoryWide = BindingIdentity.IsRepositoryWide(evidence.Scope?.CommandTarget),
        };
    }
}
