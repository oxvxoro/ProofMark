namespace CodeMap.Storage;

/// <summary>
/// SQLite 변수 한도 배치를 한곳에 모은다. 지원하는 런타임 상당수에서
/// SQLite 기본 바인딩 변수는 999개이므로, 호출자는 주변 문장이 쓰는
/// 변수를 남겨 두고 항목마다 더하는 변수 개수를 지정한다.
/// </summary>
internal static class SqliteBatch
{
    public const int DefaultVariableLimit = 999;

    public static IEnumerable<T[]> ChunkForVariables<T>(
        IEnumerable<T> values,
        int variablesPerItem,
        int fixedVariables)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (variablesPerItem <= 0)
            throw new ArgumentOutOfRangeException(nameof(variablesPerItem));
        if (fixedVariables < 0 || fixedVariables >= DefaultVariableLimit)
            throw new ArgumentOutOfRangeException(nameof(fixedVariables));

        var capacity = Math.Max(1, (DefaultVariableLimit - fixedVariables) / variablesPerItem);
        return values.Chunk(capacity);
    }
}
