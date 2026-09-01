using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Reconciliation.Models;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// SC-B-003's cross-engine proof: a closure made by the production publishing walk and codec is enough for a
/// fresh runtime-only engine to activate and execute a parent DispatchWorkflow plus its retained child dependency.
/// </summary>
public sealed class WorkflowArtifactExportImportExecutionTests
{
    private const string ParentNodeId = "parent-dispatch";
    private const string ChildNodeId = "child-probe";
    private const string ParentDefinitionId = "definition-parent";
    private const string ChildDefinitionId = "definition-child";
    private const string ParentExecutionId = "execution-parent";

    [Fact]
    public async Task Production_export_closure_imports_into_a_fresh_runtime_and_executes_parent_and_child()
    {
        var exportEngine = new WorkflowArtifactExportFixture();
        var child = WorkflowExecutionHarness.NewExecutable(
            PortableClrNode(WorkflowExecutionHarness.NewProbeNode(ChildNodeId)),
            new WorkflowExecutableIdentity(
                ArtifactId: "artifact-child-placeholder",
                DefinitionId: ChildDefinitionId,
                DefinitionVersionId: $"{ChildDefinitionId}:1.0.0",
                ArtifactVersion: "1.0.0",
                ArtifactHash: "sha256:placeholder"));

        // The test harness's NewExecutable is only a convenient way to get the real pinned Probe contract. Rebuild
        // its identity through the same production hasher before it enters the export store.
        child = Reidentify(child);
        var childReference = await exportEngine.PublishAsync(child);

        var parentNode = NewDispatchNode(child.Identity, childReference);
        var parent = WorkflowArtifactExportFixture.Executable(
            ParentDefinitionId,
            parentNode,
            artifactVersion: "1.0.0",
            dependencies: WorkflowArtifactExportFixture.DependencyOn(child, ParentNodeId));
        parent = WithCompletionResumeTarget(parent, parentNode);
        await exportEngine.SaveArtifactAsync(parent);
        var parentReference = await exportEngine.AddReferenceAsync(parent, WorkflowExecutableReferenceScope.Published);

        var closure = await exportEngine.CreateFactory().CreateAsync(parentReference.DefinitionVersionId);
        Assert.Equal(parent.Identity.ArtifactId, closure.RootArtifactId);
        var expectedArtifactIds = new[] { child.Identity.ArtifactId, parent.Identity.ArtifactId }
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            expectedArtifactIds,
            closure.Artifacts.Select(artifact => artifact.Identity.ArtifactId).Order(StringComparer.Ordinal));

        // The production download target is the export boundary. Its bytes are then mounted exactly as a runtime
        // host would mount a downloaded closure; B does not receive the in-memory closure object.
        await using var codecHost = WorkflowExecutionHarness.Create().Build("export-codec");
        var delivery = await new DownloadWorkflowArtifactExportTarget(
                codecHost.Services.GetRequiredService<IWorkflowArtifactClosureSerializer>())
            .DeliverAsync(closure);
        Assert.Equal(WorkflowArtifactExportDeliveryKind.InlinePayload, delivery.Kind);
        Assert.NotNull(delivery.Payload);

        var mount = Path.Combine(Path.GetTempPath(), "elsa-export-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mount);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(mount, "parent-closure.json"), delivery.Payload!.Value.ToArray());

            await using var runtime = BuildRuntimeOnlyEngine(mount);
            runtime.InitializeActivityTypes();
            await using var scope = runtime.Services.CreateAsyncScope();
            var reconciliation = await scope.ServiceProvider
                .GetRequiredService<IWorkflowArtifactReconciler>()
                .ReconcileAsync();

            Assert.Equal(2, reconciliation.Entries.Count);
            Assert.All(reconciliation.Entries, entry =>
                Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome));

            var importedParent = Assert.Single(reconciliation.Entries, entry =>
                StringComparer.Ordinal.Equals(entry.ArtifactId, parent.Identity.ArtifactId));
            var importedParentReference = await runtime.Services
                .GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
                .FindAsync(WorkflowActivationReferenceIdentity.Create(importedParent.ActivationId!));
            Assert.NotNull(importedParentReference);

            await runtime.StartPublishedAsync(importedParentReference!, ParentExecutionId);
            await runtime.SweepUntilQuietAsync();

            var parentRun = await runtime.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Completed, parentRun.WorkflowState?.Status);
            var dispatchState = parentRun.AssertOutcomes(ParentNodeId, DispatchWorkflowOutcomes.Completed);

            var childExecutionId = new WorkflowDispatchIdentity(
                    ParentExecutionId,
                    dispatchState.Execution.ActivityExecutionId)
                .ChildWorkflowExecutionId;
            var childRun = await runtime.ReadRunAsync(childExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Completed, childRun.WorkflowState?.Status);
            childRun.AssertCompleted(ChildNodeId);
        }
        finally
        {
            if (Directory.Exists(mount))
                Directory.Delete(mount, recursive: true);
        }
    }

    private static WorkflowExecutionHarness BuildRuntimeOnlyEngine(string mount) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = "fresh-runtime-mount",
                    FolderPath = mount
                }
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            })
            .Build(
                "parent-dispatch-execution",
                "child-probe-execution",
                "child-start-execution",
                "parent-resume-execution");

    private static ExecutableNode NewDispatchNode(
        WorkflowExecutableIdentity childIdentity,
        WorkflowExecutableSourceReference childReference)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(DispatchWorkflow));
        var inputBindings = ClrActivityContractTestBuilder.CompleteInputBindings(
            contract,
            new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
            {
                [nameof(DispatchWorkflow.WorkflowDefinitionId)] = Literal(
                    contract,
                    nameof(DispatchWorkflow.WorkflowDefinitionId),
                    childIdentity.DefinitionId),
                [nameof(DispatchWorkflow.WaitForCompletion)] = Literal(
                    contract,
                    nameof(DispatchWorkflow.WaitForCompletion),
                    true)
            });
        var pin = new DispatchWorkflowPin(
            childIdentity,
            WorkflowExecutableSourceProvenance.From(childReference));

        return new ExecutableNode(
            executableNodeId: ParentNodeId,
            authoredActivityId: $"authored-{ParentNodeId}",
            activityType: typeof(DispatchWorkflow).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                contract.DescriptorPayload),
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(
                    pin,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            },
            activityContract: contract);
    }

    private static ExecutableNode PortableClrNode(ExecutableNode node) =>
        new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                node.DescriptorPayload),
            node.InputBindings,
            node.OutputCaptures,
            node.Metadata,
            node.ChildSlots,
            node.Structure,
            node.ActivityContract,
            node.IntrinsicKind,
            node.IntrinsicVariable);

    private static RuntimeInputBinding Literal(ActivityContract contract, string key, object value)
    {
        var type = contract.Inputs[key].Type;
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            key,
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), policy));
    }

    private static WorkflowExecutable WithCompletionResumeTarget(
        WorkflowExecutable executable,
        ExecutableNode dispatchNode)
    {
        var localTargetId = DispatchWorkflowConstants.CompletionResumeTargetId;
        var scopedTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(
            dispatchNode.ExecutableNodeId,
            localTargetId);
        var targets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
        {
            [scopedTargetId] = new(
                scopedTargetId,
                dispatchNode.ExecutableNodeId,
                "OnChildCompletedAsync",
                new Dictionary<string, string>(StringComparer.Ordinal),
                localTargetId)
        };

        return new WorkflowExecutable(
            executable.Identity,
            executable.RootActivity,
            targets,
            executable.CreatedAt,
            executable.CompatibilityMetadata,
            executable.InputContract,
            executable.Dependencies,
            executable.IncidentStrategy);
    }

    private static WorkflowExecutable Reidentify(WorkflowExecutable executable)
    {
        var inputContract = executable.InputContract
            ?? new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);
        var hash = new WorkflowExecutableHasher().ComputeHash(
            executable.RootActivity,
            inputContract,
            executable.Dependencies,
            executable.CheckpointCadence,
            executable.WorkflowVariables,
            executable.IncidentStrategy);
        var identity = new WorkflowExecutableIdentity(
            new WorkflowExecutableHasher().CreateArtifactId("artifact-", hash),
            executable.Identity.DefinitionId,
            executable.Identity.DefinitionVersionId,
            executable.Identity.ArtifactVersion,
            hash);

        return new WorkflowExecutable(
            identity,
            executable.RootActivity,
            executable.ResumeTargets,
            executable.CreatedAt,
            executable.CompatibilityMetadata,
            inputContract,
            executable.Dependencies,
            executable.RuntimeRequirements,
            executable.StorageDriverRequirements,
            executable.IncidentStrategy,
            executable.CheckpointCadence,
            executable.WorkflowVariables);
    }
}
