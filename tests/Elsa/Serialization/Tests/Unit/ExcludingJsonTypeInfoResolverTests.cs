using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class ExcludingJsonTypeInfoResolverTests
{
    [Fact]
    public void Excluded_json_name_is_omitted_and_non_excluded_is_present()
    {
        var options = OptionsFor(new ExcludingJsonTypeInfoResolver(["Secret"]));
        var json = JsonSerializer.Serialize(new NamedSample { Secret = "hidden", Kept = "visible" }, options);

        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"kept\":\"visible\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Excluded_clr_property_info_name_is_omitted_when_json_name_differs()
    {
        var options = OptionsFor(new ExcludingJsonTypeInfoResolver(["ClrName"]));
        var json = JsonSerializer.Serialize(new RenamedSample { ClrName = "hidden", Kept = "visible" }, options);

        Assert.DoesNotContain("wireName", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ClrName", json, StringComparison.Ordinal);
        Assert.Contains("\"kept\":\"visible\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_object_json_type_info_kind_is_unchanged()
    {
        var resolver = new ExcludingJsonTypeInfoResolver(["Length"]);
        var options = new JsonSerializerOptions { TypeInfoResolver = resolver };

        var typeInfo = resolver.GetTypeInfo(typeof(string), options);

        Assert.NotNull(typeInfo);
        Assert.NotEqual(JsonTypeInfoKind.Object, typeInfo!.Kind);
    }

    [Fact]
    public void Null_inner_uses_default_json_type_info_resolver()
    {
        var resolver = new ExcludingJsonTypeInfoResolver(["Secret"]);
        var options = OptionsFor(resolver);
        var json = JsonSerializer.Serialize(new NamedSample { Secret = "hidden", Kept = "visible" }, options);

        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"kept\":\"visible\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_inner_is_used()
    {
        var inner = new TrackingResolver();
        var resolver = new ExcludingJsonTypeInfoResolver(["Secret"], inner);
        var options = OptionsFor(resolver);

        _ = JsonSerializer.Serialize(new NamedSample { Secret = "hidden", Kept = "visible" }, options);

        Assert.True(inner.Calls > 0);
    }

    [Fact]
    public void Excluded_properties_are_not_removed_from_type_info()
    {
        var inner = new DefaultJsonTypeInfoResolver();
        var excluding = new ExcludingJsonTypeInfoResolver(["Secret"], inner);
        var baseline = inner.GetTypeInfo(typeof(NamedSample), new JsonSerializerOptions())!;
        var excluded = excluding.GetTypeInfo(typeof(NamedSample), new JsonSerializerOptions { TypeInfoResolver = excluding })!;
        var secret = Assert.Single(excluded.Properties, property =>
            string.Equals(property.Name, "Secret", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(baseline.Properties.Count, excluded.Properties.Count);
        Assert.False(secret.ShouldSerialize!(new NamedSample(), secret));
    }

    private static JsonSerializerOptions OptionsFor(IJsonTypeInfoResolver resolver) => new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = resolver
    };

    private sealed class NamedSample
    {
        public string Secret { get; set; } = "";
        public string Kept { get; set; } = "";
    }

    private sealed class RenamedSample
    {
        [JsonPropertyName("wireName")]
        public string ClrName { get; set; } = "";

        public string Kept { get; set; } = "";
    }

    private sealed class TrackingResolver : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver inner = new DefaultJsonTypeInfoResolver();

        public int Calls { get; private set; }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            Calls++;
            return inner.GetTypeInfo(type, options);
        }
    }
}
