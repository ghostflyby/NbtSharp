namespace NbtSharp.Tests;

public sealed class NbtMalformedInputFuzzTests
{
    [Fact]
    public void RandomPayloadsOnlyFailWithNbtException()
    {
        var random = new Random(0x5EED);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var payload = new byte[random.Next(0, 96)];
            random.NextBytes(payload);

            try
            {
                _ = NbtSerializer.Deserialize(payload);
            }
            catch (NbtException)
            {
            }
        }
    }

    [Fact]
    public void MutatedDocumentsOnlyFailWithNbtException()
    {
        var random = new Random(0xBEEF);
        var data = NbtSerializer.Serialize(
            new NbtRoot(
                "root",
                new NbtCompound
                {
                    ["number"] = 42,
                    ["text"] = "hello",
                    ["list"] = new NbtList(new List<int> { 1, 2, 3 }),
                }));

        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var mutated = (byte[])data.Clone();
            var index = random.Next(mutated.Length);
            mutated[index] ^= (byte)(1 << random.Next(8));

            try
            {
                _ = NbtSerializer.Deserialize(mutated);
            }
            catch (NbtException)
            {
            }
        }
    }

    [Fact]
    public void EveryProperPrefixFailsWithNbtException()
    {
        var data = NbtSerializer.Serialize(
            new NbtRoot(
                "root",
                new NbtCompound
                {
                    ["number"] = 42,
                    ["text"] = "hello",
                }));

        for (var length = 0; length < data.Length; length++)
        {
            Assert.Throws<NbtException>(() => NbtSerializer.Deserialize(data.AsSpan(0, length)));
        }
    }
}
