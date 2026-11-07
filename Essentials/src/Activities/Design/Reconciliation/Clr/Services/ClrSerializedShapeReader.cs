using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Serialization;
using Elsa.Primitives.Extensions;
using Elsa.Primitives.Models;
using TypeReference = Elsa.Primitives.Models.TypeReference;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>Reads serializer contract metadata without constructing a DTO, converter or attribute.</summary>
internal sealed class ClrSerializedShapeReader(string sourceRevision, ActivitySerializedShapeLimits limits,
    CancellationToken cancellationToken, string? metadataSeed = null)
{
    private const string JsonPrefix = "System.Text.Json.Serialization.";
    private readonly Dictionary<string, string> _assets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visitedTypes = new(StringComparer.Ordinal);
    private int _remainingMembers = limits.MaximumMembers;
    internal Type? SelectedType { get; private set; }
    internal NullabilityInfo? SelectedNullability { get; private set; }

    internal ActivitySerializedShape UnavailableContract(Type type, string reason)
    {
        Track(type);
        return Finish(new(ActivitySerializedShapeState.Unavailable, ActivitySerializedValueKind.Unknown, null,
            sourceRevision, ActivityValueJsonProfile.Revision, string.Empty) { ReasonCode = reason });
    }

    internal ActivitySerializedShape Read(Type type, TypeReference rootType, ImmutableArray<string> path,
        bool? isNullable = null, ActivitySerializedPresence presence = ActivitySerializedPresence.Required,
        ValueRepresentation? representation = null, NullabilityInfo? rootNullability = null)
    {
        var current = new MemberType(type, rootNullability, isNullable ?? TypeNullability(type, rootNullability), presence);
        foreach (var segment in path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Track(current.Type);
            if (OpaqueReason(current.Type) is { } opaque)
                return Finish(UnknownAt(current, opaque));
            var (element, collection) = TypeReferenceFactory.Decompose(current.Type);
            if (collection == CollectionKind.Dictionary)
            {
                current = new(element, GenericInfo(current.Nullability, 1),
                    CombineNullable(current.IsNullable, TypeNullability(element, GenericInfo(current.Nullability, 1))),
                    CombinePresence(current.Presence, ActivitySerializedPresence.Optional));
                continue;
            }
            if (collection != CollectionKind.Single || IsScalar(current.Type))
                return Finish(UnknownAt(current, "activity.shape.member-parent-not-object"));
            var members = Members(current.Type, out var reason);
            if (reason is not null)
                return Finish(UnknownAt(current, reason));
            var member = members.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.Name, segment));
            if (member is null)
                return Finish(UnknownAt(current, "activity.shape.member-not-declared"));
            current = member.Value with
            {
                IsNullable = CombineNullable(current.IsNullable, member.Value.IsNullable),
                Presence = CombinePresence(current.Presence, member.Value.Presence)
            };
            if (member.OpaqueReason is { } converter)
                return Finish(UnknownAt(current, converter));
        }

        SelectedType = current.Type;
        SelectedNullability = current.Nullability;
        var selected = Build(current, 0);
        if (path.IsEmpty)
            selected = selected with { Type = rootType, Representation = representation ?? selected.Representation };
        return Finish(selected);
    }

    private ActivitySerializedShape Build(MemberType member, int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type = member.Type;
        Track(type);
        var reference = Reference(type);
        if (OpaqueReason(type) is { } opaque)
            return Unknown(type, opaque) with { IsNullable = member.IsNullable == true ? true : null, Presence = member.Presence };
        if (IsScalar(type))
            return Shape(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Scalar, reference, member);

        var (element, collection) = TypeReferenceFactory.Decompose(type);
        if (depth >= limits.MaximumDepth)
            return Shape(ActivitySerializedShapeState.Truncated, Kind(collection), reference, member)
                with { ReasonCode = "activity.shape.depth-limit" };

        if (collection != CollectionKind.Single)
        {
            var info = collection == CollectionKind.Array ? member.Nullability?.ElementType : GenericInfo(member.Nullability, collection == CollectionKind.Dictionary ? 1 : 0);
            var value = Build(new(element, info, TypeNullability(element, info), ActivitySerializedPresence.Required), depth + 1);
            var state = value.State is ActivitySerializedShapeState.Truncated ? ActivitySerializedShapeState.Truncated : ActivitySerializedShapeState.Known;
            var shape = Shape(state, Kind(collection), reference, member);
            return collection == CollectionKind.Dictionary ? shape with { MapValue = value } : shape with { Element = value };
        }

        var properties = Members(type, out var unsupported);
        if (unsupported is not null)
            return Unknown(type, unsupported) with { IsNullable = member.IsNullable, Presence = member.Presence };
        var result = ImmutableArray.CreateBuilder<ActivitySerializedMember>();
        var truncated = false;
        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_remainingMembers == 0)
            {
                truncated = true;
                break;
            }
            _remainingMembers--;
            var child = property.OpaqueReason is { } reason
                ? Unknown(property.Value.Type, reason) with { IsNullable = property.Value.IsNullable == true ? true : null, Presence = property.Value.Presence }
                : Build(property.Value, depth + 1);
            result.Add(new(property.Name, property.DisplayName, child));
            truncated |= child.State == ActivitySerializedShapeState.Truncated;
        }
        return Shape(truncated ? ActivitySerializedShapeState.Truncated : ActivitySerializedShapeState.Known,
            ActivitySerializedValueKind.Object, reference, member) with
        {
            Members = result.ToImmutable(),
            ReasonCode = truncated ? "activity.shape.member-limit" : null
        };
    }

    private List<SerializedProperty> Members(Type type, out string? reason)
    {
        reason = null;
        var allProperties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var property in allProperties)
        {
            Track(property.PropertyType);
            foreach (var declaration in ReflectionOnlyAttributes.EnumeratePropertyChain(property))
            {
                if (declaration.DeclaringType is { } declaring)
                    Track(declaring);
                TrackAttributes(declaration.GetCustomAttributesData());
            }
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            TrackAttributes(field.GetCustomAttributesData());
        for (var ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (ancestor.GetMembers(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(member => HasAttribute(member, "JsonIncludeAttribute") || HasAttribute(member, "JsonExtensionDataAttribute")))
            {
                reason = "activity.shape.include-unsupported";
                return [];
            }
        }
        if (allProperties.Any(p => Attribute(p, "JsonExtensionDataAttribute") is not null) ||
            type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(f => HasAttribute(f, "JsonExtensionDataAttribute")))
        {
            reason = "activity.shape.extension-data-opaque";
            return [];
        }
        if (allProperties.Any(p => Attribute(p, "JsonIncludeAttribute") is not null) ||
            type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(f => HasAttribute(f, "JsonIncludeAttribute")))
        {
            reason = "activity.shape.include-unsupported";
            return [];
        }

        var result = new List<SerializedProperty>();
        foreach (var property in allProperties.Where(p => p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0))
        {
            var ignore = Attribute(property, "JsonIgnoreAttribute");
            var condition = ignore is null ? 0 : Convert.ToInt32(ignore.NamedArguments.FirstOrDefault(x => x.MemberName == "Condition").TypedValue.Value ?? 1);
            if (condition is 1 or 4) // Always or WhenWriting.
            {
                if (HasUnsupportedPropertyHierarchy(property, string.Empty, ignored: true))
                {
                    reason = "activity.shape.override-contract-unsupported";
                    return [];
                }
                continue;
            }
            if (condition is not (0 or 2 or 3 or 5))
            {
                reason = "activity.shape.ignore-condition-unsupported";
                return [];
            }
            if (condition == 3 && property.PropertyType.IsValueType &&
                !(property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition().FullName == "System.Nullable`1"))
            {
                reason = "activity.shape.ignore-condition-unsupported";
                return [];
            }
            var nameAttribute = Attribute(property, "JsonPropertyNameAttribute");
            if (nameAttribute is not null && nameAttribute.ConstructorArguments.FirstOrDefault().Value is not string)
            {
                reason = "activity.shape.serialized-name-unsupported";
                return [];
            }
            var name = nameAttribute is null ? JsonNamingPolicy.CamelCase.ConvertName(property.Name) :
                (string)nameAttribute.ConstructorArguments[0].Value!;
            if (HasUnsupportedPropertyHierarchy(property, name))
            {
                reason = "activity.shape.override-contract-unsupported";
                return [];
            }
            var nullability = new NullabilityInfoContext().Create(property);
            var conditional = condition is 2 or 3;
            result.Add(new(name, property.Name.Humanize(),
                new(property.PropertyType, nullability, conditional ? false : TypeNullability(property.PropertyType, nullability),
                    conditional ? ActivitySerializedPresence.Optional : ActivitySerializedPresence.Required),
                Attribute(property, "JsonConverterAttribute") is not null ? "activity.shape.converter-opaque" :
                Attribute(property, "JsonNumberHandlingAttribute") is not null ? "activity.shape.number-handling-opaque" : null));
        }
        if (result.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
        {
            reason = "activity.shape.serialized-name-conflict";
            return [];
        }
        return result.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    }

    private string? OpaqueReason(Type type)
    {
        var name = type.FullName;
        if (name is "System.Object" or "System.Text.Json.JsonElement" or "System.Text.Json.JsonDocument" ||
            name?.StartsWith("System.Text.Json.Nodes.", StringComparison.Ordinal) == true)
            return "activity.shape.dynamic-json";
        // Nullable<T> does not inherit type-level converters from T; STJ still consults T.
        for (var current = UnwrapNullable(type); current is not null; current = current.BaseType)
        {
            var attributes = current.GetCustomAttributesData();
            TrackAttributes(attributes);
            if (attributes.Any(a => a.AttributeType.FullName == JsonPrefix + "JsonConverterAttribute"))
                return "activity.shape.converter-opaque";
            if (attributes.Any(a => a.AttributeType.FullName == JsonPrefix + "JsonNumberHandlingAttribute"))
                return "activity.shape.number-handling-opaque";
            if (attributes.Any(a => a.AttributeType.FullName is JsonPrefix + "JsonPolymorphicAttribute" or JsonPrefix + "JsonDerivedTypeAttribute"))
                return "activity.shape.polymorphic-opaque";
        }
        if (type.IsPointer || type.IsByRef || type.IsByRefLike || type.ContainsGenericParameters)
            return "activity.shape.type-unsupported";
        if (type.IsArray && type.GetArrayRank() != 1)
            return "activity.shape.collection-unsupported";
        var (_, kind) = TypeReferenceFactory.Decompose(type);
        if (kind == CollectionKind.Single && type.FullName != "System.String" &&
            type.GetInterfaces().Any(i => i.FullName == "System.Collections.IEnumerable"))
            return "activity.shape.collection-unsupported";
        if (kind == CollectionKind.Single && type.IsInterface && type.GetInterfaces().Length != 0)
            return "activity.shape.interface-inheritance-unsupported";
        if (kind == CollectionKind.Single && !IsScalar(type) &&
            (type.Namespace == "System" || type.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true))
            return "activity.shape.platform-codec-opaque";
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1" && !IsScalar(type))
            return "activity.shape.nullable-struct-unsupported";
        return null;
    }

    private ActivitySerializedShape Shape(ActivitySerializedShapeState state, ActivitySerializedValueKind kind, TypeReference type, MemberType member) =>
        new(state, kind, type, sourceRevision, ActivityValueJsonProfile.Revision, string.Empty)
        {
            IsNullable = member.IsNullable,
            Presence = member.Presence,
            Representation = ValueRepresentationDefaults.Infer(new ValueTypeDescriptor(type.Alias, type.CollectionKind))
        };

    private ActivitySerializedShape Unknown(Type type, string reason) =>
        new(ActivitySerializedShapeState.Unknown, ActivitySerializedValueKind.Unknown, Reference(type), sourceRevision, ActivityValueJsonProfile.Revision, string.Empty)
        { ReasonCode = reason };

    private ActivitySerializedShape UnknownAt(MemberType member, string reason) => Unknown(member.Type, reason) with
    {
        Type = null,
        IsNullable = member.IsNullable == true ? true : null,
        Presence = member.Presence == ActivitySerializedPresence.Optional ? ActivitySerializedPresence.Optional : ActivitySerializedPresence.Unknown
    };

    private ActivitySerializedShape Finish(ActivitySerializedShape shape)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("elsa.clr-serialized-shape/1");
            writer.Write(metadataSeed ?? string.Empty);
            writer.Write(_assets.Count);
            foreach (var asset in _assets.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                writer.Write(asset.Key);
                writer.Write(asset.Value);
            }
            writer.Write(_visitedTypes.Count);
            foreach (var type in _visitedTypes.Order(StringComparer.Ordinal))
                writer.Write(type);
            // Include the consulted facts as well as file identities. A live selectable CLR type and
            // an on-disk asset must not yield a stale equal revision after an in-place host update.
            WriteFacts(writer, shape);
        }
        var revision = "sha256:" + Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
        return Stamp(shape);

        ActivitySerializedShape Stamp(ActivitySerializedShape node) => node with
        {
            MetadataRevision = revision,
            Members = node.Members.Select(m => m with { Shape = Stamp(m.Shape) }).ToImmutableArray(),
            Element = node.Element is null ? null : Stamp(node.Element),
            MapValue = node.MapValue is null ? null : Stamp(node.MapValue)
        };
    }

    private static void WriteFacts(BinaryWriter writer, ActivitySerializedShape shape)
    {
        writer.Write((int)shape.State);
        writer.Write((int)shape.Kind);
        writer.Write(shape.Type is not null);
        if (shape.Type is { } type)
        {
            writer.Write(type.Alias);
            writer.Write((int)type.CollectionKind);
        }
        writer.Write(shape.IsNullable is null ? (byte)0 : shape.IsNullable == true ? (byte)2 : (byte)1);
        writer.Write((int)shape.Presence);
        writer.Write(shape.Representation is null ? -1 : (int)shape.Representation.Value);
        writer.Write(shape.IsSensitive is null ? (byte)0 : shape.IsSensitive == true ? (byte)2 : (byte)1);
        writer.Write(shape.IsCredential is null ? (byte)0 : shape.IsCredential == true ? (byte)2 : (byte)1);
        writer.Write(shape.ReasonCode ?? string.Empty);
        writer.Write(shape.Members.Length);
        foreach (var member in shape.Members)
        {
            writer.Write(member.SerializedName);
            writer.Write(member.DisplayName);
            WriteFacts(writer, member.Shape);
        }
        writer.Write(shape.Element is not null);
        if (shape.Element is not null)
            WriteFacts(writer, shape.Element);
        writer.Write(shape.MapValue is not null);
        if (shape.MapValue is not null)
            WriteFacts(writer, shape.MapValue);
    }

    private void Track(Type type)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (var current = type; current is not null; current = current.BaseType)
            TrackAsset(current);
        foreach (var contract in type.GetInterfaces())
            TrackAsset(contract);
    }

    private void TrackAttributes(IEnumerable<CustomAttributeData> attributes)
    {
        foreach (var attribute in attributes)
            Track(attribute.AttributeType);
    }

    private void TrackAsset(Type type)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = type.Assembly;
        var key = assembly.FullName ?? assembly.GetName().Name ?? string.Empty;
        _visitedTypes.Add(key + ":" + type.FullName);
        if (_assets.ContainsKey(key))
            return;
        var path = assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new FileNotFoundException("Serialized contract metadata is unavailable.");
        var bytes = File.ReadAllBytes(path);
        using var image = new PEReader(new MemoryStream(bytes));
        var metadata = image.GetMetadataReader();
        if (type.Module != assembly.ManifestModule || metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != assembly.ManifestModule.ModuleVersionId)
            throw new InvalidOperationException("Serialized contract metadata no longer matches its asset.");
        _assets.Add(key, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static TypeReference Reference(Type type) => TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
    private static ActivitySerializedValueKind Kind(CollectionKind kind) => kind == CollectionKind.Dictionary ? ActivitySerializedValueKind.Map :
        kind == CollectionKind.Single ? ActivitySerializedValueKind.Object : ActivitySerializedValueKind.Collection;
    private static NullabilityInfo? GenericInfo(NullabilityInfo? info, int index) => info?.GenericTypeArguments.ElementAtOrDefault(index);
    private static bool? TypeNullability(Type type, NullabilityInfo? info = null) => type.IsValueType
        ? type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1"
        : info?.ReadState switch { NullabilityState.NotNull => false, NullabilityState.Nullable => true, _ => null };
    private static bool? CombineNullable(bool? left, bool? right) => left == true || right == true ? true : left == false && right == false ? false : null;
    private static ActivitySerializedPresence CombinePresence(ActivitySerializedPresence left, ActivitySerializedPresence right) =>
        left == ActivitySerializedPresence.Optional || right == ActivitySerializedPresence.Optional ? ActivitySerializedPresence.Optional :
        left == ActivitySerializedPresence.Required && right == ActivitySerializedPresence.Required ? ActivitySerializedPresence.Required : ActivitySerializedPresence.Unknown;
    // STJ reads attributes on each declaration with inherit:false; an override does not inherit its base's JSON policy.
    private static CustomAttributeData? Attribute(PropertyInfo property, string name) =>
        property.GetCustomAttributesData().FirstOrDefault(attribute => attribute.AttributeType.FullName == JsonPrefix + name);

    private static bool HasUnsupportedPropertyHierarchy(PropertyInfo property, string serializedName, bool ignored = false)
    {
        var getter = property.GetMethod;
        var isOverride = getter?.IsVirtual == true && (getter.Attributes & MethodAttributes.NewSlot) == 0;
        if (ignored && isOverride) return false;
        for (var ancestor = property.DeclaringType?.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        foreach (var previous in ancestor.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(candidate => candidate.Name == property.Name && candidate.GetMethod?.IsPublic == true && candidate.GetIndexParameters().Length == 0))
        {
            // A differently named base declaration can survive STJ's override/shadow resolution as a second member.
            // Until that hierarchy is fully represented, do not claim a complete Known object contract.
            if (!isOverride) return true;
            var ignore = Attribute(previous, "JsonIgnoreAttribute");
            var condition = ignore is null ? 0 : Convert.ToInt32(ignore.NamedArguments.FirstOrDefault(x => x.MemberName == "Condition").TypedValue.Value ?? 1);
            if (condition is 1 or 4) continue;
            var naming = Attribute(previous, "JsonPropertyNameAttribute");
            if (naming is not null && naming.ConstructorArguments.FirstOrDefault().Value is not string) return true;
            var previousName = naming is null ? JsonNamingPolicy.CamelCase.ConvertName(previous.Name) : (string)naming.ConstructorArguments[0].Value!;
            if (previousName != serializedName) return true;
        }
        return false;
    }
    private static bool HasAttribute(MemberInfo member, string name) => member.GetCustomAttributesData().Any(a => a.AttributeType.FullName == JsonPrefix + name);
    private static Type UnwrapNullable(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1"
            ? type.GetGenericArguments()[0]
            : type;
    private static bool IsScalar(Type type) => type.IsPrimitive || type.IsEnum ||
        type.IsArray && type.GetArrayRank() == 1 && type.GetElementType()?.FullName == "System.Byte" ||
        type.FullName is "System.String" or "System.Decimal" or
        "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Guid" or "System.Uri" ||
        type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1" && IsScalar(type.GetGenericArguments()[0]);

    private sealed record MemberType(Type Type, NullabilityInfo? Nullability, bool? IsNullable, ActivitySerializedPresence Presence);
    private sealed record SerializedProperty(string Name, string DisplayName, MemberType Value, string? OpaqueReason);
}
