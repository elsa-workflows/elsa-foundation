using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The Runtime owner's converter wire shapes for lossless hash projection; not a second serializer profile.</summary>
public static class RuntimeJsonHashProjection
{
    /// <summary>Projects Runtime-owned contracts through JsonHashInput, preserving opaque UTF-16 identities and raw JSON leaves.</summary>
    public static System.Text.Json.JsonElement Project<T>(T value) => Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(value, For);

    internal static object? For(object value) => value switch
    {
        ActivityExecutionState item => ProjectActivityState(item),
        DurableValueState item => DurableValueStateJsonConverter.HashProjection(item),
        StimulusDeliverySnapshot item => StimulusDeliverySnapshotJsonConverter.HashProjection(item),
        StimulusStartRecipient item => StimulusStartRecipientJsonConverter.HashProjection(item),
        StimulusDeliveryScope item => StimulusDeliveryScopeJsonConverter.HashProjection(item),
        StimulusResumeRecipient item => StimulusResumeRecipientJsonConverter.HashProjection(item),
        StimulusResumeCapture item => StimulusResumeCaptureJsonConverter.HashProjection(item),
        RuntimeChildControlBatch item => RuntimeChildControlBatchJsonConverter.HashProjection(item),
        RuntimeChildControlInbox item => RuntimeChildControlInboxJsonConverter.HashProjection(item),
        WorkflowExecutableStimulusDeliveryAuthority item => WorkflowExecutableStimulusDeliveryAuthorityJsonConverter.HashProjection(item),
        WorkflowDeclaredInput input => input.IsNullable
            ? new { input.Name, input.Type, input.IsRequired, HasDefault = input.DefaultValue.HasValue, input.DefaultValue, input.IsNullable }
            : (object)new { input.Name, input.Type, input.IsRequired, HasDefault = input.DefaultValue.HasValue, input.DefaultValue },
        RuntimeSchedulerWorkItem work => new
        {
            work.WorkItemId, work.WorkflowExecutionId, work.CommandId, work.CommandKind, work.EnvelopeId,
            work.IdempotencyKey, work.EnqueuedAt, work.RecordedAt, work.Sequence,
            HasPayload = work.Payload.HasValue, work.Payload, work.CommandMetadata, work.EnvelopeMetadata,
            work.ExecutionScopeId, work.Attempt
        },
        RuntimePostCommitIntent intent => ProjectIntent(intent),
        _ => null
    };

    private static object ProjectIntent(RuntimePostCommitIntent intent) => intent.HistoryCausation is not null
        ? new
        {
            intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt,
            intent.ActivityExecutionId, intent.IdempotencyKey, HasPayload = intent.Payload.HasValue,
            intent.Payload, intent.Metadata, intent.NotBefore, intent.DependsOnWaitRegistrationId, intent.WaitFailurePolicy,
            intent.HistoryCausation
        }
        : (object)new
        {
            intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt,
            intent.ActivityExecutionId, intent.IdempotencyKey, HasPayload = intent.Payload.HasValue,
            intent.Payload, intent.Metadata, intent.NotBefore, intent.DependsOnWaitRegistrationId, intent.WaitFailurePolicy
        };

    // Only the owner's root metadata has this protocol. A descriptor, payload or arbitrary dictionary
    // containing the same key remains ordinary data, including any JSON-looking string it carries.
    private static JsonElement ProjectActivityState(ActivityExecutionState state)
    {
        var shape = JsonHashInput.ProjectOpaqueClr(state, candidate => ReferenceEquals(candidate, state) ? null : For(candidate));
        var root = JsonNode.Parse(shape.GetRawText(), documentOptions: new() { MaxDepth = 1000 })!.AsObject();
        foreach (var key in new[] { StepWorkflowStateEffects.MetadataKey, StepWorkflowStateEffects.ReceiptsMetadataKey })
            if (state.Metadata.TryGetValue(key, out var raw))
                root["metadata"]![JsonHashInput.OpaqueString(key)] = ParseMetadata(raw, key);
        return JsonHashInput.SerializeToElement(root);
    }

    /// <summary>Projects only executable-owner JSON metadata for a template hash, leaving retained text and data subtrees unchanged.</summary>
    public static JsonElement ProjectExecutable(ExecutableNode node)
    {
        var shape = JsonHashInput.SerializeToElement(node);
        var root = JsonNode.Parse(shape.GetRawText(), documentOptions: new() { MaxDepth = 1000 })!.AsObject();
        Visit(root, node);
        return JsonHashInput.SerializeToElement(root);

        static void Visit(JsonObject current, ExecutableNode source)
        {
            var metadata = current["metadata"]!.AsObject();
            foreach (var key in ExecutableMetadataKeys)
                // Always parse the original text: serializing a CLR string first can replace a lone
                // UTF-16 surrogate, which would make invalid protocol input appear to be valid JSON.
                if (source.Metadata.TryGetValue(key, out var raw)) metadata[key] = ParseMetadata(raw, key);
            var slots = source.ChildSlots.ToArray();
            for (var slot = 0; slot < slots.Length; slot++)
            {
                var children = slots[slot].Activities.ToArray();
                for (var child = 0; child < children.Length; child++)
                    Visit(current["childSlots"]![slot]!["activities"]![child]!.AsObject(), children[child]);
            }
        }
    }

    private static readonly string[] ExecutableMetadataKeys = [StepWorkflowStateEffects.MetadataKey, Constants.RuntimeMetadataKeys.MayWriteVariables];

    /// <summary>Projects the executable's recognized behavioral metadata protocols, excluding unrelated metadata.</summary>
    public static JsonElement ProjectExecutableMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        var result = new JsonObject();
        foreach (var key in ExecutableMetadataKeys)
            if (metadata.TryGetValue(key, out var raw)) result[key] = ParseMetadata(raw, key);
        // These two owner flags are ordinary strings, not nested JSON documents.
        foreach (var key in new[] { "accessesWorkflowState", Constants.RuntimeMetadataKeys.MayWriteEveryVisibleVariable })
            if (metadata.TryGetValue(key, out var raw)) result[key] = raw;
        return JsonHashInput.SerializeToElement(result);
    }

    // Refuse malformed known protocol text. Tolerant inspection readers are deliberately not hash inputs.
    private static JsonNode? ParseMetadata(string raw, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(raw, new() { MaxDepth = 1000 });
            _ = JsonHashInput.CanonicalBytes(document.RootElement);
            return JsonNode.Parse(document.RootElement.GetRawText(), documentOptions: new() { MaxDepth = 1000 });
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new JsonException($"Runtime metadata '{key}' is not valid canonical JSON.");
        }
    }
}
