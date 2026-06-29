using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.JsonConverters;
using Elsa.Serialization.SystemText.Services;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// FR-008 / research D8: the compiled-Type converter handles HashSet&lt;&gt; with the same alias-based
/// read/write parity it already gives [] and List&lt;&gt;.
/// </summary>
public sealed class TypeJsonConverterTests
{
    private readonly JsonSerializerOptions _options;

    public TypeJsonConverterTests()
    {
        IWellKnownTypeRegistry registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(int), "Int32");

        _options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        _options.Converters.Add(new TypeJsonConverter(registry));
    }

    [Fact]
    public void Write_HashSet_EmitsHashSetAlias()
    {
        var json = JsonSerializer.Serialize(typeof(HashSet<string>), _options);

        Assert.Equal("\"HashSet<String>\"", json);
    }

    [Fact]
    public void Read_HashSetAlias_ReturnsClosedHashSetType()
    {
        var type = JsonSerializer.Deserialize<Type>("\"HashSet<Int32>\"", _options);

        Assert.Equal(typeof(HashSet<int>), type);
    }

    [Theory]
    [InlineData(typeof(HashSet<string>))]
    [InlineData(typeof(HashSet<int>))]
    [InlineData(typeof(List<string>))]
    [InlineData(typeof(string[]))]
    [InlineData(typeof(string))]
    public void RoundTrip_PreservesType(Type type)
    {
        var json = JsonSerializer.Serialize(type, _options);
        var roundTripped = JsonSerializer.Deserialize<Type>(json, _options);

        Assert.Equal(type, roundTripped);
    }

    [Fact]
    public void Write_HashSet_IsDistinctFromList()
    {
        var hashSetJson = JsonSerializer.Serialize(typeof(HashSet<string>), _options);
        var listJson = JsonSerializer.Serialize(typeof(List<string>), _options);

        Assert.NotEqual(listJson, hashSetJson);
    }
}
