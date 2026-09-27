using CodeMap.Core.Models.Investigation;

namespace CodeMap.Engine.Application.Investigation;

public enum CostClass
{
    LocalSlice,
    GraphRow,
    SourceIo
}

/// <summary>조사 제공자 하나에 대한 상한이 있는 조회 정책.</summary>
public sealed record ProviderPlan(
    InvestigationProviderKind Kind,
    int InitialWindow,
    int ExpansionWindow,
    int MaxWindow,
    int Priority,
    CostClass CostClass)
{
    public static ProviderPlan For(InvestigationProviderKind kind) =>
        kind == InvestigationProviderKind.LocalSlice
            ? new(kind, 8, 8, 32, 0, CostClass.LocalSlice)
            : new(kind, 8, 16, 64, 10, CostClass.GraphRow);
}

internal sealed record InvestigationProviderPlan(
    IInvestigationProvider Provider,
    ProviderPlan Policy);
