using System.Buffers;
using System.IO.Pipelines;

namespace NbtSharp.IO;

/// <summary>
/// A synchronous <see cref="PipeReader"/> over a stream. <see cref="TryRead"/> performs the stream
/// read directly, so the caller never has to wait for an asynchronous operation.
/// </summary>
internal sealed class NbtSynchronousStreamPipeReader : PipeReader
{
    private const int DefaultBufferSize = 64 * 1024;

    private readonly Stream _stream;
    private byte[]? _buffer;
    private ReadOnlySequence<byte> _sequence;
    private int _start;
    private int _end;
    private bool _completed;
    private bool _resultPending;

    public NbtSynchronousStreamPipeReader(Stream stream, int bufferSize = DefaultBufferSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

        _stream = stream;
        _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    }

    public override bool TryRead(out ReadResult result)
    {
        if (_resultPending)
        {
            throw new InvalidOperationException("AdvanceTo must be called before the next read.");
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(NbtSynchronousStreamPipeReader));
        if (!_completed && _start == _end)
        {
            _start = 0;
            _end = _stream.Read(buffer);
            if (_end == 0)
            {
                _completed = true;
            }
        }

        _sequence = _completed && _start == _end
            ? ReadOnlySequence<byte>.Empty
            : new ReadOnlySequence<byte>(buffer, _start, _end - _start);
        _resultPending = true;
        result = new ReadResult(_sequence, isCanceled: false, isCompleted: _completed && _start == _end);
        return true;
    }

    public override void AdvanceTo(SequencePosition consumed) =>
        AdvanceTo(consumed, _sequence.End);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (!_resultPending)
        {
            throw new InvalidOperationException("There is no pending read result.");
        }

        var consumedLength = _sequence.Slice(0, consumed).Length;
        _ = _sequence.Slice(0, examined);
        _start += (int)consumedLength;
        _sequence = default;
        _resultPending = false;
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryRead(out var result);
        return new ValueTask<ReadResult>(result);
    }

    public override void CancelPendingRead()
    {
    }

    public override void Complete(Exception? exception = null)
    {
        var buffer = _buffer;
        _buffer = null;
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        _completed = true;
        _resultPending = false;
        _sequence = default;
    }
}
