using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Proof.Adapters.Git;

/// <summary>
/// <c>git cat-file --batch</c> 응답을 스트리밍하고, 응답 전체를 버퍼링하지
/// 않고 요청 경로마다 SHA-256 16진 다이제스트를 하나씩 반환한다. 없거나
/// 모호한 경로는 <c>null</c>이다. 잘못된 프로토콜 응답은 배치를 실패시키고
/// (<c>null</c> 반환) 호출자가 틀린 다이제스트 대신 내용 해시 수집
/// 실패를 보고하게 한다.
/// </summary>
internal static class GitCatFileBatchHasher
{
    /// <summary>
    /// <paramref name="stdout"/>에서 정확히 <paramref name="relativePaths"/>.Count개의
    /// 응답을 읽고, 각 blob 본문을 통과하면서 해시한다.
    /// 응답이 잘못되었거나 일찍 끝나면 null을 반환한다.
    /// </summary>
    public static async Task<Dictionary<string, string?>?> HashAsync(
        Stream stdout,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(relativePaths);

        using var reader = new ByteStreamReader(stdout);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var path in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var header = await ReadLineAsync(reader, cancellationToken).ConfigureAwait(false);
            if (header is null || !TryParseHeader(header, out var size, out var missing))
            {
                return null;
            }

            if (missing)
            {
                result[path] = null;
                continue;
            }

            var hash = await HashExactlyAsync(reader, size, cancellationToken).ConfigureAwait(false);
            if (hash is null)
            {
                return null;
            }

            // git은 각 blob 본문을 개행 하나로 끝낸다.
            var terminator = await ReadByteAsync(reader, cancellationToken).ConfigureAwait(false);
            if (terminator != (byte)'\n')
            {
                return null;
            }

            result[path] = hash;
        }

        return result;
    }

    private static bool TryParseHeader(string header, out long size, out bool missing)
    {
        size = 0;
        missing = false;

        if (header.EndsWith(" missing", StringComparison.Ordinal)
            || header.EndsWith(" ambiguous", StringComparison.Ordinal))
        {
            missing = true;
            return true;
        }

        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !long.TryParse(parts[^1], out size) || size < 0)
        {
            return false;
        }

        return true;
    }

    private static async Task<string?> ReadLineAsync(ByteStreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new ArrayBufferWriter<byte>();
        while (true)
        {
            if (reader.Buffered.Length == 0 && !await reader.FillAsync(cancellationToken).ConfigureAwait(false))
            {
                // 개행 없는 EOF. 이후 응답은 없다.
                return builder.WrittenCount == 0
                    ? null
                    : Encoding.UTF8.GetString(builder.WrittenSpan).TrimEnd('\r');
            }

            var buffered = reader.Buffered.Span;
            var newlineIndex = buffered.IndexOf((byte)'\n');
            if (newlineIndex >= 0)
            {
                builder.Write(buffered[..newlineIndex]);
                reader.Consume(newlineIndex + 1);
                return Encoding.UTF8.GetString(builder.WrittenSpan).TrimEnd('\r');
            }

            builder.Write(buffered);
            reader.Consume(buffered.Length);
        }
    }

    private static async Task<string?> HashExactlyAsync(
        ByteStreamReader reader,
        long length,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var remaining = length;

        while (remaining > 0)
        {
            if (reader.Buffered.Length == 0 && !await reader.FillAsync(cancellationToken).ConfigureAwait(false))
            {
                // 선언된 길이보다 먼저 본문이 끝났다. 프로토콜 실패다.
                return null;
            }

            var buffered = reader.Buffered.Span;
            var take = (int)Math.Min(buffered.Length, remaining);
            hash.AppendData(buffered[..take]);
            reader.Consume(take);
            remaining -= take;
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<byte?> ReadByteAsync(ByteStreamReader reader, CancellationToken cancellationToken)
    {
        if (reader.Buffered.Length == 0 && !await reader.FillAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var value = reader.Buffered.Span[0];
        reader.Consume(1);
        return value;
    }

    /// <summary>
    /// 원시 stdout 스트림 위의 단일 버퍼 리더. 헤더 줄과 blob
    /// 본문을 같은 버퍼로 읽어 바이트 위치가 결코 밀리지 않게 한다
    /// (<see cref="StreamReader"/>는 헤더를 지나 미리 읽는다).
    /// </summary>
    private sealed class ByteStreamReader : IDisposable
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer;
        private int _start;
        private int _end;

        public ByteStreamReader(Stream stream)
        {
            _stream = stream;
            _buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        }

        public ReadOnlyMemory<byte> Buffered => _buffer.AsMemory(_start, _end - _start);

        public void Consume(int count) => _start += count;

        public async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
        {
            _start = 0;
            _end = await _stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            return _end > 0;
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
    }
}
