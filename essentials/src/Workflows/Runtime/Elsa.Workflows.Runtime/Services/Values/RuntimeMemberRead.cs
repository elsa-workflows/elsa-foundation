using System.Text.Json;
using Elsa.Primitives.Models;
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
    // The type of the source, not of the selected member: the input's type describes the member, so the derived
    // whole-value read is labelled with the neutral Elsa.Any. The built-in resolver uses the type only as the label of
    // the envelope it returns, and the member read never relies on that label.
    private static readonly ValueTypeDescriptor SourceType = new("Elsa.Any");

    public static RuntimeInputBinding WholeValueBinding(RuntimeInputBinding binding) => binding.Source switch
    {
        RuntimeInputBindingSource.WorkflowRequestMember => new(binding.InputName, SourceType, binding.EffectivePolicy,
            RuntimeInputBindingSource.WorkflowRequest, workflowRequest: binding.WorkflowRequest, metadata: binding.Metadata),
        RuntimeInputBindingSource.VariableMember => new(binding.InputName, SourceType, binding.EffectivePolicy,
            RuntimeInputBindingSource.VariableRead, variable: binding.Variable, metadata: binding.Metadata),
        RuntimeInputBindingSource.ActivityResultMember => new(binding.InputName, SourceType, binding.EffectivePolicy,
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

        // An absent or null source has no member to read, whatever its protection, so it is reported as absent or null
        // just as the whole-value read reports it; only a source holding a value is refused for requiring encryption.
        switch (source.Presence)
        {
            case ValuePresence.Absent:
                return (source, new(RuntimeReadSelectionOutcome.Absent, default, -1));
            case ValuePresence.ExplicitNull:
                return (source, RuntimeReadSelector.Select(null, selection.Segments));
        }

        if (source.Policy.RequiresEncryption)
            throw RuntimeReadSelectionException.Unselectable(binding, nodeId,
                "its source requires encryption, and a member is never read out of a value the runtime only holds withheld");

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

        // An external result envelope is the whole result: apply its declared projection first, with the same rule the
        // whole-value read of an external result uses, so only the selection after it is exact.
        if (binding.ActivityResult is { } result && !StringComparer.Ordinal.Equals(result.ProjectionKey, "$result"))
        {
            var path = RuntimeExternalProjection.ResultPath(result, binding.InputName, nodeId, context.Executable);
            payload = RuntimeExternalProjection.Apply(payload, path, binding.InputName, nodeId);
        }

        return (source, RuntimeReadSelector.Select(payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : payload, selection.Segments));
    }
}
