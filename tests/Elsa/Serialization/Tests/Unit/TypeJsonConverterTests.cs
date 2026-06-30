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
        registry.RegisterType(typeof(Uri), "Acme.Customer");

        _options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        _options.Converters.Add(new TypeJsonConverter(registry));
    }

    [Fact]
    public void Write_RegisteredNonPrimitive_EmitsAliasOnly_NoAssemblyOrVersion()
    {
        // FR-004 / FR-004a: a registered non-primitive type serializes to its alias only — no assembly name,
        // no version, no assembly-qualified name in the JSON.
        var json = JsonSerializer.Serialize(typeof(Uri), _options);

        Assert.Equal("\"Acme.Customer\"", json);
        Assert.DoesNotContain(", Version=", json, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Private.CoreLib", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_RegisteredNonPrimitive_PreservesType()
    {
        var json = JsonSerializer.Serialize(typeof(Uri), _options);
        var roundTripped = JsonSerializer.Deserialize<Type>(json, _options);

        Assert.Equal(typeof(Uri), roundTripped);
    }

    [Fact]
    public void Read_UnknownAlias_ResolvesToObject_NoThrow()
    {
        // FR-004a / FR-018: an unresolved alias resolves to object (logged) rather than throwing.
        var type = JsonSerializer.Deserialize<Type>("\"Acme.Unregistered\"", _options);

        Assert.Equal(typeof(object), type);
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
