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
}
