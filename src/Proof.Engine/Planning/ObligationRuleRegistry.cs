namespace Proof.Engine.Planning;

internal static class ObligationRuleRegistry
{
    internal static readonly IObligationRule[] Rules =
    [
        new PublicApiObligationRule(),
        new CallerContractObligationRule(),
        new CrossProjectObligationRule(),
        new TestObligationRule(),
        new CaptureObligationRule(),
        new UncertaintyObligationRule(),
        new AppContractObligationRule(),
        new ArchitectureObligationRule(),
        new StaticAnalysisObligationRule(),
    ];
}
