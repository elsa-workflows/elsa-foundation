using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonModuleReflectionTests
{
    private static readonly BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void Constructed_owner_wire_contexts_keep_the_baseline_and_explicit_offset_contract()
    {
        var contexts = Contexts();
        Assert.True(contexts.Count >= 20, $"Only {contexts.Count} owner wire contexts were inspected; module discovery is incomplete.");
        foreach (var context in contexts)
        {
            var options = context.Options;
            Assert.True(options.IsReadOnly, context.GetType().FullName);
            Assert.Same(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
            Assert.Null(options.DictionaryKeyPolicy);
            Assert.False(options.PropertyNameCaseInsensitive);
            Assert.Equal(JsonNumberHandling.Strict, options.NumberHandling);
            Assert.Equal(JsonIgnoreCondition.Never, options.DefaultIgnoreCondition);
            Assert.Same(JavaScriptEncoder.UnsafeRelaxedJsonEscaping, options.Encoder);
            var rawConverter = Assert.IsAssignableFrom<JsonConverter<JsonElement>>(options.Converters.First(converter => converter.CanConvert(typeof(JsonElement))));
            const string raw = """{ "OrderTotal":1.00, "text":"Ren\u00e9 <>&", "explicit":null, "large":9007199254740993 }""";
            using var data = JsonDocument.Parse(raw);
            Assert.Equal(raw, Write(rawConverter, data.RootElement, options).GetRawText());

            var date = Assert.IsAssignableFrom<JsonConverter<DateTime>>(options.Converters.First(converter => converter.CanConvert(typeof(DateTime))));
            var offset = Assert.IsAssignableFrom<JsonConverter<DateTimeOffset>>(options.Converters.First(converter => converter.CanConvert(typeof(DateTimeOffset))));
            Assert.Equal("json-datetime-offset-missing", Assert.Throws<JsonConversionException>(() => ReadOffsetless(date, options)).Code);
            Assert.Equal("json-datetime-offset-missing", Assert.Throws<JsonConversionException>(() => ReadOffsetless(offset, options)).Code);
            Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(Write(offset, new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.FromHours(2)), options).GetString()!).Offset);
            var unspecified = Write(date, new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Unspecified), options);
            Assert.Equal("2026-01-15T09:30:00Z", unspecified.GetString());
        }
    }

    [Fact]
    public void Real_module_resolvers_claim_only_owned_contracts_without_overlap_or_host_mutation()
    {
        var contexts = Contexts();
        var assemblies = RuntimeLibraries.Assemblies;
        var resolverTypes = assemblies.SelectMany(assembly => assembly.GetTypes()).Where(type =>
            !type.IsAbstract && !typeof(JsonSerializerContext).IsAssignableFrom(type) && typeof(IJsonTypeInfoResolver).IsAssignableFrom(type) &&
            type.Namespace?.EndsWith(".Api", StringComparison.Ordinal) == true &&
            type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) is not null).ToArray();
        Assert.True(resolverTypes.Length >= 4, "The four module HTTP resolver contributions must all be inspected.");
        var resolvers = resolverTypes.Select(type => (Resolver: (IJsonTypeInfoResolver)Activator.CreateInstance(type, nonPublic: true)!,
            Owner: type.Assembly.GetName().Name![..^4])).ToArray();
        var candidates = contexts.SelectMany(context => CustomAttributeData.GetCustomAttributes(context.GetType())
                .Where(attribute => attribute.AttributeType == typeof(JsonSerializableAttribute))
                .Select(attribute => attribute.ConstructorArguments[0].Value).OfType<Type>())
            .Concat([typeof(string), typeof(object), typeof(JsonElement), typeof(DateTime), typeof(DateTimeOffset)])
            .Distinct().ToArray();
        Assert.True(candidates.Length >= 100, "The actual generated owner contracts must populate the resolver cross-check.");
        foreach (var type in candidates)
        {
            var host = JsonBaseline.CreateCopy();
            var converters = host.Converters.ToArray();
            var chain = host.TypeInfoResolverChain.ToArray();
            var claims = new List<string>();
            foreach (var (resolver, owner) in resolvers)
            {
                if (resolver.GetTypeInfo(type, host) is not { } info) continue;
                claims.Add(resolver.GetType().FullName!);
                Assert.Same(host, info.Options);
                Assert.True(IsOwned(type, owner), $"{resolver.GetType().FullName} claimed foreign {type.FullName} outside {owner}.");
            }
            Assert.True(claims.Count <= 1, $"Resolver overlap for {type.FullName}: {string.Join(", ", claims)}");
            Assert.Equal(converters, host.Converters);
            Assert.Equal(chain, host.TypeInfoResolverChain);
            Assert.Null(host.DictionaryKeyPolicy);
            Assert.False(host.PropertyNameCaseInsensitive);
            Assert.Equal(JsonIgnoreCondition.Never, host.DefaultIgnoreCondition);
            Assert.Same(JavaScriptEncoder.UnsafeRelaxedJsonEscaping, host.Encoder);
        }
    }

    private static IReadOnlyList<JsonSerializerContext> Contexts()
    {
        var types = RuntimeLibraries.Assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && typeof(JsonSerializerContext).IsAssignableFrom(type)).ToArray();
        var result = new List<JsonSerializerContext>();
        foreach (var type in types)
        {
            var wire = type.GetProperty("WireContext", StaticMembers);
            if (type.Namespace?.Split('.').Contains("Api", StringComparer.Ordinal) == true)
                Assert.NotNull(wire);
            if (wire is not null) result.Add(Assert.IsAssignableFrom<JsonSerializerContext>(wire.GetValue(null)));
        }
        return result;
    }

    private static bool IsOwned(Type type, string owner)
    {
        if (type.IsArray) return IsOwned(type.GetElementType()!, owner);
        if (type.Namespace == owner || type.Namespace?.StartsWith(owner + ".", StringComparison.Ordinal) == true) return true;
        return type.IsConstructedGenericType && type.GetGenericTypeDefinition().Namespace?.StartsWith("System", StringComparison.Ordinal) == true &&
            type.GenericTypeArguments.Any(argument => IsOwned(argument, owner)) &&
            type.GenericTypeArguments.All(argument => argument.Namespace?.StartsWith("System", StringComparison.Ordinal) == true || IsOwned(argument, owner));
    }

    private static T? ReadOffsetless<T>(JsonConverter<T> converter, JsonSerializerOptions options)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"2026-01-15T09:30:00\""));
        Assert.True(reader.Read());
        return converter.Read(ref reader, typeof(T), options);
    }

    private static JsonElement Write<T>(JsonConverter<T> converter, T value, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) converter.Write(writer, value, options);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
