namespace CodeMap.Core;

public sealed class GitUnavailableException : InvalidOperationException
{
    public GitUnavailableException(string message) : base(message) { }
}

public sealed class SemanticSliceException : InvalidOperationException
{
    public SemanticSliceException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
