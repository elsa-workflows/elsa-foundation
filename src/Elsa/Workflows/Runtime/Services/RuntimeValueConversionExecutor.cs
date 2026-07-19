using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Strict runtime interpreter for publication-pinned value-conversion plans.
/// It deliberately contains no converter discovery: only built-in pinned operations and the pinned
/// <c>elsa.json@1</c> / <c>elsa.xml@1</c> profiles are executable in this slice.
/// </summary>
public sealed class RuntimeValueConversionExecutor(IWellKnownTypeRegistry? wellKnownTypeRegistry = null) : IRuntimeValueConversionExecutor
{
    public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

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
            ValueConversionOperation.NumericWidening => ConvertNumeric(inline, plan),
            ValueConversionOperation.RecursiveCollection => ConvertCollection(inline, plan, depth: 1),
            ValueConversionOperation.CanonicalAny => CanonicalizeAny(inline, plan),
            ValueConversionOperation.Profile => ConvertProfile(inline, plan),
            _ => throw Reject(plan, $"the pinned operation '{plan.Operation}' is unsupported")
        };

        return ValueEnvelope.Inline(plan.TargetType, converted, source.Policy);
    }

    private static void ValidatePlan(ValueConversionPlan plan)
    {
        if (plan.Operation == ValueConversionOperation.Profile && !IsJsonProfile(plan) && !IsXmlProfile(plan))
            throw Reject(plan, "the pinned conversion profile is not available in this runtime slice");

        switch (plan.Operation)
        {
            case ValueConversionOperation.Identity when !ValueConversionCompatibility.SameType(plan.SourceType, plan.TargetType):
                throw Reject(plan, "identity conversion requires matching source and target contracts");
            case ValueConversionOperation.NullableCompatibility when !ValueConversionCompatibility.IsNullableCompatibility(plan.SourceType, plan.TargetType):
                throw Reject(plan, "nullable compatibility requires matching source and target contracts");
            case ValueConversionOperation.NumericWidening when !ValueConversionCompatibility.IsSafeNumericWidening(plan.SourceType, plan.TargetType):
                throw Reject(plan, "numeric conversion is not a safe widening");
            case ValueConversionOperation.RecursiveCollection when !ValueConversionCompatibility.CanRecursivelyWidenCollection(plan.SourceType, plan.TargetType):
                throw Reject(plan, "recursive collection conversion does not have a safe element widening");
            case ValueConversionOperation.CanonicalAny when !ValueConversionCompatibility.IsCanonicalAnyTarget(plan.TargetType):
                throw Reject(plan, "canonical dynamic projection requires the Elsa.Any target contract");
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
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in values)
                item.WriteTo(writer);
            writer.WriteEndArray();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement CanonicalizeAny(JsonElement value, ValueConversionPlan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(writer, value, plan, depth: 1);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private JsonElement ConvertProfile(JsonElement value, ValueConversionPlan plan)
    {
        if (IsXmlProfile(plan))
            return ConvertXmlProfile(value, plan);
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

        return ConvertTypedJson(root, plan);
    }

    private JsonElement ConvertXmlProfile(JsonElement value, ValueConversionPlan plan)
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
        var targetType = TypeReferenceFactory.Close(elementType, plan.TargetType.CollectionKind);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteXmlAsJson(writer, document.Root!, targetType, plan, "$", depth: 1);
        using var json = JsonDocument.Parse(stream.ToArray());
        return json.RootElement.Clone();
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

    private JsonElement ConvertTypedJson(JsonElement root, ValueConversionPlan plan)
    {
        if (wellKnownTypeRegistry is null)
            throw Reject(plan, "JSON typed conversion requires a well-known type registry at runtime");
        if (!wellKnownTypeRegistry.TryGetTypeOrDefault(plan.TargetType.Alias, out var elementType) || elementType == typeof(object))
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' is not a registered typed alias");

        var targetType = TypeReferenceFactory.Close(elementType, plan.TargetType.CollectionKind);
        ValidateJsonForType(root, targetType, plan, "$");

        return CanonicalizeAny(root, plan);
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

    private static void ValidateJsonForType(JsonElement value, Type targetType, ValueConversionPlan plan, string path)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            if (value.ValueKind == JsonValueKind.Null)
                return;
            ValidateJsonForType(value, nullableType, plan, path);
            return;
        }

        if (!targetType.IsValueType && value.ValueKind == JsonValueKind.Null)
            return;

        if (targetType == typeof(string))
        {
            if (value.ValueKind != JsonValueKind.String)
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected string at '{path}'");
            return;
        }

        if (targetType == typeof(bool))
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected boolean at '{path}'");
            return;
        }

        if (IsNumericType(targetType))
        {
            ValidateNumeric(value, targetType, plan, path);
            return;
        }

        if (targetType.IsEnum)
        {
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected enum string or number at '{path}'");
            return;
        }

        if (targetType.IsArray)
        {
            ValidateJsonArray(value, targetType.GetElementType()!, plan, path);
            return;
        }

        if (targetType.IsGenericType)
        {
            var definition = targetType.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
                definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>))
            {
                ValidateJsonArray(value, targetType.GetGenericArguments()[0], plan, path);
                return;
            }

            if ((definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) &&
                targetType.GetGenericArguments()[0] == typeof(string))
            {
                ValidateJsonDictionary(value, targetType.GetGenericArguments()[1], plan, path);
                return;
            }
        }

        ValidateJsonObject(value, targetType, plan, path);
    }

    private static void ValidateJsonArray(JsonElement value, Type elementType, ValueConversionPlan plan, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected array at '{path}'");
        var index = 0;
        foreach (var item in value.EnumerateArray())
            ValidateJsonForType(item, elementType, plan, $"{path}[{index++}]");
    }

    private static void ValidateJsonDictionary(JsonElement value, Type elementType, ValueConversionPlan plan, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected object at '{path}'");
        foreach (var property in value.EnumerateObject())
            ValidateJsonForType(property.Value, elementType, plan, $"{path}.{property.Name}");
    }

    private static void ValidateJsonObject(JsonElement value, Type targetType, ValueConversionPlan plan, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected object at '{path}'");

        var properties = GetTypeMembers(targetType);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var jsonProperty in value.EnumerateObject())
        {
            if (!properties.TryGetValue(jsonProperty.Name, out var targetProperty))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' does not support member '{jsonProperty.Name}' at '{path}'");

            seen.Add(targetProperty.JsonName);
            ValidateJsonForType(jsonProperty.Value, targetProperty.Type, plan, $"{path}.{jsonProperty.Name}");
        }

        foreach (var required in properties.Values.Where(property => property.IsRequired && !seen.Contains(property.JsonName)))
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' is missing required member '{required.JsonName}' at '{path}'");
    }

    private static IReadOnlyDictionary<string, TypeMember> GetTypeMembers(Type targetType) =>
        targetType
            .GetProperties()
            .Where(property => property.GetMethod is not null && property.GetMethod.IsPublic && property.GetIndexParameters().Length == 0)
            .Select(property => new TypeMember(
                property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: true)
                    .Cast<JsonPropertyNameAttribute>()
                    .SingleOrDefault()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                property.PropertyType,
                property.GetCustomAttributes(inherit: true).Any(attribute =>
                    attribute.GetType().FullName is "System.Runtime.CompilerServices.RequiredMemberAttribute" or "System.Text.Json.Serialization.JsonRequiredAttribute")))
            .ToDictionary(property => property.JsonName, StringComparer.OrdinalIgnoreCase);

    private static void WriteXmlAsJson(Utf8JsonWriter writer, XElement element, Type targetType, ValueConversionPlan plan, string path, int depth)
    {
        if (depth > plan.Limits.MaxDepth)
            throw Reject(plan, $"maximum conversion depth '{plan.Limits.MaxDepth}' was exceeded");

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            WriteXmlAsJson(writer, element, nullableType, plan, path, depth);
            return;
        }

        if (targetType == typeof(string))
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
            if (string.IsNullOrWhiteSpace(text))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected enum value at '{path}'");
            writer.WriteStringValue(text);
            return;
        }

        if (targetType.IsArray)
        {
            WriteXmlArray(writer, element, targetType.GetElementType()!, plan, path, depth);
            return;
        }

        if (targetType.IsGenericType)
        {
            var definition = targetType.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
                definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>))
            {
                WriteXmlArray(writer, element, targetType.GetGenericArguments()[0], plan, path, depth);
                return;
            }

            if ((definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) &&
                targetType.GetGenericArguments()[0] == typeof(string))
            {
                WriteXmlDictionary(writer, element, targetType.GetGenericArguments()[1], plan, path, depth);
                return;
            }
        }

        WriteXmlObject(writer, element, targetType, plan, path, depth);
    }

    private static void WriteXmlObject(Utf8JsonWriter writer, XElement element, Type targetType, ValueConversionPlan plan, string path, int depth)
    {
        var members = GetTypeMembers(targetType);
        var attributes = element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
        var children = element.Elements().ToArray();

        foreach (var attribute in attributes)
            if (!members.ContainsKey(attribute.Name.LocalName))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' does not support XML attribute '{attribute.Name.LocalName}' at '{path}'");
        foreach (var child in children)
            if (!members.ContainsKey(child.Name.LocalName))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' does not support XML element '{child.Name.LocalName}' at '{path}'");

        writer.WriteStartObject();
        foreach (var member in members.Values.OrderBy(member => member.JsonName, StringComparer.Ordinal))
        {
            var attribute = attributes.SingleOrDefault(attribute => StringComparer.OrdinalIgnoreCase.Equals(attribute.Name.LocalName, member.JsonName));
            var matchingChildren = children.Where(child => StringComparer.OrdinalIgnoreCase.Equals(child.Name.LocalName, member.JsonName)).ToArray();
            if (attribute is not null && matchingChildren.Length != 0)
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' has ambiguous XML mappings for member '{member.JsonName}' at '{path}'");
            if (matchingChildren.Length > 1)
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' has ambiguous repeated XML elements for member '{member.JsonName}' at '{path}'");

            if (attribute is null && matchingChildren.Length == 0)
            {
                if (member.IsRequired)
                    throw Reject(plan, $"target contract '{plan.TargetType.Alias}' is missing required XML member '{member.JsonName}' at '{path}'");
                continue;
            }

            writer.WritePropertyName(member.JsonName);
            if (attribute is not null)
                WriteXmlAttributeAsJson(writer, attribute, member.Type, plan, $"{path}.@{member.JsonName}");
            else
                WriteXmlAsJson(writer, matchingChildren[0], member.Type, plan, $"{path}.{member.JsonName}", depth + 1);
        }

        writer.WriteEndObject();
    }

    private static void WriteXmlArray(Utf8JsonWriter writer, XElement element, Type elementType, ValueConversionPlan plan, string path, int depth)
    {
        var items = element.Elements().ToArray();
        if (items.Length > plan.Limits.MaxCollectionItems)
            throw Reject(plan, $"maximum collection size '{plan.Limits.MaxCollectionItems}' was exceeded");

        writer.WriteStartArray();
        var index = 0;
        foreach (var item in items)
            WriteXmlAsJson(writer, item, elementType, plan, $"{path}[{index++}]", depth + 1);
        writer.WriteEndArray();
    }

    private static void WriteXmlDictionary(Utf8JsonWriter writer, XElement element, Type elementType, ValueConversionPlan plan, string path, int depth)
    {
        writer.WriteStartObject();
        foreach (var child in element.Elements().OrderBy(child => child.Name.LocalName, StringComparer.Ordinal))
        {
            writer.WritePropertyName(child.Name.LocalName);
            WriteXmlAsJson(writer, child, elementType, plan, $"{path}.{child.Name.LocalName}", depth + 1);
        }
        writer.WriteEndObject();
    }

    private static void WriteXmlAttributeAsJson(Utf8JsonWriter writer, XAttribute attribute, Type targetType, ValueConversionPlan plan, string path)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            WriteXmlAttributeAsJson(writer, attribute, nullableType, plan, path);
            return;
        }

        if (targetType == typeof(string))
        {
            writer.WriteStringValue(attribute.Value);
            return;
        }
        if (targetType == typeof(bool))
        {
            if (!bool.TryParse(attribute.Value.Trim(), out var value))
                throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected boolean at '{path}'");
            writer.WriteBooleanValue(value);
            return;
        }
        if (IsNumericType(targetType))
        {
            WriteXmlNumber(writer, attribute.Value.Trim(), targetType, plan, path);
            return;
        }
        if (targetType.IsEnum)
        {
            writer.WriteStringValue(attribute.Value.Trim());
            return;
        }

        throw Reject(plan, $"target contract '{plan.TargetType.Alias}' cannot map XML attribute '{attribute.Name.LocalName}' to complex member at '{path}'");
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

    private static void ValidateNumeric(JsonElement value, Type targetType, ValueConversionPlan plan, string path)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' expected number at '{path}'");

        var ok = Type.GetTypeCode(targetType) switch
        {
            TypeCode.Byte => value.TryGetByte(out _),
            TypeCode.SByte => value.TryGetSByte(out _),
            TypeCode.Int16 => value.TryGetInt16(out _),
            TypeCode.UInt16 => value.TryGetUInt16(out _),
            TypeCode.Int32 => value.TryGetInt32(out _),
            TypeCode.UInt32 => value.TryGetUInt32(out _),
            TypeCode.Int64 => value.TryGetInt64(out _),
            TypeCode.UInt64 => value.TryGetUInt64(out _),
            TypeCode.Single => value.TryGetSingle(out _),
            TypeCode.Double => value.TryGetDouble(out _),
            TypeCode.Decimal => value.TryGetDecimal(out _),
            _ => false
        };

        if (!ok)
            throw Reject(plan, $"target contract '{plan.TargetType.Alias}' cannot safely represent number at '{path}' as '{targetType.Name}'");
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

    private static bool IsXmlProfile(ValueConversionPlan plan) =>
        plan.Profile is { Id: "elsa.xml", Version: "1" };

    private static RuntimeValueConversionException Reject(ValueConversionPlan plan, string reason) => new(plan, reason);

    private sealed record TypeMember(string JsonName, Type Type, bool IsRequired);
}
