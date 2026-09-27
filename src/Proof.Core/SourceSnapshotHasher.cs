using System.Text;

namespace Proof.Core;

public static class SourceSnapshotHasher
{
    public const int ManifestVersion = 1;

    public static string Compute(SourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder();
        builder.Append("version=").Append(ManifestVersion).Append('\n');
        builder.Append("base=").Append(snapshot.BaseCommitSha).Append('\n');
        builder.Append("head=").Append(snapshot.HeadCommitSha).Append('\n');
        builder.Append("workspaceState=").Append(snapshot.IsDirty ? "dirty" : "clean").Append('\n');
        builder.Append("fileCount=").Append(snapshot.Files.Count).Append('\n');

        foreach (var file in CanonicalFiles(snapshot.Files))
        {
            builder.Append(file.Kind.ToString());
            builder.Append('\0').Append(CanonicalPath(file.OldPath));
            builder.Append('\0').Append(CanonicalPath(file.NewPath));
            builder.Append('\0').Append(file.OldContentSha256 ?? string.Empty);
            builder.Append('\0').Append(file.NewContentSha256 ?? string.Empty);
            builder.Append('\0').Append(CanonicalSpans(file.OldSpans));
            builder.Append('\0').Append(CanonicalSpans(file.NewSpans));
            builder.Append('\n');
        }

        return Sha256Hex.HashText(builder.ToString());
    }

    // Core는 Engine에 의존해서는 안 된다. 그래서 정규 텍스트 해시는
    // CertificateCanonicalHasher.HashText가 아니라 여기서 직접 계산한다.
    internal static string HashText(string text)
        => Sha256Hex.HashText(text);

    public static string HashFileContent(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        return Sha256Hex.HashStream(stream);
    }

    public static IReadOnlyList<FileDelta> CanonicalFiles(IEnumerable<FileDelta> files)
        => files
            .OrderBy(file => CanonicalPath(file.NewPath ?? file.OldPath), StringComparer.Ordinal)
            .ThenBy(file => file.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(file => CanonicalPath(file.OldPath), StringComparer.Ordinal)
            .ToArray();

    private static string CanonicalPath(string? path)
        => (path ?? string.Empty).Replace('\\', '/');

    private static string CanonicalSpans(IReadOnlyList<LineSpan> spans)
        => string.Join(',', spans
            .OrderBy(span => CanonicalPath(span.File), StringComparer.Ordinal)
            .ThenBy(span => span.StartLine)
            .ThenBy(span => span.EndLine)
            .Select(span => $"{CanonicalPath(span.File)}:{span.StartLine}-{span.EndLine}"));
}
