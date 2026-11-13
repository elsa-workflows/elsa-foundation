using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

internal sealed partial class RuntimeExecutionProtocolRouter
{
    internal async ValueTask<WorkflowExecutionCommandDispatchResult> AdmitAuthoredAsync(AuthoredModelAdmissionRequest request,
        IAuthoredModelQueue participant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(request.Command); ArgumentNullException.ThrowIfNull(request.Material);
        if (request.Material.Model is null) throw new NotSupportedException("Authored model admission cannot reinterpret native material.");
        var command = request.Command;
        var envelope = "authored-model:" + RuntimeCheckpointCommitFingerprint.ComputeModelInput(command);
        using var admission = admissionController?.TryAdmit();
        if (admission is { IsAdmitted: false })
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.DispatchShed] = "true"
            };
            if (admission.RetryAfter is { } retryAfter)
                metadata[RuntimeMetadataKeys.DispatchRetryAfterSeconds] = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
            return new(envelope, request.WorkflowExecutionId, WorkflowExecutionCommandDispatchStatus.Deferred,
                timeProvider.GetUtcNow(), admission.Reason ?? "Runtime dispatch admission is at capacity.", metadata);
        }
        var work = new RuntimeSchedulerWorkItem(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(command.InputId),
            request.WorkflowExecutionId, command.InputId, WorkflowExecutionCommandKind.ExecuteModel, envelope, envelope,
            request.RecordedAt, request.RecordedAt, payload: JsonSerializer.SerializeToElement(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var selected = await selector.SelectAsync(work, cancellationToken);
        if (selected.Context.Material.IncarnationId != request.Material.IncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(selected.Context.Material.Identity, request.Material.Identity))
            throw new RuntimeCheckpointReplayConflictException(command.InputId);
        await selected.Driver.ValidateInputAsync(selected.Context, cancellationToken);
        WorkflowExecutionCommandDispatchResult? result = null;
        // Reuse the ordinary router's complete material closure leases; the physical participant fences them
        // in the same transaction that checks the slot/source and writes the actual ordinary queue row.
        await materialLeases.ExecuteFencedAsync([request.Material.Identity], "authored-model:" + work.WorkItemId + ":" + Guid.NewGuid().ToString("N"),
            async (grants, token) => { result = await participant.AdmitAsync(request, work, grants, token); }, cancellationToken);
        return result ?? throw new InvalidOperationException("The protected authored queue participant returned no result.");
    }
}
