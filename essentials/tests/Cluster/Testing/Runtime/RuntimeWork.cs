using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Cluster.Testing.Runtime;

/// <summary>Executables and commands for runtime conformance and placement tests.</summary>
public static class RuntimeWork
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    /// <summary>An executable whose single node is activated by <paramref name="consumerKey"/> at
    /// <paramref name="schemaVersion"/>, needing <paramref name="storageDrivers"/> as well.</summary>
    public static WorkflowExecutable Needing(string artifactId, string consumerKey, string schemaVersion, params string[] storageDrivers) =>
        Executable(
            artifactId,
            new ExecutableNode(
                "node-root",
                "activity-root",
                "Acme.Root",
                "1.0.0",
                new RuntimeActivityDescriptor(consumerKey, schemaVersion, JsonSerializer.SerializeToElement(new { type = consumerKey })),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>()),
            storageDrivers);

    /// <summary>An executable whose single node is the CLR activity with type alias <paramref name="typeAlias"/>.</summary>
    public static WorkflowExecutable NeedingActivityType(string artifactId, string typeAlias) =>
        Executable(
            artifactId,
            new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: typeAlias,
                activityTypeVersion: "1.0.0",
                descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
                descriptorPayload: JsonSerializer.SerializeToElement(new ClrActivityDescriptor(typeAlias)),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)),
            []);

    /// <summary>The start command of <paramref name="workflowExecutionId"/>, pinned to <paramref name="executable"/>.</summary>
    public static WorkflowExecutionCommandEnvelope Start(WorkflowExecutable executable, string workflowExecutionId, DateTimeOffset now, string envelopeId) =>
        Envelope(
            workflowExecutionId,
            WorkflowExecutionCommandKind.Start,
            JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(
                executable.Identity,
                executable.Identity.ArtifactId,
                variables: null,
                inputs: null,
                stimulusInput: null,
                triggerNodeId: null,
                WorkflowRunKind.PublishedRun,
                pinnedSource: null,
                parentWorkflowExecutionId: null,
                correlationId: null,
                tenantId: null,
                partition: null,
                authority: null,
                startAuthority: null,
                dispatchNestingDepth: 0)),
            now,
            envelopeId);

    /// <summary>A scheduler-work command for <paramref name="workflowExecutionId"/>, as a re-drive sends it.</summary>
    public static WorkflowExecutionCommandEnvelope Work(string workflowExecutionId, DateTimeOffset now, string envelopeId) =>
        Envelope(workflowExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, payload: null, now, envelopeId);

    /// <summary>The durable state of a running execution pinned to <paramref name="executable"/>.</summary>
    public static WorkflowExecutionState Running(string workflowExecutionId, WorkflowExecutable executable, DateTimeOffset now) =>
        new(
            workflowExecutionId,
            executable.Identity,
            WorkflowExecutionStatus.Running,
            null,
            now,
            now,
            now,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>());

    private static WorkflowExecutable Executable(string artifactId, ExecutableNode root, IReadOnlyCollection<string> storageDrivers) =>
        new(
            new WorkflowExecutableIdentity(artifactId, $"definition-{artifactId}", $"version-{artifactId}", "1.0.0", $"sha256:{artifactId}"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            CreatedAt,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: storageDrivers.Select(driver => new RuntimeStorageDriverRequirement(driver)).ToArray(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static WorkflowExecutionCommandEnvelope Envelope(
        string workflowExecutionId,
        WorkflowExecutionCommandKind kind,
        JsonElement? payload,
        DateTimeOffset now,
        string envelopeId) =>
        new(
            envelopeId: envelopeId,
            workflowExecutionId: workflowExecutionId,
            command: new WorkflowExecutionCommand($"command-{envelopeId}", workflowExecutionId, kind, now, payload, new Dictionary<string, string>()),
            idempotencyKey: $"idempotency-{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now);
}
