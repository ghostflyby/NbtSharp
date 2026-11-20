namespace NbtSharp.Tests;

public sealed class NbtNamingPolicyTests
{
    [Theory]
    [InlineData("PlayerName", "playerName")]
    [InlineData("URLValue", "urlValue")]
    [InlineData("ID", "id")]
    [InlineData("Value", "value")]
    [InlineData("", "")]
    public void CamelCaseConvertsNames(string input, string expected)
    {
        Assert.Equal(expected, NbtNamingPolicy.CamelCase.ConvertName(input));
    }

    [Theory]
    [InlineData("PlayerName", "player_name")]
    [InlineData("URLValue", "url_value")]
    [InlineData("ID", "id")]
    [InlineData("Health2Points", "health2_points")]
    public void SnakeCaseConvertsNames(string input, string expected)
    {
        Assert.Equal(expected, NbtNamingPolicy.SnakeCaseLower.ConvertName(input));
        Assert.Equal(expected.ToUpperInvariant(), NbtNamingPolicy.SnakeCaseUpper.ConvertName(input));
    }

    [Theory]
    [InlineData("PlayerName", "player-name")]
    [InlineData("URLValue", "url-value")]
    public void KebabCaseConvertsNames(string input, string expected)
    {
        Assert.Equal(expected, NbtNamingPolicy.KebabCaseLower.ConvertName(input));
        Assert.Equal(expected.ToUpperInvariant(), NbtNamingPolicy.KebabCaseUpper.ConvertName(input));
    }

    [Fact]
    public void FromKindReturnsTheExpectedPreset()
    {
        Assert.Same(NbtNamingPolicy.CamelCase, NbtNamingPolicy.FromKind(NbtNamingPolicyKind.CamelCase));
        Assert.Same(
            NbtNamingPolicy.SnakeCaseLower,
            NbtNamingPolicy.FromKind(NbtNamingPolicyKind.SnakeCaseLower));
        Assert.Null(NbtNamingPolicy.FromKind(NbtNamingPolicyKind.Unspecified));
    }

    [Fact]
    public void ReflectionUsesTheConfiguredNamingPolicy()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            PropertyNamingPolicy = NbtNamingPolicy.SnakeCaseLower,
        };
        var value = new NamingPolicyDto
        {
            PlayerName = "Steve",
            HealthPoints = 20,
            ExplicitValue = 3,
        };

        var data = NbtSerializer.Serialize(value, "root", options);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = NbtSerializer.Deserialize<NamingPolicyDto>(data, options);

        Assert.True(root.ContainsKey("player_name"));
        Assert.True(root.ContainsKey("health_points"));
        Assert.True(root.ContainsKey("exact"));
        Assert.False(root.ContainsKey("PlayerName"));
        Assert.Equal("Steve", copy.PlayerName);
        Assert.Equal(20, copy.HealthPoints);
        Assert.Equal(3, copy.ExplicitValue);
    }

    [Fact]
    public void GeneratedContextAppliesCamelCase()
    {
        var context = CamelCaseContext.Default;
        var value = new NamingPolicyDto
        {
            PlayerName = "Alex",
            HealthPoints = 10,
            ExplicitValue = 1,
        };

        var data = NbtSerializer.Serialize(value, context.NamingPolicyDto, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = NbtSerializer.Deserialize(data, context.NamingPolicyDto);

        Assert.True(root.ContainsKey("playerName"));
        Assert.True(root.ContainsKey("healthPoints"));
        Assert.True(root.ContainsKey("exact"));
        Assert.False(root.ContainsKey("PlayerName"));
        Assert.Equal("Alex", copy.PlayerName);
        Assert.Equal(10, copy.HealthPoints);
        Assert.Equal(1, copy.ExplicitValue);
    }

    [Fact]
    public void GeneratedContextAppliesSnakeCase()
    {
        var context = SnakeCaseContext.Default;
        var value = new NamingPolicyDto
        {
            PlayerName = "Alex",
            HealthPoints = 10,
            ExplicitValue = 1,
        };

        var data = NbtSerializer.Serialize(value, context.NamingPolicyDto, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.True(root.ContainsKey("player_name"));
        Assert.True(root.ContainsKey("health_points"));
        Assert.True(root.ContainsKey("exact"));
        Assert.False(root.ContainsKey("PlayerName"));
    }

    [Fact]
    public void GeneratedContextAppliesOtherNamingPresets()
    {
        var value = new NamingPolicyDto
        {
            PlayerName = "Alex",
            HealthPoints = 10,
            ExplicitValue = 1,
        };

        var kebab = NbtSerializer.Serialize(value, KebabCaseContext.Default.NamingPolicyDto, "root");
        var upper = NbtSerializer.Serialize(
            value,
            UpperSnakeCaseContext.Default.NamingPolicyDto,
            "root");
        var upperKebab = NbtSerializer.Serialize(
            value,
            UpperKebabCaseContext.Default.NamingPolicyDto,
            "root");
        var kebabRoot = (NbtCompound)NbtSerializer.Deserialize(kebab).Value.Value!;
        var upperRoot = (NbtCompound)NbtSerializer.Deserialize(upper).Value.Value!;
        var upperKebabRoot = (NbtCompound)NbtSerializer.Deserialize(upperKebab).Value.Value!;

        Assert.True(kebabRoot.ContainsKey("player-name"));
        Assert.True(kebabRoot.ContainsKey("health-points"));
        Assert.True(upperRoot.ContainsKey("PLAYER_NAME"));
        Assert.True(upperRoot.ContainsKey("HEALTH_POINTS"));
        Assert.True(upperKebabRoot.ContainsKey("PLAYER-NAME"));
        Assert.True(upperKebabRoot.ContainsKey("HEALTH-POINTS"));
    }

    [Fact]
    public void ExplicitOptionsKeepTheirNamingPolicy()
    {
        var value = new NamingPolicyDto
        {
            PlayerName = "Alex",
            HealthPoints = 10,
            ExplicitValue = 1,
        };
        var context = new CamelCaseContext(new NbtSerializerOptions
        {
            PropertyNamingPolicy = NbtNamingPolicy.SnakeCaseLower,
        });

        Assert.Same(NbtNamingPolicy.SnakeCaseLower, context.Options.PropertyNamingPolicy);

        var data = NbtSerializer.Serialize(value, context.NamingPolicyDto, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.True(root.ContainsKey("playerName"));
        Assert.False(root.ContainsKey("player_name"));
    }

    [Fact]
    public void GeneratedContextDefaultOptionsExposeConfiguredPolicy()
    {
        Assert.Same(
            NbtNamingPolicy.CamelCase,
            CamelCaseContext.Default.Options.PropertyNamingPolicy);
        Assert.Same(
            NbtNamingPolicy.SnakeCaseLower,
            SnakeCaseContext.Default.Options.PropertyNamingPolicy);
        Assert.Same(
            NbtNamingPolicy.KebabCaseLower,
            KebabCaseContext.Default.Options.PropertyNamingPolicy);
        Assert.Same(
            NbtNamingPolicy.SnakeCaseUpper,
            UpperSnakeCaseContext.Default.Options.PropertyNamingPolicy);
        Assert.Same(
            NbtNamingPolicy.KebabCaseUpper,
            UpperKebabCaseContext.Default.Options.PropertyNamingPolicy);
        Assert.Equal(
            NbtNullValueHandling.Omit,
            CamelCaseContext.Default.Options.NullValueHandling);
        Assert.Equal(
            NbtNullValueHandling.Throw,
            ThrowOnNullContext.Default.Options.NullValueHandling);
    }

    [Fact]
    public void GeneratedContextAppliesConfiguredNullHandling()
    {
        var context = ThrowOnNullContext.Default;
        var value = new NullableNamingDto { OptionalName = null };

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(value, context.NullableNamingDto, "root"));

        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class NullableNamingDto
{
    public string? OptionalName { get; set; }
}
internal sealed class NamingPolicyDto
{
    public string PlayerName { get; set; } = "";

    public int HealthPoints { get; set; }

    [NbtPropertyName("exact")]
    public int ExplicitValue { get; set; }
}

[NbtSerializable(typeof(NamingPolicyDto))]
[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.CamelCase)]
internal partial class CamelCaseContext : NbtSerializerContext
{
    public CamelCaseContext()
    {
    }

    public CamelCaseContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

[NbtSerializable(typeof(NamingPolicyDto))]
[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.SnakeCaseLower)]
internal partial class SnakeCaseContext : NbtSerializerContext
{
}

[NbtSerializable(typeof(NamingPolicyDto))]
[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.KebabCaseLower)]
internal partial class KebabCaseContext : NbtSerializerContext
{
}

[NbtSerializable(typeof(NamingPolicyDto))]
[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.SnakeCaseUpper)]
internal partial class UpperSnakeCaseContext : NbtSerializerContext
{
}

[NbtSerializable(typeof(NamingPolicyDto))]
[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.KebabCaseUpper)]
internal partial class UpperKebabCaseContext : NbtSerializerContext
{
}

[NbtSerializable(typeof(NullableNamingDto))]
[NbtSourceGenerationOptions(NullValueHandling = NbtNullValueHandling.Throw)]
internal partial class ThrowOnNullContext : NbtSerializerContext
{
}
