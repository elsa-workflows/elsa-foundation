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

    private readonly DateTimeOffset _now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);
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
    public async Task BuildScope_resolves_loop_iteration_index_as_an_int_typed_value()
    {
        // Regression: ConvertJsonElement's number branch must box an integral JSON value as int (not
        // double), so the loop owner's published index resolves with its integer runtime type and
        // BuildIterationScope's `is int` index check succeeds (otherwise the index silently falls to 0).
        var executable = Executable(ContainerNode("loop", variables: []));
        var bodyState = LoopBodyState(
            iterationId: "loop:iteration:2",
            ownerNodeId: "loop",
            itemName: "currentItem",
            itemValueJson: "\"gamma\"",
            indexName: "currentIndex",
            indexValueJson: "2");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, bodyState);

        Assert.NotNull(scope);
        Assert.True(scope!.TryGetValue(new VariableReference("currentIndex", "loop"), out var index));
        Assert.Equal(2, index);              // value equality
        Assert.IsType<int>(index);           // and integer runtime type — the fix
    }

    [Fact]
    public async Task BuildScope_resolves_an_integer_loop_item_as_an_int_typed_value()
    {
        var executable = Executable(ContainerNode("loop", variables: []));
        var bodyState = LoopBodyState(
            iterationId: "loop:iteration:0",
            ownerNodeId: "loop",
            itemName: "currentItem",
            itemValueJson: "7");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, bodyState);

        Assert.True(scope!.TryGetValue(new VariableReference("currentItem", "loop"), out var item));
        Assert.Equal(7, item);
        Assert.IsType<int>(item);
    }

    [Fact]
    public async Task BuildScope_resolves_a_fractional_loop_item_as_a_double()
    {
        // The fix must not regress genuine fractional numbers: they still resolve as double.
        var executable = Executable(ContainerNode("loop", variables: []));
        var bodyState = LoopBodyState(
            iterationId: "loop:iteration:0",
            ownerNodeId: "loop",
            itemName: "currentItem",
            itemValueJson: "1.5");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, bodyState);

        Assert.True(scope!.TryGetValue(new VariableReference("currentItem", "loop"), out var item));
        Assert.Equal(1.5d, item);
        Assert.IsType<double>(item);
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

    [Fact]
    public void MarkScopeCompleted_sets_the_completed_flag_idempotently()
    {
        var state = NewState("actexec-container", "container", parentActivityExecutionId: null);

        var completed = RuntimeContainerScopeService.MarkScopeCompleted(state);

        Assert.Equal(bool.TrueString, completed.Metadata[RuntimeMetadataKeys.ScopedVariableScopeCompleted]);
        Assert.Same(completed, RuntimeContainerScopeService.MarkScopeCompleted(completed)); // idempotent
    }

    [Fact]
    public async Task BuildScope_builds_a_completed_ancestor_scope_as_non_live()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "Counter")]));
        await _store.SaveAsync(RuntimeContainerScopeService.MarkScopeCompleted(
            NewState("actexec-container", "container", parentActivityExecutionId: null,
                scopeValues: new Dictionary<string, object?> { ["var-counter"] = 42 })));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState);

        Assert.NotNull(scope);
        Assert.True(scope!.IsCompleted);
        // A completed container scope is no longer live for runtime expressions (#210).
        Assert.False(scope.TryGetValue(new VariableReference("var-counter", "container"), out _));
    }

    [Fact]
    public void ReadScopeVariableValues_returns_declared_variables_with_current_or_default_values()
    {
        var container = ContainerNode("container", variables: [("var-counter", "Counter"), ("var-flag", "Flag")]);
        var state = NewState("actexec-container", "container", parentActivityExecutionId: null,
            scopeValues: new Dictionary<string, object?> { ["var-counter"] = 7 });

        var values = _service.ReadScopeVariableValues(container, state);

        Assert.Equal(2, values.Count);
        var counter = Assert.Single(values, value => value.ReferenceKey == "var-counter");
        Assert.Equal("Counter", counter.Name);
        Assert.Equal(7, ((JsonElement)counter.Value!).GetInt32());
        var flag = Assert.Single(values, value => value.ReferenceKey == "var-flag");
        Assert.Equal("default", flag.Value?.ToString()); // falls back to the declared default
    }

    [Fact]
    public void ReadScopeVariableValues_returns_empty_for_a_non_container_node()
    {
        var node = NewState("actexec-x", "leaf", parentActivityExecutionId: null);
        var leaf = ContainerNode("leaf", variables: []);

        Assert.Empty(_service.ReadScopeVariableValues(leaf, node));
    }

    [Fact]
    public async Task BuildScope_seeds_the_root_scope_from_the_workflow_variable_projection_when_supplied()
    {
        // #286: when the caller supplies the current variables.* projection, the root scope draws its values
        // from that projection (mapped name -> reference key), not from the local snapshot, so workflow-scope
        // reads observe prior mutations. The root keeps its node-id identity for structured references.
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "counter")]));
        await _store.SaveAsync(NewState("actexec-container", "container", parentActivityExecutionId: null,
            scopeValues: new Dictionary<string, object?> { ["var-counter"] = 1 }));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");

        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState,
            workflowVariableValues: new Dictionary<string, object?> { ["counter"] = 7 });

        Assert.NotNull(scope);
        Assert.Equal("container", scope!.ScopeId); // node-id identity preserved
        Assert.True(scope.TryGetValue(new VariableReference("var-counter", "container"), out var value));
        Assert.Equal(7, value); // from the projection, not the snapshot value of 1
    }

    [Fact]
    public async Task BuildWorkflowScopeWriteBackChanges_captures_only_changed_root_scope_values_keyed_by_name()
    {
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "counter")]));
        await _store.SaveAsync(NewState("actexec-container", "container", parentActivityExecutionId: null));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");
        var source = new Dictionary<string, object?> { ["counter"] = 1 };
        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState, workflowVariableValues: source);
        // A descendant assigns the workflow variable by name through the visible chain.
        scope!.TrySetValueByName("counter", 9);

        var changes = _service.BuildWorkflowScopeWriteBackChanges(scope, WorkflowExecutionId, rootNodeId: "container", source, _now);

        var change = Assert.Single(changes);
        Assert.Equal("counter", change.State!.Metadata[RuntimeMetadataKeys.VariableName]);
        Assert.Equal(9, change.State.InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task BuildWorkflowScopeWriteBackChanges_is_empty_when_no_value_changed()
    {
        // Dirty-tracking guard (#286): a read-only activity over a workflow that declares variables leaves the
        // scope at its sourced values, so the write-back emits nothing even though the scope holds values.
        var executable = Executable(ContainerNode("container", variables: [("var-counter", "counter")]));
        await _store.SaveAsync(NewState("actexec-container", "container", parentActivityExecutionId: null));
        var childState = NewState("actexec-child", "child", parentActivityExecutionId: "actexec-container");
        var source = new Dictionary<string, object?> { ["counter"] = 1 };
        var scope = await _service.BuildScopeAsync(executable, WorkflowExecutionId, childState, workflowVariableValues: source);

        Assert.Empty(_service.BuildWorkflowScopeWriteBackChanges(scope, WorkflowExecutionId, rootNodeId: "container", source, _now));
    }

    [Fact]
    public void BuildWorkflowScopeWriteBackChanges_is_empty_when_no_root_scope_is_present()
    {
        var scope = new VariableScope("container", VariableMap(("var-counter", "counter", 0)), executionId: "actexec-container");

        Assert.Empty(_service.BuildWorkflowScopeWriteBackChanges(scope, WorkflowExecutionId, rootNodeId: "root-elsewhere",
            new Dictionary<string, object?>(), _now));
    }

    private static WorkflowExecutable Executable(ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
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
                            v.ReferenceKey, v.Name, new TypeReference("String"), null, new ArgumentValue("default", "Literal"))).ToArray()
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
    // A loop body's execution state: no parent container scope, but an IterationId plus the loop owner's
    // per-pass iteration variables published in its scheduling-provenance metadata (the keys
    // RuntimeContainerScopeService reads to layer the per-iteration scope — ADR 0028 / #259).
    private static ActivityExecutionState LoopBodyState(
        string iterationId,
        string ownerNodeId,
        string itemName,
        string itemValueJson,
        string? indexName = null,
        string? indexValueJson = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.LoopIterationOwnerNodeId] = ownerNodeId,
            [RuntimeMetadataKeys.LoopIterationItemName] = itemName,
            [RuntimeMetadataKeys.LoopIterationItemValue] = itemValueJson
        };

        if (indexName is not null)
        {
            metadata[RuntimeMetadataKeys.LoopIterationIndexName] = indexName;
            metadata[RuntimeMetadataKeys.LoopIterationIndexValue] = indexValueJson ?? "0";
        }

        var provenance = ActivitySchedulingProvenance.From(
            WorkflowExecutionId,
            parentActivityExecutionId: null,
            schedulingActivityExecutionId: null,
            branchId: null,
            iterationId: iterationId,
            executionPathId: null,
            executionScopeId: null,
            schedulingCause: null,
            metadata: metadata);

        return new ActivityExecutionState(
            Execution: new ActivityExecution("actexec-body", WorkflowExecutionId, "body", "authored-body", "test/activity", "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 0,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: iterationId,
            Provenance: provenance,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
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
