using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services.StepVariables;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services.StepState;

/// <summary>Transient assignments owned by one evaluation; stores are never changed by a setter.</summary>
internal sealed class StepWorkflowState : IStepWorkflowState
{
    private readonly bool _allowed;
    private bool _sealed;
    private readonly IReadOnlyDictionary<string, ValueEnvelope> _outputs;
    private readonly Dictionary<string, JsonElement> _payloads;
    private readonly IExternalPayloadStore? _externalStore;
    private readonly CancellationToken _cancellationToken;
    private readonly StepVariableSession? _variables;
    private readonly IReadOnlyList<ValueProtectionPolicy> _inputs;
    private readonly List<ValueProtectionPolicy> _reads = [];
    private string? _correlationId;
    private string? _instanceName;

    internal StepWorkflowState(bool allowed, string? correlationId, string? instanceName,
        IReadOnlyDictionary<string, ValueEnvelope> outputs, IReadOnlyDictionary<string, JsonElement> payloads,
        StepVariableSession? variables = null, IReadOnlyList<ValueProtectionPolicy>? inputs = null, IExternalPayloadStore? externalStore = null, CancellationToken cancellationToken = default)
    {
        _allowed = allowed;
        _correlationId = correlationId;
        _instanceName = instanceName;
        _outputs = outputs;
        _payloads = new Dictionary<string, JsonElement>(payloads, StringComparer.Ordinal);
        _externalStore = externalStore;
        _cancellationToken = cancellationToken;
        _variables = variables;
        _inputs = inputs ?? [];
    }

    internal Exception? PendingFault { get; private set; }
    internal void Seal() => _sealed = true;
    internal IEnumerable<ValueProtectionPolicy> ReadPolicies => _reads;
    internal Dictionary<string, JsonElement> Assignments { get; } = new(StringComparer.Ordinal);
    internal bool HasCorrelationAssignment { get; private set; }
    internal bool HasNameAssignment { get; private set; }
    internal bool HasEffects => Assignments.Count > 0 || HasCorrelationAssignment || HasNameAssignment;
    internal string? AssignedCorrelation => _correlationId;
    internal string? AssignedName => _instanceName;
    internal IReadOnlyDictionary<string, ValueEnvelope> Outputs => _outputs;
    public bool HasReadProtectedValue => _reads.Any(Protected) || _variables?.HasReadProtectedValue == true;
    internal bool HasReadProtectedOutput => _reads.Any(Protected);

    public string? CorrelationId
    {
        get { EnsureAvailable(); return _correlationId; }
        set { EnsureAvailable(); _correlationId = Normalize(value); HasCorrelationAssignment = true; }
    }

    public string? InstanceName
    {
        get { EnsureAvailable(); return _instanceName; }
        set { EnsureAvailable(); _instanceName = Normalize(value); HasNameAssignment = true; }
    }

    public bool TryGetOutput(string name, out JsonElement value)
    {
        EnsureAvailable();
        ValidateName(name);
        if (_outputs.TryGetValue(name, out var existing))
            _reads.Add(existing.Policy);
        if (Assignments.TryGetValue(name, out var pending))
        {
            value = pending.Clone();
            return true;
        }
        if (existing is null || existing.Presence == ValuePresence.Absent)
        {
            value = default;
            return false;
        }
        if (existing.Presence == ValuePresence.Withheld || existing.Policy.RequiresEncryption)
            throw Refuse("VF-ACT-010: Workflow output is withheld by its protection policy.");
        if (!_payloads.TryGetValue(name, out var payload))
        {
            if (existing.ExternalReference is not { } reference || _externalStore is null)
                throw Refuse("VF-STATE-003: Workflow output payload is unavailable.");
            try
            {
                payload = _externalStore.ReadAsync(reference, _cancellationToken).AsTask().GetAwaiter().GetResult().Clone();
                _payloads[name] = payload;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { throw Refuse("VF-STATE-003: Workflow output payload could not be read."); }
        }
        value = payload.Clone();
        return true;
    }

    public void SetOutput(string name, JsonElement value)
    {
        EnsureAvailable();
        ValidateName(name);
        try
        {
            if (value.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException();
            Assignments[name] = value.Clone();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        { throw Refuse("VF-STATE-001: An output assignment requires an available JSON value."); }
    }

    internal InvalidOperationException Refuse(string message)
    {
        var fault = new InvalidOperationException(message);
        PendingFault ??= fault;
        return fault;
    }

    internal ValueProtectionPolicy SourcePolicy()
    {
        var result = ValueProtectionPolicy.InstanceInline;
        foreach (var policy in _inputs.Concat(_reads).Concat(_variables?.ReadPolicies ?? []))
            result = ValuePolicyCombiner.Combine(result, policy, "Step workflow-state source");
        return result;
    }

    internal void ValidateCompletion()
    {
        if (PendingFault is { } fault) throw fault;
        if (HasEffects && Protected(SourcePolicy()))
            throw Refuse("VF-STATE-003: Protected sources cannot flow into public identity fields or output names.");
    }

    private void EnsureAvailable()
    {
        if (!_allowed || _sealed)
            throw Refuse("VF-STATE-002: Workflow state requires a declared inline leaf body outside closed graph scopes.");
    }

    private void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw Refuse("VF-STATE-001: An output name must be a nonblank string.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static bool Protected(ValueProtectionPolicy policy) => policy.IsSensitive || policy.RequiresEncryption || !string.IsNullOrWhiteSpace(policy.RedactionMode);

    internal static async ValueTask<StepWorkflowState> CreateAsync(IServiceProvider services,
        RuntimeContainerScopeService scopes, IActivity activity, ExecutableNode node,
        ActivityExecutionState invocation, IReadOnlyCollection<DurableValueState> durableValues,
        StepVariableSession? variables, CancellationToken cancellationToken)
    {
        if (activity is IRuntimeStructuralActivity && Elsa.Activities.Runtime.Core.Attributes.AccessesWorkflowStateAttribute.IsAccessedBy(activity.GetType()))
            throw new InvalidOperationException("VF-STATE-002: A structural evaluation cannot declare workflow-state access.");
        var allowed = activity is not IRuntimeStructuralActivity && node.ActivityContract?.AccessesWorkflowState == true &&
                      Elsa.Activities.Runtime.Core.Attributes.AccessesWorkflowStateAttribute.IsAccessedBy(activity.GetType()) &&
                      node.ExecutionPlacement == ActivityExecutionPlacement.Inline;
        if (allowed)
        {
            var frames = await scopes.BuildVisibleFramesAsync(invocation.Execution.WorkflowExecutionId, invocation,
                includeCurrentIteration: true, includeOwnContainer: false, cancellationToken, durableValues);
            allowed = frames.ClosedScopeBoundary is null;
            if (!allowed && (activity is not IStepWorkflowStateConsumer ||
                !node.Metadata.TryGetValue("accessesWorkflowState", out var access) || access != "false"))
                throw new InvalidOperationException("VF-STATE-002: Workflow-state access is unavailable inside a closed reusable graph.");
        }
        var outputs = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);
        var payloads = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (allowed)
        {
            foreach (var row in durableValues.Where(row => row.ValueId.StartsWith(RuntimeWorkflowStateSeed.OutputValueIdPrefix, StringComparison.Ordinal)))
            {
                var name = row.ValueId[RuntimeWorkflowStateSeed.OutputValueIdPrefix.Length..];
                bool Flag(string key) => row.Metadata.TryGetValue(key, out var flag) && bool.TryParse(flag, out var parsed) && parsed;
                var metadata = row.Metadata.Where(pair => pair.Key is not RuntimeMetadataKeys.OutputName and
                    not RuntimeMetadataKeys.IsSensitive and not RuntimeMetadataKeys.RequiresEncryption and
                    not RuntimeMetadataKeys.RedactionMode and not RuntimeMetadataKeys.RetentionPolicy).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                if (row.ExternalReference is { } reference)
                    metadata.TryAdd(ValuePolicyCombiner.StorageProfileMetadataKey, reference.StorageProfile);
                var policy = new ValueProtectionPolicy(row.Lifecycle, row.Storage, Flag(RuntimeMetadataKeys.IsSensitive),
                    Flag(RuntimeMetadataKeys.RequiresEncryption), row.Metadata.GetValueOrDefault(RuntimeMetadataKeys.RedactionMode),
                    row.Metadata.GetValueOrDefault(RuntimeMetadataKeys.RetentionPolicy), metadata);
                var type = new ValueTypeDescriptor(TypeAliasConvention.AnyAlias);
                outputs[name] = row.ExternalReference is { } external
                    ? ValueEnvelope.External(type, external, policy)
                    : row.InlineValue is { } json ? ValueEnvelope.Inline(type, json, policy) : ValueEnvelope.Absent(type, policy);
                if (row.InlineValue is { } inline) payloads[name] = inline.Clone();

            }
        }
        var identity = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
        return new StepWorkflowState(allowed, identity.CorrelationId, identity.InstanceName, outputs, payloads, variables,
            invocation.InputSnapshot?.Values.Values.Select(value => value.Policy).ToArray(), services.GetService<IExternalPayloadStore>(), cancellationToken);
    }
}
