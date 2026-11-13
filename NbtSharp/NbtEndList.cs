namespace NbtSharp;

/// <summary>
/// Represents <c>TAG_List&lt;TAG_End&gt;</c>: the empty list whose element type is
/// <see cref="NbtTagType.End"/>.
/// </summary>
/// <remarks>
/// The marker is a singleton. A <see cref="NbtList"/> whose storage is empty returns
/// <see cref="Instance"/> from its <see cref="NbtList.Value"/> property and matches the
/// <see cref="NbtEndList"/> case in patterns. A <see cref="NbtValue"/> that wraps a list exposes the
/// boxed <see cref="NbtList"/>, so match <c>NbtValue is NbtList list</c> before testing
/// <c>list is NbtEndList</c>.
/// </remarks>
public sealed class NbtEndList
{
    private NbtEndList()
    {
    }

    /// <summary>Gets the shared marker for a list of <see cref="NbtTagType.End"/>.</summary>
    public static NbtEndList Instance { get; } = new();
}
