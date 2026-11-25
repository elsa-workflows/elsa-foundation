using System.Text.Json;
using Elsa.Activities.Runtime.Services.StepVariables;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Activities.Runtime.Services.StepState;

/// <summary>Prepares the state and variable changes as one immutable checkpoint candidate.</summary>
internal static class StepStateCommit
{
    internal static async ValueTask<StepVariableCommitResult> PrepareAsync(StepWorkflowState? session,
        StepVariableCommitResult writes, IWorkflowExecutionStateStore workflows, IWorkflowStateChangePreparer? preparer,
        string executionId, ActivityExecutionState invocation, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (session is null || writes.Failure is not null || writes.LostUpdateFault is not null)
            return writes;
        session.ValidateCompletion();
        if (!session.HasEffects)
            return writes;
        var changes = new List<RuntimeStateChange<DurableValueState>>();
        var source = session.SourcePolicy();
        foreach (var (name, json) in session.Assignments)
        {
            var policy = source;
            if (session.Outputs.TryGetValue(name, out var destination))
                policy = ValuePolicyCombiner.Combine(policy, destination.Policy, "Step workflow output");
            var type = new ValueTypeDescriptor(TypeAliasConvention.AnyAlias);
            var value = json.ValueKind == JsonValueKind.Null ? ValueEnvelope.Null(type, policy) : ValueEnvelope.Inline(type, json, policy);
            if (preparer is null)
                throw new InvalidOperationException("VF-STATE-003: Workflow output encoding is unavailable.");
            changes.Add(await preparer.PrepareOutputAsync(executionId, invocation.InvocationId, name, value, occurredAt, cancellationToken));
        }

        var workflow = writes.Workflow;
        if (session.HasCorrelationAssignment || session.HasNameAssignment)
        {
            workflow ??= await workflows.FindAsync(executionId, cancellationToken)
                ?? throw new InvalidOperationException("VF-STATE-002: Workflow working state is unavailable.");
            var metadata = workflow.SystemMetadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (session.HasNameAssignment)
            {
                if (session.AssignedName is null) metadata.Remove(RuntimeMetadataKeys.InstanceName);
                else metadata[RuntimeMetadataKeys.InstanceName] = session.AssignedName;
            }
            workflow = workflow with
            {
                CorrelationId = session.HasCorrelationAssignment ? session.AssignedCorrelation : workflow.CorrelationId,
                SystemMetadata = RuntimeModelMetadata.Snapshot(metadata)
            };
            changes.AddRange(RuntimeWorkflowStateSeed.BuildIdentityChanges(executionId, session.HasCorrelationAssignment,
                session.AssignedCorrelation, session.HasNameAssignment, session.AssignedName, occurredAt));
        }
        var kinds = new List<string>();
        if (session.Assignments.Count > 0) kinds.Add("outputs");
        if (session.HasCorrelationAssignment) kinds.Add("correlationId");
        if (session.HasNameAssignment) kinds.Add("instanceName");
        var accepted = writes.Apply(invocation);
        var attribution = accepted.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        attribution[StepWorkflowStateEffects.MetadataKey] = JsonSerializer.Serialize(StepWorkflowStateEffects.Read(attribution).Concat(kinds).Distinct(StringComparer.Ordinal), Elsa.Serialization.Core.JsonBaseline.Options);
        attribution[StepWorkflowStateEffects.ReceiptsMetadataKey] = StepWorkflowStateEffects.WriteReceipts(
            StepWorkflowStateEffects.ReadReceipts(attribution).Append(new StepWorkflowStateEffectReceipt(
                invocation.Attempts?.LastOrDefault()?.AttemptId, occurredAt,
                invocation.Status == ActivityExecutionStatus.Suspended ? "Suspension" : "Completion", kinds.ToArray())));
        return writes with { Workflow = workflow, StateValues = changes, HasStateEffects = true,
            Invocation = accepted with { Metadata = RuntimeModelMetadata.Snapshot(attribution) } };
    }

    internal static ActivityExecutionState DiscloseCaptureOverlap(ActivityExecutionState state,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> writes,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> captures)
    {
        if (!writes.Select(change => change.StateId).Intersect(captures.Select(change => change.StateId), StringComparer.Ordinal).Any())
            return state;
        return state.WithWarnings([new Elsa.Expressions.Core.Models.ExecutionWarning("VF-STATE-004",
            "A completion capture replaced a body assignment at the same workflow-state address. The capture was accepted last.")]);
    }
}
