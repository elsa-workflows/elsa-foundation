using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

namespace Kernel.Agent;

// A deliberately small checkpoint, not a model of an LLM or a framework thread.
public sealed class Conversation
{
    public string Stage { get; set; } = "new";
    public List<string> Transcript { get; set; } = [];
    public int Results { get; set; }
    public bool Approved { get; set; }
    public bool Replied { get; set; }
}

public sealed record Ask(string From, string Question);

public sealed class AgentModel(Func<IJob, CancellationToken, ValueTask<object?>> tool, bool duplicateResult = false, bool persistLive = false) : IExecutionModel
{
    public const string ModelKey = "agent-spike";
    public const string Approval = "agent:approval";
    public string Key => ModelKey;
    public Type StateType => typeof(Conversation);
    public int StateVersion => 1;
    public object CreateState(StartSpec start) => new Conversation();
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => tool(job, cancellationToken);

    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
    {
        var state = (Conversation)turn.State;
        foreach (var message in turn.Inbox)
        {
            switch (message.Kind)
            {
                case Kinds.Start when turn.Instance.Definition == "coordinator" && state.Stage == "new":
                    state.Transcript.Add("user: prepare a proposal");
                    state.Stage = "checkpoint";
                    turn.Emit("tokens", "Preparing proposal");
                    if (persistLive) state.Transcript.Add("Preparing proposal"); // negative control
                    turn.Send(turn.Instance.Ref, "continue");
                    break;
                case "continue" when state.Stage == "checkpoint":
                    state.Stage = "tool";
                    turn.StartJob("lookup", "proposal");
                    break;
                case Kinds.JobFinished when state.Stage == "tool":
                    var result = message.Read<JobFinished>();
                    if (result.Error is not null) throw new InvalidOperationException(result.Error);
                    state.Results += duplicateResult ? 2 : 1; // negative control
                    state.Transcript.Add("tool: " + Json.Read<string>(result.Result!));
                    state.Stage = "approval";
                    turn.Subscribe(Approval, "approval");
                    turn.Send(new InstanceRef("0/peer"), "ask", new Ask(turn.Instance.Ref.Id, "review proposal"),
                        new SendOptions(Start: new StartSpec(ModelKey, "peer")));
                    break;
                case "ask" when turn.Instance.Definition == "peer":
                    var ask = message.Read<Ask>();
                    state.Transcript.Add("request: " + ask.Question);
                    turn.Send(new InstanceRef(ask.From), "peer-reply", "reviewed");
                    state.Stage = "done";
                    turn.SetStatus(InstanceStatus.Finished);
                    break;
                case "peer-reply" when state.Stage == "approval":
                    state.Replied = true;
                    state.Transcript.Add("peer: " + message.Read<string>());
                    break;
                case Kinds.Event when state.Stage == "approval" && !state.Approved:
                    turn.Unsubscribe(Approval, "approval");
                    if (message.Read<bool>())
                    {
                        state.Approved = true;
                        state.Transcript.Add("human: approved");
                    }
                    else
                    {
                        state.Transcript.Add("human: denied");
                        state.Stage = "cancelled";
                        turn.SetStatus(InstanceStatus.Cancelled);
                    }
                    break;
                case "cancel" when state.Stage is not ("done" or "cancelled"):
                    if (state.Stage == "tool") turn.CancelJob("lookup");
                    if (state.Stage == "approval") turn.Unsubscribe(Approval, "approval");
                    state.Stage = "cancelled";
                    turn.SetStatus(InstanceStatus.Cancelled);
                    break;
            }
        }
        if (state.Stage == "approval" && state.Approved && state.Replied)
        {
            state.Stage = "done";
            turn.SetStatus(InstanceStatus.Finished, output: "approved proposal");
        }
        return ValueTask.CompletedTask;
    }
}
