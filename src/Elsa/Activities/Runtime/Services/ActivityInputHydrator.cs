using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Hydrates annotated plain CLR properties exactly once from the committed invocation snapshot.
/// </summary>
public sealed class ActivityInputHydrator
{
    private readonly ConditionalWeakTable<IActivity, object> _hydrated = new();
    private readonly NullabilityInfoContext _nullability = new();

    public void Hydrate(IActivity activity, ActivityContract contract, ActivityInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(snapshot);
        BeginHydration(activity);

        if (!StringComparer.Ordinal.Equals(contract.SchemaFingerprint, snapshot.ContractFingerprint))
            throw new InvalidOperationException("The pinned activity input snapshot does not match the activation contract fingerprint.");

        var properties = DiscoverProperties(activity.GetType());
        foreach (var unknownKey in snapshot.Values.Keys.Except(contract.Inputs.Keys, StringComparer.Ordinal))
            throw new InvalidOperationException($"Pinned input '{unknownKey}' is not declared by activity contract '{contract.ActivityTypeKey}'.");

        foreach (var input in contract.Inputs.Values)
        {
            if (!properties.TryGetValue(input.Key, out var property))
                throw new InvalidOperationException($"Activity '{activity.GetType().FullName}' has no annotated input property for stable key '{input.Key}'.");

            if (!snapshot.Values.TryGetValue(input.Key, out var envelope))
                throw new InvalidOperationException($"Activity input '{input.Key}' is missing from the complete pinned snapshot.");

            property.SetValue(activity, Materialize(property, input, envelope));
        }
    }

    private void BeginHydration(IActivity activity)
    {
        if (!_hydrated.TryAdd(activity, new object()))
            throw new InvalidOperationException($"Activity instance '{activity.GetType().FullName}' has already been hydrated.");
    }

    private static Dictionary<string, PropertyInfo> DiscoverProperties(Type activityType)
    {
        var result = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in activityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = ActivityInputPropertyResolver.FindAttribute(property);
            if (attribute is null)
                continue;

            var key = attribute.Key ?? property.Name;
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (property.SetMethod is null || !property.SetMethod.IsPublic)
                throw new InvalidOperationException($"Activity input property '{activityType.FullName}.{property.Name}' must have a public setter.");
            if (!result.TryAdd(key, property))
                throw new InvalidOperationException($"Activity '{activityType.FullName}' declares duplicate input key '{key}'.");
        }

        return result;
    }

    private object? Materialize(
        PropertyInfo property,
        ActivityInputContract input,
        ValueEnvelope envelope)
    {
        if (envelope.Presence == ValuePresence.Absent)
        {
            if (input.IsRequired || input.IsNullable is false || !AcceptsNull(property))
                throw new InvalidOperationException($"Activity input '{input.Key}' does not accept absence.");
            return null;
        }

        if (envelope.Presence == ValuePresence.ExplicitNull)
        {
            if (input.IsNullable is false || !AcceptsNull(property))
                throw new InvalidOperationException($"Activity input '{input.Key}' does not accept null.");
            return null;
        }

        if (envelope.ExternalReference is not null)
            throw new InvalidOperationException($"Activity input '{input.Key}' must be dereferenced before CLR property hydration.");

        var inline = envelope.InlineValue
            ?? throw new InvalidOperationException($"Activity input '{input.Key}' has no inline payload.");
        try
        {
            return inline.Deserialize(property.PropertyType);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException($"Activity input '{input.Key}' cannot be materialized as '{property.PropertyType.FullName}'.", exception);
        }
    }

    private bool AcceptsNull(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null ||
        !property.PropertyType.IsValueType &&
        _nullability.Create(property).WriteState is not NullabilityState.NotNull;
}
