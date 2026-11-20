using System.Text;

namespace NbtSharp;

/// <summary>Well-known naming policies for generated and reflection-mapped member names.</summary>
public enum NbtNamingPolicyKind
{
    /// <summary>Use the CLR member name unchanged.</summary>
    Unspecified,

    /// <summary>Lowercase the leading uppercase characters, for example <c>PlayerName</c> to <c>playerName</c>.</summary>
    CamelCase,

    /// <summary>Separate words with underscores and lowercase them, for example <c>PlayerName</c> to <c>player_name</c>.</summary>
    SnakeCaseLower,

    /// <summary>Separate words with underscores and uppercase them, for example <c>PlayerName</c> to <c>PLAYER_NAME</c>.</summary>
    SnakeCaseUpper,

    /// <summary>Separate words with hyphens and lowercase them, for example <c>PlayerName</c> to <c>player-name</c>.</summary>
    KebabCaseLower,

    /// <summary>Separate words with hyphens and uppercase them, for example <c>PlayerName</c> to <c>PLAYER-NAME</c>.</summary>
    KebabCaseUpper,
}

/// <summary>
/// Converts CLR member names to NBT member names.
/// </summary>
/// <remarks>
/// Converters capture member names when they are created. Custom policies must therefore be
/// immutable and return stable results for the lifetime of the options that use them; the
/// reflection resolver caches converters per policy instance.
/// </remarks>
public abstract class NbtNamingPolicy
{
    /// <summary>Gets the camelCase policy.</summary>
    public static NbtNamingPolicy CamelCase { get; } = new CamelCasePolicy();

    /// <summary>Gets the lower_snake_case policy.</summary>
    public static NbtNamingPolicy SnakeCaseLower { get; } = new SeparatedPolicy('_', upper: false);

    /// <summary>Gets the UPPER_SNAKE_CASE policy.</summary>
    public static NbtNamingPolicy SnakeCaseUpper { get; } = new SeparatedPolicy('_', upper: true);

    /// <summary>Gets the lower-kebab-case policy.</summary>
    public static NbtNamingPolicy KebabCaseLower { get; } = new SeparatedPolicy('-', upper: false);

    /// <summary>Gets the UPPER-KEBAB-CASE policy.</summary>
    public static NbtNamingPolicy KebabCaseUpper { get; } = new SeparatedPolicy('-', upper: true);

    /// <summary>Converts a CLR member name to an NBT member name.</summary>
    /// <param name="name">The CLR member name.</param>
    /// <returns>The converted NBT member name.</returns>
    public abstract string ConvertName(string name);

    /// <summary>Gets the policy for a well-known kind, or <see langword="null"/> for an unspecified kind.</summary>
    /// <param name="kind">The policy kind.</param>
    /// <returns>The policy, or <see langword="null"/>.</returns>
    public static NbtNamingPolicy? FromKind(NbtNamingPolicyKind kind) => kind switch
    {
        NbtNamingPolicyKind.CamelCase => CamelCase,
        NbtNamingPolicyKind.SnakeCaseLower => SnakeCaseLower,
        NbtNamingPolicyKind.SnakeCaseUpper => SnakeCaseUpper,
        NbtNamingPolicyKind.KebabCaseLower => KebabCaseLower,
        NbtNamingPolicyKind.KebabCaseUpper => KebabCaseUpper,
        _ => null,
    };

    private sealed class CamelCasePolicy : NbtNamingPolicy
    {
        public override string ConvertName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
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
    }

    private sealed class SeparatedPolicy(char separator, bool upper) : NbtNamingPolicy
    {
        public override string ConvertName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
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
}
