using Microsoft.Data.Sqlite;

namespace CodeMap.Storage.Migrations;

/// <summary>
/// 증분 마이그레이션이 도입되기 전에 배포된 스키마의 이름.
/// 기준선에는 파괴적 작업이 없으며, 기존 스키마 초기화가 끝난 뒤에
/// 마이그레이션 원장만 기록한다.
/// </summary>
public sealed class Migration0004Baseline : ICodeMapMigration
{
    public string Id => "0004-baseline";

    public string FromVersion => "0";

    public string ToVersion => "4";

    public Task ApplyAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
