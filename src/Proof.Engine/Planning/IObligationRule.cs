using Proof.Core;

namespace Proof.Engine.Planning;

internal interface IObligationRule
{
    string RuleId { get; }

    void Apply(PlanningContext context);
}
