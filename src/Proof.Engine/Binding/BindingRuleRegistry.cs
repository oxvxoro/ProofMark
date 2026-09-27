namespace Proof.Engine.Binding;

internal static class BindingRuleRegistry
{
    internal static readonly IBindingRule[] Rules =
    [
        new BuildCrossProjectBindingRule(),
        new CompatibilityBindingRule(),
        new TestBindingRule(),
        new CallerContractBindingRule(),
        new TestMappingBindingRule(),
        new ManualReviewBindingRule(),
        new AppContractBindingRule(),
        new ArchitectureBindingRule(),
        new StaticAnalysisBindingRule(),
    ];
}
