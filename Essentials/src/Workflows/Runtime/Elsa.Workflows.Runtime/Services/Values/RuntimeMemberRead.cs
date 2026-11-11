using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Reads the source of a member read (#4078) and selects its member. The source is resolved exactly as the whole-value
/// read of the same role is, through the (replaceable) binding resolver, so role, causal, scope and projection
/// resolution and their failures are unchanged; the resolver never sees a member source. The selection is then
/// applied to the resolved value.
/// </summary>
/// <remarks>
/// A member is never exposed beyond what the whole value would be. A withheld source is refused as the whole-value
/// read refuses it; a source whose protection requires encryption, or a live resource, cannot be selected from at all,
/// because the runtime only ever holds those without reading them; and the selected member keeps the source's
/// protection, combined with the binding's. An externally stored source is read whole from its payload store, as an
/// external projection is today, so its size is bounded by that store; only the selected member is carried on.
/// </remarks>
internal static class RuntimeMemberRead
{
    public static RuntimeInputBinding WholeValueBinding(RuntimeInputBinding binding) => binding.Source switch
    {
        RuntimeInputBindingSource.WorkflowRequestMember => new(binding.InputName, binding.TargetType, binding.EffectivePolicy,
            RuntimeInputBindingSource.WorkflowRequest, workflowRequest: binding.WorkflowRequest, metadata: binding.Metadata),
        RuntimeInputBindingSource.VariableMember => new(binding.InputName, binding.TargetType, binding.EffectivePolicy,
            RuntimeInputBindingSource.VariableRead, variable: binding.Variable, metadata: binding.Metadata),
        RuntimeInputBindingSource.ActivityResultMember => new(binding.InputName, binding.TargetType, binding.EffectivePolicy,
            RuntimeInputBindingSource.ActivityResult, activityResult: binding.ActivityResult, metadata: binding.Metadata),
        _ => throw new ArgumentOutOfRangeException(nameof(binding), binding.Source, "Only a member source has a whole-value read to select from.")
    };

    public static async ValueTask<(ValueEnvelope Source, RuntimeReadSelectionResult Selected)> ReadAsync(
        RuntimeInputBinding binding,
        string nodeId,
        IRuntimeInputBindingResolver resolver,
        RuntimeInputBindingResolutionContext context,
        IExternalPayloadStore? externalPayloadStore,
        CancellationToken cancellationToken)
    {
        var selection = binding.Selection ?? throw new ArgumentException("A member read carries a read selection.", nameof(binding));
        var source = resolver.Resolve(WholeValueBinding(binding), context).Envelope
            ?? throw new InvalidOperationException($"VF-ACT-005: Member input '{binding.InputName}' on executable node '{nodeId}' was resolved without its source envelope.");

        if (source.Presence == ValuePresence.Withheld)
            throw SecretBindingDiagnostics.WithheldBindingNotResolved(binding);
        if (source.TransientResource is not null || source.Policy.Lifecycle == DurableValueLifecycle.None)
            throw RuntimeReadSelectionException.Unselectable(binding, nodeId, "its source is a live resource, not a value with members");
        if (source.Policy.RequiresEncryption)
            throw RuntimeReadSelectionException.Unselectable(binding, nodeId,
                "its source requires encryption, and a member is never read out of a value the runtime only holds withheld");

        switch (source.Presence)
        {
            case ValuePresence.Absent:
                return (source, new(RuntimeReadSelectionOutcome.Absent, default, -1));
            case ValuePresence.ExplicitNull:
                return (source, RuntimeReadSelector.Select(null, selection.Segments));
        }

        if (source.InlineValue is { } inline)
            return (source, RuntimeReadSelector.Select(inline, selection.Segments));

        var reference = source.ExternalReference
            ?? throw new InvalidOperationException($"VF-ACT-005: Member input '{binding.InputName}' on executable node '{nodeId}' has no readable source payload.");
        if (externalPayloadStore is null)
            throw new InvalidOperationException($"VF-ACT-005: Member input '{binding.InputName}' on executable node '{nodeId}' requires an IExternalPayloadStore to read its source.");
        JsonElement payload;
        try
        {
            payload = await externalPayloadStore.ReadAsync(reference, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"VF-ACT-005: External source payload for member input '{binding.InputName}' on executable node '{nodeId}' could not be read from storage profile '{reference.StorageProfile}'.",
                exception);
        }

        // An external result envelope is the whole result: apply its declared projection with the existing rule first.
        if (binding.ActivityResult is { } result && !StringComparer.Ordinal.Equals(result.ProjectionKey, "$result"))
            payload = Project(binding, nodeId, result, context, payload);

        return (source, RuntimeReadSelector.Select(payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : payload, selection.Segments));
    }

    // The existing whole-value projection rule for an external result (RuntimeActivityInputMaterializer's external
    // projection): the dotted path, with case-insensitive fallbacks. It is kept as is so a member read projects exactly
    // as the whole read of the same external result does; only the selection after it is exact.
    private static JsonElement Project(RuntimeInputBinding binding, string nodeId, RuntimeActivityResultReference result,
        RuntimeInputBindingResolutionContext context, JsonElement payload)
    {
        var path = context.Executable?.NodesById.GetValueOrDefault(result.ProducerExecutableNodeId)?.ActivityContract?.Result.Projections
                       .GetValueOrDefault(result.ProjectionKey)?.Path
                   ?? throw new InvalidOperationException($"Producer node '{result.ProducerExecutableNodeId}' has no result projection '{result.ProjectionKey}'.");
        if (StringComparer.Ordinal.Equals(path, "$"))
            return payload;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (payload.ValueKind != JsonValueKind.Object || !TryGetPropertyIgnoringCase(payload, segment, out payload))
                throw new InvalidOperationException($"VF-ACT-004: External source path '{path}' for input '{binding.InputName}' on executable node '{nodeId}' is unavailable.");
        }

        return payload;
    }

    private static bool TryGetPropertyIgnoringCase(JsonElement value, string name, out JsonElement property)
    {
        if (value.TryGetProperty(name, out property))
            return true;
        foreach (var candidate in value.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(candidate.Name, name))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
