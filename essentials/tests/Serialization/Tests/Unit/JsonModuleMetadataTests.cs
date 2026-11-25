using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit
{
    public sealed class JsonModuleMetadataTests
    {
        [Fact]
        public void Generated_metadata_does_not_claim_foreign_shared_or_BCL_types()
        {
            const string owner = "Elsa.Serialization.Tests.OwnerA";
            var context = Context(); var host = JsonBaseline.CreateCopy();
            Assert.NotNull(JsonModuleMetadata.Resolve(context, typeof(OwnerA.Dto), host, owner));
            Assert.NotNull(JsonModuleMetadata.Resolve(context, typeof(List<OwnerA.Dto>), host, owner));
            Assert.Null(JsonModuleMetadata.Resolve(context, typeof(OwnerB.Dto), host, owner));
            Assert.Null(JsonModuleMetadata.Resolve(context, typeof(string), host, owner));
            Assert.False(JsonModuleMetadata.Owns(typeof(Dictionary<OwnerA.Dto, OwnerB.Dto>), owner));
        }

        [Fact]
        public void Owner_converters_are_scoped_to_owned_metadata_without_mutating_host_options()
        {
            var host = JsonBaseline.CreateCopy(); var context = Context();
            var initial = host.Converters.ToArray();
            var info = JsonModuleMetadata.Resolve(context, typeof(OwnerA.Dto), host, "Elsa.Serialization.Tests.OwnerA")!;
            Assert.Same(host, info.Options);
            var element = JsonSerializer.SerializeToElement(new OwnerA.Dto(OwnerA.Status.InProgress), info);
            Assert.Equal("inProgress", element.GetProperty("status").GetString());
            Assert.Equal(initial, host.Converters);
            Assert.False(host.PropertyNameCaseInsensitive); Assert.Null(host.DictionaryKeyPolicy);
        }

        [Fact]
        public void Two_generated_resolvers_have_disjoint_ownership_even_when_both_contexts_describe_both_types()
        {
            var context = Context(); var host = JsonBaseline.CreateCopy();
            foreach (var type in new[] { typeof(OwnerA.Dto), typeof(OwnerB.Dto), typeof(List<OwnerA.Dto>), typeof(string) })
            {
                var claims = new[] { "Elsa.Serialization.Tests.OwnerA", "Elsa.Serialization.Tests.OwnerB" }
                    .Count(owner => JsonModuleMetadata.Resolve(context, type, host, owner) is not null);
                Assert.InRange(claims, 0, 1);
            }
            // Negative controls: a foreign resolver and a duplicate owner are detected independently.
            Assert.NotNull(((IJsonTypeInfoResolver)context).GetTypeInfo(typeof(OwnerB.Dto), host));
            Assert.False(JsonModuleMetadata.Owns(typeof(OwnerB.Dto), "Elsa.Serialization.Tests.OwnerA"));
            Assert.Equal(2, new[] { "Elsa.Serialization.Tests.OwnerA", "Elsa.Serialization.Tests.OwnerA" }
                .Count(owner => JsonModuleMetadata.Resolve(context, typeof(OwnerA.Dto), host, owner) is not null));
        }

        [Theory]
        [InlineData("{\"at\":\"2026-10-28T09:15:00\"}", "$.at")]
        [InlineData("{\"instant\":\"2026-10-28T09:15:00\"}", "$.instant")]
        [InlineData("{\"offsets\":[\"2026-10-28T09:15:00\"]}", "$.offsets[0]")]
        [InlineData("{\"list\":[\"2026-10-28T09:15:00\"]}", "$.list[0]")]
        [InlineData("{\"byKey\":{\"start\":\"2026-10-28T09:15:00\"}}", "$.byKey.start")]
        [InlineData("{\"nested\":[{\"start\":[null,\"2026-10-28T09:15:00\"]}]}", "$.nested[0].start[1]")]
        public void Host_metadata_refuses_offsetless_nullable_and_collection_dates(string json, string path)
        {
            var host = Host(Context());
            var error = Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<OwnerA.TemporalDto>(json, host));
            Assert.Contains("json-datetime-offset-missing", error.Message);
            Assert.Contains(path, error.Message);
        }

        [Fact]
        public void Nested_owned_metadata_keeps_the_outer_field_path_of_a_collection_refusal()
        {
            var host = Host(Context());
            var error = Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<OwnerA.TemporalHolder>(
                "{\"value\":{\"nested\":[{\"start\":[\"2026-10-28T09:15:00\"]}]}}", host));
            Assert.Contains("json-datetime-offset-missing", error.Message);
            Assert.Contains("$.value.nested[0].start[0]", error.Message);
        }

        [Fact]
        public void Host_metadata_uses_owner_temporal_formats_keeps_nulls_and_never_changes_host_options()
        {
            var context = Context(); var host = Host(context);
            var converters = host.Converters.ToArray();
            var resolver = host.TypeInfoResolver;
            host.MakeReadOnly();
            var value = JsonSerializer.Deserialize<OwnerA.TemporalDto>("""
                {"at":"2026-10-28T09:15:00+02:00","instant":"2026-10-28T09:15:00+02:00",
                 "offsets":["2026-10-28T09:15:00+02:00"],"list":["2026-10-28T09:15:00+02:00"],
                 "byKey":{"start":"2026-10-28T09:15:00+02:00"},"nested":[{"start":[null,"2026-10-28T09:15:00+02:00"]}],
                 "day":"2026-10-28T00:15:00+02:00","times":["2026-10-28T09:15:00+02:00"],"durations":["PT1H30M"]}
                """, host)!;
            var json = JsonSerializer.SerializeToElement(value, host);
            foreach (var offset in new[] { json.GetProperty("at"), json.GetProperty("offsets")[0], json.GetProperty("list")[0],
                         json.GetProperty("byKey").GetProperty("start"), json.GetProperty("nested")[0].GetProperty("start")[1] })
                Assert.Equal("2026-10-28T07:15:00+00:00", offset.GetString());
            Assert.Equal("2026-10-28T07:15:00Z", json.GetProperty("instant").GetString());
            Assert.Equal("2026-10-27", json.GetProperty("day").GetString());
            Assert.Equal("09:15:00", json.GetProperty("times")[0].GetString());
            Assert.Equal("PT1H30M", json.GetProperty("durations")[0].GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("nested")[0].GetProperty("start")[0].ValueKind);
            var empty = JsonSerializer.Deserialize<OwnerA.TemporalDto>("{\"at\":null,\"offsets\":null}", host)!;
            var emptyJson = JsonSerializer.SerializeToElement(empty, host);
            Assert.Equal(JsonValueKind.Null, emptyJson.GetProperty("at").ValueKind);
            Assert.Equal(JsonValueKind.Null, emptyJson.GetProperty("offsets").ValueKind);
            var unspecified = JsonSerializer.SerializeToElement(new OwnerA.TemporalDto(Instant: new DateTime(2026, 10, 28, 9, 15, 0)), host);
            Assert.Equal("2026-10-28T09:15:00Z", unspecified.GetProperty("instant").GetString());
            Assert.Same(resolver, host.TypeInfoResolver);
            Assert.Equal(converters, host.Converters);
            Assert.True(host.IsReadOnly);
            Assert.False(host.PropertyNameCaseInsensitive);
            Assert.Null(host.DictionaryKeyPolicy);
        }

        [Fact]
        public void Nullable_and_collection_enums_keep_the_owners_converter()
        {
            var host = Host(Context());
            var value = JsonSerializer.Deserialize<OwnerA.EnumDto>("{\"status\":\"inProgress\",\"statuses\":[\"inProgress\"]}", host)!;
            Assert.Equal(OwnerA.Status.InProgress, value.Status);
            Assert.Equal(OwnerA.Status.InProgress, Assert.Single(value.Statuses!));
            var json = JsonSerializer.SerializeToElement(value, host);
            Assert.Equal("inProgress", json.GetProperty("status").GetString());
            Assert.Equal("inProgress", json.GetProperty("statuses")[0].GetString());
        }

        [Fact]
        public void Owner_leaf_and_explicit_property_converters_take_precedence_inside_temporal_containers()
        {
            var context = Context(options => options.Converters.Insert(0, new OwnerLeafDateConverter()));
            var host = Host(context);
            var value = JsonSerializer.Deserialize<OwnerA.ConverterDto>(
                "{\"at\":\"property\",\"list\":[\"owned-leaf\"]}", host)!;
            var json = JsonSerializer.SerializeToElement(value, host);
            Assert.Equal("property", json.GetProperty("at").GetString());
            Assert.Equal("owned-leaf", json.GetProperty("list")[0].GetString());
        }

        [Fact]
        public void Temporal_container_propagation_does_not_take_over_a_foreign_resolver()
        {
            var host = Host(Context(), new ForeignResolver());
            var value = JsonSerializer.Deserialize<OwnerA.ForeignHolder>("{\"items\":[\"foreign:Ann\"]}", host)!;
            Assert.Equal("Ann", Assert.Single(value.Items).Name);
            var json = JsonSerializer.SerializeToElement(value, host);
            Assert.Equal("foreign:Ann", json.GetProperty("items")[0].GetString());
        }

        private static JsonSerializerOptions Host(OwnedContext context, params IJsonTypeInfoResolver[] others)
        {
            var host = JsonBaseline.CreateCopy();
            host.TypeInfoResolver = JsonTypeInfoResolver.Combine(new OwnerResolver(context),
                JsonTypeInfoResolver.Combine(others), new DefaultJsonTypeInfoResolver());
            return host;
        }

        private sealed class OwnerResolver(OwnedContext context) : IJsonTypeInfoResolver
        {
            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
                JsonModuleMetadata.Resolve(context, type, options, "Elsa.Serialization.Tests.OwnerA");
        }

        private sealed class ForeignResolver : IJsonTypeInfoResolver
        {
            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => type == typeof(OwnerB.Dto)
                ? JsonMetadataServices.CreateValueInfo<OwnerB.Dto>(options, new ForeignConverter()) : null;
        }

        private sealed class ForeignConverter : JsonConverter<OwnerB.Dto>
        {
            public override OwnerB.Dto Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => new(reader.GetString()![8..]);
            public override void Write(Utf8JsonWriter writer, OwnerB.Dto value, JsonSerializerOptions options) => writer.WriteStringValue("foreign:" + value.Name);
        }

        private sealed class OwnerLeafDateConverter : JsonConverter<DateTimeOffset>
        {
            public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
                reader.GetString() == "owned-leaf" ? DateTimeOffset.UnixEpoch : throw new JsonException("Expected the owner's date shape.");
            public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue("owned-leaf");
        }

        private static OwnedContext Context(Action<JsonSerializerOptions>? configure = null)
        {
            var options = JsonBaseline.CreateCopy(); options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            StrictDateTimeConverters.Configure(options, apiMode: true);
            configure?.Invoke(options);
            return new OwnedContext(options);
        }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(OwnerA.Dto), TypeInfoPropertyName = "OwnerADto")]
    [JsonSerializable(typeof(OwnerB.Dto), TypeInfoPropertyName = "OwnerBDto")]
    [JsonSerializable(typeof(List<OwnerA.Dto>), TypeInfoPropertyName = "OwnerADtoList")]
    [JsonSerializable(typeof(List<OwnerB.Dto>), TypeInfoPropertyName = "OwnerBDtoList")]
    [JsonSerializable(typeof(OwnerA.TemporalDto))]
    [JsonSerializable(typeof(OwnerA.TemporalHolder))]
    [JsonSerializable(typeof(OwnerA.EnumDto))]
    [JsonSerializable(typeof(OwnerA.ConverterDto))]
    [JsonSerializable(typeof(OwnerA.ForeignHolder))]
    [JsonSerializable(typeof(string))]
    internal partial class OwnedContext : JsonSerializerContext;
}
namespace Elsa.Serialization.Tests.OwnerA
{
    public enum Status { InProgress }
    public sealed record Dto(Status Status);
    public sealed record TemporalDto(DateTimeOffset? At = null, DateTime? Instant = null, DateTimeOffset[]? Offsets = null,
        List<DateTimeOffset>? List = null, Dictionary<string, DateTimeOffset>? ByKey = null,
        List<Dictionary<string, DateTimeOffset?[]>>? Nested = null, DateOnly? Day = null,
        List<TimeOnly>? Times = null, List<TimeSpan>? Durations = null);
    public sealed record TemporalHolder(TemporalDto Value);
    public sealed record EnumDto(Status? Status, List<Status>? Statuses);
    public sealed record ConverterDto([property: JsonConverter(typeof(PropertyDateConverter))] DateTimeOffset? At, List<DateTimeOffset> List);
    public sealed record ForeignHolder(List<OwnerB.Dto> Items);
    public sealed class PropertyDateConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.GetString() == "property" ? DateTimeOffset.UnixEpoch : throw new JsonException("Expected the member's date shape.");
        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options) => writer.WriteStringValue("property");
    }
}
namespace Elsa.Serialization.Tests.OwnerB
{
    public sealed record Dto(string Name);
}
