using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Restores and preserves protection minima independently of a durable row's physical payload location.</summary>
public static class DurableValuePolicies
{
    /// <summary>Reads policy metadata; only a payloadless JSON null may retain a non-inline storage minimum.</summary>
    public static ValueProtectionPolicy Read(DurableValueState row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var storage = row.Storage;
        if (storage == DurableValueStorage.Inline && row.ExternalReference is null && row.InlineValue is { ValueKind: JsonValueKind.Null })
            storage = row.Metadata.GetValueOrDefault(RuntimeMetadataKeys.NullMinimumStorage) switch
            {
                nameof(DurableValueStorage.External) => DurableValueStorage.External,
                nameof(DurableValueStorage.Custom) => DurableValueStorage.Custom,
                _ => storage
            };
        var metadata = row.Metadata.Where(pair => !pair.Key.StartsWith("runtime.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (row.ExternalReference is { } reference) metadata.TryAdd(ValuePolicyCombiner.StorageProfileMetadataKey, reference.StorageProfile);
        bool Flag(string key) => bool.TryParse(row.Metadata.GetValueOrDefault(key), out var flag) && flag;
        return new(row.Lifecycle, storage, Flag(RuntimeMetadataKeys.IsSensitive), Flag(RuntimeMetadataKeys.RequiresEncryption),
            row.Metadata.GetValueOrDefault(RuntimeMetadataKeys.RedactionMode), row.Metadata.GetValueOrDefault(RuntimeMetadataKeys.RetentionPolicy), metadata);
    }

    /// <summary>Strengthens a produced row or refuses a payload that its physical encoding cannot protect.</summary>
    public static DurableValueState ApplyMinimum(DurableValueState row, ValueProtectionPolicy minimum, string target)
    {
        ArgumentNullException.ThrowIfNull(row);
        var isNull = row.ExternalReference is null && row.InlineValue is { ValueKind: JsonValueKind.Null };
        var current = Read(row);
        // A physical inline null has no driver payload; an explicit Custom minimum is its logical policy.
        if (isNull && current.Storage == DurableValueStorage.Inline && minimum.Storage == DurableValueStorage.Custom)
            current = new(current.Lifecycle, minimum.Storage, current.IsSensitive, current.RequiresEncryption,
                current.RedactionMode, current.RetentionPolicy, current.Metadata);
        var policy = ValuePolicyCombiner.Combine(current, minimum, target);
        if (!isNull && policy.RequiresEncryption)
            throw new InvalidOperationException($"VF-ACT-005: {target} cannot persist a value that requires encryption.");
        if (!isNull && policy.Storage != row.Storage)
            throw new InvalidOperationException($"VF-ACT-005: {target} cannot weaken its storage minimum from '{policy.Storage}' to '{row.Storage}'.");
        var metadata = new Dictionary<string, string>(row.Metadata, StringComparer.Ordinal);
        foreach (var pair in policy.Metadata) if (!pair.Key.StartsWith("runtime.", StringComparison.Ordinal)) metadata[pair.Key] = pair.Value;
        metadata.Remove(RuntimeMetadataKeys.NullMinimumStorage);
        if (isNull && row.Storage == DurableValueStorage.Inline && policy.Storage != DurableValueStorage.Inline)
            metadata[RuntimeMetadataKeys.NullMinimumStorage] = policy.Storage.ToString();
        if (policy.IsSensitive) metadata[RuntimeMetadataKeys.IsSensitive] = bool.TrueString;
        if (policy.RequiresEncryption) metadata[RuntimeMetadataKeys.RequiresEncryption] = bool.TrueString;
        if (policy.RedactionMode is not null) metadata[RuntimeMetadataKeys.RedactionMode] = policy.RedactionMode;
        if (policy.RetentionPolicy is not null) metadata[RuntimeMetadataKeys.RetentionPolicy] = policy.RetentionPolicy;
        return new(row.DurableValueId, row.WorkflowExecutionId, row.ValueId, row.Type, policy.Lifecycle, row.Storage,
            row.InlineValue, row.ExternalReference, row.SourceActivityExecutionId, row.CapturedAt, RuntimeModelMetadata.Snapshot(metadata));
    }
}
