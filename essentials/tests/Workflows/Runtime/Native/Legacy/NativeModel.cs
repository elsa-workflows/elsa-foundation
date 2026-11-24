using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>External effect observed by the original prototype runner.</summary>
public sealed record OutboundSending(InstanceInfo Instance, string Frame, string Node, string IterationPath, int Attempt, string EffectKey, OutboundEffect Effect);

public sealed class NativeOptions : NativeExecutionOptions
{
    /// <summary>Gates a background job before it runs (the simulation decides when it completes).</summary>
    public Func<IJob, CancellationToken, ValueTask>? Gate { get; init; }
    /// <summary>
    /// Called when an External activity's side effect happens: instance, frame, node, committed attempt, and whether the
    /// turn may have run before (the kernel's <see cref="ITurn.Redelivered"/>, friction F-N6).
    /// </summary>
    public Action<InstanceRef, string, string, int, bool>? OnExternal { get; init; }
    /// <summary>
    /// The host's transport for an outbound effect (round 5 C): the instance, frame, node, iteration path and attempt it
    /// belongs to, and the effect with the key the activity put in it. Called where the effect would be sent, inside the
    /// turn, before its commit: at least once.
    /// </summary>
    public Action<OutboundSending>? OnOutbound { get; init; }
}

public sealed class NativeModel(WorkflowRegistry registry, NativeOptions? options = null) : IExecutionModel, IModelMetadata
{
    public const string ModelKey = NativeExecutionModel.ModelKey;
    public string Key => ModelKey;
    public Type StateType => typeof(NativeState);

    /// <summary>Version 13 gates mapped chart completion; old recovery policies remain unchanged.</summary>
    public int StateVersion => 13;
    public object UpgradeState(string state, int fromVersion) =>
        fromVersion < StateVersion ? NativeExecutionModel.Upgrade(state, fromVersion) : throw new NotSupportedException($"native state has no version {fromVersion}");

    internal WorkflowRegistry Registry => registry;
    internal NativeOptions Options { get; } = options ?? new NativeOptions();

    public static StartSpec Spec(string workflow, string mode = Modes.Balanced, JsonObject? input = null, string? triggerNode = null, JsonNode? trigger = null) =>
        new(ModelKey, workflow, Json.Write(new NativeStart(mode, input, triggerNode, trigger)), Ephemeral: mode == Modes.Ephemeral);

    public object CreateState(StartSpec start) => NativeExecutionModel.NewState(start.Input);

    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken) =>
        new Burst(Registry, Options, new LegacyTurn(turn, Options), cancellationToken).RunAsync();

    public bool HandlesMetadata(Message message) => Burst.HandlesMetadata(message.Kind);
    public ValueTask MetadataAsync(ITurn turn, InstanceStatus status, CancellationToken cancellationToken)
    {
        var bridge = new LegacyTurn(turn, Options);
        return turn.State is NativeState
            ? new Burst(Registry, Options, bridge, cancellationToken, (Contract.ActorStatus)status).MetadataAsync()
            : InvocationLifetime.Missing(bridge);
    }
    public void TerminateMetadata(object state)
    {
        if (state is NativeState native)
        {
            InvocationLifetime.Terminate(native, WorkflowCommitClock.Terminal, "cancelled", (Options.AdmissionFlaws & AdmissionFlaw.KeepPayload) != 0,
                (Options.AdmissionFlaws & AdmissionFlaw.RestampCompleted) != 0);
            LexicalScopes.Terminate(native);
        }
    }
    public bool ProtectsDeletion(object state, long now, ICommitClock clock) => (Options.AdmissionFlaws & AdmissionFlaw.IgnoreRetention) == 0 &&
        ((Options.AdmissionFlaws & AdmissionFlaw.UnsafeNull) != 0
            ? InvocationLifetime.Protected((NativeState)state, now, new LegacyReceiptClock(clock))
            : state is NativeState native && InvocationLifetime.Protected(native, now, new LegacyReceiptClock(clock)));

    /// <summary>A Background leaf, run as a background job: its inputs were materialized in the turn that started it.</summary>
    public async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken)
    {
        if (Options.Gate is { } gate) await gate(job, cancellationToken);
        return await NativeExecutionModel.ExecuteBackground(registry, new(job.Instance.Id), job.Arguments, cancellationToken);
    }
}
