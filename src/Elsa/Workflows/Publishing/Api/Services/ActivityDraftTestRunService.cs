using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public interface IActivityDraftTestRunService
{
    Task<ActivityDraftTestRunView> StartAsync(StartActivityDraftTestRun request, CancellationToken cancellationToken = default);
    Task<ActivityDraftTestRunView> GetAsync(string testRunId, CancellationToken cancellationToken = default);
    Task<ActivityDraftTestRunView> GetByIdempotencyKeyAsync(string draftId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<ActivityDraftTestRunView> CancelAsync(string testRunId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Compiles an exact activity draft revision, places it as the root of a synthetic workflow artifact, and uses
/// ordinary Runtime start dispatch. The activity template and wrapper artifact live in their normal
/// content-addressed stores; expiring Source References govern executable lifetime while a longer-lived receipt
/// preserves idempotency and safe status facts.
/// </summary>
public sealed class ActivityDraftTestRunService(
    IActivityDefinitionStore definitions,
    IActivityDefinitionDraftStore drafts,
    IActivityDefinitionLayoutStore layouts,
    IActivityTemplateCompiler compiler,
    IActivityPublishingAuthorizationContext authorization,
    IExecutableActivityTemplateStore activityTemplates,
    IWorkflowExecutableSourceReferenceStore sourceReferences,
    ActivityTemplatePlacer placer,
    WorkflowExecutableHasher hasher,
    IWorkflowExecutableStore workflowExecutables,
    IWorkflowStartDispatcher startDispatcher,
    IActivityDraftTestRunStore testRuns,
    IWorkflowExecutionStateStore workflowExecutions,
    IActivityExecutionStateStore activityExecutions,
    IActivityDraftTestRunCancellationPolicy cancellationPolicy,
    IWorkflowExecutionActorProvider actorProvider,
    IRuntimeExecutionIdGenerator runtimeIds,
    TimeProvider timeProvider) : IActivityDraftTestRunService
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultReceiptRetention = TimeSpan.FromDays(7);
    private static readonly IReadOnlyDictionary<string, RuntimeOutputCapture> NoOutputCaptures =
        new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal);

    public async Task<ActivityDraftTestRunView> StartAsync(
        StartActivityDraftTestRun request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequestIdentity(request.IdempotencyKey, request.CorrelationId);
        var operationScope = ActivityDraftTestRunIdentity.CreateOperationScope(authorization.TenantId);
        var requestFingerprint = RequestFingerprint(request);
        var existing = await testRuns.FindByIdempotencyKeyAsync(
            operationScope,
            request.DraftId,
            request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            EnsureReceiptAuthorized(existing);
            EnsureMatchingRequest(existing, requestFingerprint);
            var resumed = await ResumeExistingAsync(existing, request, cancellationToken);
            if (resumed is not null)
                return resumed;
        }

        var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
        if (draft is null)
        {
            if (existing is not null)
                return await RejectValidationAsync(
                    existing,
                    "activity.draft.stale-revision",
                    "The activity draft revision was no longer available before Test Run dispatch material was prepared.",
                    [],
                    cancellationToken);
            throw Reject("activity.draft.not-found", "The requested activity draft was not found.");
        }
        EnsureAuthorized(draft.TenantId);
        if (existing is not null &&
            (!StringComparer.Ordinal.Equals(existing.ResourceTenantId, draft.TenantId) ||
             !StringComparer.Ordinal.Equals(existing.DefinitionId, draft.DefinitionId)))
            return await RejectValidationAsync(
                existing,
                "activity.draft.stale-revision",
                "The activity draft identity changed before Test Run dispatch material was prepared.",
                [],
                cancellationToken);

        var definition = await definitions.GetAsync(draft.DefinitionId, cancellationToken);
        EnsureAuthorized(definition.TenantId);
        var layout = await layouts.FindDraftLayoutAsync(draft.Id, cancellationToken)
                     ?? throw Reject("activity.draft.layout-not-found", "The activity draft layout was not found.", conflict: true);
        EnsureAuthorized(layout.TenantId);

        var now = timeProvider.GetUtcNow();
        var testRunId = ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, draft.Id, request.IdempotencyKey);
        var initial = new ActivityDraftTestRunReceipt(
            testRunId,
            operationScope,
            ActivityDraftTestRunIdentity.HashIdempotencyKey(request.IdempotencyKey),
            draft.Id,
            request.ExpectedRevision,
            definition.Id,
            authorization.TenantId,
            draft.TenantId,
            requestFingerprint,
            ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(operationScope, draft.Id, request.IdempotencyKey),
            ActivityDraftTestRunReceiptStatus.Preparing,
            null,
            null,
            null,
            null,
            now,
            now,
            now.Add(DefaultRetention),
            now.Add(DefaultReceiptRetention),
            ActivityDraftTestRunCancellationStatus.Unavailable,
            1);
        var create = await testRuns.TryCreateAsync(initial, cancellationToken);
        var receipt = create.Receipt;
        EnsureReceiptAuthorized(receipt);
        EnsureMatchingRequest(receipt, requestFingerprint);
        if (!create.Created)
        {
            var resumed = await ResumeExistingAsync(receipt, request, cancellationToken);
            if (resumed is not null)
                return resumed;
        }

        if (draft.Status != ActivityDefinitionDraftStatus.Active || draft.Revision != request.ExpectedRevision)
            return await RejectValidationAsync(
                receipt,
                "activity.draft.stale-revision",
                "The requested activity draft revision is no longer active.",
                [],
                cancellationToken);
        if (layout.Revision != draft.Revision)
            return await RejectValidationAsync(
                receipt,
                "activity.draft.stale-layout",
                "The activity draft layout does not match the requested revision.",
                [],
                cancellationToken);

        var candidateVersionId = $"activity-test-version-{StableHash($"{draft.Id}\u001f{draft.Revision}")}";
        var candidateVersion = $"draft-{draft.Revision}";
        var compilation = await compiler.CompileAsync(new(
            definition,
            draft,
            candidateVersionId,
            candidateVersion,
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(layout.Records))), cancellationToken);
        if (!compilation.IsSuccessful || compilation.Template is null)
            return await RejectValidationAsync(
                receipt,
                "activity.test-run.invalid",
                "The activity draft could not be compiled for a Test Run.",
                compilation.Diagnostics,
                cancellationToken);

        var expiresAt = receipt.SourceReferenceExpiresAt;
        var templateReferenceId = $"activity-test-template-ref-{StableHash(compilation.Template.TemplateHash)}";
        var templateReference = CreateTemplateReference(
            templateReferenceId,
            definition,
            draft,
            candidateVersionId,
            candidateVersion,
            compilation.Template,
            layout.Records.ToArray(),
            now,
            expiresAt);
        var publication = CreateEphemeralPublication(
            definition,
            draft,
            candidateVersionId,
            candidateVersion,
            compilation.Template,
            templateReferenceId,
            compilation.Measurements,
            now);

        await activityTemplates.SaveAsync(compilation.Template, cancellationToken);
        await sourceReferences.SaveAsync(templateReference, cancellationToken);

        var origin = new ActivityInvocationOrigin([
            new(ActivityInvocationOriginSegmentKind.WorkflowRoot, $"activity-test-wrapper:{definition.Id}"),
            new(ActivityInvocationOriginSegmentKind.AuthoredNode, "activity-under-test"),
            new(ActivityInvocationOriginSegmentKind.TemplateBoundary, candidateVersionId)
        ]);
        IReadOnlyDictionary<string, RuntimeInputBinding> suppliedInputs;
        try
        {
            suppliedInputs = CompileInputs(draft.State.Contract, request.Inputs);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            return await RejectValidationAsync(
                receipt,
                exception.ErrorCode,
                exception.Message,
                exception.Diagnostics,
                cancellationToken);
        }
        var placement = await placer.PlaceAsync(new(
            publication,
            compilation.Template,
            templateReference,
            origin,
            definition.ActivityTypeKey,
            suppliedInputs,
            NoOutputCaptures), cancellationToken);

        var artifactHash = hasher.ComputeHash(placement.Root);
        var artifactId = hasher.CreateArtifactId("artifact-", artifactHash);
        var wrapperDefinitionId = $"activity-test-wrapper:{definition.Id}";
        var wrapperVersionId = $"activity-test-wrapper:{draft.Id}:{draft.Revision}";
        var executable = new WorkflowExecutable(
            new(artifactId, wrapperDefinitionId, wrapperVersionId, "draft", artifactHash),
            placement.Root,
            placement.ResumeTargets,
            now,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime.scope"] = "activity-draft-test-run",
                ["activity.templateHash"] = compilation.Template.TemplateHash
            });
        await workflowExecutables.SaveAsync(executable, cancellationToken);

        var wrapperReference = new WorkflowExecutableSourceReference(
            $"activity-test-source-ref-{StableHash(testRunId)}",
            artifactId,
            "ActivityDraftTestRun",
            draft.Id,
            draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            wrapperDefinitionId,
            wrapperVersionId,
            "draft",
            now,
            null,
            WorkflowExecutableReferenceScope.TestRun,
            expiresAt,
            LayoutSidecar: placement.LayoutSidecar);
        await sourceReferences.SaveAsync(wrapperReference, cancellationToken);

        receipt = await UpdateAsync(
            receipt,
            x => x with
            {
                ArtifactId = artifactId,
                SourceReferenceId = wrapperReference.SourceReferenceId,
                Status = ActivityDraftTestRunReceiptStatus.Dispatching
            },
            cancellationToken);
        return await DispatchPersistedAsync(receipt, request, cancellationToken);
    }

    private async Task<ActivityDraftTestRunView?> ResumeExistingAsync(
        ActivityDraftTestRunReceipt receipt,
        StartActivityDraftTestRun request,
        CancellationToken cancellationToken)
    {
        if (receipt.Status is not ActivityDraftTestRunReceiptStatus.Preparing
            and not ActivityDraftTestRunReceiptStatus.Dispatching
            and not ActivityDraftTestRunReceiptStatus.DispatchAmbiguous)
            return await ProjectAsync(receipt, cancellationToken);

        var execution = await workflowExecutions.FindAsync(receipt.WorkflowExecutionId, cancellationToken);
        if (execution is not null)
        {
            receipt = await UpdateAsync(
                receipt,
                x => x with
                {
                    Status = ActivityDraftTestRunReceiptStatus.DispatchAccepted,
                    CommandDispatchStatus = WorkflowExecutionCommandDispatchStatus.Accepted.ToString(),
                    Failure = null
                },
                cancellationToken);
            return await ProjectAsync(receipt, cancellationToken);
        }

        if (receipt.Status is ActivityDraftTestRunReceiptStatus.Dispatching
            or ActivityDraftTestRunReceiptStatus.DispatchAmbiguous)
            return await DispatchPersistedAsync(receipt, request, cancellationToken);

        return null;
    }

    private async Task<ActivityDraftTestRunView> DispatchPersistedAsync(
        ActivityDraftTestRunReceipt receipt,
        StartActivityDraftTestRun request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(receipt.ArtifactId) || string.IsNullOrWhiteSpace(receipt.SourceReferenceId))
        {
            receipt = await UpdateAsync(
                receipt,
                x => x with
                {
                    Status = ActivityDraftTestRunReceiptStatus.DispatchAmbiguous,
                    Failure = RuntimeFailure(
                        "activity.test-run.dispatch-material-missing",
                        "The persisted Test Run dispatch material is incomplete and cannot be safely reconstructed.")
                },
                cancellationToken);
            return await ProjectAsync(receipt, cancellationToken);
        }

        var inputValues = (request.Inputs ?? new Dictionary<string, ActivityDraftTestRunInput>())
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Value.State, "Present") && x.Value.Value.HasValue)
            .ToDictionary(x => x.Key, x => (object?)x.Value.Value!.Value.Clone(), StringComparer.Ordinal);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.scope"] = "test-run",
            ["runtime.testRunId"] = receipt.TestRunId,
            ["runtime.sourceReferenceId"] = receipt.SourceReferenceId,
            ["activity.draftId"] = receipt.DraftId,
            ["activity.draftRevision"] = receipt.DraftRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            metadata["runtime.correlationId"] = request.CorrelationId;

        try
        {
            var dispatch = await startDispatcher.DispatchAsync(
                new WorkflowExecutionStartDispatchRequest(
                    artifactId: receipt.ArtifactId,
                    requestedBy: "activity-designer-test-run",
                    workflowExecutionId: receipt.WorkflowExecutionId,
                    idempotencyKey: $"activity-test-run:{receipt.TestRunId}:start",
                    metadata: metadata,
                    variables: null,
                    inputs: inputValues,
                    stimulusInput: null,
                    triggerNodeId: null,
                    runKind: WorkflowRunKind.TestRun,
                    sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: receipt.SourceReferenceId),
                    provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                    parentWorkflowExecutionId: null,
                    correlationId: request.CorrelationId,
                    tenantId: receipt.TenantId,
                    partition: null,
                    authority: null,
                    startAuthority: null,
                    dispatchNestingDepth: 0),
                WorkflowExecutableReferenceScope.TestRun,
                cancellationToken: cancellationToken);
            receipt = await UpdateAsync(
                receipt,
                x => x with
                {
                    Status = MapDispatch(dispatch.CommandDispatch.Status),
                    CommandDispatchStatus = dispatch.CommandDispatch.Status.ToString(),
                    Failure = dispatch.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Rejected
                        ? RuntimeFailure("activity.test-run.runtime-rejected", "Runtime rejected the Test Run dispatch.")
                        : null
                },
                cancellationToken);
        }
        catch (WorkflowExecutableStartRejectedException exception)
        {
            receipt = await RuntimeRejectAsync(
                receipt,
                $"runtime.start.{exception.ReasonCode}",
                "Runtime rejected the Test Run start request.",
                cancellationToken);
        }
        catch (WorkflowExecutableReferenceRejectedException exception)
        {
            receipt = await RuntimeRejectAsync(receipt, $"runtime.reference.{exception.Reason}", "Runtime rejected the Test Run Source Reference.", cancellationToken);
        }
        catch (WorkflowExecutableNotFoundException)
        {
            receipt = await RuntimeRejectAsync(receipt, "runtime.artifact.not-found", "Runtime could not resolve the Test Run artifact.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            receipt = await UpdateAsync(
                receipt,
                x => x with
                {
                    Status = ActivityDraftTestRunReceiptStatus.DispatchAmbiguous,
                    Failure = RuntimeFailure(
                        "activity.test-run.dispatch-ambiguous",
                        "Runtime dispatch acknowledgement was unavailable; status reconciliation remains safe to retry.")
                },
                CancellationToken.None);
        }

        return await GetAsync(receipt.TestRunId, cancellationToken);
    }

    public async Task<ActivityDraftTestRunView> GetAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await testRuns.FindAsync(testRunId, cancellationToken)
                      ?? throw Reject("activity.test-run.not-found", "The requested activity Test Run was not found.");
        EnsureReceiptAuthorized(receipt);
        return await ProjectAsync(receipt, cancellationToken);
    }

    public async Task<ActivityDraftTestRunView> GetByIdempotencyKeyAsync(
        string draftId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateRequestIdentity(idempotencyKey, null);
        var operationScope = ActivityDraftTestRunIdentity.CreateOperationScope(authorization.TenantId);
        var receipt = await testRuns.FindByIdempotencyKeyAsync(operationScope, draftId, idempotencyKey, cancellationToken)
                      ?? throw Reject("activity.test-run.not-found", "The requested activity Test Run was not found.");
        EnsureReceiptAuthorized(receipt);
        return await ProjectAsync(receipt, cancellationToken);
    }

    public async Task<ActivityDraftTestRunView> CancelAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await testRuns.FindAsync(testRunId, cancellationToken)
                      ?? throw Reject("activity.test-run.not-found", "The requested activity Test Run was not found.");
        EnsureReceiptAuthorized(receipt);
        var execution = await workflowExecutions.FindAsync(receipt.WorkflowExecutionId, cancellationToken);
        var decision = await cancellationPolicy.EvaluateAsync(receipt, execution, cancellationToken);
        if (!decision.IsAllowed)
        {
            receipt = await UpdateAsync(
                receipt,
                x => x with { CancellationStatus = ActivityDraftTestRunCancellationStatus.Unavailable },
                cancellationToken);
            return await ProjectAsync(receipt, cancellationToken);
        }

        receipt = await UpdateAsync(
            receipt,
            x => x with { CancellationStatus = ActivityDraftTestRunCancellationStatus.Requested },
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.testRunId"] = receipt.TestRunId,
            ["runtime.cancellationSource"] = "activity-draft-test-run"
        };
        var command = new WorkflowExecutionCommand(
            runtimeIds.NewWorkflowExecutionCommandId(),
            receipt.WorkflowExecutionId,
            WorkflowExecutionCommandKind.Cancel,
            now,
            null,
            metadata);
        var envelope = new WorkflowExecutionCommandEnvelope(
            runtimeIds.NewWorkflowExecutionCommandEnvelopeId(),
            receipt.WorkflowExecutionId,
            command,
            $"activity-test-run:{receipt.TestRunId}:cancel",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            now,
            metadata: metadata,
            partition: execution?.Partition);
        try
        {
            var actor = await actorProvider.GetAgentAsync(new(
                receipt.WorkflowExecutionId,
                WorkflowExecutionActorActivationReason.ControlPlaneCommand,
                now,
                "activity-designer-test-run",
                WorkflowExecutionActorCapabilities.None,
                metadata,
                execution?.Partition), cancellationToken);
            var dispatch = await actor.EnqueueAsync(envelope, cancellationToken);
            receipt = await UpdateAsync(
                receipt,
                x => x with
                {
                    CancellationStatus = dispatch.Status is WorkflowExecutionCommandDispatchStatus.Accepted
                        or WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted
                        or WorkflowExecutionCommandDispatchStatus.Duplicate
                            ? ActivityDraftTestRunCancellationStatus.Cancelling
                            : dispatch.Status == WorkflowExecutionCommandDispatchStatus.Deferred
                                ? ActivityDraftTestRunCancellationStatus.Requested
                                : ActivityDraftTestRunCancellationStatus.Unavailable
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            receipt = await UpdateAsync(
                receipt,
                x => x with { CancellationStatus = ActivityDraftTestRunCancellationStatus.Requested },
                CancellationToken.None);
        }
        return await ProjectAsync(receipt, cancellationToken);
    }

    private async Task<ActivityDraftTestRunView> RejectValidationAsync(
        ActivityDraftTestRunReceipt receipt,
        string code,
        string message,
        IReadOnlyList<ActivityDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        receipt = await UpdateAsync(
            receipt,
            x => x with
            {
                Status = ActivityDraftTestRunReceiptStatus.ValidationRejected,
                Failure = new(ActivityDraftTestRunFailureKind.Validation, code, message, ActivityDiagnosticOrderer.Order(diagnostics))
            },
            cancellationToken);
        return await ProjectAsync(receipt, cancellationToken);
    }

    private async Task<ActivityDraftTestRunReceipt> RuntimeRejectAsync(
        ActivityDraftTestRunReceipt receipt,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        await UpdateAsync(
            receipt,
            x => x with
            {
                Status = ActivityDraftTestRunReceiptStatus.DispatchRejected,
                Failure = RuntimeFailure(code, message)
            },
            cancellationToken);

    private async Task<ActivityDraftTestRunView> ProjectAsync(
        ActivityDraftTestRunReceipt receipt,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var execution = await workflowExecutions.FindAsync(receipt.WorkflowExecutionId, cancellationToken);
        string? outerActivityExecutionId = null;
        if (execution is not null)
        {
            outerActivityExecutionId = (await activityExecutions.ListAsync(receipt.WorkflowExecutionId, cancellationToken))
                .Where(x => x.ParentActivityExecutionId is null)
                .OrderBy(x => x.ExecutionSequence)
                .ThenBy(x => x.Execution.ActivityExecutionId, StringComparer.Ordinal)
                .Select(x => x.Execution.ActivityExecutionId)
                .FirstOrDefault();
        }

        var sourceReference = receipt.SourceReferenceId is null
            ? null
            : await sourceReferences.FindAsync(receipt.SourceReferenceId, cancellationToken);
        var cancellation = await cancellationPolicy.EvaluateAsync(receipt, execution, cancellationToken);
        var terminal = execution?.Status.IsTerminal() == true;
        var cancellationStatus = terminal
            ? ActivityDraftTestRunCancellationStatus.Terminal
            : receipt.CancellationStatus is ActivityDraftTestRunCancellationStatus.Requested
                or ActivityDraftTestRunCancellationStatus.Cancelling
                ? receipt.CancellationStatus
                : !cancellation.IsAllowed
                    ? ActivityDraftTestRunCancellationStatus.Unavailable
                    : ActivityDraftTestRunCancellationStatus.Available;
        var status = execution?.Status.ToString() ?? receipt.Status.ToString();
        var reason = receipt.Failure?.Message;

        return new(
            receipt.TestRunId,
            receipt.DraftId,
            receipt.DraftRevision,
            receipt.ArtifactId,
            receipt.SourceReferenceId,
            receipt.WorkflowExecutionId,
            outerActivityExecutionId,
            status,
            receipt.CommandDispatchStatus,
            reason,
            receipt.Failure is null
                ? null
                : new(
                    receipt.Failure.Kind.ToString(),
                    receipt.Failure.Code,
                    receipt.Failure.Message,
                    receipt.Failure.Diagnostics),
            new(
                receipt.SourceReferenceExpiresAt,
                receipt.SourceReferenceExpiresAt <= now,
                sourceReference is not null,
                "RetainedUntilRuntimeEvidenceDeletion",
                null,
                execution is not null,
                null,
                execution is not null && !execution.Status.IsTerminal(),
                receipt.ReceiptExpiresAt),
            new(
                cancellation.CapabilityAdvertised,
                cancellation.IsAllowed,
                cancellationStatus.ToString(),
                cancellation.Reason),
            receipt.RequestedAt,
            receipt.UpdatedAt);
    }

    private async Task<ActivityDraftTestRunReceipt> UpdateAsync(
        ActivityDraftTestRunReceipt receipt,
        Func<ActivityDraftTestRunReceipt, ActivityDraftTestRunReceipt> update,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var current = attempt == 0
                ? receipt
                : await testRuns.FindAsync(receipt.TestRunId, cancellationToken)
                  ?? throw new InvalidOperationException("The activity Test Run receipt disappeared during an update.");
            var next = update(current) with
            {
                Revision = current.Revision + 1,
                UpdatedAt = timeProvider.GetUtcNow()
            };
            if (await testRuns.TryUpdateAsync(next, current.Revision, cancellationToken))
                return next;
        }

        throw new InvalidOperationException("The activity Test Run receipt could not be updated after repeated concurrency conflicts.");
    }

    private static ActivityDraftTestRunReceiptStatus MapDispatch(WorkflowExecutionCommandDispatchStatus status) =>
        status switch
        {
            WorkflowExecutionCommandDispatchStatus.Accepted or
                WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted or
                WorkflowExecutionCommandDispatchStatus.Duplicate => ActivityDraftTestRunReceiptStatus.DispatchAccepted,
            WorkflowExecutionCommandDispatchStatus.Deferred => ActivityDraftTestRunReceiptStatus.DispatchDeferred,
            _ => ActivityDraftTestRunReceiptStatus.DispatchRejected
        };

    private static ActivityDraftTestRunFailure RuntimeFailure(string code, string message) =>
        new(ActivityDraftTestRunFailureKind.RuntimeDispatch, code, message, []);

    private static void EnsureMatchingRequest(ActivityDraftTestRunReceipt receipt, string requestFingerprint)
    {
        if (StringComparer.Ordinal.Equals(receipt.RequestFingerprint, requestFingerprint))
            return;
        throw Reject(
            "activity.test-run.idempotency-mismatch",
            "The idempotency key is already bound to a different activity Test Run request.",
            conflict: true);
    }

    private static string RequestFingerprint(StartActivityDraftTestRun request)
    {
        var inputs = (request.Inputs ?? new Dictionary<string, ActivityDraftTestRunInput>())
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new
            {
                name = x.Key,
                state = x.Value.State,
                value = x.Value.Value?.GetRawText()
            });
        var canonical = JsonSerializer.Serialize(new
        {
            request.DraftId,
            request.ExpectedRevision,
            request.CorrelationId,
            inputs
        });
        return StableHash(canonical);
    }

    private static IReadOnlyDictionary<string, RuntimeInputBinding> CompileInputs(
        ActivityContract contract,
        IReadOnlyDictionary<string, ActivityDraftTestRunInput>? supplied)
    {
        supplied ??= new Dictionary<string, ActivityDraftTestRunInput>(StringComparer.Ordinal);
        var known = contract.Inputs.Select(x => x.ReferenceKey).ToHashSet(StringComparer.Ordinal);
        var unknown = supplied.Keys.Where(x => !known.Contains(x)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
            throw Reject("activity.test-run.input-unknown", $"Unknown activity input '{unknown[0]}'.");

        var result = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var input in contract.Inputs)
        {
            if (!supplied.TryGetValue(input.ReferenceKey, out var value))
                continue;
            if (StringComparer.OrdinalIgnoreCase.Equals(value.State, "Absent"))
            {
                if (value.Value.HasValue)
                    throw Reject(
                        "activity.test-run.input-state-invalid",
                        $"Activity input '{input.ReferenceKey}' cannot supply a value when its state is Absent.");
                continue;
            }
            if (!StringComparer.OrdinalIgnoreCase.Equals(value.State, "Present") || !value.Value.HasValue)
                throw Reject("activity.test-run.input-state-invalid", $"Activity input '{input.ReferenceKey}' has an invalid state/value combination.");
            result.Add(input.Name, new(
                input.Name,
                RuntimeInputBindingSource.Literal,
                literalValue: value.Value.Value.Clone(),
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["typeName"] = typeof(object).AssemblyQualifiedName!,
                    ["referenceKey"] = input.ReferenceKey
                }));
        }
        return result;
    }

    private static ActivityDefinitionVersionPublication CreateEphemeralPublication(
        Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinition definition,
        ActivityDefinitionDraft draft,
        string versionId,
        string version,
        ExecutableActivityTemplate template,
        string sourceReferenceId,
        ActivityResourceMeasurements measurements,
        DateTimeOffset now) => new()
    {
        Id = versionId,
        TenantId = definition.TenantId,
        DefinitionVersionId = versionId,
        DefinitionId = definition.Id,
        Version = version,
        ActivityTypeKey = definition.ActivityTypeKey,
        SourceDraftId = draft.Id,
        SourceVersionId = draft.SourceVersionId,
        Contract = draft.State.Contract,
        Provider = draft.State.Provider,
        TemplateId = template.TemplateId,
        TemplateHash = template.TemplateHash,
        SourceReferenceId = sourceReferenceId,
        ProviderFingerprint = template.ProviderFingerprint,
        DirectDependencyCount = template.DirectDependencies.Count,
        ClosedTemplateCount = template.ClosedTemplates.Count,
        RuntimeRequirements = template.RuntimeRequirements.Select(x => new ActivityRuntimeRequirementDeclaration(x.ConsumerKey, x.SchemaVersion)).ToArray(),
        ResourceMeasurements = measurements,
        ResumeTargetCount = template.ResumeTargets.Count,
        Lifecycle = ActivityDefinitionVersionLifecycle.Active,
        PublishedAt = now,
        CreatedAt = now,
        LastModifiedAt = now
    };

    private static WorkflowExecutableSourceReference CreateTemplateReference(
        string sourceReferenceId,
        Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinition definition,
        ActivityDefinitionDraft draft,
        string versionId,
        string version,
        ExecutableActivityTemplate template,
        IReadOnlyCollection<ActivityLayoutRecord> layout,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var records = layout.Select(x => new ExecutableActivityLayoutRecord(
            x.NodeId,
            x.NodeId,
            x.NodeId,
            0,
            0,
            AdditionalProperties: x.Data.Clone())).ToArray();
        var origin = new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.TemplateBoundary, versionId)]);
        return new(
            sourceReferenceId,
            template.TemplateId,
            "ActivityDraft",
            draft.Id,
            draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            definition.Id,
            versionId,
            version,
            now,
            null,
            WorkflowExecutableReferenceScope.TestRun,
            expiresAt,
            LayoutSidecar: new([new(versionId, origin, template.TemplateHash, records, [])]));
    }

    private static ActivityPublicationRejectedException Reject(
        string code,
        string message,
        IReadOnlyList<ActivityDiagnostic>? diagnostics = null,
        bool conflict = false) =>
        new(code, message, ActivityDiagnosticOrderer.Order(diagnostics ?? []), conflict);

    private static void ValidateRequestIdentity(string idempotencyKey, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 200 ||
            correlationId?.Length > 200)
            throw Reject(
                "activity.request.invalid",
                "The activity Test Run request identity is malformed.");
    }

    private void EnsureAuthorized(string? tenantId)
    {
        if (!authorization.CanAccessTenant(tenantId))
            throw Reject(
                "activity.tenant.reference-denied",
                "The requested activity identity is outside the caller's authorized scope.");
    }

    private void EnsureReceiptAuthorized(ActivityDraftTestRunReceipt receipt)
    {
        if (!StringComparer.Ordinal.Equals(receipt.TenantId, authorization.TenantId))
            throw Reject(
                "activity.tenant.reference-denied",
                "The requested activity identity is outside the caller's authorized scope.");
        EnsureAuthorized(receipt.ResourceTenantId);
    }

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
