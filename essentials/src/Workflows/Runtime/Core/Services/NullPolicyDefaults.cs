using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>The supported type defaults for omitted members and explicit default-on-null substitution.</summary>
public static class NullPolicyDefaults
{
    /// <summary>Enforces a declared target's null contract on a workflow write, preserving its protection policy.</summary>
    public static ValueEnvelope NormalizeNull(ValueEnvelope value, bool acceptsNull, WorkflowValuePolicies policies,
        string target, IWellKnownTypeRegistry? registry = null, IExecutionWarningSink? warnings = null)
    {
        if (value.Presence != ValuePresence.ExplicitNull || acceptsNull)
            return value;
        object? typeDefault = null;
        var supported = value.Type.CollectionKind == CollectionKind.Single &&
            (registry is not null && registry.TryGetTypeOrDefault(value.Type.Alias, out var clrType)
                ? TryGetTypeDefault(clrType, out typeDefault)
                : TryGetTypeDefault(value.Type.Alias, out typeDefault));
        if (!policies.DefaultOnNull || !supported)
            throw new InvalidOperationException($"VF-ACT-004: {target} does not accept null.");
        warnings?.Report(new ExecutionWarning("VF-NULL-001", $"{target} used its type default because DefaultOnNull is enabled.", ExecutionWarningSeverity.Information));
        return ValueEnvelope.Inline(value.Type, JsonSerializer.SerializeToElement(typeDefault, typeDefault!.GetType()), value.Policy);
    }

    /// <summary>Type-derived nullability of an authored member, including JSON kinds and collections.</summary>
    public static bool AcceptsNull(TypeReference type, IWellKnownTypeRegistry? registry = null) =>
        AcceptsNull(type, registry, descriptors: null);

    /// <summary>Uses the activated type descriptor's null contract when one is contributed.</summary>
    public static bool AcceptsNull(TypeReference type, IWellKnownTypeRegistry? registry,
        IVariableTypeDescriptorCatalog? descriptors)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.CollectionKind != CollectionKind.Single)
            return true;
        var canonicalAlias = registry?.TryGetTypeOrDefault(type.Alias, out var resolvedType) == true
            ? registry.GetAliasOrDefault(resolvedType) : type.Alias;
        if (descriptors?.GetDescriptors().FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Alias, type.Alias) ||
                StringComparer.OrdinalIgnoreCase.Equals(item.Alias, canonicalAlias)) is { } descriptor)
            return descriptor.SupportsNull;
        if (type.CollectionKind != CollectionKind.Single || TypeAliasConvention.IsAny(type.Alias) ||
            type.Alias is "String" or "System.String" or "JsonElement" or "System.Text.Json.JsonElement" or "JsonNode" or "System.Text.Json.Nodes.JsonNode")
            return true;
        if (TryGetTypeDefault(type.Alias, out _) || type.Alias is "DateTime" or "System.DateTime" or "DateTimeOffset" or "System.DateTimeOffset" or
            "DateOnly" or "System.DateOnly" or "TimeOnly" or "System.TimeOnly" or "TimeSpan" or "System.TimeSpan" or "Guid" or "System.Guid")
            return false;
        return registry is not null && registry.TryGetTypeOrDefault(type.Alias, out var clrType) &&
               (!clrType.IsValueType || Nullable.GetUnderlyingType(clrType) is not null);
    }

    /// <summary>Uses the independently dispatched runtime null contract, without resolving a design picker catalog.</summary>
    public static bool AcceptsNull(TypeReference type, IWellKnownTypeRegistry? registry,
        IRuntimeTypeNullability? nullability)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.CollectionKind != CollectionKind.Single) return true;
        if (nullability?.TryGetSupportsNull(type.Alias, out var supportsNull) == true) return supportsNull;
        return AcceptsNull(type, registry);
    }

    /// <summary>Resolves contributed scalar aliases, including enum zero, through the activated type registry.</summary>
    public static bool TryGetTypeDefault(string alias, IWellKnownTypeRegistry? registry, out object? value) =>
        registry is not null && registry.TryGetTypeOrDefault(alias, out var type)
            ? TryGetTypeDefault(type, out value) : TryGetTypeDefault(alias, out value);

    /// <summary>Finds a default for a built-in scalar alias. Reference, JSON, date and duration types have none.</summary>
    public static bool TryGetTypeDefault(string alias, out object? value)
    {
        value = alias switch
        {
            "Boolean" or "System.Boolean" => false,
            "Byte" or "System.Byte" => (byte)0,
            "SByte" or "System.SByte" => (sbyte)0,
            "Int16" or "System.Int16" => (short)0,
            "UInt16" or "System.UInt16" => (ushort)0,
            "Int32" or "System.Int32" => 0,
            "UInt32" or "System.UInt32" => 0u,
            "Int64" or "System.Int64" => 0L,
            "UInt64" or "System.UInt64" => 0UL,
            "Single" or "System.Single" => 0f,
            "Double" or "System.Double" => 0d,
            "Decimal" or "System.Decimal" => 0m,
            _ => null
        };
        return value is not null;
    }

    /// <summary>Finds a default for a Boolean, numeric primitive or enum. Nullable and collection types have none.</summary>
    public static bool TryGetTypeDefault(Type type, out object? value)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsEnum)
        {
            value = Enum.ToObject(type, 0);
            return true;
        }
        return TryGetTypeDefault(type.FullName ?? type.Name, out value);
    }
}
