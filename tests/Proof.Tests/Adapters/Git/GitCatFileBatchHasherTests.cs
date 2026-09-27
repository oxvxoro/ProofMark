using System.Security.Cryptography;
using System.Text;
using Proof.Adapters.Git;

namespace Proof.Tests;

public sealed class GitCatFileBatchHasherTests
{
    [Fact]
    public async Task HashAsync_SingleBlob_ReturnsSha256()
    {
        var result = await HashAsync("abc blob 5\nhello\n", ["a.txt"]);

        Assert.NotNull(result);
        Assert.Equal(Expected("hello"), result!["a.txt"]);
    }

    [Fact]
    public async Task HashAsync_MultipleBlobs_AssignsEachDigest()
    {
        var result = await HashAsync(
            "aaa blob 3\nfoo\nbbb blob 3\nbar\n",
            ["a.txt", "b.txt"]);

        Assert.NotNull(result);
        Assert.Equal(Expected("foo"), result!["a.txt"]);
        Assert.Equal(Expected("bar"), result["b.txt"]);
    }

    [Fact]
    public async Task HashAsync_MissingPath_IsNull()
    {
        var result = await HashAsync("aaa missing\n", ["a.txt"]);

        Assert.NotNull(result);
        Assert.Null(result!["a.txt"]);
    }

    [Fact]
    public async Task HashAsync_AmbiguousPath_IsNull()
    {
        var result = await HashAsync("aaa ambiguous\n", ["a.txt"]);

        Assert.NotNull(result);
        Assert.Null(result!["a.txt"]);
    }

    [Fact]
    public async Task HashAsync_MissingThenBlob_ContinuesWithNextEntry()
    {
        var result = await HashAsync(
            "aaa missing\nbbb blob 3\nbar\n",
            ["a.txt", "b.txt"]);

        Assert.NotNull(result);
        Assert.Null(result!["a.txt"]);
        Assert.Equal(Expected("bar"), result["b.txt"]);
    }

    [Fact]
    public async Task HashAsync_BodyShorterThanHeader_FailsBatch()
    {
        var result = await HashAsync("abc blob 20\nhi\n", ["a.txt"]);

        Assert.Null(result);
    }

    [Fact]
    public async Task HashAsync_MissingTrailingNewline_FailsBatch()
    {
        var result = await HashAsync("abc blob 5\nhello", ["a.txt"]);

        Assert.Null(result);
    }

    [Fact]
    public async Task HashAsync_BlobLargerThanReadBuffer_IsCorrect()
    {
        var content = new string('x', 100_000);
        var result = await HashAsync($"abc blob {content.Length}\n{content}\n", ["big.txt"]);

        Assert.NotNull(result);
        Assert.Equal(Expected(content), result!["big.txt"]);
    }

    [Fact]
    public async Task HashAsync_LargeDeclaredLength_FailsWithoutOverflow()
    {
        // 3,000,000,000은 int.MaxValue를 넘는다. 파서는 long을 쓰고 32비트
        // 캐스트가 넘치지 않도록 EOF에서 깨끗이 실패해야 한다.
        var result = await HashAsync("abc blob 3000000000\nhi\n", ["a.txt"]);

        Assert.Null(result);
    }

    [Fact]
    public async Task HashAsync_EmptyPathList_ReturnsEmptyResult()
    {
        var result = await HashAsync(string.Empty, []);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task HashAsync_MalformedHeader_FailsBatch()
    {
        var result = await HashAsync("abc blob\nhello\n", ["a.txt"]);

        Assert.Null(result);
    }

    private static async Task<Dictionary<string, string?>?> HashAsync(string fixture, string[] paths)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fixture));
        return await GitCatFileBatchHasher.HashAsync(stream, paths, CancellationToken.None);
    }

    private static string Expected(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
