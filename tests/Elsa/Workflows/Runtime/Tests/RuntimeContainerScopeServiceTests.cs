using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Branch coverage for <see cref="RuntimeContainerScopeService"/>: building a descendant's visible
/// container-scope chain by walking ancestor executions and restoring each container execution's
/// persisted value snapshot, and writing mutations back to the owning container state (ADR 0027, #210).
/// </summary>
public sealed class RuntimeContainerScopeServiceTests
{
    private const string WorkflowExecutionId = "wfexec-1";

    private readonly InMemoryActivityExecutionStateStore _store = new();
    private readonly RuntimeContainerScopeService _service;

    public RuntimeContainerScopeServiceTests() => _service = new RuntimeContainerScopeService(_store);

    [Fact]
    public async Task BuildScope_returns_null_when_no_ancestor_declares_variables()
    {
        var executable = Executable(ContainerNode("container", variables: []));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");
        await _store.SaveAsync(NewState("actexec-container", "container", parentActivityExecutionId: null));

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState);

        Assert.Null(scope);
    }

    [Fact]
    public async Task BuildScope_returns_null_when_activity_has_no_parent()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "Counter")]));
        var rootState = NewState("actexec-root", "container", parentActivityExecutionId: null);

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, rootState);

        Assert.Null(scope);
    }

    [Fact]
    public async Task BuildScope_projects_container_variables_and_restores_persisted_values()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "Counter")]));
        await _store.SaveAsync(NewState("actexec-container", "container", parentActivityExecutionId: null,
            scopeValues: new Dictionary<string, object?> { ["var-counter"] = 42 }));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState);

        Assert.NotNull(scope);
        Assert.Equal("container", scope!.ScopeId);
        Assert.Equal("actexec-container", scope.ExecutionId);
        Assert.True(scope.TryGetValue(new VariableReference("var-counter", "container"), out var value));
        Assert.Equal(42, ((JsonElement)value!).GetInt32());
    }

    [Fact]
    public async Task BuildScope_falls_back_to_declared_default_when_no_value_persisted()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "Counter")]));
        await _store.SaveAsync(NewState("actexec-container", "container", parentActivityExecutionId: null));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState);

        Assert.True(scope!.TryGetValue(new VariableReference("var-counter", "container"), out var value));
        Assert.Equal("default", value?.ToString());
    }

    [Fact]
    public async Task BuildScope_stops_when_an_ancestor_state_is_missing()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "Counter")]));
        // Child points at a parent that was never saved.
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-missing");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState);

        Assert.Null(scope);
    }

    [Fact]
    public async Task BuildScope_guards_against_a_parent_cycle()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "Counter")]));
        // Two states referencing each other as parents must not loop forever.
        await _store.SaveAsync(NewState("actexec-a", "container", parentActivityExecutionId: "actexec-b"));
        await _store.SaveAsync(NewState("actexec-b", "container", parentActivityExecutionId: "actexec-a"));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-a");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState);

        Assert.NotNull(scope); // nearest container resolved; cycle did not hang
    }

    [Fact]
    public void CaptureScopeMutation_returns_updated_state_when_values_change()
    {
        var containerState = NewState("actexec-container", "container", parentActivityExecutionId: null);
        var scope = new VariableScope(
            "container",
            VariableMap(("var-counter", "Counter", 0)),
            executionId: "actexec-container");
        scope.TrySetValue(new VariableReference("var-counter", "container"), 99);

        var updated = RuntimeContainerScopeService.CaptureScopeMutation(containerState, scope);

        Assert.NotNull(updated);
        Assert.Contains(RuntimeMetadataKeys.ScopedVariableValues, updated!.Metadata.Keys);
        Assert.Contains("99", updated.Metadata[RuntimeMetadataKeys.ScopedVariableValues]);
    }

    [Fact]
    public void CaptureScopeMutation_returns_null_when_snapshot_is_unchanged()
    {
        var scope = new VariableScope(
            "container",
            VariableMap(("var-counter", "Counter", 0)),
            executionId: "actexec-container");
        var serialized = JsonSerializer.Serialize(scope.SnapshotValues());
        var containerState = NewState("actexec-container", "container", parentActivityExecutionId: null) with
        {
            Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ScopedVariableValues] = serialized }
        };

        Assert.Null(RuntimeContainerScopeService.CaptureScopeMutation(containerState, scope));
    }

    [Fact]
    public async Task PersistScopeMutations_writes_changed_container_snapshots_only()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "Counter")]));
        await _store.SaveAsync(NewState("actexec-container", "container", parentActivityExecutionId: null));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");
        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState);
        scope!.TrySetValue(new VariableReference("var-counter", "container"), 7);

        var firstWrite = await _service.PersistScopeMutationsAsync(scope, WorkflowExecutionId);
        var secondWrite = await _service.PersistScopeMutationsAsync(scope, WorkflowExecutionId);

        Assert.Equal(1, firstWrite);
        Assert.Equal(0, secondWrite); // unchanged the second time

        var persisted = await _store.FindAsync(WorkflowExecutionId, "actexec-container");
        Assert.Contains("7", persisted!.Metadata[RuntimeMetadataKeys.ScopedVariableValues]);
    }

    [Fact]
    public async Task PersistScopeMutations_is_a_noop_for_a_null_scope()
    {
        Assert.Equal(0, await _service.PersistScopeMutationsAsync(null, WorkflowExecutionId));
    }

    private static WorkflowExecutable Executable(ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            publishedAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode ContainerNode(string nodeId, IReadOnlyCollection<(string ReferenceKey, string Name)> variables) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/container",
            activityTypeVersion: "1.0.0",
            descriptorType: "test/descriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot("Children",
                [
                    new ExecutableNode(
                        executableNodeId: "child",
                        authoredActivityId: "authored-child",
                        activityType: "test/probe",
                        activityTypeVersion: "1.0.0",
                        descriptorType: "test/descriptor",
                        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
                        inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                        outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                        metadata: new Dictionary<string, string>())
                ])
            ],
            structure: variables.Count == 0
                ? new ExecutableActivityStructure("test.structure", "1.0.0", JsonSerializer.SerializeToElement(new { activities = Array.Empty<string>() }))
                : new ExecutableActivityStructure("test.structure", "1.0.0", JsonSerializer.SerializeToElement(
                    new
                    {
                        variables = variables.Select(v => new VariableDefinition(
                            v.ReferenceKey, v.Name, TypeInformation.String, null, new ArgumentValue("default", "Literal"))).ToArray()
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static ActivityExecutionState NewState(
        string activityExecutionId,
        string executableNodeId,
        string? parentActivityExecutionId,
        IReadOnlyDictionary<string, object?>? scopeValues = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (scopeValues is not null)
            metadata[RuntimeMetadataKeys.ScopedVariableValues] = JsonSerializer.Serialize(scopeValues);

        return new ActivityExecutionState(
            Execution: new ActivityExecution(activityExecutionId, WorkflowExecutionId, executableNodeId, $"authored-{executableNodeId}", "test/activity", "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: parentActivityExecutionId,
            ParentActivityExecutionId: parentActivityExecutionId,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: metadata);
    }
    private static IReadOnlyDictionary<string, IVariable> VariableMap(params (string Key, string Name, object? Value)[] variables) =>
        variables.ToDictionary(
            v => v.Key,
            v => (IVariable)new InlineVariable(v.Name, v.Key, v.Value),
            StringComparer.Ordinal);

    private sealed class InlineVariable(string name, string id, object? defaultValue) : IVariable
    {
        public string Id { get; set; } = id;
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; } = defaultValue;
        public Type? StorageDriverType { get; set; }

        public IMemoryBlock Declare() => new InlineBlock(DefaultValue);
        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) => DefaultValue is T t ? t : default;
        public T? Get<T>(IExpressionExecutionContext context) => DefaultValue is T t ? t : default;

        private sealed class InlineBlock(object? value) : IMemoryBlock
        {
            public object? Value { get; set; } = value;
            public object? Metadata { get; set; }
        }
    }
}
