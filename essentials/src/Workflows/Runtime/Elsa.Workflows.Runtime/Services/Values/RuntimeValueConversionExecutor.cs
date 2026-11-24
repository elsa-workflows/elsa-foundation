using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml;
using System.Xml.Linq;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Strict runtime interpreter for publication-pinned value-conversion plans.
/// It deliberately contains no converter discovery: only built-in pinned operations and the pinned
/// <c>elsa.json@1</c>, <c>elsa.json-string@1</c>, and <c>elsa.xml@1</c> profiles are executable in this slice.
/// </summary>
public sealed class RuntimeValueConversionExecutor(IWellKnownTypeRegistry? wellKnownTypeRegistry = null) : IRuntimeValueConversionExecutor
{
    public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan) => Convert(source, plan, report: null);

    public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan, Action<JsonConversionDiagnostic>? report)
        => Convert(source, plan, conversionContract: null, report);

    public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan, JsonConversionContract? conversionContract,
        Action<JsonConversionDiagnostic>? report)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        // Conversion reads the value, and a withheld value is not here to read. Only activation converts it, with the
        // plan its marker carries, so it is refused here rather than retyped away from that plan.
        if (source.Presence == ValuePresence.Withheld)
            throw SecretBindingDiagnostics.WithheldValueNotConverted(plan.TargetType.Alias);

        ValidatePlan(plan);
        if (!ValueConversionCompatibility.SameType(source.Type, plan.SourceType))
            throw Reject(plan, $"runtime source contract '{source.Type.Alias} ({source.Type.CollectionKind})' does not match the pinned source contract");

        if (source.Presence != ValuePresence.Present)
            return new ValueEnvelope(plan.TargetType, source.Presence, null, null, source.Policy);

        ValidateRepresentation(plan);
        if (source.ExternalReference is not null && plan.Operation is not ValueConversionOperation.Identity and not ValueConversionOperation.NullableCompatibility)
            throw Reject(plan, "external-reference conversion is not available in this runtime slice");

        if (source.ExternalReference is not null)
            return ValueEnvelope.External(plan.TargetType, source.ExternalReference, source.Policy);

        var inline = source.InlineValue
            ?? throw Reject(plan, "a present source value did not contain an inline payload");
        ValidatePayloadLimits(plan, inline);

        var converted = plan.Operation switch
        {
            ValueConversionOperation.Identity => inline.Clone(),
            ValueConversionOperation.NullableCompatibility => inline.Clone(),
            ValueConversionOperation.NumericToString => FormatNumericAsString(inline, plan),
            ValueConversionOperation.NumericWidening => ConvertNumeric(inline, plan),
            ValueConversionOperation.RecursiveCollection => ConvertCollection(inline, plan, depth: 1),
            ValueConversionOperation.CanonicalAny => CanonicalizeAny(inline, plan),
            ValueConversionOperation.Profile => ConvertProfile(inline, plan, conversionContract, report),
            _ => throw Reject(plan, $"the pinned operation '{plan.Operation}' is unsupported")
        };

        return ValueEnvelope.Inline(plan.TargetType, converted, source.Policy);
    }

    private static void ValidatePlan(ValueConversionPlan plan)
    {
        if (plan.Operation == ValueConversionOperation.Profile && !IsJsonProfile(plan) && !IsJsonStringProfile(plan) && !IsXmlProfile(plan))
            throw Reject(plan, "the pinned conversion profile is not available in this runtime slice");

        if (IsJsonStringProfile(plan) &&
            (plan.Mode is not (ValueConversionMode.JsonString or ValueConversionMode.Profile) ||
             plan.SourceRepresentation is not (ValueRepresentation.TypedValue or ValueRepresentation.StructuredValue) ||
             !ValueConversionCompatibility.IsPlainString(plan.TargetType)))
            throw Reject(plan, "JSON serialization requires a typed or structured source and a plain scalar String target");
        if (plan.Mode == ValueConversionMode.JsonString && !IsJsonStringProfile(plan))
            throw Reject(plan, "JSON-string mode requires the pinned elsa.json-string@1 profile");

        switch (plan.Operation)
        {
            case ValueConversionOperation.Identity when !ValueConversionCompatibility.SameType(plan.SourceType, plan.TargetType):
                throw Reject(plan, "identity conversion requires matching source and target contracts");
            case ValueConversionOperation.NullableCompatibility when !ValueConversionCompatibility.IsNullableCompatibility(plan.SourceType, plan.TargetType):
                throw Reject(plan, "nullable compatibility requires matching source and target contracts");
            case ValueConversionOperation.NumericToString when !ValueConversionCompatibility.CanFormatNumericAsString(plan.SourceType, plan.SourceRepresentation, plan.TargetType):
                throw Reject(plan, "numeric text formatting requires a typed scalar Double and a plain String target");
            case ValueConversionOperation.NumericWidening when !ValueConversionCompatibility.IsSafeNumericWidening(plan.SourceType, plan.TargetType):
                throw Reject(plan, "numeric conversion is not a safe widening");
            case ValueConversionOperation.RecursiveCollection when !ValueConversionCompatibility.CanRecursivelyWidenCollection(plan.SourceType, plan.TargetType):
                throw Reject(plan, "recursive collection conversion does not have a safe element widening");
            case ValueConversionOperation.CanonicalAny when !ValueConversionCompatibility.IsCanonicalAnyTarget(plan.TargetType):
                throw Reject(plan, "canonical dynamic projection requires the Any target contract");
        }
    }

    private static void ValidateRepresentation(ValueConversionPlan plan)
    {
        if (plan.SourceRepresentation == ValueRepresentation.TransientResource)
            throw Reject(plan, "transient resources cannot cross a durable conversion boundary");

        if (plan.SourceRepresentation == ValueRepresentation.DurableReference &&
            plan.Operation is not ValueConversionOperation.Identity and not ValueConversionOperation.NullableCompatibility)
            throw Reject(plan, "the source representation requires a dedicated pinned profile or external-reference conversion");
        if (plan.SourceRepresentation == ValueRepresentation.FormattedContent &&
            plan.Operation is not ValueConversionOperation.Identity and not ValueConversionOperation.NullableCompatibility and not ValueConversionOperation.Profile)
            throw Reject(plan, "formatted content requires a dedicated pinned profile conversion");
    }

    private static JsonElement FormatNumericAsString(JsonElement value, ValueConversionPlan plan)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw Reject(plan, "numeric text formatting requires a finite Double");

        return JsonSerializer.SerializeToElement(number.ToString("R", CultureInfo.InvariantCulture), Elsa.Serialization.Core.JsonBaseline.Options);
    }

    private static JsonElement ConvertNumeric(JsonElement value, ValueConversionPlan plan)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Reject(plan, "numeric widening requires a JSON number source value");

        return value.Clone();
    }

    private static JsonElement ConvertCollection(JsonElement value, ValueConversionPlan plan, int depth)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");
        if (value.ValueKind != JsonValueKind.Array)
            throw Reject(plan, "recursive collection conversion requires a JSON array source value");

        var values = value.EnumerateArray().ToArray();
        if (values.Length > plan.Limits.MaxCollectionItems)
            throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");

        // The plan has only one element contract. It therefore supports identity and numeric element
        // widening here; nested collection shape is represented by the JSON payload but is not inferred.
        if (ValueConversionCompatibility.CanSafelyWidenElement(plan.SourceType, plan.TargetType) && values.Any(item => item.ValueKind != JsonValueKind.Number))
            throw Reject(plan, "recursive numeric widening requires every collection element to be a JSON number");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JsonBaseline.Options.Encoder }))
        {
            writer.WriteStartArray();
            foreach (var item in values)
                JsonSerializer.Serialize(writer, item, JsonBaseline.Options);
            writer.WriteEndArray();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement CanonicalizeAny(JsonElement value, ValueConversionPlan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JsonBaseline.Options.Encoder }))
            WriteCanonicalJson(writer, value, plan, depth: 1);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private JsonElement ConvertProfile(JsonElement value, ValueConversionPlan plan, JsonConversionContract? conversionContract, Action<JsonConversionDiagnostic>? report)
    {
        if (IsJsonStringProfile(plan))
            return SerializeJsonAsString(value, plan);
        if (IsXmlProfile(plan))
            return ConvertXmlProfile(value, plan, conversionContract, report);
        if (!IsJsonProfile(plan))
            throw Reject(plan, "the pinned conversion profile is not available in this runtime slice");
        if (plan.SourceRepresentation is not (ValueRepresentation.FormattedContent or ValueRepresentation.StructuredValue))
            throw Reject(plan, "JSON conversion requires a formatted-content or structured source representation");

        using var document = ReadJsonDocument(value, plan);
        var root = document.RootElement.Clone();
        var nodeCount = CountJsonNodes(root, plan, depth: 1);
        if (nodeCount > plan.Limits.MaxCollectionItems)
            throw Reject(plan, $"maximum JSON node count '{plan.Limits.MaxCollectionItems}' was exceeded");
        if (ValueConversionCompatibility.IsJsonObjectTarget(plan.TargetType) && root.ValueKind != JsonValueKind.Object)
            throw Reject(plan, "JSON object conversion requires a JSON object root value");
        if (ValueConversionCompatibility.IsCanonicalAnyTarget(plan.TargetType) ||
            ValueConversionCompatibility.IsJsonObjectTarget(plan.TargetType))
            return CanonicalizeAny(root, plan);

        return ConvertTypedJson(root, plan, conversionContract, report);
    }

    private static JsonElement SerializeJsonAsString(JsonElement value, ValueConversionPlan plan)
    {
        var nodeCount = CountJsonNodes(value, plan, depth: 1);
        if (nodeCount > plan.Limits.MaxCollectionItems)
            throw Reject(plan, $"maximum JSON node count '{plan.Limits.MaxCollectionItems}' was exceeded");

        return JsonSerializer.SerializeToElement(value.GetRawText(), Elsa.Serialization.Core.JsonBaseline.Options);
    }

    private JsonElement ConvertXmlProfile(JsonElement value, ValueConversionPlan plan, JsonConversionContract? conversionContract, Action<JsonConversionDiagnostic>? report)
    {
        if (plan.SourceRepresentation != ValueRepresentation.FormattedContent)
            throw Reject(plan, "XML conversion requires a formatted-content source representation");
        if (ValueConversionCompatibility.IsCanonicalAnyTarget(plan.TargetType) ||
            ValueConversionCompatibility.IsJsonObjectTarget(plan.TargetType))
            throw Reject(plan, "XML has no universal canonical Any projection; select a documented XML-to-JSON profile instead");
        if (wellKnownTypeRegistry is null)
            throw Reject(plan, "XML typed conversion requires a well-known type registry at runtime");
        if (!wellKnownTypeRegistry.TryGetTypeOrDefault(plan.TargetType.Alias, out var elementType) || elementType == typeof(object))
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' is not a registered typed alias");

        var document = ReadXmlDocument(value, plan);
        var targetType = TypeReferenceFactory.Close(elementType, plan.TargetType.ToTypeReference());
        var contract = conversionContract ?? JsonConversionContract.For(targetType);
        var context = new XmlConversionContext();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JsonBaseline.Options.Encoder }))
            WriteXmlAsJson(writer, document.Root!, targetType, plan, "$", depth: 1, contract, context);
        using var json = JsonDocument.Parse(stream.ToArray());
        var diagnostics = new List<JsonConversionDiagnostic>();
        try
        {
            var typed = contract.Read(json.RootElement, targetType, diagnostics.Add);
            // XML has no null token. Keep absence from its projection rather than adding CLR defaults
            // while applying the contract's actual output (dates, durations, GUIDs and user converters).
            var result = NormalizeXml(json.RootElement, typed, targetType, contract, context);
            ValidatePayloadLimits(plan, result);
            if (CountJsonNodes(result, plan, depth: 1) > plan.Limits.MaxCollectionItems)
                throw Reject(plan, $"maximum JSON node count '{plan.Limits.MaxCollectionItems}' was exceeded");
            foreach (var diagnostic in diagnostics) report?.Invoke(diagnostic);
            return result;
        }
        catch (JsonException exception)
        {
            throw Reject(plan, $"typed XML conversion failed at '{exception.Path ?? "$"}': {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            throw Reject(plan, $"typed XML conversion is unsupported: {exception.Message}");
        }
    }

    private static XDocument ReadXmlDocument(JsonElement value, ValueConversionPlan plan)
    {
        var xml = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
        if (Encoding.UTF8.GetByteCount(xml) > plan.Limits.MaxPayloadBytes)
            throw Reject(plan, $"maximum payload size '{plan.Limits.MaxPayloadBytes}' bytes was exceeded");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = plan.Limits.MaxPayloadBytes
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Root is null)
                throw Reject(plan, "XML content must contain a root element");
            var nodes = CountXmlNodes(document.Root, plan, depth: 1);
            if (nodes > plan.Limits.MaxCollectionItems)
                throw Reject(plan, $"maximum XML node count '{plan.Limits.MaxCollectionItems}' was exceeded");
            return document;
        }
        catch (XmlException exception)
        {
            throw Reject(plan, $"malformed or unsafe XML content: {exception.Message}");
        }
    }

    private static JsonDocument ReadJsonDocument(JsonElement value, ValueConversionPlan plan)
    {
        if (plan.SourceRepresentation == ValueRepresentation.StructuredValue)
            return JsonDocument.Parse(value.GetRawText(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = plan.Limits.MaxDepth
            });

        var json = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > plan.Limits.MaxPayloadBytes)
            throw Reject(plan, $"maximum payload size '{plan.Limits.MaxPayloadBytes}' bytes was exceeded");

        return ParseJsonContent(json, plan);
    }

    private JsonElement ConvertTypedJson(JsonElement root, ValueConversionPlan plan, JsonConversionContract? conversionContract, Action<JsonConversionDiagnostic>? report)
    {
        if (wellKnownTypeRegistry is null)
            throw Reject(plan, "JSON typed conversion requires a well-known type registry at runtime");
        if (!wellKnownTypeRegistry.TryGetTypeOrDefault(plan.TargetType.Alias, out var elementType) || elementType == typeof(object))
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' is not a registered typed alias");

        var targetType = TypeReferenceFactory.Close(elementType, plan.TargetType.ToTypeReference());
        var diagnostics = new List<JsonConversionDiagnostic>();
        try
        {
            // Validate a typed copy with the same package-owned contract as hydration. Keep the received
            // JSON for storage: naming, converters and unmatched-member detection never rewrite data.
            _ = (conversionContract ?? JsonConversionContract.For(targetType)).Read(root, targetType, diagnostics.Add);
        }
        catch (JsonException exception)
        {
            throw Reject(plan, $"typed JSON conversion failed at '{exception.Path ?? "$"}': {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            throw Reject(plan, $"typed JSON conversion is unsupported: {exception.Message}");
        }
        foreach (var diagnostic in diagnostics) report?.Invoke(diagnostic);
        return root.Clone();
    }

    private static JsonDocument ParseJsonContent(string json, ValueConversionPlan plan)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = plan.Limits.MaxDepth
            });
        }
        catch (JsonException exception)
        {
            throw Reject(plan, $"malformed JSON content: {exception.Message}");
        }
    }

    private static int CountJsonNodes(JsonElement value, ValueConversionPlan plan, int depth)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");

        return value.ValueKind switch
        {
            JsonValueKind.Object => 1 + value.EnumerateObject().Sum(property => CountJsonNodes(property.Value, plan, depth + 1)),
            JsonValueKind.Array => 1 + value.EnumerateArray().Sum(item => CountJsonNodes(item, plan, depth + 1)),
            _ => 1
        };
    }

    private static int CountXmlNodes(XElement element, ValueConversionPlan plan, int depth)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");

        return 1 +
               element.Attributes().Count(attribute => !attribute.IsNamespaceDeclaration) +
               element.Elements().Sum(child => CountXmlNodes(child, plan, depth + 1));
    }

    private static JsonConversionContract ChildContract(Type type, JsonConversionContract parent, MemberInfo? member = null) =>
        member?.GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null ||
        (Nullable.GetUnderlyingType(type) ?? type).GetCustomAttribute<JsonConversionAttribute>(inherit: true) is not null
            ? JsonConversionContract.For(type, member) : parent;

    private static Type? CollectionElement(Type type, bool dictionary = false) => type.IsArray ? type.GetElementType() :
        type.GetInterfaces().Append(type).FirstOrDefault(item => item.IsGenericType &&
            (dictionary ? item.GetGenericTypeDefinition() == typeof(IDictionary<,>) || item.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                : item.GetGenericTypeDefinition() == typeof(IEnumerable<>)))?.GetGenericArguments()[dictionary ? 1 : 0];

    private static void WriteXmlAsJson(Utf8JsonWriter writer, XElement element, Type targetType, ValueConversionPlan plan, string path, int depth,
        JsonConversionContract contract, XmlConversionContext context, JsonConverter? customConverter = null)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");

        var declaredType = targetType;
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (targetType == typeof(object) || targetType == typeof(JsonElement) || typeof(System.Text.Json.Nodes.JsonNode).IsAssignableFrom(targetType))
            throw Reject(plan, $"XML has no universal JSON projection for member at '{path}'; select an explicitly typed member");
        var options = context.Options(contract);
        var info = options.GetTypeInfo(targetType);
        if (customConverter is not null && JsonConversionContract.IsContractAdapter(customConverter)) customConverter = null;
        var converter = customConverter ?? (info.Kind == JsonTypeInfoKind.None ? info.Converter : null);
        if (converter is JsonConverterFactory factory) converter = factory.CreateConverter(declaredType, options);
        if (converter is IJsonConversionMemberMatcher matcher)
        {
            WriteXmlMatchedObject(writer, element, matcher, plan, path, depth, contract, context);
            return;
        }
        if (customConverter is not null || info.Kind == JsonTypeInfoKind.None && !IsXmlScalar(targetType))
        {
            // An opaque converter owns its JSON shape. A text leaf can be offered as a string token;
            // structured XML needs its explicit matching information, never inferred CLR properties.
            if (element.HasElements || element.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
                throw Reject(plan, $"custom JSON converter '{converter?.GetType().FullName}' has no XML member-matching contract at '{path}'");
            writer.WriteStringValue(element.Value);
            return;
        }
        if (targetType == typeof(string) || targetType == typeof(char) || targetType == typeof(Guid) || targetType == typeof(Uri) ||
            targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset) || targetType == typeof(DateOnly) ||
            targetType == typeof(TimeOnly) || targetType == typeof(TimeSpan))
        {
            writer.WriteStringValue(element.Value);
            return;
        }

        if (targetType == typeof(bool))
        {
            if (!bool.TryParse(element.Value.Trim(), out var value))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected boolean at '{path}'");
            writer.WriteBooleanValue(value);
            return;
        }

        if (IsNumericType(targetType))
        {
            WriteXmlNumber(writer, element.Value.Trim(), targetType, plan, path);
            return;
        }

        if (targetType.IsEnum)
        {
            var text = element.Value.Trim();
            if (!Enum.TryParse(targetType, text, ignoreCase: true, out var value))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected enum value at '{path}'");
            JsonSerializer.Serialize(writer, value, targetType, options);
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Dictionary && CollectionElement(targetType, dictionary: true) is { } valueType)
        {
            var shape = targetType.GetInterfaces().Append(targetType).First(item => item.IsGenericType &&
                (item.GetGenericTypeDefinition() == typeof(IDictionary<,>) || item.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
            if (shape.GetGenericArguments()[0] != typeof(string)) throw Reject(plan, $"XML dictionaries require string keys at '{path}'");
            WriteXmlDictionary(writer, element, valueType, plan, path, depth, ChildContract(valueType, contract), context);
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Enumerable && CollectionElement(targetType) is { } elementType)
        {
            WriteXmlArray(writer, element, elementType, plan, path, depth, ChildContract(elementType, contract), context);
            return;
        }
        WriteXmlObject(writer, element, info, plan, path, depth, contract, context);
    }

    private static bool IsXmlScalar(Type type) => type == typeof(string) || type == typeof(bool) || type == typeof(char) || type.IsEnum ||
        IsNumericType(type) || type == typeof(Guid) || type == typeof(Uri) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
        type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);

    private static void WriteXmlObject(Utf8JsonWriter writer, XElement element, JsonTypeInfo info, ValueConversionPlan plan, string path, int depth,
        JsonConversionContract contract, XmlConversionContext context)
    {
        // elsa.xml@1 keeps its source vocabulary: JsonPropertyName or camelCase CLR names,
        // case-insensitive. A scoped JSON contract changes the emitted name, not admitted XML names.
        var members = context.Members(info, contract);
        var attributes = element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
        var children = element.Elements().ToArray();

        foreach (var attribute in attributes)
            if (!members.ContainsKey(attribute.Name.LocalName))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' does not support XML attribute '{attribute.Name.LocalName}' at '{path}'");
        foreach (var child in children)
            if (!members.ContainsKey(child.Name.LocalName))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' does not support XML element '{child.Name.LocalName}' at '{path}'");

        writer.WriteStartObject();
        foreach (var member in members.Values.OrderBy(member => member.Property.Name, StringComparer.Ordinal))
        {
            var matchingAttributes = attributes.Where(attribute => StringComparer.OrdinalIgnoreCase.Equals(attribute.Name.LocalName, member.SourceName)).ToArray();
            if (matchingAttributes.Length > 1) throw Reject(plan, $"target contract '{plan.TargetType.Alias}' has ambiguous XML mappings for member '{member.SourceName}' at '{path}'");
            var attribute = matchingAttributes.SingleOrDefault();
            var matchingChildren = children.Where(child => StringComparer.OrdinalIgnoreCase.Equals(child.Name.LocalName, member.SourceName)).ToArray();
            if (attribute is not null && matchingChildren.Length != 0)
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' has ambiguous XML mappings for member '{member.SourceName}' at '{path}'");
            if (matchingChildren.Length > 1)
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' has ambiguous repeated XML elements for member '{member.SourceName}' at '{path}'");

            if (attribute is null && matchingChildren.Length == 0)
            {
                if (member.Property.IsRequired)
                    throw Reject(plan, $"target contract '{plan.TargetType.Alias}' is missing required XML member '{member.SourceName}' at '{path}'");
                continue;
            }

            writer.WritePropertyName(member.Property.Name);
            if (attribute is not null)
                WriteXmlAttributeAsJson(writer, attribute, member.Property.PropertyType, plan, $"{path}.@{member.Property.Name}", depth, member.Contract, context, member.Property.CustomConverter);
            else
                WriteXmlAsJson(writer, matchingChildren[0], member.Property.PropertyType, plan, $"{path}.{member.Property.Name}", depth + 1, member.Contract, context, member.Property.CustomConverter);
        }

        writer.WriteEndObject();
    }

    private static void WriteXmlArray(Utf8JsonWriter writer, XElement element, Type elementType, ValueConversionPlan plan, string path, int depth,
        JsonConversionContract contract, XmlConversionContext context)
    {
        var items = element.Elements().ToArray();
        if (items.Length > plan.Limits.MaxCollectionItems)
            throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");

        writer.WriteStartArray();
        var index = 0;
        foreach (var item in items)
            WriteXmlAsJson(writer, item, elementType, plan, $"{path}[{index++}]", depth + 1, contract, context);
        writer.WriteEndArray();
    }

    private static void WriteXmlDictionary(Utf8JsonWriter writer, XElement element, Type elementType, ValueConversionPlan plan, string path, int depth,
        JsonConversionContract contract, XmlConversionContext context)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        writer.WriteStartObject();
        foreach (var child in element.Elements().OrderBy(child => child.Name.LocalName, StringComparer.Ordinal))
        {
            if (!names.Add(child.Name.LocalName)) throw Reject(plan, $"ambiguous repeated XML dictionary key '{child.Name.LocalName}' at '{path}'");
            writer.WritePropertyName(child.Name.LocalName);
            WriteXmlAsJson(writer, child, elementType, plan, $"{path}.{child.Name.LocalName}", depth + 1, contract, context);
        }
        writer.WriteEndObject();
    }

    private static void WriteXmlAttributeAsJson(Utf8JsonWriter writer, XAttribute attribute, Type targetType, ValueConversionPlan plan, string path, int depth,
        JsonConversionContract contract, XmlConversionContext context, JsonConverter? converter)
    {
        if (!IsXmlScalar(Nullable.GetUnderlyingType(targetType) ?? targetType) && converter is null &&
            context.Options(contract).GetTypeInfo(Nullable.GetUnderlyingType(targetType) ?? targetType).Kind != JsonTypeInfoKind.None)
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' cannot map XML attribute '{attribute.Name.LocalName}' to complex member at '{path}'");
        WriteXmlAsJson(writer, new XElement(attribute.Name, attribute.Value), targetType, plan, path, depth, contract, context, converter);
    }

    private static void WriteXmlMatchedObject(Utf8JsonWriter writer, XElement element, IJsonConversionMemberMatcher matcher,
        ValueConversionPlan plan, string path, int depth, JsonConversionContract contract, XmlConversionContext context)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        writer.WriteStartObject();
        foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            var name = attribute.Name.LocalName;
            if (!names.Add(name)) throw Reject(plan, $"ambiguous XML mappings for member '{name}' at '{path}'");
            if (!matcher.TryGetMemberType(name, out var type)) throw Reject(plan, $"custom JSON converter does not support XML attribute '{name}' at '{path}'");
            writer.WritePropertyName(name);
            WriteXmlAttributeAsJson(writer, attribute, type, plan, $"{path}.@{name}", depth, ChildContract(type, contract), context, null);
        }
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            if (!names.Add(name)) throw Reject(plan, $"ambiguous XML mappings for member '{name}' at '{path}'");
            if (!matcher.TryGetMemberType(name, out var type)) throw Reject(plan, $"custom JSON converter does not support XML element '{name}' at '{path}'");
            writer.WritePropertyName(name);
            WriteXmlAsJson(writer, child, type, plan, $"{path}.{name}", depth + 1, ChildContract(type, contract), context);
        }
        writer.WriteEndObject();
    }

    private static JsonElement NormalizeXml(JsonElement projected, object? typed, Type type, JsonConversionContract contract, XmlConversionContext context)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JsonBaseline.Options.Encoder }))
            WriteNormalizedXml(writer, projected, typed, type, contract, context);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteNormalizedXml(Utf8JsonWriter writer, JsonElement projected, object? typed, Type type,
        JsonConversionContract contract, XmlConversionContext context, JsonConverter? customConverter = null, JsonSerializerOptions? converterOptions = null)
    {
        var options = context.Options(contract);
        var info = options.GetTypeInfo(Nullable.GetUnderlyingType(type) ?? type);
        if (customConverter is not null && !JsonConversionContract.IsContractAdapter(customConverter))
        {
            // A property converter is not a global type converter. Keep its owner's options unchanged
            // so its own delegation to ordinary type metadata does not select itself recursively.
            JsonSerializer.Serialize(writer, typed, context.ConverterInfo(type, converterOptions ?? options, customConverter));
            return;
        }
        if (typed is null || projected.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array) || info.Kind == JsonTypeInfoKind.None)
        {
            JsonSerializer.Serialize(writer, typed, type, options);
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Enumerable && projected.ValueKind == JsonValueKind.Array && typed is System.Collections.IEnumerable items)
        {
            var itemType = CollectionElement(type)!;
            var selected = ChildContract(itemType, contract);
            writer.WriteStartArray();
            var index = 0;
            foreach (var item in items)
            {
                if (index < projected.GetArrayLength()) WriteNormalizedXml(writer, projected[index], item, itemType, selected, context);
                else JsonSerializer.Serialize(writer, item, itemType, context.Options(selected));
                index++;
            }
            writer.WriteEndArray();
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Dictionary && typed is System.Collections.IDictionary dictionary)
        {
            var valueType = CollectionElement(type, dictionary: true)!;
            var selected = ChildContract(valueType, contract);
            writer.WriteStartObject();
            foreach (System.Collections.DictionaryEntry item in dictionary)
            {
                var name = (string)item.Key;
                writer.WritePropertyName(name);
                if (projected.TryGetProperty(name, out var supplied)) WriteNormalizedXml(writer, supplied, item.Value, valueType, selected, context);
                else JsonSerializer.Serialize(writer, item.Value, valueType, context.Options(selected));
            }
            writer.WriteEndObject();
            return;
        }
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            JsonSerializer.Serialize(writer, typed, type, options);
            return;
        }
        writer.WriteStartObject();
        foreach (var property in projected.EnumerateObject())
        {
            var member = info.Properties.Single(member => member.Name == property.Name);
            writer.WritePropertyName(property.Name);
            var readsMember = member.Set is not null || member.AssociatedParameter is not null ||
                (member.ObjectCreationHandling ?? info.PreferredPropertyObjectCreationHandling ?? options.PreferredObjectCreationHandling) == JsonObjectCreationHandling.Populate;
            if (member.Get is null || !readsMember) JsonSerializer.Serialize(writer, property.Value, JsonBaseline.Options);
            else WriteNormalizedXml(writer, property.Value, member.Get(typed), member.PropertyType,
                ChildContract(member.PropertyType, contract, member.AttributeProvider as MemberInfo), context, member.CustomConverter, options);
        }
        writer.WriteEndObject();
    }

    private static void WriteXmlNumber(Utf8JsonWriter writer, string text, Type targetType, ValueConversionPlan plan, string path)
    {
        var styles = System.Globalization.NumberStyles.Number;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        switch (Type.GetTypeCode(targetType))
        {
            case TypeCode.Byte when byte.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.SByte when sbyte.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.Int16 when short.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.UInt16 when ushort.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.Int32 when int.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.UInt32 when uint.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.Int64 when long.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.UInt64 when ulong.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.Single when float.TryParse(text, styles, culture, out var value) && float.IsFinite(value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.Double when double.TryParse(text, styles, culture, out var value) && double.IsFinite(value):
                writer.WriteNumberValue(value);
                return;
            case TypeCode.Decimal when decimal.TryParse(text, styles, culture, out var value):
                writer.WriteNumberValue(value);
                return;
        }

        throw Reject(plan, $"target contract '{plan.TargetType.Alias}' cannot safely represent XML number at '{path}' as '{targetType.Name}'");
    }

    private static bool IsNumericType(Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or
            TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal => true,
        _ => false
    };

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value, ValueConversionPlan plan, int depth)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Length > plan.Limits.MaxCollectionItems)
                    throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");
                writer.WriteStartObject();
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value, plan, depth + 1);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                var items = value.EnumerateArray().ToArray();
                if (items.Length > plan.Limits.MaxCollectionItems)
                    throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");
                writer.WriteStartArray();
                foreach (var item in items)
                    WriteCanonicalJson(writer, item, plan, depth + 1);
                writer.WriteEndArray();
                return;
            default:
                value.WriteTo(writer);
                return;
        }
    }

    private static void ValidatePayloadLimits(ValueConversionPlan plan, JsonElement value)
    {
        if (Encoding.UTF8.GetByteCount(value.GetRawText()) > plan.Limits.MaxPayloadBytes)
            throw Reject(plan, $"maximum payload size '{plan.Limits.MaxPayloadBytes}' bytes was exceeded");
    }

    private static bool IsJsonProfile(ValueConversionPlan plan) =>
        plan.Profile is { Id: "elsa.json", Version: "1" };

    private static bool IsJsonStringProfile(ValueConversionPlan plan) =>
        plan.Profile is { Id: "elsa.json-string", Version: "1" };

    private static bool IsXmlProfile(ValueConversionPlan plan) =>
        plan.Profile is { Id: "elsa.xml", Version: "1" };

    private static RuntimeValueConversionException Reject(ValueConversionPlan plan, string reason) => new(plan, reason);

    private sealed record XmlMember(string SourceName, JsonPropertyInfo Property, JsonConversionContract Contract);

    // Metadata belongs to a conversion, so it can be reused for collection elements without pinning a module type.
    private sealed class XmlConversionContext
    {
        private static readonly MethodInfo CreateValueInfo = typeof(XmlConversionContext).GetMethod(nameof(ValueInfo), BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly Dictionary<JsonConversionContract, JsonSerializerOptions> options = new();
        private readonly Dictionary<JsonTypeInfo, IReadOnlyDictionary<string, XmlMember>> members = new();
        private readonly Dictionary<(Type Type, JsonSerializerOptions Options, JsonConverter Converter), JsonTypeInfo> converterInfos = new();
        internal JsonSerializerOptions Options(JsonConversionContract contract)
        {
            if (!options.TryGetValue(contract, out var selected)) options.Add(contract, selected = contract.Options);
            return selected;
        }

        internal JsonTypeInfo ConverterInfo(Type type, JsonSerializerOptions activeOptions, JsonConverter converter)
        {
            var key = (type, activeOptions, converter);
            if (converterInfos.TryGetValue(key, out var existing)) return existing;
            var expanded = converter is JsonConverterFactory factory ? factory.CreateConverter(type, activeOptions) : converter;
            var info = (JsonTypeInfo)CreateValueInfo.MakeGenericMethod(type).Invoke(null, [activeOptions, expanded])!;
            converterInfos.Add(key, info);
            return info;
        }

        private static JsonTypeInfo ValueInfo<T>(JsonSerializerOptions activeOptions, JsonConverter converter) =>
            JsonMetadataServices.CreateValueInfo<T>(activeOptions, converter);

        internal IReadOnlyDictionary<string, XmlMember> Members(JsonTypeInfo info, JsonConversionContract contract)
        {
            if (members.TryGetValue(info, out var existing)) return existing;
            var selected = info.Properties.Where(property => !property.IsExtensionData && property.AttributeProvider is MemberInfo).Select(property =>
            {
                var member = (MemberInfo)property.AttributeProvider!;
                var sourceName = member.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true)?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(member.Name);
                return new XmlMember(sourceName, property, ChildContract(property.PropertyType, contract, member));
            }).ToDictionary(member => member.SourceName, StringComparer.OrdinalIgnoreCase);
            members.Add(info, selected);
            return selected;
        }
    }
}
