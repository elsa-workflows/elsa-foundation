using System.Globalization;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

namespace Kernel.WorkflowActor;

/// <summary>Runs workflow models through the public actor contract.</summary>
public sealed partial class WorkflowActor(IEnumerable<IExecutionModel> models) : IActorType
{
    public const string ActorType = "workflow";

    private readonly Dictionary<string, IExecutionModel> _models = models.ToDictionary(model => model.Key, StringComparer.Ordinal);

    public string Type => ActorType;

    public static StartSpec Start(WorkflowStart start) =>
        new(ActorType, JsonSerializer.Serialize(new WorkflowHeader(start.Model, start.Definition, null,
            Ephemeral: start.Ephemeral, History: start.History)), start.Input, start.Ephemeral, start.History);

    public bool Withholds(ActorHeader header)
    {
        if (header.Status != ActorStatus.Running) return false;
        var workflow = ReadHeader(header);
        return workflow.Suspended || !_models.TryGetValue(workflow.Model, out var model) || workflow.Version > model.StateVersion;
    }

    public async ValueTask TurnAsync(IActorTurn turn, CancellationToken cancellationToken)
    {
        var header = turn.Header;
        var workflow = ReadHeader(header);
        var command = turn.Inbox.Count == 1 ? turn.Inbox[0].Kind : null;
        if (command is Kinds.Suspend or Kinds.Resume)
        {
            if (header.Status == ActorStatus.Running)
                WriteHeader(turn, workflow, workflow with { Suspended = command == Kinds.Suspend });
            return;
        }

        if (command == Kinds.Terminate)
        {
            if (turn.State is { } saved && _models.TryGetValue(workflow.Model, out var terminatingModel) &&
                workflow.Version <= terminatingModel.StateVersion && terminatingModel is IModelMetadata metadata)
            {
                var state = ReadState(terminatingModel, workflow, saved, turn.Inbox);
                metadata.TerminateMetadata(state);
                turn.State = JsonSerializer.Serialize(state, terminatingModel.StateType);
                WriteHeader(turn, workflow, workflow with { Version = terminatingModel.StateVersion });
            }
            if (header.Status == ActorStatus.Running) turn.End(ActorStatus.Cancelled);
            return;
        }

        if (command == Kinds.Migrate)
        {
            ApplyMigration(turn, workflow, turn.Inbox[0]);
            return;
        }

        var model = GetModel(workflow.Model);
        var reader = model as IModelMetadata;
        var inbox = header.Status == ActorStatus.Running ? turn.Inbox : turn.Inbox.Where(message => reader?.HandlesMetadata(message) == true).ToArray();
        if (inbox.Count == 0) return;
        if (header.Status != ActorStatus.Running && workflow.Version is { } version && version > model.StateVersion)
        {
            var unavailable = new ModelMetadataUnavailable(header.Ref.Id, workflow.Model, version, model.StateVersion);
            foreach (var query in inbox) turn.Reply(query, "response", unavailable);
            turn.Record(unavailable);
            return;
        }
        var current = ReadState(model, workflow, turn.State, inbox);
        var modelTurn = new WorkflowTurn(turn, workflow, current, inbox);
        if (header.Status == ActorStatus.Running)
            await model.TurnAsync(modelTurn, cancellationToken);
        else
            await reader!.MetadataAsync(modelTurn, header.Status, header.EndedAt, cancellationToken);

        turn.State = JsonSerializer.Serialize(modelTurn.State, model.StateType);
        WriteHeader(turn, workflow, modelTurn.Workflow with { Version = model.StateVersion });
        if (header.Status == ActorStatus.Running && modelTurn.Status != ActorStatus.Running)
            turn.End(modelTurn.Status, modelTurn.Output);
    }

    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken)
    {
        if (job.Arguments is not { } payload || JsonSerializer.Deserialize<WorkflowJobArguments>(payload) is not { } arguments)
            throw new InvalidOperationException("The workflow job has no model.");
        return GetModel(arguments.Model).ExecuteJobAsync(new WorkflowJob(job, arguments.Arguments), cancellationToken);
    }

    public RestorePlan RestoreWaits(ActorHeader header, string state)
    {
        var workflow = ReadHeader(header);
        var model = GetModel(workflow.Model);
        var plan = model.RestoreWaits(Instance(header, workflow), ReadState(model, workflow, state, []));
        return plan with { Jobs = plan.Jobs.Select(job => job with { Arguments = JobArguments(workflow.Model, job.Arguments) }).ToArray() };
    }

    public long TerminalGrowth(ActorHeader header, string state)
    {
        var workflow = ReadHeader(header);
        // Termination preserves the stored state while this version cannot enter the model.
        if (!_models.TryGetValue(workflow.Model, out var model) || workflow.Version > model.StateVersion) return 0;
        return model.TerminalGrowth(Instance(header, workflow), ReadState(model, workflow, state, []));
    }

    public bool ProtectsDeletion(ActorHeader header, string state, long now)
    {
        var workflow = ReadHeader(header);
        return !_models.TryGetValue(workflow.Model, out var model) || workflow.Version > model.StateVersion ||
            model is IModelMetadata metadata && metadata.ProtectsDeletion(ReadState(model, workflow, state, []), header.EndedAt, now);
    }

    private IExecutionModel GetModel(string model) => _models.TryGetValue(model, out var found) ? found :
        throw new InvalidOperationException($"No execution model named '{model}' is registered.");

    private sealed record ModelMetadataUnavailable(string Actor, string Model, int StoredVersion, int AvailableVersion);

    private static WorkflowHeader ReadHeader(ActorHeader header) =>
        JsonSerializer.Deserialize<WorkflowHeader>(header.TypeData) ?? throw new InvalidOperationException("The workflow header is absent.");

    internal static bool IsRunningMigrationPlan(ActorView? view, ActorRef plan) =>
        IsMigrationPlan(view, plan, requireRunning: true);

    internal static bool IsMigrationPlan(ActorView? view, ActorRef plan, bool requireRunning = false) =>
        view?.Header is { } header && header.Ref == plan && TryReadWorkflow(header) is
        { Model: MigrationPlanModel.ModelKey, Definition: MigrationPlanModel.ModelKey, Version: > 0 and <= MigrationPlanModel.PlanVersion,
          History: History.Full, Ephemeral: false } workflow &&
        (!requireRunning || header.Status == ActorStatus.Running && !workflow.Suspended);

    internal static bool CanSelectForMigration(ActorView view, string model, string definition) =>
        view.Header.Status == ActorStatus.Running && TryReadWorkflow(view.Header) is
            { Definition: var current, Model: var currentModel, Version: > 0, Ephemeral: false } &&
        current == definition && currentModel == model;

    private static WorkflowHeader? TryReadWorkflow(ActorHeader header)
    {
        if (header.Type != ActorType) return null;
        try { return JsonSerializer.Deserialize<WorkflowHeader>(header.TypeData); }
        catch (JsonException) { return null; }
    }

    private static object ReadState(IExecutionModel model, WorkflowHeader workflow, string? state, IReadOnlyList<Message> inbox)
    {
        if (state is null)
        {
            var start = inbox.FirstOrDefault(message => message.Kind == Kinds.Start) ??
                throw new InvalidOperationException("A workflow without state needs its start message.");
            return model.CreateState(new WorkflowStart(workflow.Model, workflow.Definition, start.Payload, workflow.Ephemeral, workflow.History));
        }
        if (workflow.Version is not { } version || version > model.StateVersion)
            throw new InvalidOperationException("This model cannot read the stored state version.");
        return version < model.StateVersion ? model.UpgradeState(state, version) :
            JsonSerializer.Deserialize(state, model.StateType) ?? throw new InvalidOperationException("The workflow state is absent.");
    }

    private static void WriteHeader(IActorTurn turn, WorkflowHeader previous, WorkflowHeader next, string? answeredPlan = null)
    {
        if (previous == next && answeredPlan is null) return;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["definition"] = next.Definition };
        if (next.Version is { } version) labels.Add("version", version.ToString(CultureInfo.InvariantCulture));
        answeredPlan ??= turn.Header.Labels.GetValueOrDefault(AnsweredPlanLabel);
        if (answeredPlan is not null) labels.Add(AnsweredPlanLabel, answeredPlan);
        turn.SetHeader(previous == next ? turn.Header.TypeData : JsonSerializer.Serialize(next), labels);
    }

    private static InstanceInfo Instance(ActorHeader header, WorkflowHeader workflow) =>
        new(header.Ref, header.Tenant, workflow.Model, workflow.Definition, header.Parent);

    private static string? SerializeValue(object? value) => value is null ? null : JsonSerializer.Serialize(value, value.GetType());

    private static string JobArguments(string model, string? arguments) => JsonSerializer.Serialize(new WorkflowJobArguments(model, arguments));

    private sealed record WorkflowHeader(string Model, string Definition, int? Version, bool Suspended = false,
        string? SubStatus = null, bool Ephemeral = false, History History = History.Full);

    private sealed record WorkflowJobArguments(string Model, string? Arguments);

    private sealed class WorkflowJob(IJob original, string? arguments) : IJob
    {
        public ActorRef Actor => original.Actor;
        public string Key => original.Key;
        public string? Arguments => arguments;
        public string AttemptId => original.AttemptId;
        public int AttemptNumber => original.AttemptNumber;
        public long? Deadline => original.Deadline;
    }

    private sealed class WorkflowTurn(IActorTurn turn, WorkflowHeader workflow, object state, IReadOnlyList<Message> inbox) : ITurn
    {
        public WorkflowHeader Workflow { get; private set; } = workflow;
        public ActorStatus Status { get; private set; } = turn.Header.Status;
        public object? Output { get; private set; }

        public InstanceInfo Instance { get; } = WorkflowActor.Instance(turn.Header, workflow);
        public long Now => turn.Now;
        public IReadOnlyList<Message> Inbox => inbox;
        public object State { get; set; } = state;
        public object? Ambient => turn.Ambient;

        public void Emit(string channel, object? item) => turn.Emit(channel, item);
        public void Reply(Message message, string channel, object? item) => turn.Reply(message, channel, item);
        public string Send(ActorRef target, string kind, object? payload, long? dueAt) => turn.Send(target, kind, SerializeValue(payload), dueAt);
        public void Recall(string id) => turn.Recall(id);
        public ActorRef Start(WorkflowStart spec, Placement placement, bool detached) => turn.Start(WorkflowActor.Start(spec), placement, detached);
        public void StartJob(string key, object? arguments, TimeSpan? timeout) => turn.StartJob(key, JobArguments(Instance.Model, SerializeValue(arguments)), timeout);
        public void CancelJob(string key) => turn.CancelJob(key);
        public void Hold(Message message) => turn.Hold(message);
        public void Subscribe(string key, string wait, bool keep) => turn.Subscribe(key, wait, keep);
        public void Unsubscribe(string key, string wait) => turn.Unsubscribe(key, wait);
        public string Publish(string key, string? payload, TimeSpan keepFor) => turn.Publish(key, payload, keepFor);
        public void Record(object entry) => turn.Record(entry);
        public void Rebind(string definition) => Workflow = Workflow with { Definition = definition };

        public void SetStatus(ActorStatus status, string? subStatus, object? output)
        {
            if (turn.Header.Status != ActorStatus.Running && status != turn.Header.Status)
                throw new InvalidOperationException("The status of an ended workflow is final.");
            (Status, Output) = (status, output);
            Workflow = Workflow with { SubStatus = subStatus };
        }
    }
}
