using System.Buffers;

namespace NbtSharp.Tests;

public sealed class NbtPolymorphismTests
{
    [Fact]
    public void GeneratedAndReflectionPathsRoundTripPolymorphicMembers()
    {
        var value = CreateZoo();
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generatedData = NbtSerializer.Serialize(
            value,
            PolyContext.Default.PolyZoo,
            "root");
        var reflectedData = NbtSerializer.Serialize(value, "root", reflection);

        var generatedRoot = (NbtCompound)NbtSerializer.Deserialize(generatedData).Value.Value!;
        var star = (NbtCompound)generatedRoot["Star"].Value!;
        Assert.Equal("PolyCat", (string)star["$type"].Value!);
        Assert.IsType<NbtCompound>(star["$value"].Value);

        var generatedCopy = NbtSerializer.Deserialize(generatedData, PolyContext.Default.PolyZoo);
        var reflectedCopy = NbtSerializer.Deserialize<PolyZoo>(reflectedData, reflection);
        var crossGenerated = NbtSerializer.Deserialize<PolyZoo>(generatedData, reflection);
        var crossReflected = NbtSerializer.Deserialize(reflectedData, PolyContext.Default.PolyZoo);

        Assert.Equal(reflectedData, generatedData);
        AssertZoo(generatedCopy);
        AssertZoo(reflectedCopy);
        AssertZoo(crossGenerated);
        AssertZoo(crossReflected);
    }

    [Fact]
    public void StructImplementationsRoundTripAsRoots()
    {
        var value = new PolyRock { Name = "rock" };
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generatedData = NbtSerializer.Serialize(
            value,
            PolyContext.Default.IPolyAnimal,
            "root");
        var reflectedData = NbtSerializer.Serialize<IPolyAnimal>(value, "root", reflection);

        Assert.IsType<PolyRock>(
            NbtSerializer.Deserialize(generatedData, PolyContext.Default.IPolyAnimal));
        Assert.IsType<PolyRock>(
            NbtSerializer.Deserialize<IPolyAnimal>(reflectedData, reflection));
    }

    [Fact]
    public void ExplicitDiscriminatorIsUsed()
    {
        var value = new PolyZoo
        {
            Star = new PolyDog { Name = "Rex", GoodBoy = true },
        };

        var data = NbtSerializer.Serialize(value, PolyContext.Default.PolyZoo, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var star = (NbtCompound)root["Star"].Value!;
        var copy = NbtSerializer.Deserialize(data, PolyContext.Default.PolyZoo);

        Assert.Equal("doggo", (string)star["$type"].Value!);
        Assert.IsType<PolyDog>(copy.Star);
    }

    [Fact]
    public void PolymorphicRootRoundTrips()
    {
        var value = new PolyCat { Name = "Milo", Lives = 9 };

        var data = NbtSerializer.Serialize(
            value,
            PolyContext.Default.IPolyAnimal,
            "root");
        var copy = NbtSerializer.Deserialize(data, PolyContext.Default.IPolyAnimal);

        var cat = Assert.IsType<PolyCat>(copy);
        Assert.Equal("Milo", cat.Name);
        Assert.Equal(9, cat.Lives);
    }

    [Theory]
    [InlineData(null, true, "missing the '$type' discriminator")]
    [InlineData("PolyCat", false, "missing the '$value' member")]
    [InlineData("Bird", true, "The polymorphic type 'Bird' is not registered")]
    public void InvalidEnvelopesThrowNbtException(
        string? discriminator,
        bool includeValue,
        string expectedMessage)
    {
        var wrapper = new NbtCompound();
        if (discriminator is not null)
        {
            wrapper["$type"] = discriminator;
        }

        if (includeValue)
        {
            wrapper["$value"] = new NbtCompound();
        }

        var data = NbtSerializer.Serialize(new NbtRoot("root", wrapper));
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generated = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize(data, PolyContext.Default.IPolyAnimal));
        var reflected = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<IPolyAnimal>(data, options));

        Assert.Contains(expectedMessage, generated.Message, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, reflected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnregisteredRuntimeTypeThrowsOnWrite()
    {
        var value = new PolyZoo { Star = new Bird() };

        var generated = Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(value, PolyContext.Default.PolyZoo, "root"));
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var reflected = Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(value, "root", reflection));

        Assert.Contains("is not registered", generated.Message, StringComparison.Ordinal);
        Assert.Contains("is not registered", reflected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReflectionRejectsInvalidRegistrations()
    {
        var resolver = new NbtReflectionTypeInfoResolver();
        var options = new NbtSerializerOptions();

        var duplicate = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(IBadDuplicates), options));
        var notAssignable = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(IBadAssignable), options));
        var concreteBase = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(ConcreteAnimal), options));
        var collectionDerived = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(ICollectionBase), options));
        var genericBase = Assert.Throws<NotSupportedException>(() =>
            resolver.GetTypeInfo(typeof(IGenericBase<int>), options));

        Assert.Contains("registered more than once", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("not assignable", notAssignable.Message, StringComparison.Ordinal);
        Assert.Contains("must be abstract", concreteBase.Message, StringComparison.Ordinal);
        Assert.Contains("collection shape", collectionDerived.Message, StringComparison.Ordinal);
        Assert.Contains("Generic polymorphic", genericBase.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseConverterOverridesGeneratedDispatch()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new PolyAnimalIntConverter());
        var context = new PolyContext(options);
        var value = CreateZoo();

        var data = NbtSerializer.Serialize(value, context.PolyZoo, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = NbtSerializer.Deserialize(data, context.PolyZoo);

        Assert.Equal(NbtTagType.Int, root["Star"].TagType);
        var cat = Assert.IsType<PolyCat>(copy.Star);
        Assert.Equal(9, cat.Lives);
    }

    [Fact]
    public void DuplicateEnvelopeMembersHonorDuplicateHandling()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteString("$type", "PolyCat");
        writer.WriteStartCompound("$value");
        writer.WriteString("Name", "first");
        writer.WriteInt32("Lives", 1);
        writer.WriteEndCompound();
        writer.WriteStartCompound("$value");
        writer.WriteString("Name", "second");
        writer.WriteInt32("Lives", 2);
        writer.WriteEndCompound();
        writer.WriteEndCompound();
        var data = buffer.WrittenSpan.ToArray();
        var generatedOptions = new NbtSerializerOptions { RejectDuplicateCompoundNames = true };
        var reflectionOptions = new NbtSerializerOptions
        {
            RejectDuplicateCompoundNames = true,
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var context = new PolyContext(generatedOptions);

        var generated = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize(data, context.IPolyAnimal));
        var reflected = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<IPolyAnimal>(data, reflectionOptions));

        Assert.Contains("duplicate member '$value'", generated.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate member '$value'", reflected.Message, StringComparison.Ordinal);
    }

    private static PolyZoo CreateZoo() => new()
    {
        Star = new PolyCat { Name = "Milo", Lives = 9 },
        Animals =
        [
            new PolyCat { Name = "Milo", Lives = 9 },
            new PolyDog { Name = "Rex", GoodBoy = true },
        ],
        Shape = new PolySquare { Label = "square", Side = 4 },
    };

    private static void AssertZoo(PolyZoo zoo)
    {
        var cat = Assert.IsType<PolyCat>(zoo.Star);
        Assert.Equal("Milo", cat.Name);
        Assert.Equal(9, cat.Lives);
        Assert.Collection(
            zoo.Animals,
            animal =>
            {
                var first = Assert.IsType<PolyCat>(animal);
                Assert.Equal("Milo", first.Name);
            },
            animal =>
            {
                var second = Assert.IsType<PolyDog>(animal);
                Assert.Equal("Rex", second.Name);
                Assert.True(second.GoodBoy);
            });
        var square = Assert.IsType<PolySquare>(zoo.Shape);
        Assert.Equal("square", square.Label);
        Assert.Equal(4, square.Side);
    }

    private sealed class Bird : IPolyAnimal
    {
        public string Name { get; set; } = "";
    }

    private sealed class PolyAnimalIntConverter : NbtConverter<IPolyAnimal>
    {
        public override IPolyAnimal Read(
            ref NbtReader reader,
            NbtSerializerOptions options) =>
            new PolyCat { Lives = reader.GetInt32() };

        public override void Write(
            ref NbtWriter writer,
            IPolyAnimal value,
            NbtSerializerOptions options) =>
            writer.WriteInt32(value is PolyCat cat ? cat.Lives : 0);

        public override bool TryGetTagType(
            IPolyAnimal value,
            NbtSerializerOptions options,
            out NbtTagType tagType)
        {
            tagType = NbtTagType.Int;
            return true;
        }
    }
}

[NbtDerived(typeof(PolyCat))]
[NbtDerived(typeof(PolyDog), TypeDiscriminator = "doggo")]
[NbtDerived(typeof(PolyRock))]
internal interface IPolyAnimal
{
    string Name { get; set; }
}

internal sealed class PolyCat : IPolyAnimal
{
    public string Name { get; set; } = "";

    public int Lives { get; set; }
}

internal sealed class PolyDog : IPolyAnimal
{
    public string Name { get; set; } = "";

    public bool GoodBoy { get; set; }
}

internal struct PolyRock : IPolyAnimal
{
    public string Name { get; set; }
}

[NbtDerived(typeof(PolyCircle))]
[NbtDerived(typeof(PolySquare))]
internal abstract class PolyShape
{
    public string Label { get; set; } = "";
}

internal sealed class PolyCircle : PolyShape
{
    public double Radius { get; set; }
}

internal sealed class PolySquare : PolyShape
{
    public double Side { get; set; }
}

internal sealed class PolyZoo
{
    public IPolyAnimal? Star { get; set; }

    public List<IPolyAnimal> Animals { get; set; } = [];

    public PolyShape? Shape { get; set; }
}

[NbtDerived(typeof(BadA), TypeDiscriminator = "same")]
[NbtDerived(typeof(BadB), TypeDiscriminator = "same")]
internal interface IBadDuplicates
{
}

internal sealed class BadA : IBadDuplicates
{
}

internal sealed class BadB : IBadDuplicates
{
}

[NbtDerived(typeof(string))]
internal interface IBadAssignable
{
}

[NbtDerived(typeof(ConcreteCat))]
internal class ConcreteAnimal
{
}

internal sealed class ConcreteCat : ConcreteAnimal
{
}

[NbtDerived(typeof(TagList))]
internal interface ICollectionBase
{
}

internal sealed class TagList : List<string>, ICollectionBase
{
}

[NbtDerived(typeof(GenericCat))]
internal interface IGenericBase<T>
{
}

internal sealed class GenericCat : IGenericBase<int>
{
}

[NbtSerializable(typeof(PolyZoo))]
[NbtSerializable(typeof(IPolyAnimal))]
[NbtSerializable(typeof(PolyShape))]
internal partial class PolyContext : NbtSerializerContext
{
    public PolyContext()
    {
    }

    public PolyContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}
