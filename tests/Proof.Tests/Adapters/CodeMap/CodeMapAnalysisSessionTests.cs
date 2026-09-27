using Proof.Adapters.CodeMap.Sessions;
using Proof.Core;

namespace Proof.Tests;

public sealed class CodeMapAnalysisSessionTests
{
    [Fact]
    public async Task DeletionImpact_IsCapturedBeforeHeadIndexUpdate()
    {
        var lifecycle = new RecordingLifecycle { Skip = false, LocateSucceeds = true };
        await CodeMapAnalysisSession.RunAsync(lifecycle, CancellationToken.None);

        Assert.True(lifecycle.Steps.IndexOf("AnalyzeDeletionImpact") < lifecycle.Steps.IndexOf("UpdateHeadIndex"));
    }

    [Fact]
    public async Task HeadQueries_RunAfterIndexUpdate()
    {
        var lifecycle = new RecordingLifecycle { Skip = false, LocateSucceeds = true };
        await CodeMapAnalysisSession.RunAsync(lifecycle, CancellationToken.None);

        Assert.True(lifecycle.Steps.IndexOf("UpdateHeadIndex") < lifecycle.Steps.IndexOf("QueryHead"));
    }

    [Fact]
    public async Task SkippedIndexUpdate_RebuildsOnceWhenLocateFails()
    {
        var lifecycle = new RecordingLifecycle { Skip = true, FailFirstLocate = true };
        await CodeMapAnalysisSession.RunAsync(lifecycle, CancellationToken.None);

        Assert.Equal(
            [
                "ResolveIndex",
                "ShouldSkipIndexUpdate",
                "AnalyzeDeletionImpact",
                "LocateChangedSymbols",
                "UpdateHeadIndex",
                "LocateChangedSymbols",
                "QueryHead"
            ],
            lifecycle.Steps);
        Assert.Equal(1, lifecycle.Steps.Count(step => step == "UpdateHeadIndex"));
    }

    [Fact]
    public async Task ArchitectureCheck_UsesHeadIndex()
    {
        var lifecycle = new RecordingLifecycle { Skip = false, LocateSucceeds = true, RunArchitectureCheck = true };
        await CodeMapAnalysisSession.RunAsync(lifecycle, CancellationToken.None);

        Assert.True(lifecycle.Steps.IndexOf("UpdateHeadIndex") < lifecycle.Steps.IndexOf("QueryHead"));
        Assert.True(lifecycle.ArchitectureQueriedAfterHeadUpdate);
    }

    [Fact]
    public async Task PublicSurfaceBaseline_IsNotReusedAcrossRuns()
    {
        var lifecycle = new RecordingLifecycle { Skip = true, LocateSucceeds = true };
        await CodeMapAnalysisSession.RunAsync(lifecycle, CancellationToken.None);
        await CodeMapAnalysisSession.RunAsync(lifecycle, CancellationToken.None);

        Assert.Equal(2, lifecycle.States.Count);
        Assert.NotSame(lifecycle.States[0], lifecycle.States[1]);
    }

    private sealed class RecordingLifecycle : ICodeMapAnalysisLifecycle
    {
        public List<string> Steps { get; } = [];

        public List<CodeMapAnalysisState> States { get; } = [];

        public bool Skip { get; init; }

        public bool LocateSucceeds { get; init; }

        public bool FailFirstLocate { get; init; }

        public bool RunArchitectureCheck { get; init; }

        public bool ArchitectureQueriedAfterHeadUpdate { get; private set; }

        private int _locateAttempts;

        public string ResolveIndex()
        {
            Steps.Add("ResolveIndex");
            return "index";
        }

        public bool ShouldSkipIndexUpdate(string indexInput)
        {
            Steps.Add("ShouldSkipIndexUpdate");
            return Skip;
        }

        public Task AnalyzeDeletionAsync(CodeMapAnalysisState state, CancellationToken cancellationToken)
        {
            Steps.Add("AnalyzeDeletionImpact");
            States.Add(state);
            return Task.CompletedTask;
        }

        public Task UpdateHeadIndexAsync(CancellationToken cancellationToken)
        {
            Steps.Add("UpdateHeadIndex");
            return Task.CompletedTask;
        }

        public Task LocateAsync(CodeMapAnalysisState state, CancellationToken cancellationToken)
        {
            _locateAttempts++;
            Steps.Add("LocateChangedSymbols");
            var succeeded = LocateSucceeds || (FailFirstLocate && _locateAttempts > 1);
            state.LocateSucceeded = succeeded;
            state.LocateHasValue = succeeded;
            state.LocateError = succeeded ? null : "locate failed";
            return Task.CompletedTask;
        }

        public Task<ChangeAnalysisResult> QueryHeadAsync(CodeMapAnalysisState state, CancellationToken cancellationToken)
        {
            Steps.Add("QueryHead");
            ArchitectureQueriedAfterHeadUpdate = RunArchitectureCheck
                && Steps.IndexOf("UpdateHeadIndex") >= 0
                && Steps.IndexOf("UpdateHeadIndex") < Steps.IndexOf("QueryHead");
            return Task.FromResult(new ChangeAnalysisResult(
                new ChangeImpact("base", "head", [], [], [], [], [], "complete", false),
                []));
        }
    }
}
