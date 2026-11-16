using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Reintroduces the ordinary cancellation input for physical work left by the old final-fault writer.</summary>
internal sealed class BpmnLegacyFaultWorkRecovery(IPersistenceScopeRunner scopes, TimeProvider clock) : IStartupTask
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(CancellationToken cancellationToken) =>
        await scopes.RunAsync(RecoverAsync, cancellationToken);

    private async ValueTask RecoverAsync(PersistenceScope scope, PersistenceOperationScope operation, CancellationToken cancellationToken)
    {
        var reader = operation.ServiceProvider.GetService<IRuntimeModelExecutionReader>();
        if (reader is null) return; // Native-only providers have no accepted model state to recover.
        var queue = operation.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        string? continuation = null;
        do
        {
            var page = await reader.ListPageAsync(new(continuationToken: continuation), cancellationToken);
            foreach (var model in page.Items)
            {
                if (model.Protocol.Key != BpmnModelProtocol.Key || model.Protocol.Version != BpmnModelProtocol.Version ||
                    model.Lifecycle != RuntimeModelLifecycle.TerminalSettled) continue;
                if (model.TerminalResult is not { ValueKind: JsonValueKind.Object } result ||
                    !result.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
                    throw new BpmnModelValidationException($"BPMN fault recovery for execution '{model.ExecutionId}' " +
                        $"in scope '{scope.Value}' has an invalid owned terminal result.");
                if (status.GetString() != "Faulted") continue;
                var document = BpmnDirectExecutionDriver.Read(model);
                if (!document.Work.Any(work => work.NativeWork is not null)) continue;
                if (!queue.SupportsClaimTransitions || !queue.SupportsClaimValidation)
                    throw new NotSupportedException("Accepted BPMN fault recovery requires the qualified physical input queue.");

                // The accepted state owns this debt. Preserve its pin, incarnation and authority, and let the
                // existing fenced decision path record cancellation without replacing the semantic fault.
                var inputId = "bpmn-fault-recovery:" + BpmnModelIdentity.Create(scope.Value, model.ExecutionId,
                    model.Revision.ToString(CultureInfo.InvariantCulture));
                var command = new RuntimeModelExecutionCommand(model.Executable, model.MaterialIncarnationId,
                    inputId, "cancel", JsonSerializer.SerializeToElement(new { }), model.Authority, model.TenantId);
                var now = clock.GetUtcNow();
                await queue.EnqueueAsync(new(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(inputId), model.ExecutionId,
                    inputId, WorkflowExecutionCommandKind.ExecuteModel, inputId, inputId, now, now,
                    payload: JsonSerializer.SerializeToElement(command, Web)), cancellationToken);
            }
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
    }
}
