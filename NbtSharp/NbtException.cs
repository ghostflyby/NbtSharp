namespace NbtSharp;

/// <summary>
/// The exception thrown when NBT data is malformed or a structural operation is invalid.
/// </summary>
public class NbtException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="NbtException"/> class.</summary>
    public NbtException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public NbtException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public NbtException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the zero-based byte offset in the document at which the error was detected, when known.
    /// </summary>
    public long? BytePosition { get; internal init; }

    /// <summary>
    /// Gets the JSON-path-like location of the value that caused the error, when known. The root
    /// value is <c>$</c>, compound members use <c>.Name</c>, and list elements use <c>[index]</c>.
    /// </summary>
    public string? Path { get; internal set; }

    /// <summary>Gets the NBT tag type that the operation expected, when known.</summary>
    public NbtTagType? ExpectedTagType { get; internal init; }

    /// <summary>Gets the NBT tag type that was found, when known.</summary>
    public NbtTagType? ActualTagType { get; internal init; }

    internal bool IsTruncated { get; init; }
}
