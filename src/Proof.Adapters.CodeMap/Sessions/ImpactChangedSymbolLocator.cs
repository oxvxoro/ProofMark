using CodeMap.Engine.Application;
using Proof.Core;

namespace Proof.Adapters.CodeMap.Sessions;

internal static class ImpactChangedSymbolLocator
{
    internal static async Task<ApplicationResponse<LocateChangedSymbolsResult>> LocateAsync(
        CodeMapApplication application,
        string workspaceRoot,
        ChangeRequest request,
        CancellationToken cancellationToken)
    {
        var projects = CodeMapProjectPathMap.Load(workspaceRoot);
        return await application.LocateChangedSymbolsAsync(
            new LocateChangedSymbolsRequest(
                SelectIndexableSpans(request.Spans, projects),
                workspaceRoot),
            cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<ChangedFileSpan> SelectIndexableSpans(
        IEnumerable<LineSpan> spans,
        IReadOnlyList<CodeMapProjectPathMap.Project> projects)
        => spans
            .Where(span => IsCodeMapSourceFile(span.File))
            .Select(span => new ChangedFileSpan(
                CodeMapProjectPathMap.ToQueryPath(span.File, projects),
                span.StartLine,
                span.EndLine))
            .ToArray();

    private static bool IsCodeMapSourceFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is
            ".cs" or ".razor" or ".cshtml" or ".xaml"
            or ".js" or ".jsx" or ".ts" or ".tsx" or ".html" or ".css";
}
