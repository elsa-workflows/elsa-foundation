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
    private static readonly ConditionalWeakTable<MemberInfo, ConditionalWeakTable<Type, JsonConversionContract>> Members = new();
    private readonly JsonKnownNamingPolicy namingPolicy;
    private readonly bool caseInsensitive;
    private readonly (string Json, string Member)[] mappings;
    private readonly Type? rootType;

    private JsonConversionContract(JsonConversionAttribute? selection, Type? rootType = null)
    {
        namingPolicy = selection?.NamingPolicy ?? JsonKnownNamingPolicy.CamelCase;
        caseInsensitive = selection?.CaseInsensitive ?? false;
        RefuseUnmatchedMembers = selection?.RefuseUnmatchedMembers ?? false;
        mappings = (selection?.MemberMappings ?? []).Select(ParseMapping).ToArray();
        this.rootType = rootType;
    }

    private JsonSerializerOptions CreateOptions(Operation operation)
    {
        var options = JsonBaseline.CreateCopy();
        options.PropertyNamingPolicy = Naming(namingPolicy);
        options.PropertyNameCaseInsensitive = caseInsensitive;
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
                        property.CustomConverter = CreateBoundaryConverter(property.PropertyType, For(property.PropertyType, member), operation);
                }
            } }
        };
        options.Converters.Insert(0, new ScopedConverterFactory(rootType, operation));
        StrictDateTimeConverters.Configure(options);
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Gets the default camelCase, exact-match, warn-and-continue contract.</summary>
    public static JsonConversionContract Default { get; } = new(null);
    /// <summary>Gets read-only options with operation-local metadata for this contract.</summary>
    /// <remarks>A stable root can carry collectible runtime types in object slots. Its cached contract
    /// must not retain the serializer's discovered metadata for those types.</remarks>
    public JsonSerializerOptions Options => new Operation().GetOptions(this);
    /// <summary>Gets whether unmatched members refuse conversion.</summary>
    public bool RefuseUnmatchedMembers { get; }

    /// <summary>Resolves a member override, otherwise the target type's contract. The cache does not pin unloadable types.</summary>
    public static JsonConversionContract For(Type type, MemberInfo? member = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (member?.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null)
            return Members.GetValue(member, _ => new()).GetValue(type, key => new(member.GetCustomAttribute<JsonConversionAttribute>(inherit: true), key));
        return Types.GetValue(type, key => new(key.GetCustomAttribute<JsonConversionAttribute>(inherit: true), key));
    }

    internal static bool HasSelection(Type type, MemberInfo? member = null) => member?.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null ||
        (Nullable.GetUnderlyingType(type) ?? type).GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null;

    /// <summary>Reads a typed copy without modifying stored JSON. Reports every unmatched key through the supplied recording sink.</summary>
    public object? Read(JsonElement value, Type type, Action<JsonConversionDiagnostic>? report = null)
    {
        // These fixed BCL contracts have no member naming, custom converter or temporal policy.
        // Reusing their baseline metadata avoids rebuilding a reflection resolver on every scalar input.
        // User, enum, collection and object contracts remain operation-local, including nested collectible types.
        if (IsBaselineScalar(Nullable.GetUnderlyingType(type) ?? type))
        {
            _ = Naming(namingPolicy); // Invalid selections must still refuse even though scalar naming has no effect.
            if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                foreach (var diagnostic in UnmatchedMemberScanner.Scan(value, type, this)) report?.Invoke(diagnostic);
            return value.Deserialize(type, JsonBaseline.Options);
        }
        var operation = new Operation();
        foreach (var diagnostic in UnmatchedMemberScanner.Scan(value, type, this, operation)) report?.Invoke(diagnostic);
        return value.Deserialize(type, operation.GetOptions(this));
    }

    private static bool IsBaselineScalar(Type type) => type == typeof(string) || type == typeof(bool) || type == typeof(char) ||
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

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

    private static JsonConverter CreateBoundaryConverter(Type type, JsonConversionContract contract, Operation operation) =>
        (JsonConverter)Activator.CreateInstance(typeof(BoundaryConverter<>).MakeGenericType(type), contract, operation)!;

    private sealed class ScopedConverterFactory(Type? rootType, Operation operation) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert != rootType && typeToConvert.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null;
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => CreateBoundaryConverter(typeToConvert, For(typeToConvert), operation);
    }

    private sealed class BoundaryConverter<T>(JsonConversionContract contract, Operation operation) : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonSerializer.Deserialize<T>(ref reader, options: operation.GetOptions(contract));
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value, options: operation.GetOptions(contract));
    }

    // Reuse effective metadata within one conversion, including repeated scoped collection elements.
    // The cache belongs to the operation/options graph, never to a stable type or global serializer.
    internal sealed class Operation
    {
        private readonly object gate = new();
        private readonly Dictionary<JsonConversionContract, JsonSerializerOptions> options = new();
        private readonly Dictionary<(Type Type, JsonConversionContract Parent), JsonConversionContract> children = new();

        internal JsonConversionContract ChildContract(Type type, JsonConversionContract parent)
        {
            lock (gate)
            {
                var key = (type, parent);
                if (children.TryGetValue(key, out var existing)) return existing;
                var selected = HasSelection(type) ? For(type) : parent;
                children.Add(key, selected);
                return selected;
            }
        }

        internal JsonSerializerOptions GetOptions(JsonConversionContract contract)
        {
            lock (gate)
            {
                if (options.TryGetValue(contract, out var existing)) return existing;
                var created = contract.CreateOptions(this);
                options.Add(contract, created);
                return created;
            }
        }
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
