using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

namespace Kernel.WorkflowActor;

public sealed partial class WorkflowActor
{
    internal const int DefinitionLimit = 1_024;
    private const int HeaderLimit = 16 * 1_024;
    private const string AnsweredPlanLabel = "migration-plan";

    private void ApplyMigration(IActorTurn turn, WorkflowHeader workflow, Message command)
    {
        MigrationRequest? request;
        try { request = command.Payload is { } payload ? JsonSerializer.Deserialize<MigrationRequest>(payload) : null; }
        catch (JsonException) { request = null; }
        if (request is not { Plan.Id.Length: > 0 } || request.Plan == turn.Header.Ref)
        {
            turn.Record(new MigrationPlanResult(MigrationPlanOutcome.Refused, "InvalidRequest"));
            return;
        }

        _models.TryGetValue(workflow.Model, out var model);
        var unreadable = model is null
            ? workflow.Version is not null || turn.Header.Status == ActorStatus.Running
            : workflow.Version > model.StateVersion;
        if (unreadable)
        {
            turn.Hold(command);
            return;
        }

        var plan = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Plan.Id)));
        if (turn.Header.Labels.GetValueOrDefault(AnsweredPlanLabel) == plan) return;

        object? state = null;
        MigrationPlanResult result;
        var next = workflow;
        if (request is not { From.Length: > 0, To.Length: > 0 })
            result = new(MigrationPlanOutcome.Refused, "InvalidRequest");
        else if (turn.Header.Status != ActorStatus.Running)
            result = new(MigrationPlanOutcome.Refused, "Ended");
        else if (workflow.Definition != request.From)
            result = new(MigrationPlanOutcome.NotAtSource);
        else if (turn.State is null || workflow.Ephemeral)
            result = new(MigrationPlanOutcome.Refused, "UnavailableCheckpoint");
        else if (model is not ISimpleModelMigration migration)
            result = new(MigrationPlanOutcome.Refused, "ModelUnsupported");
        else if (Encoding.UTF8.GetByteCount(request.To) > DefinitionLimit || request.From == request.To)
            result = new(MigrationPlanOutcome.Refused, "InvalidDefinition");
        else
        {
            state = ReadState(model, workflow, turn.State, turn.Inbox);
            if (migration.ValidateMigration(Instance(turn.Header, workflow), state, request.To) is { } finding)
                result = new(MigrationPlanOutcome.Refused, finding);
            else
            {
                next = next with { Definition = request.To, Version = model.StateVersion };
                result = new(MigrationPlanOutcome.Migrated);
            }
        }

        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(next)) > HeaderLimit)
        {
            result = new(MigrationPlanOutcome.Refused, "HeaderLimit");
            next = workflow;
        }

        // Send validates the plan's exact reference in the same tenant. An invalid address cannot change the definition.
        try { turn.Send(request.Plan, MigrationPlanResult.Kind, JsonSerializer.Serialize(result)); }
        catch (ArgumentException error) when (error.ParamName == "target")
        {
            turn.Record(new MigrationPlanResult(MigrationPlanOutcome.Refused, "PlanUnavailable"));
            return;
        }

        if (result.Outcome == MigrationPlanOutcome.Migrated)
        {
            if (workflow.Version != model!.StateVersion)
                turn.State = JsonSerializer.Serialize(state, model.StateType);
            turn.Record(ActorLifecycle.Migrated);
        }
        WriteHeader(turn, workflow, next, plan);
        turn.Record(new
        {
            Migration = result.Outcome,
            result.Finding,
            From = workflow.Definition,
            Target = result.Outcome == MigrationPlanOutcome.Migrated ? request.To : null,
            Plan = request.Plan.Id
        });
    }
}
