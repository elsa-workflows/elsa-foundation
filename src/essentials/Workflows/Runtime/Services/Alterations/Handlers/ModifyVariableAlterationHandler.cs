using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>Trusted <c>ModifyVariable/1</c> implementation for one declared root-frame variable.</summary>
public sealed class ModifyVariableAlterationHandler(IWorkflowExecutableStore executableStore) : IWorkflowAlterationPreflightHandler
{
    private readonly IWorkflowExecutableStore _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));

    public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryReadPayload(context.Envelope.Payload, out var variableKey, out var replacement))
            return WorkflowAlterationPreflightResult.Rejected("InvalidModifyVariablePayload", "ModifyVariable requires a stable variable key and replacement value.");

        var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
        var rootFrame = workflow.RootVariableFrame;
        if (rootFrame is null || rootFrame.Kind != VariableFrameKind.Root)
            return WorkflowAlterationPreflightResult.Rejected("RootVariableFrameUnavailable", "The target workflow has no writable root variable frame.");

        var originalWorkflow = context.ProjectedState.GetRequiredOriginal<WorkflowExecutionState>();
        var capturedRevision = context.Job.CapturedConcurrency?.RootVariableFrameRevision;
        if (capturedRevision is null || capturedRevision.Value != originalWorkflow.RootVariableFrame?.Revision)
            return WorkflowAlterationPreflightResult.Rejected("RootVariableFrameRevisionConflict", "The target root variable frame changed after target capture.");

        var capturedExecutable = context.Job.CapturedConcurrency?.PinnedExecutable;
        if (capturedExecutable is not null && !Equals(capturedExecutable, workflow.PinnedExecutable))
            return WorkflowAlterationPreflightResult.Rejected("PinnedExecutableConflict", "The target workflow executable changed after target capture.");

        var executable = await _executableStore.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || !Equals(executable.Identity, workflow.PinnedExecutable))
            return WorkflowAlterationPreflightResult.Rejected("PinnedExecutableUnavailable", "The target workflow executable is unavailable.");
        var declaration = executable.WorkflowVariables.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.VariableKey, variableKey));
        if (declaration is null || !rootFrame.Values.ContainsKey(variableKey))
            return WorkflowAlterationPreflightResult.Rejected("VariableNotDeclared", "The requested root variable is not declared by the target executable.");
        if (!SupportsInlineReplacement(declaration.Policy))
            return WorkflowAlterationPreflightResult.Rejected("VariableProtectionPolicyUnsupported", "The declared variable protection policy cannot accept an inline operator replacement.");
        if (!IsCompatible(replacement, declaration.Type))
            return WorkflowAlterationPreflightResult.Rejected("VariableValueTypeMismatch", "The replacement value does not match the declared variable contract.");

        var value = replacement.ValueKind == JsonValueKind.Null
            ? ValueEnvelope.Null(declaration.Type, declaration.Policy)
            : ValueEnvelope.Inline(declaration.Type, replacement, declaration.Policy);
        VariableFrameState updatedFrame;
        try
        {
            updatedFrame = rootFrame.Set(variableKey, value, rootFrame.Revision);
        }
        catch (InvalidOperationException)
        {
            return WorkflowAlterationPreflightResult.Rejected("RootVariableFrameRevisionConflict", "The target root variable frame is no longer writable at the captured revision.");
        }

        var updatedWorkflow = workflow with { RootVariableFrame = updatedFrame };
        context.ProjectedState.Set(updatedWorkflow);
        context.Staging.Stage(new ModifyRootVariableStagedChange(context.Ordinal, variableKey, updatedWorkflow));
        return WorkflowAlterationPreflightResult.Accepted();
    }

    private static bool TryReadPayload(JsonElement payload, out string variableKey, out JsonElement replacement)
    {
        variableKey = string.Empty;
        replacement = default;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("variableKey", out var key) || key.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("value", out replacement) ||
            payload.EnumerateObject().Any(property => property.Name is not "variableKey" and not "value"))
        {
            return false;
        }

        variableKey = key.GetString() ?? string.Empty;
        // Variable keys are durable identities, not display labels.  Do not silently normalize a submitted key into
        // a different stable reference (for example, " key" into "key").
        return !string.IsNullOrWhiteSpace(variableKey) &&
               StringComparer.Ordinal.Equals(variableKey, variableKey.Trim()) &&
               replacement.ValueKind != JsonValueKind.Undefined;
    }

    private static bool SupportsInlineReplacement(ValueProtectionPolicy policy) =>
        policy.Storage == DurableValueStorage.Inline && !policy.RequiresEncryption;

    private static bool IsCompatible(JsonElement value, Elsa.Primitives.Models.ValueTypeDescriptor type)
    {
        if (value.ValueKind == JsonValueKind.Null || StringComparer.Ordinal.Equals(type.Alias, "Elsa.Any"))
            return true;
        if (type.CollectionKind != Elsa.Primitives.Models.CollectionKind.Single)
            return value.ValueKind == JsonValueKind.Array;

        return type.Alias switch
        {
            "String" or "System.String" => value.ValueKind == JsonValueKind.String,
            "Boolean" or "System.Boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "Byte" or "System.Byte" => value.ValueKind == JsonValueKind.Number && value.TryGetByte(out _),
            "SByte" or "System.SByte" => value.ValueKind == JsonValueKind.Number && value.TryGetSByte(out _),
            "Int16" or "System.Int16" => value.ValueKind == JsonValueKind.Number && value.TryGetInt16(out _),
            "UInt16" or "System.UInt16" => value.ValueKind == JsonValueKind.Number && value.TryGetUInt16(out _),
            "Int32" or "System.Int32" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "UInt32" or "System.UInt32" => value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out _),
            "Int64" or "System.Int64" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "UInt64" or "System.UInt64" => value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out _),
            "Single" or "System.Single" => value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var single) && float.IsFinite(single),
            "Double" or "System.Double" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var @double) && double.IsFinite(@double),
            "Decimal" or "System.Decimal" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            _ => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
        };
    }

    private sealed record ModifyRootVariableStagedChange(
        int Ordinal,
        string VariableKey,
        WorkflowExecutionState Workflow) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => $"runtime.alteration.modify-root-variable:{Ordinal}:{VariableKey}";

        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.SetWorkflowExecution(Workflow);
        }
    }
}
