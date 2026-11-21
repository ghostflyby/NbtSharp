namespace NbtSharp.Tests;

public sealed class NbtConverterParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitConverterIsUsedAndOverridesOptionsConverters(bool reflection)
    {
        var path = CreatePath(
            reflection,
            addConverters: static options =>
                options.Converters.Add(new OffsetIntConverter(200)));
        var value = new ConverterParityDto { ExplicitInt = 5 };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = path.Deserialize<ConverterParityDto>(data);

        Assert.Equal(105, (int)root["ExplicitInt"].Value!);
        Assert.Equal(5, copy.ExplicitInt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitConverterHandlesTypesWithNoBuiltInMapping(bool reflection)
    {
        var path = CreatePath(reflection);
        var value = new ConverterParityDto
        {
            ExplicitGuid = new Guid("11111111-1111-1111-1111-111111111111"),
        };

        var data = path.Serialize(value);
        var copy = path.Deserialize<ConverterParityDto>(data);

        Assert.Equal(value.ExplicitGuid, copy.ExplicitGuid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeConverterUsesOptionsConverters(bool reflection)
    {
        var guid = new Guid("22222222-2222-2222-2222-222222222222");
        var path = CreatePath(
            reflection,
            addConverters: static options =>
                options.Converters.Add(new SourceGenGuidConverter()));
        var value = new RuntimeConverterDto { RuntimeGuid = guid };

        var data = path.Serialize(value);
        var copy = path.Deserialize<RuntimeConverterDto>(data);

        Assert.Equal(guid, copy.RuntimeGuid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeConverterWithoutRegistrationThrows(bool reflection)
    {
        var path = CreatePath(reflection);
        var value = new RuntimeConverterDto
        {
            RuntimeGuid = new Guid("33333333-3333-3333-3333-333333333333"),
        };

        Assert.Throws<NotSupportedException>(() => path.Serialize(value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitConverterFactoryIsUsed(bool reflection)
    {
        var path = CreatePath(reflection);
        var value = new ConverterParityDto { ExplicitFactoryInt = 7 };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = path.Deserialize<ConverterParityDto>(data);

        Assert.Equal(307, (int)root["ExplicitFactoryInt"].Value!);
        Assert.Equal(7, copy.ExplicitFactoryInt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitConverterNullMemberHonorsNullHandling(
        bool reflection,
        bool throwOnNull)
    {
        var path = CreatePath(
            reflection,
            nullValueHandling: throwOnNull
                ? NbtNullValueHandling.Throw
                : NbtNullValueHandling.Omit);
        var value = new ConverterParityDto { ExplicitText = null };

        if (throwOnNull)
        {
            Assert.Throws<NbtException>(() => path.Serialize(value));
            return;
        }

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.False(root.ContainsKey("ExplicitText"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitConverterOnIncludedFieldIsUsed(bool reflection)
    {
        var path = CreatePath(reflection);
        var value = new ConverterFieldDto { Value = 4 };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = path.Deserialize<ConverterFieldDto>(data);

        Assert.Equal(104, (int)root["Value"].Value!);
        Assert.Equal(4, copy.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverridesInheritExplicitConverterAttributes(bool reflection)
    {
        var path = CreatePath(reflection);
        var value = new ConverterOverrideDto { Value = 6 };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = path.Deserialize<ConverterOverrideDto>(data);

        Assert.Equal(106, (int)root["Value"].Value!);
        Assert.Equal(6, copy.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitFactoryMustReturnConverterForMemberType(bool reflection)
    {
        var path = CreatePath(reflection);
        var value = new BadFactoryDto { Id = Guid.NewGuid() };

        var write = Assert.Throws<NotSupportedException>(() => path.Serialize(value));
        var data = NbtSerializer.Serialize(
            new NbtRoot("root", new NbtCompound { ["Id"] = new sbyte[16] }));
        var read = Assert.Throws<NotSupportedException>(() => path.Deserialize<BadFactoryDto>(data));

        Assert.Contains("does not support", write.Message, StringComparison.Ordinal);
        Assert.Contains("does not support", read.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitConverterWithoutTagPredictionFailsOnlyOnWrite(bool reflection)
    {
        var path = CreatePath(reflection);
        var value = new NoTagTypeDto { Value = 3 };

        var write = Assert.Throws<NotSupportedException>(() => path.Serialize(value));
        var data = NbtSerializer.Serialize(
            new NbtRoot("root", new NbtCompound { ["Value"] = 3 }));
        var copy = path.Deserialize<NoTagTypeDto>(data);

        Assert.Contains("TryGetTagType", write.Message, StringComparison.Ordinal);
        Assert.Equal(3, copy.Value);
    }

    [Fact]
    public void ReflectionRejectsInvalidConverterDeclarations()
    {
        var resolver = new NbtReflectionTypeInfoResolver();
        var options = new NbtSerializerOptions();

        var mismatched = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(InvalidConverterDto), options));
        var missingInclude = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(InvalidFieldConverterDto), options));
        var notConverter = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(InvalidNotConverterDto), options));
        var abstractConverter = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(InvalidAbstractConverterDto), options));
        var missingCtor = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(InvalidCtorConverterDto), options));
        var openGeneric = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(InvalidOpenGenericConverterDto), options));

        Assert.Contains("converter handles", mismatched.Message, StringComparison.Ordinal);
        Assert.Contains("fields require [NbtInclude]", missingInclude.Message, StringComparison.Ordinal);
        Assert.Contains("must derive from NbtSharp.NbtConverter", notConverter.Message, StringComparison.Ordinal);
        Assert.Contains("must not be abstract", abstractConverter.Message, StringComparison.Ordinal);
        Assert.Contains("public parameterless constructor", missingCtor.Message, StringComparison.Ordinal);
        Assert.Contains("closed generic type", openGeneric.Message, StringComparison.Ordinal);
        Assert.Equal(0, new InvalidFieldConverterDto().Value);
    }

    private static TestPath CreatePath(
        bool reflection,
        NbtNullValueHandling nullValueHandling = NbtNullValueHandling.Omit,
        Action<NbtSerializerOptions>? addConverters = null)
    {
        if (reflection)
        {
            var options = new NbtSerializerOptions
            {
                NullValueHandling = nullValueHandling,
                TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            };
            addConverters?.Invoke(options);
            return new ReflectionPath(options);
        }

        var generatedOptions = new NbtSerializerOptions
        {
            NullValueHandling = nullValueHandling,
        };
        addConverters?.Invoke(generatedOptions);
        return new GeneratedPath(new ConverterParityContext(generatedOptions));
    }

    private abstract class TestPath
    {
        public abstract byte[] Serialize<T>(T value);

        public abstract T Deserialize<T>(byte[] data);
    }

    private sealed class GeneratedPath(ConverterParityContext context) : TestPath
    {
        public override byte[] Serialize<T>(T value) =>
            NbtSerializer.Serialize(value, GetTypeInfo<T>(), "root");

        public override T Deserialize<T>(byte[] data) =>
            NbtSerializer.Deserialize(data, GetTypeInfo<T>());

        private NbtTypeInfo<T> GetTypeInfo<T>() =>
            (NbtTypeInfo<T>)context.GetTypeInfo(typeof(T), context.Options)!;
    }

    private sealed class ReflectionPath(NbtSerializerOptions options) : TestPath
    {
        public override byte[] Serialize<T>(T value) =>
            NbtSerializer.Serialize(value, "root", options);

        public override T Deserialize<T>(byte[] data) =>
            NbtSerializer.Deserialize<T>(data, options);
    }
}

internal sealed class ConverterParityDto
{
    [NbtUseConverter(typeof(OffsetIntConverter))]
    public int ExplicitInt { get; set; }

    [NbtUseConverter(typeof(SourceGenGuidConverter))]
    public Guid ExplicitGuid { get; set; }

    [NbtUseConverter(typeof(OffsetIntFactory))]
    public int ExplicitFactoryInt { get; set; }

    [NbtUseConverter(typeof(ParityStringConverter))]
    public string? ExplicitText { get; set; }
}

internal sealed class RuntimeConverterDto
{
    [NbtUseConverter]
    public Guid RuntimeGuid { get; set; }
}

internal sealed class BadFactoryDto
{
    [NbtUseConverter(typeof(SourceGenBadFactory))]
    public Guid Id { get; set; }
}

internal sealed class NoTagTypeDto
{
    [NbtUseConverter(typeof(NoTagTypeIntConverter))]
    public int Value { get; set; }
}

internal sealed class NoTagTypeIntConverter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32();

    public override void Write(
        ref NbtWriter writer,
        int value,
        NbtSerializerOptions options) =>
        writer.WriteInt32(value);
}

internal sealed class ConverterFieldDto
{
    [NbtInclude]
    [NbtUseConverter(typeof(OffsetIntConverter))]
    public int Value;
}

internal class ConverterOverrideBase
{
    [NbtUseConverter(typeof(OffsetIntConverter))]
    public virtual int Value { get; set; }
}

internal sealed class ConverterOverrideDto : ConverterOverrideBase
{
    public override int Value { get; set; }
}

internal sealed class InvalidConverterDto
{
    [NbtUseConverter(typeof(SourceGenGuidConverter))]
    public int Value { get; set; }
}

internal sealed class InvalidFieldConverterDto
{
    [NbtUseConverter]
    public int Value = 0;
}

internal sealed class NotAConverter
{
}

internal abstract class AbstractIntConverter : NbtConverter<int>
{
}

internal sealed class NoDefaultCtorIntConverter : NbtConverter<int>
{
    public NoDefaultCtorIntConverter(int value)
    {
        _ = value;
    }

    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32();

    public override void Write(
        ref NbtWriter writer,
        int value,
        NbtSerializerOptions options) =>
        writer.WriteInt32(value);
}

internal sealed class OpenGenericConverter<T> : NbtConverter<T>
{
    public override T Read(ref NbtReader reader, NbtSerializerOptions options) =>
        default!;

    public override void Write(
        ref NbtWriter writer,
        T value,
        NbtSerializerOptions options)
    {
    }
}

internal sealed class InvalidNotConverterDto
{
    [NbtUseConverter(typeof(NotAConverter))]
    public int Value { get; set; }
}

internal sealed class InvalidAbstractConverterDto
{
    [NbtUseConverter(typeof(AbstractIntConverter))]
    public int Value { get; set; }
}

internal sealed class InvalidCtorConverterDto
{
    [NbtUseConverter(typeof(NoDefaultCtorIntConverter))]
    public int Value { get; set; }
}

internal sealed class InvalidOpenGenericConverterDto
{
    [NbtUseConverter(typeof(OpenGenericConverter<>))]
    public int Value { get; set; }
}

internal sealed class OffsetIntFactory : NbtConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

    public override NbtConverter CreateConverter(
        Type typeToConvert,
        NbtSerializerOptions options) =>
        new OffsetIntConverter(300);
}

internal sealed class ParityStringConverter : NbtConverter<string>
{
    public override string Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetString();

    public override void Write(
        ref NbtWriter writer,
        string value,
        NbtSerializerOptions options) =>
        writer.WriteString(value);

    public override bool TryGetTagType(
        string value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.String;
        return true;
    }
}

[NbtSerializable(typeof(ConverterParityDto))]
[NbtSerializable(typeof(RuntimeConverterDto))]
[NbtSerializable(typeof(BadFactoryDto))]
[NbtSerializable(typeof(NoTagTypeDto))]
[NbtSerializable(typeof(ConverterFieldDto))]
[NbtSerializable(typeof(ConverterOverrideDto))]
internal partial class ConverterParityContext : NbtSerializerContext
{
    public ConverterParityContext()
    {
    }

    public ConverterParityContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}
