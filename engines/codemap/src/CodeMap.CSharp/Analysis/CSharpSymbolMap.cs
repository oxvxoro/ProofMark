using CodeMap.Core.Models;

namespace CodeMap.CSharp.Analysis;

/// <summary>단계형 C# 수집기가 공유하는 안정적인 조회 뷰.</summary>
public sealed class CSharpSymbolMap
{
    private readonly IReadOnlyDictionary<string, CodeNode> _byId;

    public CSharpSymbolMap(IEnumerable<CodeNode> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        _byId = declarations.Where(node => node.Kind != NodeKind.File)
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<CodeNode> Declarations => _byId.Values.ToArray();
    public bool TryGet(string id, out CodeNode? node) => _byId.TryGetValue(id, out node);
}
