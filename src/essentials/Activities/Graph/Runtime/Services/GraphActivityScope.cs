using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Runtime.Services;

public delegate ValueTask<object?> GraphActivityExpressionEvaluator(
    GraphActivityExpression expression,
    CancellationToken cancellationToken);

/// <summary>Read-only descendant access to public inputs captured by the nearest graph boundary.</summary>
public interface IGraphActivityInputReader
{
    ValueTask<RuntimeDurableValueReadResult> ReadInputAsync(
        string referenceKey,
        CancellationToken cancellationToken = default);

    ValueTask<object?> GetRequiredInputAsync(
        string referenceKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Side-effect-free durable-value builder for one ordinary graph activity execution scope. The later
/// checkpoint coordinator commits the returned states atomically with scheduling/completion.
/// </summary>
public sealed class GraphActivityScope : IGraphActivityInputReader
{
    private readonly IRuntimeDurableValueStorageDriverRegistry _storageDrivers;
    private readonly Dictionary<string, DurableValueState> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DurableValueState> _variables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DurableValueState> _outputs = new(StringComparer.Ordinal);

    public GraphActivityScope(
        ActivityExecutionScope executionScope,
        IRuntimeDurableValueStorageDriverRegistry storageDrivers)
    {
        ArgumentNullException.ThrowIfNull(executionScope);
        ArgumentNullException.ThrowIfNull(storageDrivers);
        if (!StringComparer.Ordinal.Equals(executionScope.ExecutionScopeId, executionScope.OuterActivityExecutionId))
            throw new ArgumentException("A graph execution scope id must equal its outer activity execution id.", nameof(executionScope));
        if (executionScope.Capabilities.Contains(ActivityExecutionScopeCapability.WorkflowRootMutation) ||
            executionScope.Capabilities.Contains(ActivityExecutionScopeCapability.TriggerEntry))
        {
            throw new ArgumentException("Graph schema 1 cannot expose workflow-root mutation or trigger-entry capabilities.", nameof(executionScope));
        }

        ExecutionScope = executionScope;
        _storageDrivers = storageDrivers;
    }

    public ActivityExecutionScope ExecutionScope { get; }
    public IReadOnlyDictionary<string, DurableValueState> Inputs => ReadOnly(_inputs);
    public IReadOnlyDictionary<string, DurableValueState> Variables => ReadOnly(_variables);
    public IReadOnlyDictionary<string, DurableValueState> Outputs => ReadOnly(_outputs);
    public string BoundaryDurableValueId => GraphActivityScopeKeys.Boundary(ExecutionScope.OuterActivityExecutionId);

    public async ValueTask<IReadOnlyCollection<DurableValueState>> CaptureInputsAsync(
        IReadOnlyList<GraphActivityInputDescriptor> descriptors,
        IReadOnlyDictionary<string, object?> effectiveInputs,
        IReadOnlySet<string> requiredReferenceKeys,
        GraphActivityExpressionEvaluator evaluateExpression,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(effectiveInputs);
        ArgumentNullException.ThrowIfNull(requiredReferenceKeys);
        ArgumentNullException.ThrowIfNull(evaluateExpression);
        if (_inputs.Count != 0)
            throw new InvalidOperationException("Graph public inputs have already been captured and are read-only.");

        var descriptorKeys = descriptors.Select(item => item.ReferenceKey).ToHashSet(StringComparer.Ordinal);
        var unknown = effectiveInputs.Keys.Where(key => !descriptorKeys.Contains(key)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
            throw new InvalidOperationException($"Graph input values contain unknown reference keys: {string.Join(", ", unknown)}.");

        var drivers = ResolveDrivers(descriptors, item => item.ReferenceKey, item => item.StorageDriverKey);
        var staged = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            object? value;
            if (effectiveInputs.TryGetValue(descriptor.ReferenceKey, out var supplied))
            {
                // ContainsKey distinguishes an explicitly supplied null from absence.
                value = supplied;
            }
            else if (descriptor.Default is not null)
            {
                value = await evaluateExpression(descriptor.Default, cancellationToken);
            }
            else if (requiredReferenceKeys.Contains(descriptor.ReferenceKey))
            {
                throw new InvalidOperationException($"Required graph input '{descriptor.ReferenceKey}' has no effective value.");
            }
            else
            {
                continue;
            }

            staged.Add(
                descriptor.ReferenceKey,
                await NewStateAsync(
                    "input",
                    descriptor.ReferenceKey,
                    descriptor.Type,
                    descriptor.StorageDriverKey,
                    drivers[descriptor.ReferenceKey],
                    value,
                    capturedAt,
                    cancellationToken,
                    descriptor.Name ?? descriptor.ReferenceKey));
        }

        Publish(_inputs, staged);
        return _inputs.Values.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<DurableValueState>> InitializeVariablesAsync(
        IReadOnlyList<GraphActivityVariableDescriptor> descriptors,
        GraphActivityExpressionEvaluator evaluateExpression,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(evaluateExpression);
        if (_variables.Count != 0)
            throw new InvalidOperationException("Graph-local variables have already been initialized.");

        var drivers = ResolveDrivers(descriptors, item => item.ReferenceKey, item => item.StorageDriverKey);
        var staged = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = descriptor.InitialValue is null
                ? null
                : await evaluateExpression(descriptor.InitialValue, cancellationToken);
            staged.Add(
                descriptor.ReferenceKey,
                await NewStateAsync(
                    "variable",
                    descriptor.ReferenceKey,
                    descriptor.Type,
                    descriptor.StorageDriverKey,
                    drivers[descriptor.ReferenceKey],
                    value,
                    capturedAt,
                    cancellationToken));
        }

        Publish(_variables, staged);
        return _variables.Values.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<DurableValueState>> CaptureOutputsAsync(
        IReadOnlyList<GraphActivityOutputDescriptor> descriptors,
        IReadOnlyList<GraphActivityOutputMappingDescriptor> mappings,
        IReadOnlySet<string> requiredReferenceKeys,
        GraphActivityExpressionEvaluator evaluateExpression,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(requiredReferenceKeys);
        ArgumentNullException.ThrowIfNull(evaluateExpression);
        if (_outputs.Count != 0)
            throw new InvalidOperationException("Graph outputs have already been captured.");

        var mappingsByKey = mappings.ToDictionary(item => item.OutputReferenceKey, StringComparer.Ordinal);
        var drivers = ResolveDrivers(descriptors, item => item.ReferenceKey, item => item.StorageDriverKey);
        var staged = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mappingsByKey.TryGetValue(descriptor.ReferenceKey, out var mapping))
            {
                if (requiredReferenceKeys.Contains(descriptor.ReferenceKey))
                    throw new InvalidOperationException($"Required graph output '{descriptor.ReferenceKey}' has no boundary mapping.");
                continue;
            }

            var value = await evaluateExpression(mapping.Source, cancellationToken);
            staged.Add(
                descriptor.ReferenceKey,
                await NewStateAsync(
                    "output",
                    descriptor.ReferenceKey,
                    descriptor.Type,
                    descriptor.StorageDriverKey,
                    drivers[descriptor.ReferenceKey],
                    value,
                    capturedAt,
                    cancellationToken));
        }

        Publish(_outputs, staged);
        return _outputs.Values.ToArray();
    }

    public ValueTask<RuntimeDurableValueReadResult> ReadInputAsync(
        string referenceKey,
        CancellationToken cancellationToken = default) =>
        ReadValueAsync(_inputs, referenceKey, cancellationToken);

    public async ValueTask<object?> GetRequiredInputAsync(
        string referenceKey,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadInputAsync(referenceKey, cancellationToken);
        return result.Exists
            ? result.Value
            : throw new KeyNotFoundException($"Graph input '{referenceKey}' is not captured in execution scope '{ExecutionScope.ExecutionScopeId}'.");
    }

    public ValueTask<RuntimeDurableValueReadResult> ReadVariableAsync(
        string referenceKey,
        CancellationToken cancellationToken = default) =>
        ReadValueAsync(_variables, referenceKey, cancellationToken);

    public ValueTask<RuntimeDurableValueReadResult> ReadOutputAsync(
        string referenceKey,
        CancellationToken cancellationToken = default) =>
        ReadValueAsync(_outputs, referenceKey, cancellationToken);

    public async ValueTask<DurableValueState> SetVariableAsync(
        GraphActivityVariableDescriptor descriptor,
        object? value,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateReference(descriptor.ReferenceKey, descriptor.StorageDriverKey);
        var driver = _storageDrivers.GetRequired(descriptor.StorageDriverKey);
        var state = await NewStateAsync(
            "variable",
            descriptor.ReferenceKey,
            descriptor.Type,
            descriptor.StorageDriverKey,
            driver,
            value,
            capturedAt,
            cancellationToken);
        _variables[descriptor.ReferenceKey] = state;
        return state;
    }

    public void Demand(ActivityExecutionScopeCapability capability)
    {
        if (!ExecutionScope.Capabilities.Contains(capability))
            throw new InvalidOperationException($"Execution scope '{ExecutionScope.ExecutionScopeId}' does not permit capability '{capability}'.");
    }

    private async ValueTask<DurableValueState> NewStateAsync(
        string role,
        string referenceKey,
        JsonElement type,
        string storageDriverKey,
        IRuntimeDurableValueStorageDriver storageDriver,
        object? value,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken,
        string? boundaryInputName = null)
    {
        var durableValueId = role switch
        {
            "input" => GraphActivityScopeKeys.Input(ExecutionScope.OuterActivityExecutionId, referenceKey),
            "variable" => GraphActivityScopeKeys.Variable(ExecutionScope.OuterActivityExecutionId, referenceKey),
            "output" => GraphActivityScopeKeys.Output(ExecutionScope.OuterActivityExecutionId, referenceKey),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown graph durable-value role.")
        };

        var typeDescriptor = TypeDescriptor(type, storageDriverKey);
        var encoding = await storageDriver.EncodeAsync(value, typeDescriptor, cancellationToken);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.BoundaryExecutionScopeId] = ExecutionScope.ExecutionScopeId,
            [RuntimeMetadataKeys.BoundaryValueRole] = role,
            [RuntimeMetadataKeys.BoundaryReferenceKey] = referenceKey,
            ["runtime.storageDriverKey"] = storageDriverKey
        };
        if (boundaryInputName is not null)
            metadata[RuntimeMetadataKeys.BoundaryInputName] = boundaryInputName;

        return new DurableValueState(
            durableValueId,
            ExecutionScope.WorkflowExecutionId,
            durableValueId,
            typeDescriptor,
            DurableValueLifecycle.Instance,
            encoding.Storage,
            encoding.InlineValue,
            encoding.ExternalReference,
            sourceActivityExecutionId: ExecutionScope.OuterActivityExecutionId,
            capturedAt,
            metadata);
    }

    private static RuntimeValueTypeDescriptor TypeDescriptor(JsonElement type, string storageDriverKey)
    {
        var alias = type.ValueKind == JsonValueKind.Object && type.TryGetProperty("alias", out var aliasElement)
            ? aliasElement.GetString()
            : null;
        return new RuntimeValueTypeDescriptor(alias ?? "object", storageDriverKey, type.Clone());
    }

    private IReadOnlyDictionary<string, IRuntimeDurableValueStorageDriver> ResolveDrivers<TDescriptor>(
        IReadOnlyList<TDescriptor> descriptors,
        Func<TDescriptor, string> getReferenceKey,
        Func<TDescriptor, string> getStorageDriverKey)
    {
        var resolved = new Dictionary<string, IRuntimeDurableValueStorageDriver>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            var referenceKey = getReferenceKey(descriptor);
            var storageDriverKey = getStorageDriverKey(descriptor);
            ValidateReference(referenceKey, storageDriverKey);
            resolved.Add(referenceKey, _storageDrivers.GetRequired(storageDriverKey));
        }

        return resolved;
    }

    private async ValueTask<RuntimeDurableValueReadResult> ReadValueAsync(
        IReadOnlyDictionary<string, DurableValueState> values,
        string referenceKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);
        if (!values.TryGetValue(referenceKey, out var state))
            return RuntimeDurableValueReadResult.Missing;

        var storageDriverKey = state.Type.Id;
        if (string.IsNullOrWhiteSpace(storageDriverKey))
            throw new InvalidOperationException($"Durable value '{state.DurableValueId}' has no storage-driver key.");
        var value = await _storageDrivers.GetRequired(storageDriverKey).DecodeAsync(state, cancellationToken);
        return new RuntimeDurableValueReadResult(true, value);
    }

    private static void ValidateReference(string referenceKey, string storageDriverKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDriverKey);
    }

    private static IReadOnlyDictionary<string, DurableValueState> ReadOnly(
        IDictionary<string, DurableValueState> values) =>
        new ReadOnlyDictionary<string, DurableValueState>(
            new Dictionary<string, DurableValueState>(values, StringComparer.Ordinal));

    private static void Publish(
        IDictionary<string, DurableValueState> destination,
        IReadOnlyDictionary<string, DurableValueState> staged)
    {
        foreach (var (referenceKey, state) in staged)
            destination.Add(referenceKey, state);
    }

    internal void Rehydrate(IReadOnlyCollection<DurableValueState> persistedStates)
    {
        ArgumentNullException.ThrowIfNull(persistedStates);
        if (_inputs.Count != 0 || _variables.Count != 0 || _outputs.Count != 0)
            throw new InvalidOperationException("An activity execution scope can only be rehydrated once.");

        var stagedInputs = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        var stagedVariables = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        var stagedOutputs = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        foreach (var state in persistedStates)
        {
            if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, ExecutionScope.WorkflowExecutionId) ||
                !StringComparer.Ordinal.Equals(state.SourceActivityExecutionId, ExecutionScope.OuterActivityExecutionId))
            {
                throw new InvalidOperationException($"Durable value '{state.DurableValueId}' does not belong to activity execution scope '{ExecutionScope.ExecutionScopeId}'.");
            }

            if (!state.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryExecutionScopeId, out var scopeId) ||
                !StringComparer.Ordinal.Equals(scopeId, ExecutionScope.ExecutionScopeId) ||
                !state.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryValueRole, out var role) ||
                !state.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryReferenceKey, out var referenceKey))
            {
                throw new InvalidOperationException($"Durable value '{state.DurableValueId}' lacks valid activity execution scope metadata.");
            }

            var expectedId = role switch
            {
                "input" => GraphActivityScopeKeys.Input(ExecutionScope.OuterActivityExecutionId, referenceKey),
                "variable" => GraphActivityScopeKeys.Variable(ExecutionScope.OuterActivityExecutionId, referenceKey),
                "output" => GraphActivityScopeKeys.Output(ExecutionScope.OuterActivityExecutionId, referenceKey),
                _ => throw new InvalidOperationException($"Durable value '{state.DurableValueId}' has unknown activity execution scope role '{role}'.")
            };
            if (!StringComparer.Ordinal.Equals(expectedId, state.DurableValueId))
                throw new InvalidOperationException($"Durable value '{state.DurableValueId}' does not match its activity execution scope key.");

            var storageDriverKey = state.Type.Id;
            if (string.IsNullOrWhiteSpace(storageDriverKey))
                throw new InvalidOperationException($"Durable value '{state.DurableValueId}' has no storage-driver key.");
            _storageDrivers.GetRequired(storageDriverKey);

            var destination = role switch
            {
                "input" => stagedInputs,
                "variable" => stagedVariables,
                "output" => stagedOutputs,
                _ => throw new InvalidOperationException($"Durable value '{state.DurableValueId}' has unknown activity execution scope role '{role}'.")
            };
            destination.Add(referenceKey, state);
        }

        Publish(_inputs, stagedInputs);
        Publish(_variables, stagedVariables);
        Publish(_outputs, stagedOutputs);
    }
}

public sealed class GraphActivityScopeFactory(IRuntimeDurableValueStorageDriverRegistry storageDrivers)
{
    private static readonly IReadOnlySet<ActivityExecutionScopeCapability> SchemaOneCapabilities =
        new HashSet<ActivityExecutionScopeCapability>
        {
            ActivityExecutionScopeCapability.DurableValues,
            ActivityExecutionScopeCapability.Services,
            ActivityExecutionScopeCapability.Tracing,
            ActivityExecutionScopeCapability.Time,
            ActivityExecutionScopeCapability.Cancellation
        };

    public GraphActivityScope Create(
        GraphActivityDescriptor descriptor,
        string workflowExecutionId,
        string outerActivityExecutionId,
        ActivityInvocationOrigin? invocationOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outerActivityExecutionId);

        return new GraphActivityScope(new ActivityExecutionScope(
            outerActivityExecutionId,
            workflowExecutionId,
            outerActivityExecutionId,
            descriptor.DefinitionId,
            descriptor.DefinitionVersionId,
            descriptor.Version,
            descriptor.TemplateHash,
            invocationOrigin ?? ActivityInvocationOrigin.Empty,
            SchemaOneCapabilities), storageDrivers);
    }

    public GraphActivityScope Rehydrate(
        GraphActivityDescriptor descriptor,
        string workflowExecutionId,
        string outerActivityExecutionId,
        IReadOnlyCollection<DurableValueState> persistedStates,
        ActivityInvocationOrigin? invocationOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(persistedStates);
        var declarations = new Dictionary<string, (JsonElement Type, string StorageDriverKey)>(StringComparer.Ordinal);
        foreach (var item in descriptor.Inputs)
            declarations.Add(GraphActivityScopeKeys.Input(outerActivityExecutionId, item.ReferenceKey), (item.Type, item.StorageDriverKey));
        foreach (var item in descriptor.Variables)
            declarations.Add(GraphActivityScopeKeys.Variable(outerActivityExecutionId, item.ReferenceKey), (item.Type, item.StorageDriverKey));
        foreach (var item in descriptor.Outputs)
            declarations.Add(GraphActivityScopeKeys.Output(outerActivityExecutionId, item.ReferenceKey), (item.Type, item.StorageDriverKey));

        var unknown = persistedStates
            .Where(state => !declarations.ContainsKey(state.DurableValueId))
            .Select(state => state.DurableValueId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length != 0)
            throw new InvalidOperationException($"Persisted durable values are not declared by the activity boundary: {string.Join(", ", unknown)}.");

        foreach (var state in persistedStates)
        {
            var declaration = declarations[state.DurableValueId];
            var expectedKind = declaration.Type.ValueKind == JsonValueKind.Object &&
                               declaration.Type.TryGetProperty("alias", out var aliasElement)
                ? aliasElement.GetString() ?? "object"
                : "object";
            var hasMatchingMetadata = state.Metadata.TryGetValue("runtime.storageDriverKey", out var metadataDriverKey) &&
                                      StringComparer.Ordinal.Equals(metadataDriverKey, declaration.StorageDriverKey);
            var hasMatchingType = StringComparer.Ordinal.Equals(state.Type.Id, declaration.StorageDriverKey) &&
                                  StringComparer.Ordinal.Equals(state.Type.Kind, expectedKind) &&
                                  state.Type.Schema is { } schema &&
                                  JsonElement.DeepEquals(schema, declaration.Type);
            if (!hasMatchingMetadata || !hasMatchingType)
            {
                throw new InvalidOperationException(
                    $"Persisted durable value '{state.DurableValueId}' does not match the activity boundary's declared type and storage driver.");
            }
        }

        var scope = Create(descriptor, workflowExecutionId, outerActivityExecutionId, invocationOrigin);
        scope.Rehydrate(persistedStates);
        return scope;
    }

    public GraphActivityScope? FindNearest(
        ActivityExecutionState activityExecution,
        IEnumerable<GraphActivityScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(activityExecution);
        ArgumentNullException.ThrowIfNull(scopes);
        var snapshot = scopes.ToArray();
        var executionId = activityExecution.Execution.ActivityExecutionId;
        var ownScope = snapshot.FirstOrDefault(scope =>
            StringComparer.Ordinal.Equals(scope.ExecutionScope.ExecutionScopeId, executionId));
        if (ownScope is not null)
            return ValidateWorkflow(ownScope, activityExecution.Execution.WorkflowExecutionId);

        var nearestScopeId = activityExecution.Provenance.ExecutionScopeId;
        if (string.IsNullOrWhiteSpace(nearestScopeId))
            return null;

        var nearest = snapshot.FirstOrDefault(scope =>
            StringComparer.Ordinal.Equals(scope.ExecutionScope.ExecutionScopeId, nearestScopeId));
        return nearest is null ? null : ValidateWorkflow(nearest, activityExecution.Execution.WorkflowExecutionId);
    }

    private static GraphActivityScope ValidateWorkflow(GraphActivityScope scope, string workflowExecutionId)
    {
        if (!StringComparer.Ordinal.Equals(scope.ExecutionScope.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException("A graph execution scope cannot cross workflow execution identities.");
        return scope;
    }
}

public static class GraphActivityScopeKeys
{
    public static string Input(string outerActivityExecutionId, string referenceKey) =>
        ActivityExecutionScopeKeys.Input(outerActivityExecutionId, referenceKey);

    public static string Variable(string outerActivityExecutionId, string referenceKey) =>
        ActivityExecutionScopeKeys.Variable(outerActivityExecutionId, referenceKey);

    public static string Output(string outerActivityExecutionId, string referenceKey) =>
        ActivityExecutionScopeKeys.Output(outerActivityExecutionId, referenceKey);

    public static string Boundary(string outerActivityExecutionId)
    {
        return ActivityExecutionScopeKeys.Boundary(outerActivityExecutionId);
    }
}
