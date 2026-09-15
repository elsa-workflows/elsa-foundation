using System.Buffers.Binary;
using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfRelationalIdentityTests
{
    [Fact]
    public void Encode_decode_are_lossless_for_unpaired_surrogates()
    {
        var value = "prefix\uD800middle\uDC00suffix";

        var encoded = EfRelationalIdentity.Encode(value);

        Assert.Equal(value, EfRelationalIdentity.Decode(encoded));
    }

    [Fact]
    public void Hash_is_stable_and_distinguishes_exact_code_units()
    {
        Assert.Equal(EfRelationalIdentity.Hash("abc"), EfRelationalIdentity.Hash("abc"));
        Assert.NotEqual(EfRelationalIdentity.Hash("abc"), EfRelationalIdentity.Hash("abd"));
        Assert.NotEqual(EfRelationalIdentity.Hash("\uD800"), EfRelationalIdentity.Hash("\uFFFD"));
    }

    [Fact]
    public void Decode_rejects_malformed_base64_and_odd_byte_lengths()
    {
        Assert.Throws<InvalidOperationException>(() => EfRelationalIdentity.Decode("not-base64"));
        var odd = Convert.ToBase64String([0x01]);
        Assert.Throws<InvalidOperationException>(() => EfRelationalIdentity.Decode(odd));
    }

    [Fact]
    public void Create_order_key_enforces_positive_maximum_and_length_bound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EfRelationalIdentity.CreateOrderKey("x", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EfRelationalIdentity.CreateOrderKey("x", -1));
        Assert.Throws<ArgumentException>(() => EfRelationalIdentity.CreateOrderKey("abcd", 3));

        var key = EfRelationalIdentity.CreateOrderKey("a\u0000", 3);
        Assert.Equal(8, key.Length);
        Assert.Equal((ushort)'a', BinaryPrimitives.ReadUInt16BigEndian(key.AsSpan(0, 2)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(key.AsSpan(2, 2)));
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16BigEndian(key.AsSpan(6, 2)));
    }

    [Fact]
    public void Unbounded_text_key_preserves_exact_utf16_ordinal_order()
    {
        string[] values = ["a", "a\u0000", "a\uD800", "a\uFFFD", "b", new string('x', 451) + "-2", new string('x', 451) + "-1"];
        var expected = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var projected = values.OrderBy(EfRelationalIdentity.CreateOrdinalTextOrderKey, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, projected);
        Assert.NotEqual(EfRelationalIdentity.CreateOrdinalTextOrderKey("a\uD800"),
            EfRelationalIdentity.CreateOrdinalTextOrderKey("a\uFFFD"));
    }
}
