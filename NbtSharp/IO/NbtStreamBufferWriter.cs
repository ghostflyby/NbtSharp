using System.Buffers;

namespace NbtSharp.IO;

/// <summary>
/// Adapts a stream to <see cref="IBufferWriter{T}"/> with a bounded internal buffer.
/// </summary>
internal sealed class NbtStreamBufferWriter(Stream stream) : IBufferWriter<byte>
{
    private byte[] _buffer = new byte[64 * 1024];
    private int _written;

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _written += count;
        if (_written == _buffer.Length)
        {
            FlushBuffer();
        }
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Flush()
    {
        FlushBuffer();
        stream.Flush();
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (_written + sizeHint <= _buffer.Length)
        {
            return;
        }

        FlushBuffer();
        if (sizeHint > _buffer.Length)
        {
            Array.Resize(ref _buffer, sizeHint);
        }
    }

    private void FlushBuffer()
    {
        if (_written == 0)
        {
            return;
        }

        stream.Write(_buffer.AsSpan(0, _written));
        _written = 0;
    }
}
