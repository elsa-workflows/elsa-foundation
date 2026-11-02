using System.Text;
using System.Text.Json;
using Bpmn.Model;
using Elsa.Primitives.Models;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnModelRuntime(IWorkflowExecutableStore materials, IWorkflowExecutableSourceReferenceStore sources,
    IWorkflowExecutableRootWriteLeaseManager leases, BpmnDirectExecutionDriver driver,
    IWorkflowExecutionActorProvider actors, IPersistenceAccessContextAccessor access, IRuntimeRecoveryContinuationCodec continuations,
    TimeProvider clock, IWorkflowSchedulerWorkQueue queue, ILogger<BpmnModelRuntime>? logger = null,
    IRuntimeModelExecutionReader? states = null) : IBpmnModelRuntime
{
    private const string SourceKind = "BpmnModel";
    private const string WorkPagePurpose = "bpmn-human-work-v1";
    private const string IncidentPagePurpose = "bpmn-service-incidents-v1";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private IRuntimeModelExecutionReader States => states ?? throw new RuntimeSchedulerWorkUnavailableException(
        "bpmn.state-unavailable", "The selected host has no model state reader.");
    public bool AdmitNewExecutions => driver.AdmitNewExecutions;

    public async ValueTask<BpmnPublishedModel> PublishAsync(BpmnModelDefinition definition, string requestId, BpmnModelCaller caller,
        CancellationToken cancellationToken = default)
    {
        try { return await PublishCoreAsync(definition, requestId, caller, cancellationToken); }
        catch (JsonException exception)
        {
            throw new BpmnModelValidationException($"BPMN publication '{requestId}' has invalid definition JSON.", exception);
        }
    }

    public async ValueTask<BpmnPublishedModel> PublishFromSourceAsync(BpmnModelDefinition definition, BpmnModelPublicationProvenance source,
        string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try { return await PublishCoreAsync(definition, requestId, caller, cancellationToken, source); }
        catch (JsonException exception)
        {
            throw new BpmnModelValidationException($"BPMN publication '{requestId}', source version '{source.VersionId}' has invalid definition JSON.", exception);
        }
    }

    private async ValueTask<BpmnPublishedModel> PublishCoreAsync(BpmnModelDefinition definition, string requestId, BpmnModelCaller caller,
        CancellationToken cancellationToken, BpmnModelPublicationProvenance? source = null)
    {
        Scope(caller); InputId(requestId);
        if (!materials.SupportsModelMaterial || !sources.CanWriteRootReferences(materials))
            throw new NotSupportedException("The selected artifact/source providers cannot publish model material safely.");
        var seed = new WorkflowExecutableIdentity("pending", "pending", "pending", "1.0.0", "pending");
        var dependencies = (definition.ServiceBindings ?? []).Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable))
            .Concat((definition.NativeEventBindings ?? []).Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable))).ToArray();
        foreach (var binding in definition.ServiceBindings ?? [])
            await ValidatePublishedWorkerAsync(binding.BindingId, binding.Executable, caller, cancellationToken);
        foreach (var binding in definition.NativeEventBindings ?? [])
        {
            var worker = await ValidatePublishedWorkerAsync(binding.BindingId, binding.Executable, caller, cancellationToken);
            var process = definition.Process.Deserialize<BpmnProcessDefinition>(Web) ?? throw new BpmnModelValidationException("BPMN process definition is missing.");
            var catches = process.Elements.Where(element => element.BindingRef == binding.BindingId).ToArray();
            if (catches.Length != 1 || catches[0].ElementType != BpmnElementTypes.IntermediateCatchEvent ||
                catches[0].EventDefinitions.Count != 1 || catches[0].EventDefinitions.Single().Type != BpmnEventDefinitionTypes.Signal ||
                !catches[0].EventDefinitions.Single().Properties.TryGetValue(BpmnEventDefinitionProperties.Name, out var name))
                throw new BpmnModelValidationException("A native event binding requires one named signal catch.");
            ValidateEventWorker(worker.RootActivity, name);
        }
        var profile = definition.FormatVersion switch
        {
            1 => BpmnModelProtocol.Profile,
            6 => BpmnModelProtocol.ClaimsProfile,
            _ => BpmnModelProtocol.NativeWorkProfile
        };
        var material = new RuntimeModelMaterial(1, seed, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), profile,
            JsonSerializer.SerializeToElement(definition, Web), new(1, []), [], dependencies, []);
        await driver.ValidateMaterialAsync(material, cancellationToken);
        BpmnDirectExecutionDriver.ValidateTimerDeadlines(material, clock.GetUtcNow());
        var hash = material.ComputeContentHash();
        var pin = new WorkflowExecutableIdentity(hash, "bpmn-model:" + hash, hash, "1.0.0", hash);
        material = new(1, pin, material.Protocol, material.Profile, material.Definition, material.InputContract, [], dependencies, []);
        var sourceId = "bpmn-source:" + (source is null
            ? BpmnModelIdentity.Create(caller.TenantId ?? "", caller.ActorId, requestId)
            : BpmnModelIdentity.Create("authored-v1", caller.TenantId ?? "", caller.ActorId, requestId));
        var at = clock.GetUtcNow();
        var reference = new WorkflowExecutableSourceReference(sourceId, hash, SourceKind, source?.VersionId ?? sourceId, source?.Version ?? "1",
            pin.DefinitionId, pin.DefinitionVersionId, pin.ArtifactVersion, at, at, WorkflowExecutableReferenceScope.Published,
            Layout: source?.Layout, TenantId: caller.TenantId, ActivityPresentation: source?.ActivityPresentation);
        if (await sources.FindAsync(sourceId, cancellationToken) is { } prior &&
            (prior.ArtifactId != hash || prior.TenantId != caller.TenantId || prior.SourceKind != SourceKind || !prior.IsLive(clock.GetUtcNow()) ||
             source is not null && !SameAuthoredSource(prior)))
            throw new RuntimeCheckpointReplayConflictException("publish:" + requestId);
        await materials.SaveModelAsync(material, cancellationToken);
        var snapshot = await materials.FindMaterialAsync(hash, cancellationToken) ?? throw new BpmnModelValidationException("Published model material could not be read back.");
        at = clock.GetUtcNow();
        reference = reference with { CreatedAt = at, PublishedAt = at };
        try
        {
            await leases.ExecuteFencedAsync([pin], "bpmn-publish:" + BpmnModelIdentity.Create(sourceId), async (grants, token) =>
                _ = await sources.SaveWithRootWriteLeasesAsync(reference, pin, grants, token), cancellationToken);
        }
        catch (InvalidOperationException) when (source is not null)
        {
            // The store arbitrates concurrent publications atomically. Map only a proved competing
            // source winner to a replay conflict; unrelated persistence failures retain their cause.
            if (await sources.FindAsync(sourceId, cancellationToken) is { } winner &&
                (!winner.IsLive(clock.GetUtcNow()) || !SameAuthoredSource(winner)))
                throw new RuntimeCheckpointReplayConflictException("publish:" + requestId);
            throw;
        }
        return new(pin, snapshot.IncarnationId, sourceId, material.Profile);

        bool SameAuthoredSource(WorkflowExecutableSourceReference current) =>
            WorkflowExecutableSourceReferenceComparer.SameIdentity(current,
                reference with { CreatedAt = current.CreatedAt, PublishedAt = current.PublishedAt });
    }

    public async ValueTask<BpmnModelCommandOutcome> StartAsync(string sourceReferenceId, string requestId,
        IReadOnlyDictionary<string, JsonElement>? inputs, BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        var scope = Scope(caller); InputId(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        var execution = "bpmn:" + BpmnModelIdentity.Create(scope.Value, caller.TenantId ?? "", caller.ActorId, requestId);
        var payload = JsonSerializer.SerializeToElement(new { inputs = inputs ?? new Dictionary<string, JsonElement>(), sourceReferenceId }, Web);
        RuntimeModelExecutionCommand Command(WorkflowExecutableIdentity pin, string incarnation) =>
            new(pin, incarnation, requestId, "start", payload, new("bpmn-runtime", caller.ActorId), caller.TenantId);
        // Check the queue before the receipt so consumption between these reads still finds
        // its committed outcome. Retirement prevents fresh admission, not accepted-work replay.
        if (await PendingAsync(execution, requestId, caller, cancellationToken) is { } pending)
            return await DispatchAsync(execution, Command(pending.Executable, pending.MaterialIncarnationId), caller, cancellationToken);
        if (await States.FindInputAsync(execution, requestId, cancellationToken) is not null)
        {
            var retained = await FindExecutionAsync(execution, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
            return await DispatchAsync(execution, Command(retained.Executable, retained.MaterialIncarnationId), caller, cancellationToken);
        }
        var source = await sources.FindAsync(sourceReferenceId, cancellationToken);
        if (source is null || source.SourceKind != SourceKind || source.TenantId != caller.TenantId || !source.IsLive(clock.GetUtcNow()))
            throw new BpmnModelResourceMissingException();
        var material = await materials.FindMaterialAsync(source.ArtifactId, cancellationToken);
        if (material?.Model is null || material.Identity.DefinitionId != source.DefinitionId ||
            material.Identity.DefinitionVersionId != source.DefinitionVersionId || material.Identity.ArtifactVersion != source.ArtifactVersion)
            throw new BpmnModelValidationException("The selected source does not name available pinned model material.");
        return await DispatchAsync(execution, Command(material.Identity, material.IncarnationId), caller, cancellationToken);
    }

    public async ValueTask<RuntimeModelExecutionState?> FindExecutionAsync(string executionId, BpmnModelCaller caller,
        CancellationToken cancellationToken = default)
    {
        Scope(caller);
        var state = await States.FindAsync(executionId, cancellationToken);
        return state is not null && state.TenantId == caller.TenantId && state.Protocol == new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version)
            ? state : null;
    }

    public async ValueTask<BpmnModelInspection> InspectAsync(string executionId, BpmnModelCaller caller,
        CancellationToken cancellationToken = default)
    {
        var scope = Scope(caller);
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        if (state.Scope != scope) throw new BpmnModelResourceMissingException();
        var material = await materials.FindMaterialAsync(state.Executable.ArtifactId, cancellationToken);
        if (material?.Model is not { } model || material.IncarnationId != state.MaterialIncarnationId || model.Protocol != state.Protocol ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, state.Executable))
            throw new BpmnModelValidationException("The exact material for this BPMN execution is unavailable.");
        try
        {
            var definition = BpmnDirectExecutionDriver.Definition(model);
            var document = BpmnDirectExecutionDriver.Read(state);
            BpmnInspectionProvenance? source = null;
            if (document.SourceReferenceId is { } referenceId && await sources.FindAsync(referenceId, cancellationToken) is { } reference &&
                reference.TenantId == caller.TenantId && reference.SourceKind == SourceKind && reference.ArtifactId == state.Executable.ArtifactId &&
                reference.DefinitionId == state.Executable.DefinitionId && reference.DefinitionVersionId == state.Executable.DefinitionVersionId &&
                reference.ArtifactVersion == state.Executable.ArtifactVersion)
                source = new(reference); // Accepted work retains its frozen provenance even after source retirement.
            var work = document.Work.Select(item => new BpmnInspectionWork(item.Timer is not null ? "Timer" : item.NativeWork is not null ? "Native" : "Human",
                item.ElementId, item.BindingId, item.TokenId, item.IterationId, item.InvocationId, item.AttemptId,
                item.NativeWork?.WorkerExecutionId, item.Timer?.DueAt)
                { ClaimRequired = definition.ClaimBindings?.Contains(item.BindingId, StringComparer.Ordinal) == true, Claim = CurrentClaim(document, item) }).ToArray();
            var attempts = InspectAttempts(state, model, document);
            var incidents = (document.Failures ?? []).OrderBy(item => item.IncidentId, StringComparer.Ordinal).Select(item => Incident(document, item)).ToArray();
            var settlements = (document.Cancellations ?? []).Select(item => new BpmnInspectionSettlement("NativeCancellation",
                item.Origin.WorkerExecutionId, item.Origin.InvocationId, item.Origin.AttemptId, item.Settled, item.NotAdmitted)).ToList();
            if (await driver.InspectParentSettlementAsync(state, document, cancellationToken) is { } parent) settlements.Add(parent);
            return new(state, model.Profile, definition.Process, source, work, attempts, incidents, settlements);
        }
        catch (JsonException exception)
        {
            throw new BpmnModelValidationException($"BPMN execution '{executionId}' has invalid inspection material '{state.Executable.ArtifactId}'.", exception);
        }
    }

    internal static IReadOnlyList<BpmnInspectionAttempt> InspectAttempts(RuntimeModelExecutionState state, RuntimeModelMaterial model,
        BpmnDirectExecutionDriver.Document document)
    {
        var allOrigins = document.Work.Where(item => item.NativeWork is not null).Select(item => item.NativeWork!)
            .Concat((document.NativeResults ?? []).Select(item => item.Origin))
            .Concat((document.Cancellations ?? []).Select(item => item.Origin))
            .ToArray();
        if (allOrigins.Any(origin => !origin.MatchesModel(state) ||
            origin.WorkerExecutionId != BpmnNativeWorkBoundary.WorkerIdentity(origin.Scope, state.ExecutionId, origin.InvocationId,
                origin.AttemptId, origin.WorkerExecutable).WorkflowExecutionId ||
            !model.Dependencies.Any(dependency => dependency.BindingId == origin.BindingId &&
                WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dependency.Executable, origin.WorkerExecutable))))
            throw new BpmnModelValidationException("The retained native attempt differs from this model and its pinned dependency.");
        var groups = allOrigins.GroupBy(origin => (origin.InvocationId, origin.AttemptId)).ToArray();
        if (groups.Any(group => group.Any(origin => !group.First().IsEquivalentTo(origin))))
            throw new BpmnModelValidationException("Conflicting native origins name the same model attempt.");
        var origins = groups.Select(group => group.First()).OrderBy(origin => origin.InvocationId, StringComparer.Ordinal).ThenBy(origin => origin.AttemptId, StringComparer.Ordinal);
        return origins.Select(origin =>
        {
            var result = document.NativeResults?.SingleOrDefault(item => item.Origin.IsEquivalentTo(origin));
            var cancellation = document.Cancellations?.SingleOrDefault(item => item.Origin.IsEquivalentTo(origin));
            var current = document.Work.SingleOrDefault(item => item.InvocationId == origin.InvocationId && item.AttemptId == origin.AttemptId);
            var failure = document.Failures?.SingleOrDefault(item => item.InvocationId == origin.InvocationId && item.FailedAttemptId == origin.AttemptId);
            var status = cancellation is { NotAdmitted: true } ? "NotAdmitted" : result?.Status.ToString() ??
                (cancellation is { Settled: false } ? "CancellationPending" : "Requested");
            return new BpmnInspectionAttempt(origin.BindingId, origin.InvocationId, origin.AttemptId, origin.WorkerExecutionId,
                origin.WorkerExecutable, status, current?.AttemptNumber ?? failure?.AttemptNumber, result?.Completion?.OutcomeKey, result?.FaultCode);
        }).ToArray();
    }

    public async ValueTask<RuntimeStorePage<BpmnHumanWork>> ListWorkAsync(string executionId, RuntimeStorePageRequest request,
        BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        var scope = Scope(caller);
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        var scopeKey = BpmnModelIdentity.Create(scope.Value, caller.TenantId ?? "");
        string? after = null;
        if (request.ContinuationToken is not null)
        {
            var cursor = JsonSerializer.Deserialize<WorkCursor>(continuations.Decode(WorkPagePurpose, request.ContinuationToken), Web)
                ?? throw new ArgumentException("The work continuation is invalid.");
            if (cursor.Scope != scopeKey || cursor.Execution != executionId || cursor.Revision != state.Revision)
                throw new ArgumentException("The work continuation belongs to a different execution snapshot.");
            after = cursor.After;
        }
        var document = BpmnDirectExecutionDriver.Read(state);
        var claimBindings = document.Claims is null ? null : await ClaimBindingsAsync(state, cancellationToken);
        var items = document.Work.Where(item => item.NativeWork is null && item.Timer is null).OrderBy(item => item.InvocationId, StringComparer.Ordinal)
            .Where(item => after is null || string.CompareOrdinal(item.InvocationId, after) > 0).Take(request.Limit + 1).ToArray();
        var more = items.Length > request.Limit;
        if (more) items = items[..request.Limit];
        var next = more ? continuations.Encode(WorkPagePurpose,
            JsonSerializer.SerializeToUtf8Bytes(new WorkCursor(scopeKey, executionId, state.Revision, items[^1].InvocationId), Web)) : null;
        return new(request, items.Select(item => new BpmnHumanWork(item.ElementId, item.BindingId, item.TokenId, item.IterationId, item.InvocationId, item.AttemptId)
            { ClaimRequired = claimBindings?.Contains(item.BindingId, StringComparer.Ordinal) == true, Claim = CurrentClaim(document, item) }).ToArray(), next);
    }

    private static BpmnHumanClaim? CurrentClaim(BpmnDirectExecutionDriver.Document document, BpmnDirectExecutionDriver.Work work) =>
        document.Claims?.SingleOrDefault(item => item.InvocationId == work.InvocationId && item.AttemptId == work.AttemptId) is { } claim
            ? new(claim.ClaimId, claim.ClaimantId, claim.ClaimedAt) : null;

    private async ValueTask<IReadOnlyList<string>> ClaimBindingsAsync(RuntimeModelExecutionState state, CancellationToken cancellationToken)
    {
        var material = await materials.FindMaterialAsync(state.Executable.ArtifactId, cancellationToken);
        if (material?.Model is not { } model || material.IncarnationId != state.MaterialIncarnationId || model.Protocol != state.Protocol ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, state.Executable))
            throw new BpmnModelValidationException("The exact material for this BPMN execution is unavailable.");
        return BpmnDirectExecutionDriver.Definition(model).ClaimBindings ?? [];
    }

    public async ValueTask<RuntimeStorePage<BpmnServiceIncident>> ListIncidentsAsync(string executionId, RuntimeStorePageRequest request,
        BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        var scope = Scope(caller);
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        var scopeKey = BpmnModelIdentity.Create(scope.Value, caller.TenantId ?? "");
        string? after = null;
        if (request.ContinuationToken is not null)
        {
            var cursor = JsonSerializer.Deserialize<WorkCursor>(continuations.Decode(IncidentPagePurpose, request.ContinuationToken), Web)
                ?? throw new ArgumentException("The incident continuation is invalid.");
            if (cursor.Scope != scopeKey || cursor.Execution != executionId || cursor.Revision != state.Revision)
                throw new ArgumentException("The incident continuation belongs to a different execution snapshot.");
            after = cursor.After;
        }
        var document = BpmnDirectExecutionDriver.Read(state);
        var items = (document.Failures ?? []).OrderBy(item => item.IncidentId, StringComparer.Ordinal)
            .Where(item => after is null || string.CompareOrdinal(item.IncidentId, after) > 0).Take(request.Limit + 1).ToArray();
        var more = items.Length > request.Limit;
        if (more) items = items[..request.Limit];
        var next = more ? continuations.Encode(IncidentPagePurpose,
            JsonSerializer.SerializeToUtf8Bytes(new WorkCursor(scopeKey, executionId, state.Revision, items[^1].IncidentId), Web)) : null;
        return new(request, items.Select(item => Incident(document, item)).ToArray(), next);
    }

    private static BpmnServiceIncident Incident(BpmnDirectExecutionDriver.Document document, BpmnDirectExecutionDriver.ServiceFailure item)
    {
        var result = document.NativeResults!.Single(result => result.Origin.ResultInputId == item.ResultInputId);
        return new(item.IncidentId, item.ElementId, item.BindingId, item.InvocationId, item.FailedAttemptId,
            result.Origin.WorkerExecutionId, result.FaultCode ?? "bpmn.native-worker-faulted", item.FailedAt, item.AttemptNumber, item.MaxAttempts, item.Status, item.RetryAttemptId);
    }

    public async ValueTask<BpmnModelCommandOutcome> RetryServiceAsync(string executionId, string requestId, string incidentId,
        string invocationId, string failedAttemptId, BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        Scope(caller); InputId(requestId);
        if (new[] { incidentId, invocationId, failedAttemptId }.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512))
            throw new ArgumentException("Service retry requires its exact incident, invocation and failed attempt.");
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        var command = new RuntimeModelExecutionCommand(state.Executable, state.MaterialIncarnationId, requestId, "retry-service",
            JsonSerializer.SerializeToElement(new { incidentId, invocationId, failedAttemptId }, Web), state.Authority, state.TenantId);
        return await DispatchAsync(executionId, command, caller, cancellationToken);
    }

    public async ValueTask<BpmnModelCommandOutcome> CompleteHumanAsync(string executionId, string requestId, string invocationId,
        string attemptId, string[]? outcomes, BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        Scope(caller); InputId(requestId);
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        var command = new RuntimeModelExecutionCommand(state.Executable, state.MaterialIncarnationId, requestId, "complete-human",
            JsonSerializer.SerializeToElement(new { invocationId, attemptId, outcomes = outcomes is { Length: > 0 } ? outcomes : ["Done"] }, Web), state.Authority, state.TenantId);
        return await DispatchAsync(executionId, command, caller, cancellationToken);
    }

    public async ValueTask<BpmnModelCommandOutcome> ClaimHumanAsync(string executionId, string requestId, string invocationId,
        string attemptId, BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        Scope(caller); InputId(requestId);
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        // The claimant is the authenticated caller; Authority remains the execution's immutable start authority.
        var command = new RuntimeModelExecutionCommand(state.Executable, state.MaterialIncarnationId, requestId, "claim-human",
            JsonSerializer.SerializeToElement(new { invocationId, attemptId, claimantId = caller.ActorId }, Web), state.Authority, state.TenantId);
        return await DispatchAsync(executionId, command, caller, cancellationToken);
    }

    public async ValueTask<BpmnModelCommandOutcome> CompleteClaimedHumanAsync(string executionId, string requestId, string invocationId,
        string attemptId, string claimId, string[]? outcomes, BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        Scope(caller); InputId(requestId);
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        var command = new RuntimeModelExecutionCommand(state.Executable, state.MaterialIncarnationId, requestId, "complete-claimed-human",
            JsonSerializer.SerializeToElement(new { invocationId, attemptId, claimId, claimantId = caller.ActorId,
                outcomes = outcomes is { Length: > 0 } ? outcomes : ["Done"] }, Web), state.Authority, state.TenantId);
        return await DispatchAsync(executionId, command, caller, cancellationToken);
    }

    public async ValueTask<BpmnModelCommandOutcome> CancelAsync(string executionId, string requestId, string? reason,
        BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        Scope(caller); InputId(requestId);
        if (reason?.Length > 2048) throw new ArgumentException("A BPMN cancellation reason cannot exceed 2048 UTF-16 code units.", nameof(reason));
        var state = await FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        var command = new RuntimeModelExecutionCommand(state.Executable, state.MaterialIncarnationId, requestId, "cancel",
            JsonSerializer.SerializeToElement(new { reason }, Web), state.Authority, state.TenantId);
        return await DispatchAsync(executionId, command, caller, cancellationToken);
    }

    public async ValueTask<BpmnModelCommandOutcome?> FindCommandAsync(string executionId, string requestId, BpmnModelCaller caller,
        CancellationToken cancellationToken = default)
    {
        Scope(caller); InputId(requestId);
        if (await FindExecutionAsync(executionId, caller, cancellationToken) is null)
        {
            return await PendingAsync(executionId, requestId, caller, cancellationToken) is not null
                ? new(executionId, requestId, true, true, null) : throw new BpmnModelResourceMissingException();
        }
        var receipt = await States.FindInputAsync(executionId, requestId, cancellationToken);
        if (receipt is not null) return new(executionId, requestId, true, false, receipt.Result);
        return await PendingAsync(executionId, requestId, caller, cancellationToken) is not null ? new(executionId, requestId, true, true, null) : null;
    }

    private async ValueTask<BpmnModelCommandOutcome> DispatchAsync(string executionId, RuntimeModelExecutionCommand command,
        BpmnModelCaller caller, CancellationToken cancellationToken)
    {
        var scope = Scope(caller);
        if (actors is not InProcessWorkflowExecutionActorProvider)
            throw new NotSupportedException("This BPMN hosting profile requires the qualified built-in actor provider.");
        var prior = await States.FindInputAsync(executionId, command.InputId, cancellationToken);
        if (prior is not null)
        {
            if (prior.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command))
                throw new RuntimeCheckpointReplayConflictException(command.InputId);
            return new(executionId, command.InputId, true, false, prior.Result);
        }
        if (await PendingAsync(executionId, command.InputId, caller, cancellationToken) is { } pending)
        {
            if (RuntimeCheckpointCommitFingerprint.ComputeModelInput(pending) != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command))
                throw new RuntimeCheckpointReplayConflictException(command.InputId);
            return new(executionId, command.InputId, true, true, null);
        }
        var at = clock.GetUtcNow();
        var actor = await actors.GetAgentAsync(new(executionId, WorkflowExecutionActorActivationReason.Start, at, caller.ActorId,
            WorkflowExecutionActorCapabilities.InProcessMailbox, partition: new(scope.Value)), cancellationToken);
        var envelopeId = "bpmn-command:" + BpmnModelIdentity.Create(executionId, command.InputId);
        WorkflowExecutionCommandDispatchResult result;
        try
        {
            result = await actor.EnqueueAsync(new(envelopeId, executionId,
                new(command.InputId, executionId, WorkflowExecutionCommandKind.ExecuteModel, at, JsonSerializer.SerializeToElement(command, Web), new Dictionary<string, string>()),
                envelopeId, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, at, partition: new(scope.Value)), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not RuntimeCheckpointReplayConflictException)
        {
            var consumed = await States.FindInputAsync(executionId, command.InputId, cancellationToken);
            if (consumed is not null && consumed.ContentFingerprint == RuntimeCheckpointCommitFingerprint.ComputeModelInput(command))
                return new(executionId, command.InputId, false, false, consumed.Result);
            var retained = await PendingAsync(executionId, command.InputId, caller, cancellationToken);
            if (retained is null || RuntimeCheckpointCommitFingerprint.ComputeModelInput(retained) != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command)) throw;
            logger?.LogWarning(exception, "BPMN execution {ExecutionId} input {InputId} remains durably queued after immediate processing failed.", executionId, command.InputId);
            return new(executionId, command.InputId, false, true, null);
        }
        if (result.Status is WorkflowExecutionCommandDispatchStatus.Rejected or WorkflowExecutionCommandDispatchStatus.Deferred or WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted)
            throw new RuntimeSchedulerWorkUnavailableException("bpmn.admission-unavailable", "The BPMN command could not be admitted for execution.");
        var receipt = await States.FindInputAsync(executionId, command.InputId, cancellationToken);
        return new(executionId, command.InputId, false, receipt is null, receipt?.Result);
    }

    private async ValueTask<RuntimeModelExecutionCommand?> PendingAsync(string executionId, string requestId, BpmnModelCaller caller,
        CancellationToken cancellationToken)
    {
        var query = new RuntimeSchedulerWorkQuery(executionId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await queue.ListAsync(query, cancellationToken);
            foreach (var work in page.Items.Where(item => item.CommandKind == WorkflowExecutionCommandKind.ExecuteModel && item.CommandId == requestId))
            {
                RuntimeModelExecutionCommand? command;
                try { command = work.Payload?.Deserialize<RuntimeModelExecutionCommand>(Web); }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                {
                    throw new BpmnModelValidationException($"BPMN execution '{executionId}', request '{requestId}' has invalid owned queued-command JSON in work '{work.WorkItemId}'.", exception);
                }
                if (command is null || command.InputId != requestId || command.TenantId != caller.TenantId) continue;
                var material = await materials.FindMaterialAsync(command.Executable.ArtifactId, cancellationToken);
                if (material?.Model?.Protocol == new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version)) return command;
            }
            if (page.NextContinuationToken is null) return null;
            if (!seen.Add(page.NextContinuationToken)) throw new BpmnModelValidationException("The scheduler input provider repeated its continuation.");
            query = new(executionId, continuationToken: page.NextContinuationToken);
        }
    }

    private PersistenceScope Scope(BpmnModelCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller.ActorId);
        if (caller.TenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(caller.TenantId);
        access.Current.EnsureTenantScope(caller.TenantId);
        return access.Current.Scope ?? throw new BpmnModelValidationException("A model operation requires an explicit persistence scope.");
    }

    private async ValueTask<WorkflowExecutable> ValidatePublishedWorkerAsync(string bindingId, WorkflowExecutableIdentity executable, BpmnModelCaller caller,
        CancellationToken cancellationToken)
    {
        var native = await materials.FindMaterialAsync(executable.ArtifactId, cancellationToken);
        if (native?.NativeWorkflow is not { } worker || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(native.Identity, executable) ||
            worker.InputContract is not { Inputs.Count: 0 })
            throw new BpmnModelValidationException($"Native work binding '{bindingId}' for worker '{executable.ArtifactId}' requires its complete native pin and an explicit closed worker input contract.");
        var query = new WorkflowExecutableSourceReferenceArtifactPageQuery(executable.ArtifactId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await sources.ListByArtifactPageAsync(query, cancellationToken);
            if (page.Items.Any(source => source.Scope == WorkflowExecutableReferenceScope.Published && source.TenantId == caller.TenantId &&
                source.IsLive(clock.GetUtcNow()) && source.DefinitionId == executable.DefinitionId &&
                source.DefinitionVersionId == executable.DefinitionVersionId && source.ArtifactVersion == executable.ArtifactVersion)) return worker;
            if (page.NextContinuationToken is null) break;
            if (!seen.Add(page.NextContinuationToken)) throw new BpmnModelValidationException("The worker publication source page did not advance.");
            query = new(executable.ArtifactId, continuationToken: page.NextContinuationToken);
        }
        throw new BpmnModelValidationException("The native worker pin has no live published source in this tenant.");
    }

    internal static void ValidateEventWorker(ExecutableNode root, string name)
    {
        if (root.ActivityType is not ("Elsa.Event" or "Elsa.Activities.Primitives.Activities.Event") || root.ChildSlots.Count != 0 || root.IntrinsicKind is not null ||
            !Literal("EventName", out var eventName) || eventName.ValueKind != JsonValueKind.String || eventName.GetString() != name ||
            !Literal("CanStartWorkflow", out var canStart) || canStart.ValueKind != JsonValueKind.False ||
            !IsUncorrelated())
            throw new BpmnModelValidationException("A signal catch requires a closed native Event worker with the same fixed name, CanStartWorkflow=false and no correlation filter.");

        bool Literal(string key, out JsonElement value)
        {
            value = default;
            if (!root.InputBindings.TryGetValue(key, out var binding) || binding.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
                return false;
            value = literal;
            return true;
        }

        bool IsUncorrelated() => !root.InputBindings.TryGetValue("CorrelationId", out var correlation) ||
            correlation.Source == RuntimeInputBindingSource.Literal && (correlation.Literal?.Presence == ValuePresence.Absent ||
                correlation.LiteralValue is { } value && (value.ValueKind == JsonValueKind.Null ||
                    value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())));
    }
    private static void InputId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128) throw new ArgumentException("A BPMN request id cannot exceed 128 UTF-16 code units.", nameof(value));
    }
    private sealed record WorkCursor(string Scope, string Execution, long Revision, string After);
}
