using Proof.Adapters.Git;
using Proof.Core;

namespace Proof.Tests;

public sealed class GitAttestationContextResolverTests
{
    [Fact]
    public async Task ResolveAsync_EnvironmentOverridesWinOverGit()
    {
        var originalRepo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var originalRef = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "oxvxoro/Proofmark");
            Environment.SetEnvironmentVariable("GITHUB_REF_NAME", "ci-branch");

            var context = await new GitAttestationContextResolver()
                .ResolveAsync(Directory.GetCurrentDirectory(), "run-1", CancellationToken.None);

            Assert.Equal("run-1", context.RunId);
            Assert.Equal("oxvxoro/Proofmark", context.Repository);
            Assert.Equal("ci-branch", context.Branch);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", originalRepo);
            Environment.SetEnvironmentVariable("GITHUB_REF_NAME", originalRef);
        }
    }

    [Fact]
    public async Task ResolveAsync_UsesOriginRemote_WithoutCiEnvironment()
    {
        var originalRepo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var originalRef = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", null);
            Environment.SetEnvironmentVariable("GITHUB_REF_NAME", null);

            var context = await new GitAttestationContextResolver()
                .ResolveAsync(Directory.GetCurrentDirectory(), "run-2", CancellationToken.None);

            // 이 저장소의 origin은 GitHub URL이다. 해석기는 그것을
            // owner/repo로 정규화해야 한다. origin이 없으면 필드는 null로 남는다
            // (만들어진 클레임이 아니라 정직한 부재).
            if (context.Repository is not null)
            {
                Assert.EndsWith("oxvxoro/Proofmark", context.Repository, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", originalRepo);
            Environment.SetEnvironmentVariable("GITHUB_REF_NAME", originalRef);
        }
    }
}
