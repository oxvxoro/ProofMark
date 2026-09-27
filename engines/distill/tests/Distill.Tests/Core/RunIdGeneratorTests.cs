using Distill.Core.Runs;

namespace Distill.Tests.Core;

public class RunIdGeneratorTests
{
    [Fact]
    public void Create_SameTimestamp_ProducesDistinctIds()
    {
        var timestamp = new DateTimeOffset(2026, 9, 5, 8, 51, 13, TimeSpan.Zero);

        var ids = Enumerable.Range(0, 20)
            .Select(_ => RunIdGenerator.Create(timestamp))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(20, ids.Count);
        Assert.All(ids, id => Assert.StartsWith("d-20260905-085113-", id, StringComparison.Ordinal));
    }
}
