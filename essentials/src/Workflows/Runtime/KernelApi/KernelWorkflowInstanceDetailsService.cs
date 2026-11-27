using System.Collections.Frozen;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelWorkflowInstanceDetailsService(
    KernelRunReader runs,
    IActivityInspectionContextAsync authorization,
    WorkflowInstanceDetailsService? legacy = null) : IWorkflowInstanceDetailsService
{
    // A kernel turn commits one durable boundary and can execute several activities within it.
    private static readonly RuntimePersistenceModeProjection Balanced = RuntimePersistenceModeProjection.For(PersistenceMode.Balanced);
    private static readonly RuntimePersistenceModeProjection Ephemeral = RuntimePersistenceModeProjection.For(PersistenceMode.Ephemeral);
    private static readonly IReadOnlyDictionary<string, WorkflowOutputView> Outputs = FrozenDictionary<string, WorkflowOutputView>.Empty;

    public async Task<WorkflowInstanceDetailsView?> GetAsync(GetWorkflowInstance request, CancellationToken cancellationToken)
    {
        if (await runs.GetAsync(request.WorkflowExecutionId, cancellationToken) is not { } run) return null;
        if (run.Header is null) return legacy is null ? null : await legacy.GetAsync(run.State, request, cancellationToken);
        var state = run.State;
        if (!await authorization.CanInspectStructureAsync(state, cancellationToken)) return null;

        var canInspectSensitive = await authorization.CanInspectSensitiveValuesAsync(state, cancellationToken);
        var mode = run.Header.Ephemeral ? Ephemeral : Balanced;
        return new WorkflowInstanceDetailsView(
            WorkflowInstanceSummaryView.From(state, canInspectSensitiveValues: canInspectSensitive),
            [],
            [],
            ProjectOutputs(await runs.ReadTerminalOutputAsync(run, canInspectSensitive, cancellationToken), canInspectSensitive),
            mode.PersistenceMode,
            mode.InspectionGranularity,
            // The runtime's existing operator endpoints still address legacy executions.
            Permissions: new WorkflowInstancePermissionsView(CanManage: false, CanInspectValues: canInspectSensitive))
            { ActivitiesDeferred = KernelActivityTimeline.Supports(run.Header) };
    }

    private static IReadOnlyDictionary<string, WorkflowOutputView> ProjectOutputs(ActorTerminalOutput? output, bool canInspectSensitive)
    {
        if (output is null) return Outputs;
        var at = DateTimeOffset.FromUnixTimeMilliseconds(output.CapturedAt);
        if (canInspectSensitive && output.Values is { } bytes)
        {
            using var values = JsonDocument.Parse(bytes);
            return values.RootElement.EnumerateObject().ToDictionary(value => value.Name,
                value => new WorkflowOutputView(value.Value.Clone(), false, null, at) { CaptureMode = nameof(RuntimePayloadCaptureMode.Payload) },
                StringComparer.Ordinal);
        }

        var names = JsonSerializer.Deserialize<string[]>(output.Names) ?? [];
        var placeholder = canInspectSensitive && output.Omitted
            ? new WorkflowOutputView(null, true, "The retained workflow output is unavailable.", at)
            : WorkflowOutputView.Redacted(at);
        return names.ToDictionary(name => name, _ => placeholder, StringComparer.Ordinal);
    }
}
