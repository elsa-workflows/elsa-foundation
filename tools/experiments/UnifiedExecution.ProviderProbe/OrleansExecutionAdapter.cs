using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Elsa.UnifiedExecution.WorkProof;
using Orleans;
using Orleans.Placement;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService.SiloMetadata;
using Orleans.Runtime.Placement;

/// <summary>Per-grain placement policy for complete workflow executions.</summary>
public sealed class CapabilityPlacementStrategy : PlacementStrategy { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class CapabilityPlacementAttribute : PlacementAttribute
{
    public CapabilityPlacementAttribute() : base(new CapabilityPlacementStrategy()) { }
}

/// <summary>
/// Filters Orleans' current compatible-silo membership candidates using silo metadata.
/// Capability support is declarative metadata; membership remains Orleans' liveness source.
/// </summary>
public sealed class CapabilityPlacementDirector(ISiloMetadataCache metadataCache) : IPlacementDirector
{
    public const string MetadataKey = "elsa.execution-capabilities";

    public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
    {
        if (strategy is not CapabilityPlacementStrategy)
            throw new InvalidOperationException($"Unexpected placement strategy '{strategy.GetType().Name}'.");

        string key = Encoding.UTF8.GetString(target.GrainIdentity.Key.AsSpan());
        int separator = key.IndexOf('|');
        if (separator <= 0)
            throw new InvalidOperationException($"Execution grain key '{key}' does not begin with a capability.");
        string requiredCapability = key[..separator];

        SiloAddress[] compatible = context.GetCompatibleSilos(target);
        SiloAddress[] capable = compatible
            .Where(silo => metadataCache.GetSiloMetadata(silo).Metadata.TryGetValue(MetadataKey, out string? value)
                && value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Contains(requiredCapability, StringComparer.Ordinal))
            .OrderBy(silo => silo.ToString(), StringComparer.Ordinal)
            .ToArray();

        if (capable.Length == 0)
            throw new InvalidOperationException($"No active Orleans silo advertises execution capability '{requiredCapability}'.");

        // A stable choice makes this local failover witness reproducible. Orleans membership
        // removes dead silos from 'compatible'; this director does not track process liveness.
        SiloAddress selected = capable[^1];
        Console.WriteLine($"CAPABILITY_PLACEMENT required={requiredCapability} compatible={compatible.Length} capable={capable.Length} selected={selected}");
        return Task.FromResult(selected);
    }
}

public static class ExecutionGrainKey
{
    public static string Create(WorkScope scope, string executionId, string policyName)
    {
        ValidateScopeIdentity(scope, executionId);
        string capability = CapabilityFor(policyName);
        byte[] identity = SerializeIdentity(capability, scope, executionId);
        string digest = Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
        return $"{capability}|{digest}";
    }

    private static void ValidateScopeIdentity(WorkScope scope, string executionId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        EnsureLineBreakFree(nameof(scope.TenantId), scope.TenantId);
        EnsureLineBreakFree(nameof(scope.PersistenceScope), scope.PersistenceScope);
        EnsureLineBreakFree(nameof(scope.Authority), scope.Authority);
        EnsureLineBreakFree(nameof(executionId), executionId);
    }

    private static void EnsureLineBreakFree(string field, string value)
    {
        if (value.Contains('\n') || value.Contains('\r'))
            throw new ArgumentException($"Canonical execution identity field '{field}' must not contain CR or LF.", field);
    }

    private static byte[] SerializeIdentity(string capability, WorkScope scope, string executionId)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("elsa.execution-grain-key.v1");
        writer.Write(capability);
        writer.Write(scope.TenantId);
        writer.Write(scope.PersistenceScope);
        writer.Write(scope.Authority);
        writer.Write(executionId);
        writer.Flush();
        return stream.ToArray();
    }

    public static string CapabilityFor(string policyName) => policyName switch
    {
        "sequence" => "sequence-v1",
        "flowchart" => "flowchart-v1",
        _ => throw new ArgumentException($"Unsupported proof policy '{policyName}'.", nameof(policyName))
    };
}

public interface IExecutionGrain : IGrainWithStringKey
{
    Task<string> GetLocation();
    Task<string> Start(string scopeJson, string executionId, string policyName, string inputJson);
    Task<string> Read(string scopeJson, string executionId, string policyName);
    Task<string> Pump(string scopeJson, string executionId, string policyName);
    Task<string> ProbeStaleOwner(string scopeJson, string executionId, string policyName, string staleOwner, long staleFence);
}

/// <summary>
/// One actor owns a complete composition. Native policy and durable work semantics stay in
/// WorkProof; this adapter only binds an Orleans activation to an execution owner epoch.
/// </summary>
[CapabilityPlacement]
public sealed class ExecutionGrain(
    DurableWorkStore store,
    HmacWorkScopeAuthorizer authorizer,
    IndependentSink sink) : Grain, IExecutionGrain
{
    private readonly string _owner = $"orleans:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private CompositionCoordinator? _coordinator;
    private WorkScope? _scope;
    private string? _executionId;
    private string? _policyName;

    public Task<string> GetLocation()
        => Task.FromResult(JsonSerializer.Serialize(new { pid = Environment.ProcessId, owner = _owner }));

    public Task<string> Start(string scopeJson, string executionId, string policyName, string inputJson)
    {
        EnsureBound(scopeJson, executionId, policyName);
        CommitReceipt receipt = _coordinator!.Start(inputJson);
        return Task.FromResult(SerializeSnapshot(receipt.CommitId));
    }

    public Task<string> Read(string scopeJson, string executionId, string policyName)
    {
        EnsureBound(scopeJson, executionId, policyName);
        return Task.FromResult(SerializeSnapshot());
    }

    public Task<string> Pump(string scopeJson, string executionId, string policyName)
    {
        EnsureBound(scopeJson, executionId, policyName);
        int results = _coordinator!.ApplyPendingResults();
        int continuations = _coordinator.RunPendingContinuations();
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            resultsApplied = results,
            continuationsRun = continuations,
            snapshot = JsonDocument.Parse(SerializeSnapshot()).RootElement.Clone()
        }));
    }

    public Task<string> ProbeStaleOwner(string scopeJson, string executionId, string policyName, string staleOwner, long staleFence)
    {
        EnsureBound(scopeJson, executionId, policyName);
        ExecutionSnapshot current = store.ReadExecution(_scope!, _executionId!);
        bool rejected;
        string reason;
        try
        {
            store.CommitPlan(_scope!, _executionId!, staleOwner, staleFence, current.Revision,
                $"stale-probe:{Guid.NewGuid():N}",
                new CompositionPlan(current.StateJson, [], [], current.Status == "Completed"));
            rejected = false;
            reason = "commit unexpectedly accepted";
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("Stale coordinator fence", StringComparison.Ordinal))
        {
            rejected = true;
            reason = exception.Message;
        }

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            rejected,
            reason,
            supplied = new { owner = staleOwner, fence = staleFence },
            current = new { current.Owner, current.Fence, current.Revision }
        }));
    }

    private void EnsureBound(string scopeJson, string executionId, string policyName)
    {
        WorkScope scope = JsonSerializer.Deserialize<WorkScope>(scopeJson)
            ?? throw new InvalidDataException("The execution scope was empty.");
        string canonicalKey = ExecutionGrainKey.Create(scope, executionId, policyName);
        // Reject ambiguous scope encodings before the proof HMAC authorizer, whose
        // experiment-only signature format uses newline-delimited fields.
        authorizer.EnsureAuthorized(scope);
        if (!string.Equals(this.GetPrimaryKeyString(), canonicalKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Execution grain identity mismatch: addressed '{this.GetPrimaryKeyString()}', expected canonical key '{canonicalKey}'.");
        if (_coordinator is not null)
        {
            if (_scope != scope || _executionId != executionId || _policyName != policyName)
                throw new InvalidOperationException("An Orleans execution grain cannot be rebound to a different durable execution.");
            return;
        }

        ICompositionPolicy policy = policyName switch
        {
            "sequence" => new SequencePolicy(),
            "flowchart" => new FlowchartPolicy(),
            _ => throw new ArgumentException($"Unsupported proof policy '{policyName}'.", nameof(policyName))
        };
        _scope = scope;
        _executionId = executionId;
        _policyName = policyName;
        try
        {
            ExecutionSnapshot current = store.ReadExecution(scope, executionId);
            if (current.PolicyId != policy.Id)
                throw new InvalidOperationException($"Persisted policy '{current.PolicyId}' does not match requested '{policy.Id}'.");
            store.AcquireExecution(scope, executionId, _owner);
            _coordinator = new CompositionCoordinator(store, scope, executionId, _owner, policy);
            _coordinator.Activate();
        }
        catch (UnauthorizedAccessException exception) when (exception.Message.StartsWith("Execution is absent from the supplied", StringComparison.Ordinal))
        {
            _coordinator = new CompositionCoordinator(store, scope, executionId, _owner, policy);
        }
    }

    private string SerializeSnapshot(string? commitId = null)
    {
        ExecutionSnapshot execution = store.ReadExecution(_scope!, _executionId!);
        CoordinatorSnapshot coordinator = store.ReadCoordinator(_scope!, _executionId!);
        return JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId,
            owner = coordinator.Owner,
            fence = coordinator.Fence,
            revision = coordinator.Revision,
            execution.PolicyId,
            execution.Status,
            execution.StateJson,
            commitId,
            work = store.ReadWork(_scope!, _executionId!).Select(item => new
            {
                item.InvocationId,
                item.HandlerId,
                item.HandlerVersion,
                item.InputJson,
                item.Status,
                item.Attempt
            }),
            pendingContinuations = store.ReadPendingContinuations(_scope!, _executionId!),
            effects = sink.CountEffects(_scope!, _executionId!)
        });
    }
}
