using System.Text.Json;
using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;

namespace Kernel.Native;

public sealed record NativeOutbound(Contract.ActorRef Actor, string Frame, string Node, string IterationPath, int Attempt,
    string EffectKey, OutboundEffect Effect);

/// <summary>The Native execution model hosted through the public workflow turn.</summary>
public sealed class NativeExecutionModel(WorkflowRegistry registry, NativeExecutionOptions? options = null,
    Action<NativeOutbound>? outbound = null) : Models.IExecutionModel, Models.IModelMetadata, Models.ISimpleModelMigration
{
    private readonly NativeExecutionOptions _options = options ?? new();
    public const string ModelKey = "native";
    public string Key => ModelKey;
    public Type StateType => typeof(NativeState);
    public int StateVersion => 14;

    /// <param name="workflow">The workflow to run (<see cref="Workflow.Id"/>).</param>
    /// <param name="definition">
    /// Its <see cref="Workflow.Definition"/>, when it is not the workflow itself. A publishing host starts a published version
    /// with <see cref="Start(Workflow, string, JsonObject?)"/>, which passes it; without it, the instance's definition is the
    /// workflow.
    /// </param>
    public static Models.WorkflowStart Start(string workflow, string mode = Modes.Balanced, JsonObject? input = null,
        string? triggerNode = null, JsonNode? trigger = null, string? definition = null) =>
        new(ModelKey, workflow, JsonSerializer.Serialize(new NativeStart(mode, input, triggerNode, trigger)),
            Ephemeral: mode == Modes.Ephemeral, Definition: definition == workflow ? null : definition);

    /// <summary>A start of a registered workflow, under its definition.</summary>
    public static Models.WorkflowStart Start(Workflow workflow, string mode = Modes.Balanced, JsonObject? input = null) =>
        Start(workflow.Id, mode, input, definition: workflow.Definition);

    public object CreateState(Models.WorkflowStart start) => NewState(start.Input);

    internal static NativeState NewState(string? input) =>
        new() { Mode = JsonSerializer.Deserialize<NativeStart>(input ?? "{}")!.Mode };

    public object UpgradeState(string state, int fromVersion) => Upgrade(state, fromVersion);

    public string? ValidateMigration(Models.InstanceInfo source, object state, string target)
    {
        if (!registry.TryGet(source.Workflow, out var before) || !registry.TryGet(target, out var after))
            return "DefinitionUnavailable";
        // A migration moves an instance between versions of its definition, and its definition label stays: a published
        // version does not move to another definition's.
        if (before.Definition != after.Definition && (before.Definition != before.Id || after.Definition != after.Id))
            return "DefinitionChanged";
        return StructuredMigration.Validate(before, after, (NativeState)state)?.ToString();
    }

    internal static NativeState Upgrade(string state, int fromVersion)
    {
        if (fromVersion is < 1 or > 13) throw new NotSupportedException($"native state has no version {fromVersion}");
        var native = System.Text.Json.JsonSerializer.Deserialize<NativeState>(state);
        // A failed initial turn can leave a genuine JSON-null checkpoint; preserve its existing missing-state behavior.
        if (native is null) return null!;
        if (fromVersion < 4 && LexicalScopes.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported lexical scope data.");
        if (fromVersion < 5 && NativeInterventions.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported recovery data.");
        if (fromVersion < 6 && NativeRetries.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported successor data.");
        if (fromVersion < 7 && native.InterventionPolicy != 0) throw new InvalidOperationException("Legacy native state contains unsupported recovery policy data.");
        if (fromVersion < 12 && (native.CheckpointControl || native.Inboxes?.Values.Any(i => i.Receipts.Values.Any(r => r.Status == "Evaluating" || r.EvaluationStarted)) == true))
            throw new InvalidOperationException("Legacy native state contains unsupported evaluation data.");
        if (native.InterventionPolicy < 0 || native.InterventionPolicy > (fromVersion < 8 ? 2 : fromVersion < 9 ? 3 : fromVersion < 11 ? 4 : 5) ||
            fromVersion < 8 && NativeStatefulRecovery.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported stateful recovery data.");
        return native;
    }

    public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken) =>
        new Burst(registry, _options, new ContractTurn(turn, outbound, definitionOf: DefinitionOf), cancellationToken).RunAsync();

    private string? DefinitionOf(string workflow) => registry.TryGet(workflow, out var found) ? found.Definition : null;

    public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) =>
        ExecuteBackground(registry, job.Actor, job.Arguments, cancellationToken, _options.Http);

    internal static async ValueTask<object?> ExecuteBackground(WorkflowRegistry registry, Contract.ActorRef actor,
        string? arguments, CancellationToken cancellationToken, NativeHttpOptions? http = null)
    {
        var args = JsonSerializer.Deserialize<BackgroundArgs>(arguments!)!;
        var node = registry.Get(args.Workflow).Nodes[args.Node];
        var activity = (ILeaf)Hydrator.Create(node, args.Inputs);
        var path = args.IterationPath ?? throw new InvalidOperationException("A background job needs an iteration path.");
        var key = args.EffectKey ?? throw new InvalidOperationException("A background job needs an effect key.");
        var result = await activity.ExecuteAsync(new ActivityContext(actor, args.Frame, node.Id, 0,
            cancellationToken, path, key) { Http = http });
        return new BackgroundResult(result.Kind, result.Result, result.Outcome, result.Fault);
    }

    public Contract.RestorePlan RestoreWaits(Models.InstanceInfo instance, object state)
    {
        var timers = new List<Contract.TimerSpec>();
        var jobs = new List<Contract.JobSpec>();
        var subscriptions = new List<Contract.SubscriptionSpec>();
        var native = (NativeState)state;
        var queuedResumes = native.Agenda.Where(work => work.Resume && !work.Gone).Select(work => work.Wait)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (wait, entry) in native.Waits)
        {
            switch (entry.Kind)
            {
                case "timer" when entry.Key.Length > 0:
                    timers.Add(new(entry.Key, NativeKinds.Timer, JsonSerializer.Serialize(new TimerFired(wait)),
                        entry.Due ?? throw new InvalidOperationException("The timer checkpoint has no original due time.")));
                    break;
                case "job" when !queuedResumes.Contains(wait):
                    jobs.Add(entry.Job ?? throw new InvalidOperationException("The job checkpoint has no invocation recipe."));
                    break;
                case "event":
                    subscriptions.Add(new(entry.Key, wait, Keep: false));
                    break;
            }
        }
        return new(timers, jobs, subscriptions);
    }

    public long TerminalGrowth(Models.InstanceInfo instance, object state) => ((NativeState)state).Inboxes?.Values
        .Where(inbox => inbox.Closed is null)
        .Sum(inbox => 7 + 23L * inbox.Receipts.Values.Count(receipt => receipt.Status is "Queued" or "Evaluating" or "Selected")) ?? 0;

    public bool HandlesMetadata(Contract.Message message) => Burst.HandlesMetadata(message.Kind);
    public ValueTask MetadataAsync(Models.ITurn turn, Contract.ActorStatus status, long? endedAt, CancellationToken cancellationToken)
    {
        var bridge = new ContractTurn(turn, outbound, endedAt);
        return turn.State is NativeState
            ? new Burst(registry, _options, bridge, cancellationToken, status).MetadataAsync()
            : InvocationLifetime.Missing(bridge);
    }

    public void TerminateMetadata(object state)
    {
        if (state is NativeState native)
        {
            InvocationLifetime.Terminate(native, new ModelReceiptClock(0).Terminal, "cancelled");
            LexicalScopes.Terminate(native);
        }
    }

    public bool ProtectsDeletion(object state, long? endedAt, long now) =>
        state is NativeState native && InvocationLifetime.Protected(native, now, new ModelReceiptClock(now, endedAt));
}
