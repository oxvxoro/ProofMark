namespace Distill.Git;

public enum GitFileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    BinaryModified,
    SubmoduleChanged,
    Unsupported
}

public sealed record GitFileChange(
    GitFileChangeKind Kind,
    string? OldPath,
    string? NewPath,
    bool IsBinary = false);
