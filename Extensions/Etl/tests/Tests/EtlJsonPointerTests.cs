using System.Text.Json;
using Elsa.Etl.Runtime.Http;

namespace Elsa.Etl.Tests;

public sealed class EtlJsonPointerTests
{
    [Fact]
    public void Resolves_rfc6901_escaped_slash_and_tilde_segments()
    {
        using var document = JsonDocument.Parse("{\"a/b\":{\"~key\":\"resolved\"}}");
        var pointer = new EtlJsonPointer("/a~1b/~0key");

        Assert.True(pointer.TryResolve(document.RootElement, out var value));
        Assert.Equal("resolved", value.GetString());
    }

    [Fact]
    public void Empty_pointer_resolves_the_document_root()
    {
        using var document = JsonDocument.Parse("{\"items\":[1,2]}");

        Assert.True(new EtlJsonPointer(string.Empty).TryResolve(document.RootElement, out var value));
        Assert.Equal(document.RootElement.GetRawText(), value.GetRawText());
    }

    [Fact]
    public void Resolves_canonical_array_indices_and_rejects_leading_zeroes()
    {
        using var document = JsonDocument.Parse("{\"items\":[\"zero\",\"one\"]}");

        Assert.True(new EtlJsonPointer("/items/0").TryResolve(document.RootElement, out var first));
        Assert.Equal("zero", first.GetString());
        Assert.True(new EtlJsonPointer("/items/1").TryResolve(document.RootElement, out var second));
        Assert.Equal("one", second.GetString());
        Assert.False(new EtlJsonPointer("/items/01").TryResolve(document.RootElement, out _));
        Assert.False(new EtlJsonPointer("/items/00").TryResolve(document.RootElement, out _));
    }

    [Fact]
    public void Distinguishes_missing_property_from_present_null_value()
    {
        using var document = JsonDocument.Parse("{\"present\":null}");

        Assert.True(new EtlJsonPointer("/present").TryResolve(document.RootElement, out var present));
        Assert.Equal(JsonValueKind.Null, present.ValueKind);
        Assert.False(new EtlJsonPointer("/missing").TryResolve(document.RootElement, out _));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("/bad~")]
    [InlineData("/bad~2")]
    [InlineData("/bad~~0")]
    public void Rejects_non_pointer_paths_and_invalid_escape_sequences(string pointer)
    {
        Assert.Throws<ArgumentException>(() => new EtlJsonPointer(pointer));
    }
}
