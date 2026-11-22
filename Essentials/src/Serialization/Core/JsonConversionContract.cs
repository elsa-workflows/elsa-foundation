using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.Core;

/// <summary>A cached, scoped contract used symmetrically to read and write a user's .NET type.</summary>
public sealed class JsonConversionContract
{
    private static readonly ConditionalWeakTable<Type, JsonConversionContract> Types = new();
    private static readonly ConditionalWeakTable<MemberInfo, JsonConversionContract> Members = new();

    private JsonConversionContract(JsonConversionAttribute? selection, Type? rootType = null)
    {
        var options = JsonBaseline.CreateCopy();
        options.PropertyNamingPolicy = Naming(selection?.NamingPolicy ?? JsonKnownNamingPolicy.CamelCase);
        options.PropertyNameCaseInsensitive = selection?.CaseInsensitive ?? false;
        RefuseUnmatchedMembers = selection?.RefuseUnmatchedMembers ?? false;
        var mappings = (selection?.MemberMappings ?? []).Select(ParseMapping).ToArray();
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { info =>
            {
                info.NumberHandling = JsonNumberHandling.Strict;
                foreach (var property in info.Properties)
                {
                    property.NumberHandling = JsonNumberHandling.Strict;
                    if (property.AttributeProvider is not MemberInfo member) continue;
                    var mapping = info.Type == rootType ? mappings.FirstOrDefault(x => x.Member == member.Name) : default;
                    if (mapping.Member is not null) property.Name = mapping.Json;
                    var temporalType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    if (temporalType == typeof(DateTime) || temporalType == typeof(DateTimeOffset) || temporalType == typeof(DateOnly) || temporalType == typeof(TimeOnly) || temporalType == typeof(TimeSpan))
                        property.CustomConverter = null;
                    if (member.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null)
                        property.CustomConverter = CreateBoundaryConverter(property.PropertyType, For(property.PropertyType, member));
                }
            } }
        };
        options.Converters.Insert(0, new ScopedConverterFactory(rootType));
        StrictDateTimeConverters.Configure(options);
        options.MakeReadOnly();
        Options = options;
    }

    /// <summary>Gets the default camelCase, exact-match, warn-and-continue contract.</summary>
    public static JsonConversionContract Default { get; } = new(null);
    /// <summary>Gets the read-only options for this contract.</summary>
    public JsonSerializerOptions Options { get; }
    /// <summary>Gets whether unmatched members refuse conversion.</summary>
    public bool RefuseUnmatchedMembers { get; }

    /// <summary>Resolves a member override, otherwise the target type's contract. The cache does not pin unloadable types.</summary>
    public static JsonConversionContract For(Type type, MemberInfo? member = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (member?.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null)
            return Members.GetValue(member, key => new(key.GetCustomAttribute<JsonConversionAttribute>(inherit: true), type));
        return Types.GetValue(type, key => new(key.GetCustomAttribute<JsonConversionAttribute>(inherit: true), key));
    }

    internal static bool HasSelection(Type type, MemberInfo? member = null) => member?.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null ||
        (Nullable.GetUnderlyingType(type) ?? type).GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null;

    /// <summary>Reads a typed copy without modifying stored JSON. Reports every unmatched key through the supplied recording sink.</summary>
    public object? Read(JsonElement value, Type type, Action<JsonConversionDiagnostic>? report = null)
    {
        foreach (var diagnostic in UnmatchedMemberScanner.Scan(value, type, this)) report?.Invoke(diagnostic);
        return value.Deserialize(type, Options);
    }

    /// <summary>Writes with the same effective contract used for reads.</summary>
    public JsonElement Write(object? value, Type type) => JsonSerializer.SerializeToElement(value, type, Options);

    /// <summary>Converts a CLR value using its type's declared contract.</summary>
    public static JsonElement SerializeToElement(object? value, Type? type = null)
    {
        type ??= value?.GetType() ?? typeof(object);
        return For(type).Write(value, type);
    }

    /// <summary>Converts a value with an explicitly selected CLR contract.</summary>
    public static JsonElement SerializeToElement<T>(T value) => For(typeof(T)).Write(value, typeof(T));

    /// <summary>Converts a CLR value to JSON text using its type's declared contract.</summary>
    public static string Serialize(object? value) => SerializeToElement(value).GetRawText();
    /// <summary>Converts a value to text with an explicitly selected CLR contract.</summary>
    public static string Serialize(object? value, Type type) => For(type).Write(value, type).GetRawText();

    /// <summary>Reads a typed copy with the target's contract.</summary>
    public static object? Deserialize(JsonElement value, Type type) => For(type).Read(value, type);
    /// <summary>Reads a typed copy with the target's contract.</summary>
    public static T? Deserialize<T>(JsonElement value) => (T?)For(typeof(T)).Read(value, typeof(T));
    /// <summary>Reads a typed copy of JSON text with the target's contract.</summary>
    public static object? Deserialize(string value, Type type)
    {
        using var document = JsonDocument.Parse(value);
        return Deserialize(document.RootElement, type);
    }
    /// <summary>Reads a typed copy of JSON text with the target's contract.</summary>
    public static T? Deserialize<T>(string value) => (T?)Deserialize(value, typeof(T));

    private static JsonConverter CreateBoundaryConverter(Type type, JsonConversionContract contract) =>
        (JsonConverter)Activator.CreateInstance(typeof(BoundaryConverter<>).MakeGenericType(type), contract)!;

    private sealed class ScopedConverterFactory(Type? rootType) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert != rootType && typeToConvert.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null;
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => CreateBoundaryConverter(typeToConvert, For(typeToConvert));
    }

    private sealed class BoundaryConverter<T>(JsonConversionContract contract) : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonSerializer.Deserialize<T>(ref reader, contract.Options);
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value, contract.Options);
    }

    private static (string Json, string Member) ParseMapping(string mapping)
    {
        var separator = mapping.IndexOf('=');
        if (separator <= 0 || separator == mapping.Length - 1) throw new ArgumentException("A JSON member mapping must be JSON-name=CLR-member-name.");
        return (mapping[..separator], mapping[(separator + 1)..]);
    }

    private static JsonNamingPolicy? Naming(JsonKnownNamingPolicy policy) => policy switch
    {
        JsonKnownNamingPolicy.Unspecified => null,
        JsonKnownNamingPolicy.CamelCase => JsonNamingPolicy.CamelCase,
        JsonKnownNamingPolicy.SnakeCaseLower => JsonNamingPolicy.SnakeCaseLower,
        JsonKnownNamingPolicy.SnakeCaseUpper => JsonNamingPolicy.SnakeCaseUpper,
        JsonKnownNamingPolicy.KebabCaseLower => JsonNamingPolicy.KebabCaseLower,
        JsonKnownNamingPolicy.KebabCaseUpper => JsonNamingPolicy.KebabCaseUpper,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
}
