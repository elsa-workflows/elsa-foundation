using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

namespace Kernel.WorkflowActor;

/// <param name="From">The workflow the plan's instances run now.</param>
/// <param name="To">The workflow they move to.</param>
/// <param name="Definition">The definition label the source's instances carry, when it is not <paramref name="From"/>.</param>
internal sealed record MigrationPlanStart(string From, string To, long Deadline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Definition = null);

/// <summary>A bounded durable plan hosted as a separate model of the workflow actor.</summary>
/// <param name="definitionOf">
/// The definition a model's workflow is a version of, when the host knows it: a plan that names none selects by it, and one
/// that names another is refused, so that a plan never selects by an artifact's id where instances carry their definition.
/// </param>
internal sealed class MigrationPlanModel(MigrationPlanAuthority authority, Func<string?, string, string, bool> canPlan,
    Func<string?, string, string?>? definitionOf = null)
    : IExecutionModel, IModelMetadata
{
    internal const string ModelKey = "migration-plan";
    internal const int BatchLimit = 128;
    public string Key => ModelKey;
    public Type StateType => typeof(PlanState);
    internal const int PlanVersion = 2;
    public int StateVersion => PlanVersion;

    internal WorkflowStart Start(MigrationPlanStart plan)
    {
        if (definitionOf?.Invoke(plan.Model, plan.From) is { } definition)
        {
            if ((plan.Definition ?? plan.From) != definition && plan.Definition is not null)
                throw new ArgumentException($"The plan selects by definition '{plan.Definition}', and its source is a version of '{definition}'.", nameof(plan));
            plan = plan with { Definition = definition == plan.From ? null : definition };
        }
        return new(ModelKey, ModelKey, JsonSerializer.Serialize(Validate(plan)));
    }

    public object CreateState(WorkflowStart start)
    {
        if (start.Ephemeral || start.History != History.Full || start.Workflow != ModelKey)
            throw new InvalidOperationException("A migration plan needs a durable Full journal.");
        return new PlanState(Validate(JsonSerializer.Deserialize<MigrationPlanStart>(start.Input ?? "null")));
    }

    private MigrationPlanStart Validate(MigrationPlanStart? plan)
    {
        if (plan is not { From.Length: > 0, To.Length: > 0, Deadline: > 0 } ||
            Encoding.UTF8.GetByteCount(plan.From) > WorkflowActor.DefinitionLimit ||
            Encoding.UTF8.GetByteCount(plan.To) > WorkflowActor.DefinitionLimit ||
            plan.Definition is { } definition && (definition.Length == 0 || Encoding.UTF8.GetByteCount(definition) > WorkflowActor.DefinitionLimit) ||
            plan.Model is { } model && (model.Length == 0 || model == ModelKey || Encoding.UTF8.GetByteCount(model) > WorkflowActor.DefinitionLimit) ||
            plan.From == plan.To || !canPlan(plan.Model, plan.From, plan.To))
            throw new ArgumentException("Choose two compatible definitions and a deadline.", nameof(plan));
        return plan;
    }

    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
    {
        var state = (PlanState)turn.State;
        foreach (var message in turn.Inbox)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (message.Kind == Kinds.Start)
            {
                turn.Record(new MigrationPlanJournal.Created(state.Request.From, state.Request.To, state.Request.Deadline, state.Request.Model, state.Request.Definition));
                if (turn.Now < state.Request.Deadline)
                    state.DeadlineTimer = turn.Send(turn.Instance.Ref, MigrationPlanControl.Deadline, dueAt: state.Request.Deadline);
            }

            if (message.Kind == MigrationPlanResult.Kind)
            {
                RecordAnswer(turn, state, message);
                continue;
            }

            if (state.Closed) continue;
            if (turn.Now >= state.Request.Deadline)
            {
                Close(turn, state);
                continue;
            }

            if (message.Kind is not (MigrationPlanControl.Select or MigrationPlanControl.Acknowledge or
                MigrationPlanControl.Complete or MigrationPlanControl.Close)) continue;
            if (authority.Read(turn.Instance.Tenant, turn.Instance.Ref, message) is not { } control) continue;
            switch (message.Kind)
            {
                case MigrationPlanControl.Select:
                    Select(turn, state, control);
                    break;
                case MigrationPlanControl.Acknowledge when state.Pending == control.Number:
                    state.Pending = null;
                    turn.Record(new MigrationPlanJournal.Dispatched(control.Number));
                    break;
                case MigrationPlanControl.Complete when state.AllSelected && state.Pending is null && state.Number == control.Number:
                case MigrationPlanControl.Close:
                    Close(turn, state);
                    break;
            }
        }
        return ValueTask.CompletedTask;
    }

    private static void Select(ITurn turn, PlanState state, MigrationPlanControl control)
    {
        if (state.AllSelected || state.Pending is not null || state.Number == long.MaxValue ||
            control.Number != state.Number + 1 || control.Before != state.Cursor ||
            control.After is { } after && after.Value is not { Length: > 0 and <= 1_024 } ||
            control.Actors is not { Length: <= BatchLimit } actors ||
            actors.Any(actor => actor == turn.Instance.Ref || actor.Id is not { Length: > 0 and <= 1_024 }) ||
            actors.Distinct().Count() != actors.Length) return;

        var cursorAdvances = control.AllSelected ? control.After is null : control.After is not null && control.After != control.Before;
        if (control.KeepCursor
            ? control.AllSelected || control.After != control.Before || actors.Length != BatchLimit
            : !cursorAdvances) return;

        state.Number = control.Number;
        state.Cursor = control.After;
        state.AllSelected = control.AllSelected;
        if (actors.Length > 0) state.Pending = control.Number;
        turn.Record(new MigrationPlanJournal.Selected(actors, control.Number, control.After, control.AllSelected));
    }

    private static void RecordAnswer(ITurn turn, PlanState state, Message message)
    {
        if (message is not { Sender: { } actor, Payload: { } payload } || actor == turn.Instance.Ref) return;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(nameof(MigrationPlanResult.Outcome), out _) ||
                document.RootElement.Deserialize<MigrationPlanResult>() is not { } result || !Enum.IsDefined(result.Outcome)) return;
            switch (result.Outcome)
            {
                case MigrationPlanOutcome.Migrated:
                    if (state.Migrated < long.MaxValue) state.Migrated++;
                    break;
                case MigrationPlanOutcome.Refused:
                    if (state.Refused < long.MaxValue) state.Refused++;
                    break;
                case MigrationPlanOutcome.NotAtSource:
                    if (state.NotAtSource < long.MaxValue) state.NotAtSource++;
                    break;
            }
            turn.Record(new MigrationPlanJournal.Answered(actor, result.Outcome));
        }
        catch (JsonException) { }
    }

    private static void Close(ITurn turn, PlanState state)
    {
        if (turn.Now < state.Request.Deadline && state.DeadlineTimer is { } timer)
            turn.Recall(timer);
        state.Closed = true;
        state.Pending = null;
        turn.Record(new MigrationPlanJournal.Closed());
        turn.SetStatus(ActorStatus.Finished);
    }

    public bool HandlesMetadata(Message message) => message.Kind == MigrationPlanResult.Kind;
    public ValueTask MetadataAsync(ITurn turn, ActorStatus status, long? endedAt, CancellationToken cancellationToken)
    {
        if (status is ActorStatus.Finished or ActorStatus.Cancelled && turn.State is PlanState { Closed: true } state)
            foreach (var message in turn.Inbox) RecordAnswer(turn, state, message);
        return ValueTask.CompletedTask;
    }

    public void TerminateMetadata(object state)
    {
        var plan = (PlanState)state;
        plan.Closed = true;
        plan.Pending = null;
    }
    public bool ProtectsDeletion(object state, long? endedAt, long now) => false;
    public object UpgradeState(string state, int fromVersion) => fromVersion == 1
        ? JsonSerializer.Deserialize<PlanState>(state) ?? throw new InvalidOperationException("The migration plan state is absent.")
        : throw new NotSupportedException();
    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    public RestorePlan RestoreWaits(InstanceInfo instance, object state) =>
        state is PlanState { Closed: false, DeadlineTimer: { } timer } plan
            ? new([new(timer, MigrationPlanControl.Deadline, null, plan.Request.Deadline)], [], [])
            : new([], [], []);

    // Pending can grow from a single digit to null while Closed shrinks from false to true.
    public long TerminalGrowth(InstanceInfo instance, object state) => 2;

    private sealed record PlanState(MigrationPlanStart Request)
    {
        public long Number { get; set; }
        public string? DeadlineTimer { get; set; }
        public long? Pending { get; set; }
        public PageToken? Cursor { get; set; }
        public bool AllSelected { get; set; }
        public bool Closed { get; set; }
        public long Migrated { get; set; }
        public long Refused { get; set; }
        public long NotAtSource { get; set; }
    }
}
