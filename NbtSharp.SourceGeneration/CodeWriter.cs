using System.Text;

namespace NbtSharp.SourceGeneration;

internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public void Indent() => _indent++;

    public void Outdent()
    {
        if (_indent > 0)
        {
            _indent--;
        }
    }

    public void WriteLine() => _builder.AppendLine();

    public void WriteLine(string line)
    {
        for (var i = 0; i < _indent; i++)
        {
            _builder.Append("    ");
        }

        _builder.AppendLine(line);
    }

    public override string ToString() => _builder.ToString();
}
