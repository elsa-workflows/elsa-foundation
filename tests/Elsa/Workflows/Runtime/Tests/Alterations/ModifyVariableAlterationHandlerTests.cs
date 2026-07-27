using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class ModifyVariableAlterationHandlerTests
{
    [Fact]
    public async Task PreflightAsync_UsesStableKeyCapturedRevisionAndDeclaredProtection()
    {
        var identity = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable(identity));
        var handler = new ModifyVariableAlterationHandler(executableStore);
        var workflow = Workflow(identity, revision: 4);
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(workflow, 4, staging, new { variableKey = "stable-name", value = "replacement" }));

        Assert.True(result.IsAccepted);
        var change = Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges));
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(workflow);
        change.Apply(builder);
        var replacement = builder.WorkflowExecution.RootVariableFrame!.Values["stable-name"];
        Assert.Equal("String", replacement.Type.Alias);
        Assert.Equal(ValueProtectionPolicy.InstanceInline, replacement.Policy);
        Assert.Equal("replacement", replacement.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task PreflightAsync_RejectsMissingOrStaleRootVariableWithoutStaging()
    {
        var identity = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable(identity));
        var handler = new ModifyVariableAlterationHandler(store);
        var staging = new Staging();

        var stale = await handler.PreflightAsync(Context(Workflow(identity, 4), 3, staging, new { variableKey = "stable-name", value = "replacement" }));
        var missing = await handler.PreflightAsync(Context(Workflow(identity, 4), 4, staging, new { variableKey = "unknown", value = "replacement" }));

        Assert.Equal("RootVariableFrameRevisionConflict", stale.Failure!.Code);
        Assert.Equal("VariableNotDeclared", missing.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsOutOfRangeIntegralAndUnsupportedProtectedReplacement()
    {
        var integerIdentity = new WorkflowExecutableIdentity("integer-artifact", "definition", "version", "1", "hash");
        var protectedIdentity = new WorkflowExecutableIdentity("protected-artifact", "definition", "version", "1", "hash");
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable(integerIdentity, new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline));
        await store.SaveAsync(Executable(protectedIdentity, new ValueTypeDescriptor("String"), new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.External)));
        var handler = new ModifyVariableAlterationHandler(store);
        var staging = new Staging();

        var fractional = await handler.PreflightAsync(Context(Workflow(integerIdentity, 1, new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline), 1, staging, new { variableKey = "stable-name", value = 1.5 }));
        var protectedValue = await handler.PreflightAsync(Context(Workflow(protectedIdentity, 1, new ValueTypeDescriptor("String"), new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.External)), 1, staging, new { variableKey = "stable-name", value = "replacement" }));

        Assert.Equal("VariableValueTypeMismatch", fractional.Failure!.Code);
        Assert.Equal("VariableProtectionPolicyUnsupported", protectedValue.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsNormalizedOrShadowedKeysWithoutMutatingTheRootFrame()
    {
        var identity = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable(identity));
        var handler = new ModifyVariableAlterationHandler(store);
        var workflow = Workflow(identity, 2);
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(workflow, 2, staging, new { variableKey = " stable-name", value = "replacement" }));

        Assert.Equal("InvalidModifyVariablePayload", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
        Assert.Equal("before", workflow.RootVariableFrame!.Values["stable-name"].InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task PreflightAsync_AppliesOrderedModificationsAgainstTheCapturedOriginalRevision()
    {
        var identity = new WorkflowExecutableIdentity("artifact-multiple", "definition", "version", "1", "hash");
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(new WorkflowExecutable(
            identity,
            new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            IncidentStrategyBuiltIns.FaultReference,
            workflowVariables:
            [
                new RuntimeVariableDeclaration("first", "First", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline),
                new RuntimeVariableDeclaration("second", "Second", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline)
            ]));
        var workflow = Workflow(identity, 4) with
        {
            RootVariableFrame = new VariableFrameState(
                "root",
                "workflow",
                "workflow",
                null,
                VariableFrameKind.Root,
                new Dictionary<string, ValueEnvelope>
                {
                    ["first"] = ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("before-1"), ValueProtectionPolicy.InstanceInline),
                    ["second"] = ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("before-2"), ValueProtectionPolicy.InstanceInline)
                },
                4)
        };
        var job = new WorkflowAlterationJobState(
            "job",
            "plan",
            workflow.WorkflowExecutionId,
            "tenant",
            0,
            WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue),
            1,
            [],
            null,
            null,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            0,
            new WorkflowAlterationCapturedConcurrency("1", 4, identity));
        var projected = new WorkflowAlterationProjectedState([workflow]);
        var staging = new Staging();
        var handler = new ModifyVariableAlterationHandler(store);

        var first = await handler.PreflightAsync(new WorkflowAlterationPreflightContext(
            job,
            new WorkflowAlterationEnvelope("ModifyVariable", 1, JsonSerializer.SerializeToElement(new { variableKey = "first", value = "after-1" })),
            0,
            projected,
            staging));
        var second = await handler.PreflightAsync(new WorkflowAlterationPreflightContext(
            job,
            new WorkflowAlterationEnvelope("ModifyVariable", 1, JsonSerializer.SerializeToElement(new { variableKey = "second", value = "after-2" })),
            1,
            projected,
            staging));

        Assert.True(first.IsAccepted);
        Assert.True(second.IsAccepted);
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(workflow);
        foreach (var change in staging.StagedChanges.Cast<IWorkflowAlterationRuntimeCheckpointStagedChange>())
            change.Apply(builder);
        Assert.Equal(6, builder.WorkflowExecution.RootVariableFrame!.Revision);
        Assert.Equal("after-1", builder.WorkflowExecution.RootVariableFrame.Values["first"].InlineValue!.Value.GetString());
        Assert.Equal("after-2", builder.WorkflowExecution.RootVariableFrame.Values["second"].InlineValue!.Value.GetString());
    }

    private static WorkflowAlterationPreflightContext Context(WorkflowExecutionState workflow, long capturedRevision, Staging staging, object payload) =>
        new(new WorkflowAlterationJobState("job", "plan", workflow.WorkflowExecutionId, "tenant", 0, WorkflowAlterationJobStatus.Running,
                new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0,
                new Elsa.Workflows.Runtime.Core.Models.Alterations.WorkflowAlterationCapturedConcurrency("1", capturedRevision, workflow.PinnedExecutable)),
            new Elsa.Workflows.Runtime.Core.Models.Alterations.WorkflowAlterationEnvelope("ModifyVariable", 1, JsonSerializer.SerializeToElement(payload)), 0,
            new WorkflowAlterationProjectedState([workflow]), staging);

    private static WorkflowExecutionState Workflow(WorkflowExecutableIdentity identity, long revision, ValueTypeDescriptor? type = null, ValueProtectionPolicy? policy = null) => new("workflow", identity, WorkflowExecutionStatus.Running,
        null, DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch, null, null, null, "tenant", new Dictionary<string, string>())
    {
        RootVariableFrame = new VariableFrameState("root", "workflow", "workflow", null, VariableFrameKind.Root,
            new Dictionary<string, ValueEnvelope> { ["stable-name"] = ValueEnvelope.Inline(type ?? new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("before"), policy ?? ValueProtectionPolicy.InstanceInline) }, revision)
    };

    private static WorkflowExecutable Executable(WorkflowExecutableIdentity identity, ValueTypeDescriptor? variableType = null, ValueProtectionPolicy? policy = null) => new(identity,
        new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), inputContract: null, dependencies: null,
        runtimeRequirements: null, storageDriverRequirements: null, IncidentStrategyBuiltIns.FaultReference,
        workflowVariables: [new RuntimeVariableDeclaration("stable-name", "VisibleName", variableType ?? new ValueTypeDescriptor("String"), policy ?? ValueProtectionPolicy.InstanceInline)]);

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
