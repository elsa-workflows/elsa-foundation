using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Scaffolding shared by <see cref="RuntimeResumeExecutionCarrierTests"/> and
/// <see cref="RuntimeParentCompletionExecutionCarrierTests"/>. Both drive a real scheduler handler over in-memory
/// stores to lock the ADR-0030 execution-time expression carrier; this file holds the members that were
/// byte-identical across the two (shared constants, the pinned identity, the workflow-execution state, the
/// durable-value seed helpers, and the small pass-through test doubles). File-specific scaffolding (executables,
/// nodes, activity-execution states, work items, and the observing activities) stays in each test file.
/// </summary>
internal static class ExecutionCarrierTestData
{
    public const string WorkflowExecutionId = "wfexec-1";
    public const string VariableName = "greeting";

    public static readonly DateTimeOffset Now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    // Artifact version "7.0.0" so the definition-version accessor resolves the major (7).
    public static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-7", "7.0.0", "sha256:test");

    public static WorkflowExecutionState NewWorkflowState(DateTimeOffset now, string variableValue = "seed")
    {
        var type = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var policy = ValueProtectionPolicy.InstanceInline;
        var rootFrame = new VariableFrameFactory().CreateRoot(
            WorkflowExecutionId,
            VariableReference.WorkflowScopeId,
            new Dictionary<string, ValueEnvelope>
            {
                ["var-greeting"] = ValueEnvelope.Inline(
                    type,
                    JsonSerializer.SerializeToElement(variableValue),
                    policy)
            });

        return new(
            WorkflowExecutionId: WorkflowExecutionId,
            PinnedExecutable: NewIdentity(),
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: now.AddMinutes(-5),
            StartedAt: now.AddMinutes(-5),
            UpdatedAt: now.AddMinutes(-1),
            CompletedAt: null,
            CorrelationId: "corr-1",
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string> { [RuntimeMetadataKeys.InstanceName] = "Instance A" })
        {
            RootVariableFrame = rootFrame
        };
    }

    public static Task SeedInputAsync(InMemoryDurableValueStateStore store, DateTimeOffset now, string name, string value) =>
        SeedDurableValueAsync(store, now, $"input:{name}", RuntimeMetadataKeys.InputName, name, value);

    public static Task SeedVariableAsync(InMemoryDurableValueStateStore store, DateTimeOffset now, string value) =>
        SeedDurableValueAsync(store, now, $"variable:{VariableName}", RuntimeMetadataKeys.VariableName, VariableName, value);

    public static Task SeedOutputAsync(InMemoryDurableValueStateStore store, DateTimeOffset now, string name, string value) =>
        SeedDurableValueAsync(store, now, $"output:{name}", RuntimeMetadataKeys.OutputName, name, value);

    // Seeds the workflow identity (correlation id / instance name) as IdentityName-tagged durable values, the
    // channel the execution-time carrier projects identity from (spec 083 review). Mirrors
    // RuntimeWorkflowStateSeed.BuildIdentityChanges so the handlers' RuntimeIdentityStateProjection surfaces these.
    public static async Task SeedIdentityAsync(InMemoryDurableValueStateStore store, DateTimeOffset now, string correlationId, string instanceName)
    {
        await SeedDurableValueAsync(store, now, $"{RuntimeWorkflowStateSeed.IdentityValueIdPrefix}{RuntimeWorkflowStateSeed.IdentityCorrelationIdName}", RuntimeMetadataKeys.IdentityName, RuntimeWorkflowStateSeed.IdentityCorrelationIdName, correlationId);
        await SeedDurableValueAsync(store, now, $"{RuntimeWorkflowStateSeed.IdentityValueIdPrefix}{RuntimeWorkflowStateSeed.IdentityInstanceNameName}", RuntimeMetadataKeys.IdentityName, RuntimeWorkflowStateSeed.IdentityInstanceNameName, instanceName);
    }

    public static async Task SeedDurableValueAsync(
        InMemoryDurableValueStateStore store,
        DateTimeOffset now,
        string valueId,
        string nameMetadataKey,
        string name,
        string value) =>
        await store.SaveAsync(new DurableValueState(
            durableValueId: $"durable-{valueId}",
            workflowExecutionId: WorkflowExecutionId,
            valueId: valueId,
            type: new RuntimeValueTypeDescriptor("clr", typeof(string).FullName, null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonSerializer.SerializeToElement(value),
            externalReference: null,
            sourceActivityExecutionId: null,
            capturedAt: now,
            metadata: new Dictionary<string, string> { [nameMetadataKey] = name }));

    public static string? AsString(object? value) => value is JsonElement json ? json.GetString() : value?.ToString();
}

/// <summary>A pass-through <see cref="IActivityFactory"/> that always returns the supplied activity instance.</summary>
internal sealed class RecordingActivityFactory(IActivity activity) : IActivityFactory
{
    public ValueTask<IActivity> Create(
        string descriptorType,
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(activity);
}

/// <summary>A <see cref="TimeProvider"/> pinned to a fixed instant.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
