using CodeMap.Core.Models;

namespace CodeMap.Storage.Queries;

/// <summary>선택적 증거 가공. 위상만 보는 질의에서는 호출자가 건너뛸 수 있다.</summary>
public sealed class QueryEvidenceService
{
    public RelationEvidence For(
        IndexedEdge edge,
        IndexedSymbol source,
        IndexedSymbol target,
        string? sourceFilePath = null) =>
        RelationEvidenceMapper.FromEdge(edge, source, target, sourceFilePath, edge.Line);
}
