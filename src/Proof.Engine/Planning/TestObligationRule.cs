using Proof.Core;
using Proof.Engine.Planning.Specifications;

namespace Proof.Engine.Planning;

internal sealed class TestObligationRule : IObligationRule
{
    public string RuleId => "Test";

    public void Apply(PlanningContext context)
    {
        var impact = context.Impact;
        var policy = context.Policy;
        var index = context.Index;
        var production = new ProductionSymbolSpecification();
        var testMappingRequired = new TestMappingRequiredSpecification(policy);

        foreach (var test in impact.ImpactedSymbols.Where(symbol => symbol.IsTest))
        {
            context.Obligations.Add(ObligationPlanningSupport.Create(
                "P004",
                ObligationKind.Test,
                $"Impacted test '{test.DisplayName}' remains valid",
                test.Id,
                required: true,
                riskWeight: 4,
                "CodeMap found impacted test symbol",
                new ProofSubject(SubjectKind.Test, test.Id, test.Project, test.File, test.DisplayName)));
        }

        var p004SubjectIds = impact.ImpactedSymbols
            .Where(symbol => symbol.IsTest)
            .Select(symbol => symbol.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var changed in impact.ChangedSymbols.Where(symbol => production.Evaluate(symbol).IsSatisfied))
        {
            var testCallers = index.TestCallersByChangedId.GetValueOrDefault(changed.Id, []);

            var hasTest = index.TestImpactRoots.Contains(changed.Id) || testCallers.Count > 0;

            foreach (var testCaller in testCallers)
            {
                if (!p004SubjectIds.Add(testCaller.CallerSymbolId))
                {
                    continue;
                }

                context.Obligations.Add(ObligationPlanningSupport.Create(
                    "P004",
                    ObligationKind.Test,
                    $"Caller test '{testCaller.CallerSymbolId}' remains valid",
                    testCaller.CallerSymbolId,
                    required: true,
                    riskWeight: 4,
                    "CodeMap found caller relation from a test symbol",
                    new ProofSubject(
                        SubjectKind.Test,
                        testCaller.CallerSymbolId,
                        testCaller.CallerProject,
                        testCaller.File,
                        testCaller.CallerSymbolId)));
            }

            var mappedTests = (policy.TestMaps ?? [])
                .Where(entry => TestMapMatching.SymbolMatches(changed.Id, changed.DisplayName, entry.Symbol))
                .SelectMany(entry => entry.Tests)
                .Where(test => !string.IsNullOrWhiteSpace(test))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var mappedTest in mappedTests)
            {
                if (!p004SubjectIds.Add(mappedTest))
                {
                    continue;
                }

                context.Obligations.Add(ObligationPlanningSupport.Create(
                    "P004",
                    ObligationKind.Test,
                    $"Mapped test '{mappedTest}' remains valid",
                    mappedTest,
                    required: true,
                    riskWeight: 4,
                    "explicit test map entry",
                    new ProofSubject(SubjectKind.Test, mappedTest, DisplayName: mappedTest)));
            }

            if (!hasTest && mappedTests.Length == 0)
            {
                context.Obligations.Add(ObligationPlanningSupport.Create(
                    "P005",
                    ObligationKind.TestMapping,
                    $"Changed symbol '{changed.DisplayName}' has a mapped verification test",
                    changed.Id,
                    required: testMappingRequired.Evaluate(changed).IsSatisfied,
                    riskWeight: 2,
                    "no mapped test relation found",
                    new ProofSubject(SubjectKind.Symbol, changed.Id, changed.Project, changed.File, changed.DisplayName)));
            }
        }
    }
}
