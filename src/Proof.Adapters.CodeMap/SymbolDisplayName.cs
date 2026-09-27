using CodeMap.Core.Models;

namespace Proof.Adapters.CodeMap;

/// <summary>
/// CodeMap 심볼의 Proof 주체 표시 이름. CodeMap은 C# 멤버 서명을
/// 멤버 이름과 함께 저장하고(<c>Cancel(string)</c>, <c>.ctor(BillingService)</c>)
/// <see cref="IndexedSymbol.DisplayName"/>은 그 서명을 이미 같은 이름으로
/// 끝나는 한정 이름 뒤에 붙인다. 바인딩은 서명을 자른 표시 이름을
/// 테스트 케이스와 커버리지 FQN과 비교하므로, 이름이 두 번 붙으면
/// 결코 정확히 일치하지 않는다.
/// </summary>
internal static class SymbolDisplayName
{
    public static string For(IndexedSymbol symbol)
    {
        if (symbol.Kind is not (NodeKind.Method or NodeKind.Constructor) || string.IsNullOrEmpty(symbol.Signature))
        {
            return symbol.DisplayName;
        }

        var name = symbol.Kind == NodeKind.Constructor ? ".ctor" : symbol.Name;
        var signature = symbol.Signature;
        var startsWithName = signature.Length > name.Length
                             && signature.StartsWith(name, StringComparison.Ordinal)
                             && signature[name.Length] is '(' or '<';
        return startsWithName ? symbol.QualifiedName + signature[name.Length..] : symbol.DisplayName;
    }
}
