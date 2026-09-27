using CodeMap.Core.Models;

namespace CodeMap.Storage.Queries.Sqlite;

/// <summary>증거와 맵 가공이 사용하는 파일 조회 어댑터.</summary>
internal sealed class SqliteFileQueries(CodeMapQueryService service)
{
    public IReadOnlyList<IndexedFile> All() => service.Files();
    public IReadOnlyDictionary<string, IndexedFile> ByIds(IEnumerable<string> ids) => service.FindFilesByIds(ids);
}
