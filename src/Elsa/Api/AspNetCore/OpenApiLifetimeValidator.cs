using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// Validates the completed, API Explorer-facing endpoint metadata without retaining any inspected
/// implementation objects. The validator deliberately does not inspect the routing request delegate:
/// routing owns that delegate and releases it during generation drain.
/// </summary>
public static class OpenApiLifetimeValidator
{
    private const int MaximumEnumerableItems = 256;

    /// <summary>Validates completed endpoint metadata and returns its value-only lifetime marker.</summary>
    public static OpenApiLifetimeMetadata Validate(EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoint = NormalizeEndpoint(builder.DisplayName);
        var ownership = builder.Metadata.OfType<EndpointOwnershipMetadata>().ToArray();
        if (ownership.Length == 0)
        {
            throw new UnsafeOpenApiMetadataException(new UnsafeOpenApiMetadataViolation(
                "<unknown>",
                null,
                null,
                endpoint,
                OpenApiLifetimeViolationCategory.MissingOwnership,
                typeof(EndpointOwnershipMetadata).FullName!,
                "<host>"));
        }

        if (ownership.Length != 1)
        {
            var ownerIdentity = string.Join(", ", ownership.Select(item => item.OwnerId).Order(StringComparer.Ordinal));
            var shells = ownership.Select(item => item.ShellId).Where(item => item is not null).Distinct(StringComparer.Ordinal).ToArray();
            var generations = ownership.Select(item => item.Generation).Where(item => item is not null).Distinct().ToArray();
            var shell = shells.Length == 1 ? shells[0] : null;
            var generation = generations.Length == 1 ? generations[0] : null;
            throw new UnsafeOpenApiMetadataException(new UnsafeOpenApiMetadataViolation(
                ownerIdentity,
                shell,
                generation,
                endpoint,
                OpenApiLifetimeViolationCategory.DuplicateOwnership,
                ownerIdentity,
                "<host>"));
        }

        var owner = ownership[0];
        var violations = new List<UnsafeOpenApiMetadataViolation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < builder.Metadata.Count; index++)
        {
            var metadata = builder.Metadata[index];
            if (metadata is null)
            {
                AddViolation(
                    violations,
                    seen,
                    owner,
                    endpoint,
                    OpenApiLifetimeViolationCategory.UnknownMetadataShape,
                    $"metadata[{index}]",
                    "<host>");
                continue;
            }

            InspectValue(
                metadata,
                CategoryForMetadata(metadata.GetType()),
                $"metadata[{index}]",
                owner,
                endpoint,
                violations,
                seen,
                depth: 0);
        }

        if (violations.Count != 0)
            throw new UnsafeOpenApiMetadataException(violations);

        var classification = owner.Kind == EndpointOwnerKind.Host
            ? OpenApiLifetimeClassification.HostStatic
            : OpenApiLifetimeClassification.SharedContract;
        return new OpenApiLifetimeMetadata(owner.OwnerId, classification, endpoint);
    }

    /// <summary>Validates completed endpoint metadata and attaches one value-only lifetime marker.</summary>
    public static void ValidateAndMark(EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var marker = Validate(builder);
        var existing = builder.Metadata.OfType<OpenApiLifetimeMetadata>().ToArray();

        // A convention can be applied through more than one composition layer. Keep the marker
        // singular while preserving every other metadata item and the original builder instance.
        if (existing.Length == 0)
        {
            builder.Metadata.Add(marker);
            return;
        }

        if (existing.Length == 1 && MarkerMatches(existing[0], marker))
            return;

        var owner = builder.Metadata.OfType<EndpointOwnershipMetadata>().Single();
        throw new UnsafeOpenApiMetadataException(new UnsafeOpenApiMetadataViolation(
            owner.OwnerId,
            owner.ShellId,
            owner.Generation,
            marker.Endpoint,
            OpenApiLifetimeViolationCategory.UnknownMetadataShape,
            "Existing OpenAPI lifetime marker does not match the completed endpoint metadata.",
            "<host>"));
    }

    private static void InspectValue(
        object value,
        OpenApiLifetimeViolationCategory category,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen,
        int depth)
    {
        if (value is Type type)
        {
            InspectType(type, category, path, owner, endpoint, violations, seen);
            return;
        }

        if (value is MemberInfo member)
        {
            InspectMember(member, path, owner, endpoint, violations, seen);
            return;
        }

        if (value is Delegate callback)
        {
            InspectDelegate(callback, path, owner, endpoint, violations, seen, depth);
            return;
        }

        if (value is JsonSerializerContext serializerContext)
        {
            InspectType(serializerContext.GetType(), OpenApiLifetimeViolationCategory.SerializerMetadata, path, owner, endpoint, violations, seen);
            RejectSerializerArtifact(serializerContext.GetType(), "serializer context", path, owner, endpoint, violations, seen);
            return;
        }

        if (value is JsonTypeInfo typeInfo)
        {
            InspectType(typeInfo.GetType(), OpenApiLifetimeViolationCategory.SerializerMetadata, path, owner, endpoint, violations, seen);
            if (typeInfo.Type is not null)
                InspectType(typeInfo.Type, OpenApiLifetimeViolationCategory.SerializerMetadata, path + ".Type", owner, endpoint, violations, seen);

            RejectSerializerArtifact(typeInfo.GetType(), "JsonTypeInfo", path, owner, endpoint, violations, seen);
            return;
        }

        var runtimeType = value.GetType();
        var collectibleRuntimeType = FindCollectibleType(runtimeType);
        if (collectibleRuntimeType is not null)
        {
            AddViolation(
                violations,
                seen,
                owner,
                endpoint,
                category switch
                {
                    OpenApiLifetimeViolationCategory.SerializerMetadata => OpenApiLifetimeViolationCategory.SerializerMetadata,
                    OpenApiLifetimeViolationCategory.DelegateOrTransformer => OpenApiLifetimeViolationCategory.DelegateOrTransformer,
                    _ => OpenApiLifetimeViolationCategory.MetadataObject
                },
                path,
                collectibleRuntimeType);
            return;
        }

        if (value is JsonSerializerOptions options)
        {
            RejectSerializerArtifact(options.GetType(), "JsonSerializerOptions", path, owner, endpoint, violations, seen);
            return;
        }

        // Metadata implementations commonly expose arrays/lists of transformers, examples, or
        // serializer descriptors. Bound the walk: this is a validation pass, not an object-graph
        // cache, and no inspected object is returned or stored in the accepted marker.
        if (depth < 3 && value is IEnumerable values && value is not string)
        {
            try
            {
                var itemCount = 0;
                foreach (var item in values)
                {
                    if (itemCount++ == MaximumEnumerableItems)
                    {
                        AddViolation(
                            violations,
                            seen,
                            owner,
                            endpoint,
                            OpenApiLifetimeViolationCategory.UnknownMetadataShape,
                            $"{path} (enumeration exceeds {MaximumEnumerableItems} items)",
                            runtimeType);
                        break;
                    }

                    if (item is not null)
                        InspectValue(item, category, path + "[]", owner, endpoint, violations, seen, depth + 1);
                }
            }
            catch (Exception)
            {
                AddViolation(
                    violations,
                    seen,
                    owner,
                    endpoint,
                    OpenApiLifetimeViolationCategory.UnknownMetadataShape,
                    path + " (enumeration failed)",
                    runtimeType);
            }

            // Enumeration proves only the yielded values. API Explorer retains the enumerable object
            // itself, so inspect its backing state as well before accepting the metadata graph.
            InspectPrivateFields(value, category, path, owner, endpoint, violations, seen, depth);
            return;
        }

        if (IsTerminal(runtimeType))
            return;

        if (depth >= 3)
        {
            AddViolation(
                violations,
                seen,
                owner,
                endpoint,
                OpenApiLifetimeViolationCategory.UnknownMetadataShape,
                path + " (inspection depth exceeded)",
                runtimeType);
            return;
        }

        InspectPublicMembers(value, category, path, owner, endpoint, violations, seen, depth);
    }

    private static void InspectPublicMembers(
        object metadata,
        OpenApiLifetimeViolationCategory fallbackCategory,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen,
        int depth)
    {
        var type = metadata.GetType();
        var collectibleType = FindCollectibleType(type);
        if (collectibleType is not null)
        {
            AddViolation(violations, seen, owner, endpoint, OpenApiLifetimeViolationCategory.MetadataObject, path, collectibleType);
            return;
        }

        if (metadata is IAcceptsMetadata accepts && accepts.RequestType is not null)
            InspectType(accepts.RequestType, OpenApiLifetimeViolationCategory.RequestType, path + ".RequestType", owner, endpoint, violations, seen);

        if (metadata is IProducesResponseTypeMetadata produces && produces.Type is not null)
            InspectType(produces.Type, OpenApiLifetimeViolationCategory.ResponseType, path + ".ResponseType", owner, endpoint, violations, seen);

        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ThenBy(property => property.PropertyType.FullName, StringComparer.Ordinal)
            .ToArray();
        foreach (var property in properties)
        {
            InspectDeclaredType(property.PropertyType, CategoryForMember(property.Name, fallbackCategory), path + "." + property.Name, owner, endpoint, violations, seen);
            object? value;
            try
            {
                value = property.GetValue(metadata);
            }
            catch (Exception)
            {
                AddViolation(
                    violations,
                    seen,
                    owner,
                    endpoint,
                    OpenApiLifetimeViolationCategory.UnknownMetadataShape,
                    path + "." + property.Name + " (getter failed)",
                    type);
                continue;
            }

            if (value is not null)
                InspectValue(value, CategoryForMember(property.Name, fallbackCategory), path + "." + property.Name, owner, endpoint, violations, seen, depth + 1);
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ThenBy(field => field.FieldType.FullName, StringComparer.Ordinal)
            .ToArray();
        foreach (var field in fields)
        {
            InspectDeclaredType(field.FieldType, CategoryForMember(field.Name, fallbackCategory), path + "." + field.Name, owner, endpoint, violations, seen);
            object? value;
            try
            {
                value = field.GetValue(metadata);
            }
            catch (Exception)
            {
                AddViolation(
                    violations,
                    seen,
                    owner,
                    endpoint,
                    OpenApiLifetimeViolationCategory.UnknownMetadataShape,
                    path + "." + field.Name + " (getter failed)",
                    type);
                continue;
            }

            if (value is not null)
                InspectValue(value, CategoryForMember(field.Name, fallbackCategory), path + "." + field.Name, owner, endpoint, violations, seen, depth + 1);
        }

        InspectPrivateFields(metadata, fallbackCategory, path, owner, endpoint, violations, seen, depth);
    }

    private static void InspectPrivateFields(
        object metadata,
        OpenApiLifetimeViolationCategory fallbackCategory,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen,
        int depth)
    {
        var type = metadata.GetType();
        for (var currentType = type; currentType is not null && currentType != typeof(object); currentType = currentType.BaseType)
        {
            var privateFields = currentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !IsPublicPropertyBackingField(currentType, field))
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .ThenBy(field => field.FieldType.FullName, StringComparer.Ordinal)
                .ToArray();
            foreach (var field in privateFields)
            {
                var fieldPath = path + "." + field.Name;
                var fieldCategory = CategoryForMember(field.Name, fallbackCategory);
                InspectDeclaredType(field.FieldType, fieldCategory, fieldPath, owner, endpoint, violations, seen);
                object? value;
                try
                {
                    value = field.GetValue(metadata);
                }
                catch (Exception)
                {
                    AddViolation(
                        violations,
                        seen,
                        owner,
                        endpoint,
                        OpenApiLifetimeViolationCategory.UnknownMetadataShape,
                        fieldPath + " (field read failed)",
                        type);
                    continue;
                }

                if (value is not null)
                    InspectValue(value, fieldCategory, fieldPath, owner, endpoint, violations, seen, depth + 1);
            }
        }
    }

    private static void InspectDeclaredType(
        Type declaredType,
        OpenApiLifetimeViolationCategory category,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen)
    {
        if (declaredType == typeof(object) || declaredType.ContainsGenericParameters)
            return;

        InspectType(declaredType, category, path, owner, endpoint, violations, seen);
    }

    private static void InspectType(
        Type type,
        OpenApiLifetimeViolationCategory category,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen)
    {
        var collectibleType = FindCollectibleType(type);
        if (collectibleType is null)
            return;

        AddViolation(violations, seen, owner, endpoint, category, path, collectibleType);
    }

    private static void InspectMember(
        MemberInfo member,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen)
    {
        var declaringType = member.DeclaringType;
        var assembly = declaringType?.Assembly ?? member.Module.Assembly;
        if (IsCollectible(assembly) || (declaringType is not null && FindCollectibleType(declaringType) is not null))
            AddViolation(violations, seen, owner, endpoint, OpenApiLifetimeViolationCategory.MemberOrMethod, $"{path}: {ArtifactIdentity(member)}", LoadContextIdentity(assembly));

        InspectMemberSignature(member, OpenApiLifetimeViolationCategory.MemberOrMethod, path, owner, endpoint, violations, seen);
    }

    private static void InspectDelegate(
        Delegate callback,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen,
        int depth)
    {
        InspectType(callback.GetType(), OpenApiLifetimeViolationCategory.DelegateOrTransformer, path + ".DelegateType", owner, endpoint, violations, seen);
        var invocationList = callback.GetInvocationList();
        for (var index = 0; index < invocationList.Length; index++)
        {
            var invocation = invocationList[index];
            var invocationPath = $"{path}.Invocation[{index}]";
            var method = invocation.Method;
            var methodIsCollectible = IsCollectible(method.Module.Assembly) ||
                                      (method.DeclaringType is not null && FindCollectibleType(method.DeclaringType) is not null);
            if (methodIsCollectible)
            {
                AddViolation(
                    violations,
                    seen,
                    owner,
                    endpoint,
                    OpenApiLifetimeViolationCategory.DelegateOrTransformer,
                    $"{invocationPath}: {ArtifactIdentity(method)}",
                    LoadContextIdentity(method.Module.Assembly));
            }

            InspectMemberSignature(
                method,
                OpenApiLifetimeViolationCategory.DelegateOrTransformer,
                invocationPath,
                owner,
                endpoint,
                violations,
                seen);

            if (invocation.Target is null)
                continue;

            var targetType = invocation.Target.GetType();
            if (IsCollectible(targetType))
            {
                AddViolation(
                    violations,
                    seen,
                    owner,
                    endpoint,
                    OpenApiLifetimeViolationCategory.DelegateOrTransformer,
                    $"{invocationPath}.Target: {ArtifactIdentity(targetType)}",
                    LoadContextIdentity(targetType.Assembly));
                continue;
            }

            InspectValue(
                invocation.Target,
                OpenApiLifetimeViolationCategory.DelegateOrTransformer,
                invocationPath + ".Target",
                owner,
                endpoint,
                violations,
                seen,
                depth + 1);
        }
    }

    private static void InspectMemberSignature(
        MemberInfo member,
        OpenApiLifetimeViolationCategory category,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen)
    {
        switch (member)
        {
            case MethodInfo method:
                InspectType(method.ReturnType, category, path + ".ReturnType", owner, endpoint, violations, seen);
                foreach (var argument in method.GetGenericArguments())
                    InspectType(argument, category, path + ".GenericArgument", owner, endpoint, violations, seen);
                InspectParameters(method.GetParameters(), category, path, owner, endpoint, violations, seen);
                break;
            case ConstructorInfo constructor:
                InspectParameters(constructor.GetParameters(), category, path, owner, endpoint, violations, seen);
                break;
            case PropertyInfo property:
                InspectType(property.PropertyType, category, path + ".PropertyType", owner, endpoint, violations, seen);
                InspectParameters(property.GetIndexParameters(), category, path, owner, endpoint, violations, seen);
                break;
            case FieldInfo field:
                InspectType(field.FieldType, category, path + ".FieldType", owner, endpoint, violations, seen);
                break;
            case EventInfo @event when @event.EventHandlerType is not null:
                InspectType(@event.EventHandlerType, category, path + ".EventHandlerType", owner, endpoint, violations, seen);
                break;
        }
    }

    private static void InspectParameters(
        IEnumerable<ParameterInfo> parameters,
        OpenApiLifetimeViolationCategory category,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen)
    {
        foreach (var parameter in parameters.OrderBy(parameter => parameter.Position))
            InspectType(parameter.ParameterType, category, $"{path}.Parameter[{parameter.Position}]", owner, endpoint, violations, seen);
    }

    private static Type? FindCollectibleType(Type type)
    {
        if (IsCollectible(type))
            return type;
        if (type.IsArray || type.IsByRef || type.IsPointer)
            return FindCollectibleType(type.GetElementType()!);
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                var collectible = FindCollectibleType(argument);
                if (collectible is not null)
                    return collectible;
            }
        }

        return null;
    }

    private static bool IsCollectible(Type type) => IsCollectible(type.Assembly);

    private static bool IsTerminal(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan) ||
        type == typeof(Guid) ||
        type == typeof(Uri);

    private static bool IsPublicPropertyBackingField(Type declaringType, FieldInfo field)
    {
        var name = field.Name;
        var closingBracket = name.IndexOf('>');
        if (name.Length < 4 || name[0] != '<' || closingBracket <= 1 ||
            !name.EndsWith("k__BackingField", StringComparison.Ordinal))
            return false;

        var propertyName = name[1..closingBracket];
        return declaringType.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) is not null;
    }

    private static void RejectSerializerArtifact(
        Type artifactType,
        string artifactKind,
        string path,
        EndpointOwnershipMetadata owner,
        string endpoint,
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen) =>
        AddViolation(
            violations,
            seen,
            owner,
            endpoint,
            OpenApiLifetimeViolationCategory.SerializerMetadata,
            $"{path}: {artifactKind} objects are not allowed in endpoint metadata ({ArtifactIdentity(artifactType)})",
            LoadContextIdentity(artifactType.Assembly));

    private static bool IsCollectible(Assembly assembly) =>
        assembly.IsCollectible || AssemblyLoadContext.GetLoadContext(assembly)?.IsCollectible == true;

    private static void AddViolation(
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen,
        EndpointOwnershipMetadata owner,
        string endpoint,
        OpenApiLifetimeViolationCategory category,
        string path,
        Type artifact)
    {
        AddViolation(violations, seen, owner, endpoint, category, $"{path}: {ArtifactIdentity(artifact)}", LoadContextIdentity(artifact.Assembly));
    }

    private static void AddViolation(
        ICollection<UnsafeOpenApiMetadataViolation> violations,
        ISet<string> seen,
        EndpointOwnershipMetadata owner,
        string endpoint,
        OpenApiLifetimeViolationCategory category,
        string artifact,
        string loadContext)
    {
        var key = $"{category}|{owner.OwnerId}|{owner.ShellId}|{owner.Generation}|{endpoint}|{artifact}|{loadContext}";
        if (!seen.Add(key))
            return;

        violations.Add(new UnsafeOpenApiMetadataViolation(
            owner.OwnerId,
            owner.ShellId,
            owner.Generation,
            endpoint,
            category,
            artifact,
            loadContext));
    }

    private static OpenApiLifetimeViolationCategory CategoryForMetadata(Type metadataType)
    {
        if (typeof(JsonSerializerContext).IsAssignableFrom(metadataType) || typeof(JsonTypeInfo).IsAssignableFrom(metadataType))
            return OpenApiLifetimeViolationCategory.SerializerMetadata;
        if (typeof(Delegate).IsAssignableFrom(metadataType))
            return OpenApiLifetimeViolationCategory.DelegateOrTransformer;
        if (typeof(MemberInfo).IsAssignableFrom(metadataType) || typeof(Type).IsAssignableFrom(metadataType))
            return OpenApiLifetimeViolationCategory.MemberOrMethod;
        return OpenApiLifetimeViolationCategory.MetadataObject;
    }

    private static OpenApiLifetimeViolationCategory CategoryForMember(string name, OpenApiLifetimeViolationCategory fallback)
    {
        if (name.Contains("serializer", StringComparison.OrdinalIgnoreCase) || name.Contains("json", StringComparison.OrdinalIgnoreCase))
            return OpenApiLifetimeViolationCategory.SerializerMetadata;
        if (name.Contains("request", StringComparison.OrdinalIgnoreCase) || name.Contains("parameter", StringComparison.OrdinalIgnoreCase))
            return OpenApiLifetimeViolationCategory.RequestType;
        if (name.Contains("response", StringComparison.OrdinalIgnoreCase) || name.Contains("result", StringComparison.OrdinalIgnoreCase) || name.Contains("return", StringComparison.OrdinalIgnoreCase))
            return OpenApiLifetimeViolationCategory.ResponseType;
        if (name.Contains("transform", StringComparison.OrdinalIgnoreCase) || name.Contains("delegate", StringComparison.OrdinalIgnoreCase) || name.Contains("callback", StringComparison.OrdinalIgnoreCase))
            return OpenApiLifetimeViolationCategory.DelegateOrTransformer;
        if (name.Contains("member", StringComparison.OrdinalIgnoreCase) || name.Contains("method", StringComparison.OrdinalIgnoreCase))
            return OpenApiLifetimeViolationCategory.MemberOrMethod;
        return fallback;
    }

    private static string ArtifactIdentity(Type type) => type.AssemblyQualifiedName ?? type.FullName ?? type.Name;

    private static string ArtifactIdentity(MemberInfo member)
    {
        var declaringType = member.DeclaringType?.AssemblyQualifiedName ?? member.DeclaringType?.FullName ?? "<global>";
        if (member is not MethodBase method)
            return $"{declaringType}.{member.Name}";

        var genericArguments = method.IsGenericMethod
            ? $"<{string.Join(",", method.GetGenericArguments().Select(ArtifactIdentity))}>"
            : string.Empty;
        var parameters = string.Join(",", method.GetParameters().Select(parameter => ArtifactIdentity(parameter.ParameterType)));
        return $"{declaringType}.{member.Name}{genericArguments}({parameters})";
    }

    private static string LoadContextIdentity(Assembly assembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(assembly);
        if (context is null)
            return assembly.IsCollectible ? "<unnamed> (collectible)" : "<unknown>";
        var name = string.IsNullOrWhiteSpace(context.Name) ? "<unnamed>" : context.Name.Trim();
        return assembly.IsCollectible || context.IsCollectible ? $"{name} (collectible)" : $"{name} (shared)";
    }

    private static string NormalizeEndpoint(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "<unnamed endpoint>" : displayName.Trim();

    private static bool MarkerMatches(OpenApiLifetimeMetadata existing, OpenApiLifetimeMetadata expected) =>
        existing.Owner == expected.Owner &&
        existing.Classification == expected.Classification &&
        existing.Endpoint == expected.Endpoint &&
        existing.CheckedCategories.SequenceEqual(expected.CheckedCategories);
}
