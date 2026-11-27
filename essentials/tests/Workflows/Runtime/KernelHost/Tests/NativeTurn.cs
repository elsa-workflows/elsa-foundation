using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>
/// One turn of the Native execution model, without a foundation: the turn records what the model asks of the kernel (its
/// status and output, its labels, the children it starts, the messages it sends), which is all a mapped workflow that runs
/// to its end in one Balanced turn needs. <see cref="ResumeAsync"/> runs the next turn of the instance on the messages it
/// sent itself (a Delay's timer), from its state as the kernel would have stored it.
/// </summary>
internal sealed class NativeTurn : Models.ITurn
{
    private int _sent;

    private NativeTurn(Models.InstanceInfo instance, IReadOnlyList<Contract.Message> inbox, object state, int sent = 0)
    {
        Instance = instance;
        Inbox = inbox;
        State = state;
        _sent = sent;
    }

    public static Task<NativeTurn> StartAsync(Workflow workflow, NativeExecutionOptions? options = null, JsonObject? input = null) =>
        StartAsync(new NativeExecutionModel(new WorkflowRegistry().Add(workflow), options), workflow, input);

    /// <summary>A start of <paramref name="workflow"/> on <paramref name="model"/>, whose registry holds it.</summary>
    public static async Task<NativeTurn> StartAsync(NativeExecutionModel model, Workflow workflow, JsonObject? input = null)
    {
        var start = NativeExecutionModel.Start(workflow, Modes.Balanced, input);
        var instance = new Models.InstanceInfo(new Contract.ActorRef("instance-1"), "tenant", NativeExecutionModel.ModelKey, start.Workflow, null);
        var turn = new NativeTurn(instance, [new Contract.Message("start-1", Contract.Kinds.Start, start.Input)], model.CreateState(start));
        await model.TurnAsync(turn, CancellationToken.None);
        return turn;
    }

    /// <summary>The instance's next turn on <paramref name="model"/>, which may be another host's after a restart: its inbox the messages this turn sent the instance.</summary>
    public async Task<NativeTurn> ResumeAsync(NativeExecutionModel model)
    {
        var stored = JsonSerializer.Serialize((NativeState)State);
        var next = new NativeTurn(Instance, [.. Sent.Where(message => message.Target == Instance.Ref).Select(message => message.Message)],
            JsonSerializer.Deserialize<NativeState>(stored)!, _sent);
        await model.TurnAsync(next, CancellationToken.None);
        return next;
    }

    public Contract.ActorStatus Status { get; private set; } = Contract.ActorStatus.Running;
    public JsonNode? Output { get; private set; }
    public Dictionary<string, string?> Labels { get; } = new(StringComparer.Ordinal);
    public List<(Models.WorkflowStart Start, bool Detached)> Children { get; } = [];
    public List<(Contract.ActorRef Target, Contract.Message Message)> Sent { get; } = [];
    public NativeState Native => (NativeState)State;

    public Models.InstanceInfo Instance { get; }
    public long Now => 1_700_000_000_000;
    public IReadOnlyList<Contract.Message> Inbox { get; }
    public object State { get; set; }
    public object? Ambient => null;

    public void Emit(string channel, object? item) { }
    public void Reply(Contract.Message message, string channel, object? item) { }
    public string Send(Contract.ActorRef target, string kind, object? payload = null, long? dueAt = null)
    {
        var id = $"sent-{++_sent}";
        Sent.Add((target, new Contract.Message(id, kind, payload is null ? null : JsonSerializer.Serialize(payload), Instance.Ref)));
        return id;
    }
    public void Recall(string id) { }

    public Contract.ActorRef Start(Models.WorkflowStart spec, Contract.Placement placement = Contract.Placement.WithParent, bool detached = false)
    {
        Children.Add((spec, detached));
        return new Contract.ActorRef($"child-{Children.Count}");
    }

    public void StartJob(string key, object? arguments = null, TimeSpan? timeout = null) { }
    public void CancelJob(string key) { }
    public void Hold(Contract.Message message) { }
    public void Subscribe(string key, string wait, bool keep = false) { }
    public void Unsubscribe(string key, string wait) { }
    public string Publish(string key, string? payload, TimeSpan keepFor) => $"event-{++_sent}";
    public void Record(object entry) { }
    public void Rebind(string workflow) { }
    public void SetLabel(string name, string? value) => Labels[name] = value;

    public void SetStatus(Contract.ActorStatus status, string? subStatus = null, object? output = null)
    {
        Status = status;
        Output = output as JsonNode;
    }
}
