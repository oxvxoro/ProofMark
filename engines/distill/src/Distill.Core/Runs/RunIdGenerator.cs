namespace Distill.Core.Runs;

public static class RunIdGenerator
{
    public static string Create(DateTimeOffset? timestamp = null)
    {
        var value = timestamp ?? DateTimeOffset.Now;
        var suffix = Guid.NewGuid().ToString("N")[..6];
        return $"d-{value:yyyyMMdd-HHmmss}-{suffix}";
    }
}
