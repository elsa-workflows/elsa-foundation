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
    private const int PreviousStateVersion = 14;
    private const int UserTaskStateVersion = 15;
    public int StateVersion => _options.UserTaskOpenJobs is null ? PreviousStateVersion : UserTaskStateVersion;

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
        Start(workflow.Id, mode, input, definition: workflow.Definition) with { ExecutableSelection = workflow.ExecutableSelection };

    public object CreateState(Models.WorkflowStart start) => NewState(start.Input);

    internal static NativeState NewState(string? input) =>
        new() { Mode = JsonSerializer.Deserialize<NativeStart>(input ?? "{}")!.Mode };

    public object UpgradeState(string state, int fromVersion) =>
        Upgrade(state, fromVersion, permitsUserTaskWaits: _options.UserTaskOpenJobs is not null);

    public string? ValidateMigration(Models.InstanceInfo source, object state, string target)
        => ValidateMigration(source, state, target, out _);

    public string? ValidateMigration(Models.InstanceInfo source, object state, string target, out string? targetExecutableSelection)
    {
        targetExecutableSelection = null;
        if (!registry.TryGet(source.Workflow, out var before) || !registry.TryGet(target, out var after) ||
            source.ExecutableSelection is { } expected && !MatchesSelection(before, expected))
            return "DefinitionUnavailable";
        // A migration moves an instance between versions of its definition, and its definition label stays: a published
        // version does not move to another definition's.
        var definition = source.ExecutableSelection is null ? before.Definition : source.Definition ?? before.Definition;
        if (definition != after.Definition && (definition != before.Id || after.Definition != after.Id))
            return "DefinitionChanged";
        if (StructuredMigration.Validate(before, after, (NativeState)state) is { } finding)
            return finding.ToString();
        if (source.ExecutableSelection is not null) targetExecutableSelection = after.ExecutableSelection;
        return null;
    }

    internal static NativeState Upgrade(string state, int fromVersion, bool permitsUserTaskWaits = false)
    {
        var latestPreviousVersion = permitsUserTaskWaits ? PreviousStateVersion : PreviousStateVersion - 1;
        if (fromVersion < 1 || fromVersion > latestPreviousVersion)
            throw new NotSupportedException($"native state has no version {fromVersion}");
        var native = System.Text.Json.JsonSerializer.Deserialize<NativeState>(state);
        // A failed initial turn can leave a genuine JSON-null checkpoint; preserve its existing missing-state behavior.
        if (native is null) return null!;
        if (fromVersion < 4 && LexicalScopes.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported lexical scope data.");
        if (native.Waits.Values.Any(wait => wait.Kind == NativeUserTaskWaits.Kind))
            throw new InvalidOperationException("Legacy native state contains unsupported user task waits.");
        if (fromVersion < 5 && NativeInterventions.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported recovery data.");
        if (fromVersion < 6 && NativeRetries.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported successor data.");
        if (fromVersion < 7 && native.InterventionPolicy != 0) throw new InvalidOperationException("Legacy native state contains unsupported recovery policy data.");
        if (fromVersion < 12 && (native.CheckpointControl || native.Inboxes?.Values.Any(i => i.Receipts.Values.Any(r => r.Status == "Evaluating" || r.EvaluationStarted)) == true))
            throw new InvalidOperationException("Legacy native state contains unsupported evaluation data.");
        if (native.InterventionPolicy < 0 || native.InterventionPolicy > (fromVersion < 8 ? 2 : fromVersion < 9 ? 3 : fromVersion < 11 ? 4 : 5) ||
            fromVersion < 8 && NativeStatefulRecovery.Present(native)) throw new InvalidOperationException("Legacy native state contains unsupported stateful recovery data.");
        return native;
    }

    private sealed record InlineMaterial(NativeExecutionModel Owner, Workflow Workflow);

    /// <summary>Captures the already-authorized exact executable for a trusted owner-local start.</summary>
    /// <remarks>The returned token is process-local and belongs to this model instance. The turn must select the same executable identity.</remarks>
    public object CaptureInlineMaterial(Workflow executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if (string.IsNullOrWhiteSpace(executable.ExecutableSelection))
            throw new ArgumentException("Inline material requires an exact executable identity.", nameof(executable));
        return new InlineMaterial(this, executable);
    }

    /// <summary>Checks an opaque local capture against the exact executable and definition in an admitted start.</summary>
    public bool MatchesInlineMaterial(object material, string workflow, string definition, string? selection) =>
        material is InlineMaterial local && ReferenceEquals(this, local.Owner) &&
        selection is not null && MatchesSelection(local.Workflow, selection) &&
        local.Workflow.Id == workflow && local.Workflow.Definition == definition;

    public ValueTask TurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
    {
        if (turn.Ambient is InlineMaterial local)
        {
            var instance = turn.Instance;
            if (!MatchesInlineMaterial(local, instance.Workflow, instance.Definition ?? instance.Workflow, instance.ExecutableSelection))
                throw SelectionUnavailable();
            return RunTurnAsync(turn, cancellationToken, local.Workflow);
        }
        return turn.Instance.ExecutableSelection is { } selection
            ? SelectedTurnAsync(turn, selection, cancellationToken)
            : Resolves(turn.Instance.Workflow) ? ResolvedTurnAsync(turn, cancellationToken) : RunTurnAsync(turn, cancellationToken);
    }

    private async ValueTask SelectedTurnAsync(Models.ITurn turn, string selection, CancellationToken cancellationToken)
    {
        var executable = await SelectAsync(turn.Instance.Workflow, selection, cancellationToken);
        await RunTurnAsync(turn, cancellationToken, executable);
    }

    private async ValueTask ResolvedTurnAsync(Models.ITurn turn, CancellationToken cancellationToken)
    {
        await ResolveAsync(turn.Instance.Workflow, cancellationToken);
        await RunTurnAsync(turn, cancellationToken);
    }

    private ValueTask RunTurnAsync(Models.ITurn turn, CancellationToken cancellationToken, Workflow? executable = null)
    {
        var bridge = new ContractTurn(turn, outbound, structuralJournal: _options.UseStructuralJournal);
        bridge.RecordClock();
        return new Burst(registry, _options, bridge, cancellationToken, executable: executable).RunAsync();
    }

    /// <summary>Whether <paramref name="workflow"/> is one the registry lacks and the host may load (<see cref="NativeExecutionOptions.ResolveWorkflow"/>).</summary>
    private bool Resolves(string workflow) => _options.ResolveWorkflow is not null && !registry.Contains(workflow);

    private async ValueTask<Workflow?> ResolveAsync(string workflow, CancellationToken cancellationToken, string? selection = null)
    {
        if (_options.ResolveWorkflow is not { } resolve || await resolve(workflow, cancellationToken) is not { } found)
            return null;
        if (!StringComparer.Ordinal.Equals(found.Id, workflow))
            throw new InvalidOperationException($"The host resolved workflow '{workflow}' to '{found.Id}'.");
        if (selection is not null && !MatchesSelection(found, selection)) return null;
        registry.Add(found);
        return found;
    }

    private async ValueTask<Workflow> SelectAsync(string workflow, string selection, CancellationToken cancellationToken)
    {
        if (registry.TryGet(workflow, out var cached) && MatchesSelection(cached, selection)) return cached;
        return await ResolveAsync(workflow, cancellationToken, selection) ?? throw SelectionUnavailable();
    }

    private static bool MatchesSelection(Workflow workflow, string selection) =>
        StringComparer.Ordinal.Equals(workflow.ExecutableSelection, selection);

    private static InvalidOperationException SelectionUnavailable() =>
        new("The exact executable selected for this workflow is unavailable.");

    public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
    {
        if (NativeUserTaskWaits.TryGetOpenWait(job.Key, out var wait))
            return await OpenUserTaskAsync(job, wait, cancellationToken);
        var args = JsonSerializer.Deserialize<BackgroundArgs>(job.Arguments!)!;
        if (args.ExecutableSelection is { } selection)
            return await ExecuteBackground(await SelectAsync(args.Workflow, selection, cancellationToken), job.Actor, args,
                cancellationToken, _options.Http, job.Tenant);
        if (Resolves(args.Workflow))
            await ResolveAsync(args.Workflow, cancellationToken);
        return await ExecuteBackground(registry.Get(args.Workflow), job.Actor, args, cancellationToken, _options.Http, job.Tenant);
    }

    private async ValueTask<NativeUserTaskWaits.Opened> OpenUserTaskAsync(Contract.IJob job, string wait, CancellationToken cancellationToken)
    {
        if (_options.UserTaskOpenJobs is not { } openJobs || job.Tenant is not { } tenant || string.IsNullOrWhiteSpace(tenant) ||
            job.Arguments is not { } body || JsonSerializer.Deserialize<NativeUserTaskWaits.OpenArguments>(body) is not { } arguments ||
            arguments.Recipient.Actor != job.Actor || arguments.Recipient.Wait != wait)
            throw new InvalidOperationException("The job does not own this Actor user task wait.");

        await openJobs.OpenAsync(tenant, arguments.Recipient, arguments.Request, cancellationToken);
        return new(arguments.Recipient.TaskId);
    }

    internal static ValueTask<object?> ExecuteBackground(WorkflowRegistry registry, Contract.ActorRef actor,
        string? arguments, CancellationToken cancellationToken, NativeHttpOptions? http = null)
    {
        var args = JsonSerializer.Deserialize<BackgroundArgs>(arguments!)!;
        var executable = registry.Get(args.Workflow);
        if (args.ExecutableSelection is { } selection && !MatchesSelection(executable, selection))
            throw SelectionUnavailable();
        return ExecuteBackground(executable, actor, args, cancellationToken, http);
    }

    private static async ValueTask<object?> ExecuteBackground(Workflow executable, Contract.ActorRef actor,
        BackgroundArgs args, CancellationToken cancellationToken, NativeHttpOptions? http, string? jobTenant = null)
    {
        var node = executable.Nodes[args.Node];
        var activity = (ILeaf)Hydrator.Create(node, args.Inputs);
        var path = args.IterationPath ?? throw new InvalidOperationException("A background job needs an iteration path.");
        var key = args.EffectKey ?? throw new InvalidOperationException("A background job needs an effect key.");
        var result = await activity.ExecuteAsync(new ActivityContext(actor, args.Frame, node.Id, 0,
            cancellationToken, path, key) { Http = http, JobTenant = jobTenant });
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
                case NativeUserTaskWaits.Kind when entry.Job is { } opening:
                    jobs.Add(opening);
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
        var bridge = new ContractTurn(turn, outbound, endedAt, structuralJournal: _options.UseStructuralJournal);
        if (_options.UseStructuralJournal) bridge.RecordClock();
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
