using System.Globalization;
using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Owns interpretation of private BPMN state and accepted-input meaning for optional native integrations.</summary>
internal sealed class BpmnNativeWorkConsumer(BpmnNativeWorkBoundary boundary, IWorkflowExecutableStore materials,
    IPersistenceAccessContextAccessor access, IRuntimeModelExecutionReader? models = null) : IBpmnNativeWorkConsumer
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private IRuntimeModelExecutionReader Models => models ?? throw BpmnNativeWorkBoundary.Unavailable("The selected host has no model owner reader.");

    public async ValueTask<BpmnNativeWorkAddress> ResolveAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default)
    {
        try { return (await ResolveCoreAsync(origin, cancellationToken)).Address; }
        catch (JsonException error) { throw Invalid(origin, error); }
    }

    public async ValueTask<BpmnNativeWorkObservation> InspectAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolved = await ResolveCoreAsync(origin, cancellationToken);
            var (state, address, document) = resolved;
            var receipt = await Models.FindInputAsync(state.ExecutionId, origin.ResultInputId, cancellationToken);
            var result = document.NativeResults?.SingleOrDefault(item => item.Origin.IsEquivalentTo(origin));
            if (receipt is not null)
            {
                if (receipt.Scope != state.Scope || receipt.ExecutionId != state.ExecutionId || receipt.InputId != origin.ResultInputId ||
                    receipt.InputKind != "native-result" || receipt.InvocationId != origin.InvocationId || receipt.AttemptId != origin.AttemptId ||
                    receipt.AcceptedRevision > state.Revision || receipt.Result.ValueKind != JsonValueKind.Object ||
                    !receipt.Result.TryGetProperty("status", out var accepted) || accepted.ValueKind != JsonValueKind.String)
                    throw Invalid(origin);
                if (accepted.GetString() == "Accepted")
                {
                    if (result is null) throw Invalid(origin);
                    await boundary.ValidateRetainedResultAsync(state, result, cancellationToken);
                    var command = ResultCommand(state, result);
                    if (receipt.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command)) throw Invalid(origin);
                    var input = new BpmnNativeWorkInput(receipt.InputId, receipt.ContentFingerprint, receipt.AcceptedRevision,
                        receipt.AcceptedAt, Project(result));
                    return new(address, BpmnNativeWorkObservationKind.AcceptedRootInput, state.Revision, state.Lifecycle,
                        TerminalStatus(state), InputReceiptId(state, receipt), input, TerminalReceiptId(state));
                }
                if (accepted.GetString() != "Rejected") throw Invalid(origin);
                // A matching rejected input can settle transport; it is never semantic consumption proof.
            }
            if (state.Lifecycle is RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled)
            {
                var status = TerminalStatus(state) ?? throw Invalid(origin);
                var identity = TerminalReceiptId(state)!;
                return new(address, BpmnNativeWorkObservationKind.RootTerminal, state.Revision, state.Lifecycle, status, identity, null, identity);
            }
            return new(address, BpmnNativeWorkObservationKind.NotConsumed, state.Revision, state.Lifecycle, null, null, null);
        }
        catch (JsonException error) { throw Invalid(origin, error); }
    }

    private async ValueTask<Resolved> ResolveCoreAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        boundary.EnsureQualified(); access.Current.EnsureScope(origin.Scope);
        if (origin.Protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) ||
            origin.ResultIntentKind != BpmnNativeWorkBoundary.ResultKind || origin.WorkerExecutionId !=
            BpmnNativeWorkBoundary.WorkerIdentity(origin.Scope, origin.ExecutionId, origin.InvocationId, origin.AttemptId, origin.WorkerExecutable).WorkflowExecutionId)
            throw Invalid(origin);
        var state = await Models.FindAsync(origin.ExecutionId, cancellationToken)
            ?? throw BpmnNativeWorkBoundary.Unavailable("The native service's authorizing BPMN owner is unavailable.");
        access.Current.EnsureTenantScope(state.TenantId);
        if (!origin.MatchesModel(state)) throw Invalid(origin);
        var material = await materials.FindMaterialAsync(state.Executable.ArtifactId, cancellationToken);
        if (material?.Model is not { } model || material.IncarnationId != state.MaterialIncarnationId || model.Protocol != state.Protocol ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, state.Executable))
            throw BpmnNativeWorkBoundary.Unavailable("The exact BPMN owner material is unavailable.");
        var definition = BpmnDirectExecutionDriver.Definition(model);
        var bindings = definition.ServiceBindings?.Where(item => item.BindingId == origin.BindingId).ToArray() ?? [];
        if (bindings.Length != 1 || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(bindings[0].Executable, origin.WorkerExecutable) ||
            !model.Dependencies.Any(item => item.BindingId == origin.BindingId && WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(item.Executable, origin.WorkerExecutable)))
            throw Invalid(origin);
        var process = definition.Process.Deserialize<BpmnProcessDefinition>(Web) ?? throw Invalid(origin);
        var elements = process.Elements.Where(item => item.BindingRef == origin.BindingId && item.ElementType == BpmnElementTypes.ServiceTask).ToArray();
        if (elements.Length != 1 || string.IsNullOrWhiteSpace(process.ProcessId)) throw Invalid(origin);
        var document = BpmnDirectExecutionDriver.Read(state);
        if (!document.Work.Any(item => origin.IsEquivalentTo(item.NativeWork)) &&
            document.NativeResults?.Any(item => origin.IsEquivalentTo(item.Origin)) != true &&
            document.Cancellations?.Any(item => origin.IsEquivalentTo(item.Origin)) != true)
            throw Invalid(origin);
        // This read authenticates the immutable source even after successful delivery.
        // RequireLiveAsync is intentionally stricter because it authorizes initial native admission.
        await boundary.RequireStartSourceAsync(origin, cancellationToken);
        var workerMaterial = await materials.FindMaterialAsync(origin.WorkerExecutable.ArtifactId, cancellationToken);
        if (workerMaterial?.NativeWorkflow is not { } native || native.InputContract is not { Inputs.Count: 0 } ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workerMaterial.Identity, origin.WorkerExecutable) ||
            native.RootActivity.ActivityContract is not { } contract)
            throw BpmnNativeWorkBoundary.Unavailable("The exact closed native service material is unavailable.");
        var worker = await boundary.FindWorkerAsync(origin, cancellationToken);
        if (worker is not null && worker.TenantId != state.TenantId) throw Invalid(origin);
        var root = native.RootActivity;
        var address = new BpmnNativeWorkAddress(1, origin, state.TenantId, process.ProcessId, elements[0].ElementId,
            root.ExecutableNodeId, root.ActivityType, root.ActivityTypeVersion,
            new(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint), bindings[0].FaultPolicy);
        return new(state, address, document);
    }

    private static RuntimeModelExecutionCommand ResultCommand(RuntimeModelExecutionState state, BpmnNativeWorkResult result) =>
        new(state.Executable, state.MaterialIncarnationId, result.Origin.ResultInputId, "native-result",
            JsonSerializer.SerializeToElement(result, Web), state.Authority, state.TenantId);

    internal static BpmnNativeWorkResultSnapshot Project(BpmnNativeWorkResult result) => new(result.SourceOutboxItemId, result.Status,
        result.Completion is { } value ? new(value.InvocationId, value.AttemptId, value.ContractFingerprint, value.OutcomeKey,
            value.ResultType, value.Result, value.IsRedacted) : null, result.FaultCode);

    private static string InputReceiptId(RuntimeModelExecutionState state, RuntimeModelInputReceipt input) =>
        "bpmn-native-owner-input:" + BpmnModelIdentity.Create(state.Scope.Value, state.TenantId ?? "", state.ExecutionId,
            state.Executable.ArtifactId, state.MaterialIncarnationId, input.InputId, input.ContentFingerprint,
            input.AcceptedRevision.ToString(CultureInfo.InvariantCulture));

    internal static string? TerminalStatus(RuntimeModelExecutionState state)
    {
        if (state.TerminalResult is not { } terminal) return null;
        if (!terminal.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String ||
            status.GetString() is not ("Completed" or "Faulted" or "Cancelled")) throw new BpmnModelValidationException("The BPMN owner has an invalid terminal classification.");
        return status.GetString();
    }

    internal static string? TerminalReceiptId(RuntimeModelExecutionState state) => (state.Lifecycle is
        RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled) && TerminalStatus(state) is not null
        ? "bpmn-native-owner-terminal:" + BpmnModelIdentity.Create(state.Scope.Value, state.TenantId ?? "", state.ExecutionId,
            state.Executable.ArtifactId, state.Executable.ArtifactHash!, state.MaterialIncarnationId,
            state.Revision.ToString(CultureInfo.InvariantCulture), state.Lifecycle.ToString(), state.TerminalResult!.Value.GetRawText())
        : null;

    private static BpmnModelValidationException Invalid(RuntimeModelNativeWorkOrigin origin, Exception? inner = null) =>
        new($"Native service invocation '{origin.InvocationId}' does not match its exact retained BPMN owner '{origin.ExecutionId}'.", inner);

    private sealed record Resolved(RuntimeModelExecutionState State, BpmnNativeWorkAddress Address, BpmnDirectExecutionDriver.Document Document);
}
