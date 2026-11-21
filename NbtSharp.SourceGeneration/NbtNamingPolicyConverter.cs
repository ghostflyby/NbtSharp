using System.Text;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Mirrors the runtime <c>NbtNamingPolicy</c> presets without a runtime dependency. Kind values match
/// <c>NbtNamingPolicyKind</c>.
/// </summary>
internal static class NbtNamingPolicyConverter
{
    private const int CamelCase = 1;
    private const int SnakeCaseLower = 2;
    private const int SnakeCaseUpper = 3;
    private const int KebabCaseLower = 4;
    private const int KebabCaseUpper = 5;

    public static Func<string, string>? Create(int kind) => kind switch
    {
        CamelCase => static name => ConvertCamelCase(name),
        SnakeCaseLower => static name => ConvertSeparated(name, '_', upper: false),
        SnakeCaseUpper => static name => ConvertSeparated(name, '_', upper: true),
        KebabCaseLower => static name => ConvertSeparated(name, '-', upper: false),
        KebabCaseUpper => static name => ConvertSeparated(name, '-', upper: true),
        _ => null,
    };

    private static string ConvertCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var characters = name.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (!char.IsUpper(characters[index]))
            {
                break;
            }

            if (index > 0 &&
                index + 1 < characters.Length &&
                char.IsLower(characters[index + 1]))
            {
                break;
            }

            characters[index] = char.ToLowerInvariant(characters[index]);
        }

        return new string(characters);
    }

    private static string ConvertSeparated(string name, char separator, bool upper)
    {
        if (name.Length == 0)
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) &&
                index > 0 &&
                (char.IsLower(name[index - 1]) ||
                 char.IsDigit(name[index - 1]) ||
                 (index + 1 < name.Length && char.IsLower(name[index + 1]))) &&
                name[index - 1] != separator)
            {
                builder.Append(separator);
            }

            builder.Append(upper
                ? char.ToUpperInvariant(character)
                : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
