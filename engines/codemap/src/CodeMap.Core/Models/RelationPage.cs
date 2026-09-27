namespace CodeMap.Core.Models;

/// <summary>상한이 있는 그래프 결과 한 페이지와, 다음 페이지가 있는지.</summary>
public readonly record struct RelationPage<T>(IReadOnlyList<T> Items, bool HasMore);
