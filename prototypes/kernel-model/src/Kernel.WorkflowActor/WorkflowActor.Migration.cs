using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

namespace Kernel.WorkflowActor;

public sealed partial class WorkflowActor
{
    private const int DefinitionLimit = 1_024;
    private const int HeaderLimit = 16 * 1_024;

    private void ApplyMigration(IActorTurn turn, WorkflowHeader workflow, Message command)
    {
        MigrationRequest? request;
        try { request = command.Payload is { } payload ? JsonSerializer.Deserialize<MigrationRequest>(payload) : null; }
        catch (JsonException) { request = null; }
        if (request is not { From.Length: > 0, To.Length: > 0, Plan.Id.Length: > 0 } || request.Plan == turn.Header.Ref)
        {
            turn.Record(new MigrationPlanResult(MigrationPlanOutcome.Refused, "InvalidRequest"));
            return;
        }

        var plan = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Plan.Id)));
        if (workflow.LastMigrationPlan == plan) return;

        object? state = null;
        IExecutionModel? model = null;
        MigrationPlanResult result;
        var next = workflow with { LastMigrationPlan = plan };
        if (turn.Header.Status != ActorStatus.Running)
            result = new(MigrationPlanOutcome.Refused, "Ended");
        else if (workflow.Definition != request.From)
            result = new(MigrationPlanOutcome.NotAtSource);
        else if (!_models.TryGetValue(workflow.Model, out model) || workflow.Version > model.StateVersion)
        {
            turn.Hold(command);
            return;
        }
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
            // A legacy model header at the cap cannot retain the plan marker or rebind.
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

        if (result.Outcome == MigrationPlanOutcome.Migrated && workflow.Version != model!.StateVersion)
            turn.State = JsonSerializer.Serialize(state, model.StateType);
        WriteHeader(turn, workflow, next);
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
