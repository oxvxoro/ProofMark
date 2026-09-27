using System.Text;
using BenchmarkDotNet.Attributes;
using Proof.Adapters.Git;

namespace Proof.Benchmarks;

/// <summary>
/// 스트리밍 git base-blob 해싱(PR-05). 옛 blob 바이트 총량은 늘어도
/// 스트림 버퍼는 고정이므로, 할당 바이트는 blob 총량이 아니라
/// 버퍼를 따라야 한다.
/// </summary>
[MemoryDiagnoser]
public class GitHashBenchmarks
{
    private const int BlobSize = 64 * 1024;

    [Params(10, 100, 500)]
    public int TotalMegabytes { get; set; }

    private MemoryStream _stream = null!;
    private string[] _paths = null!;

    [GlobalSetup]
    public void Setup()
    {
        var totalBytes = (long)TotalMegabytes * 1024 * 1024;
        var blobCount = Math.Max(1, (int)(totalBytes / BlobSize));
        _paths = Enumerable.Range(0, blobCount).Select(index => $"file-{index:D6}.txt").ToArray();

        var content = new string('x', BlobSize);
        var stream = new MemoryStream(blobCount * (BlobSize + 64));
        using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            for (var index = 0; index < blobCount; index++)
            {
                writer.Write("sha" + index);
                writer.Write(" blob ");
                writer.Write(BlobSize);
                writer.Write('\n');
                writer.Write(content);
                writer.Write('\n');
            }
        }

        _stream = stream;
    }

    [Benchmark]
    public int Hash()
    {
        _stream.Position = 0;
        var hashes = GitCatFileBatchHasher
            .HashAsync(_stream, _paths, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return hashes?.Count ?? 0;
    }
}
