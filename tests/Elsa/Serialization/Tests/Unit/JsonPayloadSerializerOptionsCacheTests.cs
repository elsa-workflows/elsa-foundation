using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// IN-3 (elsa-4-review-remediation, W11): <see cref="JsonPayloadSerializer.GetOptions"/> used to build a
/// brand-new <see cref="JsonSerializerOptions"/> on every call, defeating System.Text.Json's per-options
/// <c>JsonTypeInfo</c> cache on a hot path. It now caches the built options and rebuilds only when the
/// converter registry's revision changes.
/// </summary>
public sealed class JsonPayloadSerializerOptionsCacheTests
{
    [Fact]
    public void GetOptions_CalledRepeatedly_ReturnsSameInstance()
    {
        var registry = new JsonPayloadConverterRegistry();
        var serializer = new JsonPayloadSerializer(registry);

        var first = serializer.GetOptions();
        var second = serializer.GetOptions();
        var third = serializer.GetOptions();

        Assert.Same(first, second);
        Assert.Same(second, third);
    }

    [Fact]
    public void GetOptions_AfterRegistryRevisionChanges_RebuildsOptions()
    {
        var registry = new JsonPayloadConverterRegistry();
        var serializer = new JsonPayloadSerializer(registry);

        var before = serializer.GetOptions();

        // Mutating the registry must invalidate the cached options so the new converter is honoured.
        registry.Register(new DummyConverter());
        var after = serializer.GetOptions();

        Assert.NotSame(before, after);
        Assert.Contains(after.Converters, c => c is DummyConverter);
    }

    [Fact]
    public void Register_And_RegisterAll_BumpRevision()
    {
        var registry = new JsonPayloadConverterRegistry();
        var r0 = registry.Revision;

        registry.Register(new DummyConverter());
        var r1 = registry.Revision;

        registry.RegisterAll(new JsonConverter[] { new DummyConverter(), new DummyConverter() });
        var r2 = registry.Revision;

        Assert.True(r1 > r0);
        Assert.True(r2 > r1);
    }

    [Fact]
    public void GetOptions_StableRegistry_DoesNotRebuildAcrossManyCalls()
    {
        var registry = new JsonPayloadConverterRegistry();
        registry.Register(new DummyConverter());
        var serializer = new JsonPayloadSerializer(registry);

        var reference = serializer.GetOptions();
        for (var i = 0; i < 1000; i++)
            Assert.Same(reference, serializer.GetOptions());
    }

    private sealed class DummyType
    {
        public string Value { get; set; } = "";
    }

    private sealed class DummyConverter : JsonConverter<DummyType>
    {
        public override DummyType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new();

        public override void Write(Utf8JsonWriter writer, DummyType value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }
}
