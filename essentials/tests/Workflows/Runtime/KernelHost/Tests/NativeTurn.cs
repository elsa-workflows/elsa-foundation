using System.Text.Json.Nodes;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>
/// One start turn of the Native execution model, without a foundation: the turn records what the model asks of the kernel
/// (its status and output, its labels, the children it starts), which is all a mapped workflow that runs to its end in one
/// Balanced turn needs.
/// </summary>
internal sealed class NativeTurn : Models.ITurn
{
    private int _sent;

    private NativeTurn(Models.WorkflowStart start, object state)
    {
        Instance = new Models.InstanceInfo(new Contract.ActorRef("instance-1"), "tenant", NativeExecutionModel.ModelKey, start.Workflow, null);
        Inbox = [new Contract.Message("start-1", Contract.Kinds.Start, start.Input)];
        State = state;
    }

    public static async Task<NativeTurn> StartAsync(Workflow workflow, NativeExecutionOptions? options = null, JsonObject? input = null)
    {
        var model = new NativeExecutionModel(new WorkflowRegistry().Add(workflow), options);
        var start = NativeExecutionModel.Start(workflow, Modes.Balanced, input);
        var turn = new NativeTurn(start, model.CreateState(start));
        await model.TurnAsync(turn, CancellationToken.None);
        return turn;
    }

    public Contract.ActorStatus Status { get; private set; } = Contract.ActorStatus.Running;
    public JsonNode? Output { get; private set; }
    public Dictionary<string, string?> Labels { get; } = new(StringComparer.Ordinal);
    public List<(Models.WorkflowStart Start, bool Detached)> Children { get; } = [];
    public NativeState Native => (NativeState)State;

    public Models.InstanceInfo Instance { get; }
    public long Now => 1_700_000_000_000;
    public IReadOnlyList<Contract.Message> Inbox { get; }
    public object State { get; set; }
    public object? Ambient => null;

    public void Emit(string channel, object? item) { }
    public void Reply(Contract.Message message, string channel, object? item) { }
    public string Send(Contract.ActorRef target, string kind, object? payload = null, long? dueAt = null) => $"sent-{++_sent}";
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
