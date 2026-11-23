using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Runtime.Core.Configuration;
using WorkflowExecutableRootWriteLeaseManager = Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableRootWriteLeaseManager;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Publishing.Api.Services;

public interface IActivityDraftTestRunService
{
    /// <summary>Prepares or resumes the receipt selected by the request's idempotency key.</summary>
    /// <exception cref="ActivityDraftTestRunStorageException">Wrapper material/source retention failed;
    /// identifies the Test Run and preserves the infrastructure cause. Retry the same key.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    Task<ActivityDraftTestRunView> StartAsync(StartActivityDraftTestRun request, CancellationToken cancellationToken = default);
    Task<ActivityDraftTestRunView> StartVersionAsync(StartActivityVersionTestRun request, CancellationToken cancellationToken = default) =>
        Task.FromException<ActivityDraftTestRunView>(new NotSupportedException("Published activity Test Runs are not supported by this service."));
    Task<ActivityDraftTestRunView> GetAsync(string testRunId, CancellationToken cancellationToken = default);
    Task<ActivityDraftTestRunView> GetByIdempotencyKeyAsync(string draftId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<ActivityDraftTestRunView> GetVersionByIdempotencyKeyAsync(string versionId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromException<ActivityDraftTestRunView>(new NotSupportedException("Published activity Test Runs are not supported by this service."));
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
    IWorkflowTestScopeStore testScopeStore,
    IWorkflowExecutionPartitionAccessor partitionAccessor,
    IActivityDraftTestRunStore testRuns,
    IWorkflowExecutionStateStore workflowExecutions,
    IActivityExecutionStateStore activityExecutions,
    IActivityDraftTestRunCancellationPolicy cancellationPolicy,
    IWorkflowExecutionActorProvider actorProvider,
    IRuntimeExecutionIdGenerator runtimeIds,
    TimeProvider timeProvider,
    IWorkflowExecutableRootWriteLeaseManager rootWrites,
    IActivityDefinitionVersionPublicationStore? versionPublications = null) : IActivityDraftTestRunService
{
    // Preserve the original CLR constructor. Direct callers retain the built-in lease defaults;
    // DI uses the additive constructor and the host's selected coordinator/options.
    public ActivityDraftTestRunService(IActivityDefinitionStore definitions, IActivityDefinitionDraftStore drafts,
        IActivityDefinitionLayoutStore layouts, IActivityTemplateCompiler compiler, IActivityPublishingAuthorizationContext authorization,
        IExecutableActivityTemplateStore activityTemplates, IWorkflowExecutableSourceReferenceStore sourceReferences,
        ActivityTemplatePlacer placer, WorkflowExecutableHasher hasher, IWorkflowExecutableStore workflowExecutables,
        IWorkflowStartDispatcher startDispatcher, IWorkflowTestScopeStore testScopeStore,
        IWorkflowExecutionPartitionAccessor partitionAccessor, IActivityDraftTestRunStore testRuns,
        IWorkflowExecutionStateStore workflowExecutions, IActivityExecutionStateStore activityExecutions,
        IActivityDraftTestRunCancellationPolicy cancellationPolicy, IWorkflowExecutionActorProvider actorProvider,
        IRuntimeExecutionIdGenerator runtimeIds, TimeProvider timeProvider)
        : this(definitions, drafts, layouts, compiler, authorization, activityTemplates, sourceReferences, placer,
            hasher, workflowExecutables, startDispatcher, testScopeStore, partitionAccessor, testRuns,
            workflowExecutions, activityExecutions, cancellationPolicy, actorProvider, runtimeIds, timeProvider,
            new WorkflowExecutableRootWriteLeaseManager(workflowExecutables,
                Options.Create(new WorkflowExecutableGarbageCollectionOptions()), timeProvider), null) { }

    public ActivityDraftTestRunService(IActivityDefinitionStore definitions, IActivityDefinitionDraftStore drafts,
        IActivityDefinitionLayoutStore layouts, IActivityTemplateCompiler compiler, IActivityPublishingAuthorizationContext authorization,
        IExecutableActivityTemplateStore activityTemplates, IWorkflowExecutableSourceReferenceStore sourceReferences,
        ActivityTemplatePlacer placer, WorkflowExecutableHasher hasher, IWorkflowExecutableStore workflowExecutables,
        IWorkflowStartDispatcher startDispatcher, IWorkflowTestScopeStore testScopeStore,
        IWorkflowExecutionPartitionAccessor partitionAccessor, IActivityDraftTestRunStore testRuns,
        IWorkflowExecutionStateStore workflowExecutions, IActivityExecutionStateStore activityExecutions,
        IActivityDraftTestRunCancellationPolicy cancellationPolicy, IWorkflowExecutionActorProvider actorProvider,
        IRuntimeExecutionIdGenerator runtimeIds, TimeProvider timeProvider,
        IWorkflowExecutableRootWriteLeaseManager rootWrites)
        : this(definitions, drafts, layouts, compiler, authorization, activityTemplates, sourceReferences, placer,
            hasher, workflowExecutables, startDispatcher, testScopeStore, partitionAccessor, testRuns,
            workflowExecutions, activityExecutions, cancellationPolicy, actorProvider, runtimeIds, timeProvider,
            rootWrites, null) { }

    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultReceiptRetention = TimeSpan.FromDays(7);
    private static readonly IReadOnlyDictionary<string, RuntimeOutputCapture> NoOutputCaptures =
        new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal);
    private const string ActivityVersionReceiptPrefix = "activity-version:";

    /// <inheritdoc />
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
            var resumed = await ResumeExistingAsync(existing, request.CorrelationId, cancellationToken);
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
            var resumed = await ResumeExistingAsync(receipt, request.CorrelationId, cancellationToken);
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
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(layout.Records, Elsa.Serialization.Core.JsonBaseline.Options)) + JsonSerializer.SerializeToUtf8Bytes(layout.ActivityPresentation, Elsa.Serialization.Core.JsonBaseline.Options).LongLength), cancellationToken);
        if (!compilation.IsSuccessful || compilation.Template is null)
            return await RejectValidationAsync(
                receipt,
                "activity.test-run.invalid",
                "The activity draft could not be compiled for a Test Run.",
                compilation.Diagnostics,
                cancellationToken);

        // The port owns its native tuple; the host must qualify the actual selected stores before using it.
        // A mixed composition must not create hidden references in a different material boundary.
        if (!activityTemplates.CanMaintainReferences(workflowExecutables, sourceReferences) ||
            !sourceReferences.CanWriteRootReferences(workflowExecutables))
            return await RejectValidationAsync(receipt, "activity.test-run.preparation-unavailable",
                "The activity draft cannot be prepared for a Test Run in this Runtime.", [], cancellationToken);

        var expiresAt = receipt.SourceReferenceExpiresAt;
        // Material stays content-addressed; each durable Test Run owns its own immutable expiring root.
        // Reusing a hash-only root would silently borrow another run's expiry, source identity or layout.
        var templateReferenceId = $"activity-test-template-ref-{StableHash($"{testRunId}\u001f{compilation.Template.TemplateHash}")}";
        var templateReference = CreateTemplateReference(
            templateReferenceId,
            definition,
            draft,
            candidateVersionId,
            candidateVersion,
            compilation.Template,
            layout.Records.ToArray(),
            layout.ActivityPresentation.ToArray(),
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

        templateReference = await activityTemplates.SaveWithReferenceAsync(compilation.Template, templateReference, cancellationToken);

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
        ActivityTemplatePlacement placement;
        try
        {
            placement = await placer.PlaceAsync(new(
                publication,
                compilation.Template,
                templateReference,
                origin,
                definition.ActivityTypeKey,
                suppliedInputs,
                NoOutputCaptures), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ActivityTemplateMissingRequiredInputException)
        {
            return await RejectValidationAsync(
                receipt,
                "activity.test-run.required-input",
                "A required activity input has no value or default. Provide values for required inputs before running the Test Run.",
                [],
                cancellationToken);
        }
        catch (Exception)
        {
            // Placement runs before any wrapper artifact or Runtime dispatch exists. Persist only a safe summary:
            // provider/consumer exception details can contain authored values and must not enter the receipt/API.
            return await RejectValidationAsync(
                receipt,
                "activity.test-run.preparation-failed",
                "The activity draft could not be prepared for a Test Run.",
                [],
                cancellationToken);
        }

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
            },
            IncidentStrategyBuiltIns.FaultReference);
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
            LayoutSidecar: placement.LayoutSidecar,
            ActivityPresentation: placement.ActivityPresentation);
        return await PersistAndDispatchAsync(receipt, executable, wrapperReference, request.CorrelationId, cancellationToken);
    }

    public async Task<ActivityDraftTestRunView> StartVersionAsync(
        StartActivityVersionTestRun request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequestIdentity(request.IdempotencyKey, request.CorrelationId);
        if (versionPublications is null)
            throw Reject("activity.test-run.version-unavailable", "Published Activity Definition Test Runs are not available on this Runtime.");

        var operationScope = ActivityDraftTestRunIdentity.CreateOperationScope(authorization.TenantId);
        var resourceId = ActivityVersionReceiptId(request.VersionId);
        var requestFingerprint = RequestFingerprint(request);
        var existing = await testRuns.FindByIdempotencyKeyAsync(operationScope, resourceId, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            EnsureReceiptAuthorized(existing);
            EnsureMatchingRequest(existing, requestFingerprint);
            var resumed = await ResumeExistingAsync(existing, request.CorrelationId, cancellationToken);
            if (resumed is not null) return resumed;
        }

        var version = await versionPublications.FindAsync(request.VersionId, cancellationToken)
                      ?? throw Reject("activity.version.not-found", "The requested published activity version was not found.");
        EnsureAuthorized(version.TenantId);
        if (!StringComparer.Ordinal.Equals(request.VersionId, version.DefinitionVersionId))
            throw Reject("activity.version.not-found", "The requested published activity version was not found.");
        var definition = await definitions.GetAsync(version.DefinitionId, cancellationToken);
        EnsureAuthorized(definition.TenantId);
        if (!StringComparer.Ordinal.Equals(definition.TenantId, version.TenantId))
            throw Reject("activity.tenant.reference-denied", "The requested activity identity is outside the caller's authorized scope.");

        var now = timeProvider.GetUtcNow();
        var testRunId = ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, resourceId, request.IdempotencyKey);
        var initial = new ActivityDraftTestRunReceipt(
            testRunId,
            operationScope,
            ActivityDraftTestRunIdentity.HashIdempotencyKey(request.IdempotencyKey),
            resourceId,
            0,
            definition.Id,
            authorization.TenantId,
            version.TenantId,
            requestFingerprint,
            ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(operationScope, resourceId, request.IdempotencyKey),
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
            var resumed = await ResumeExistingAsync(receipt, request.CorrelationId, cancellationToken);
            if (resumed is not null) return resumed;
        }

        if (version.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
            return await RejectValidationAsync(receipt, "activity.test-run.version-not-runnable",
                "This published activity version is not available for a Test Run.", [], cancellationToken);
        if (!activityTemplates.CanMaintainReferences(workflowExecutables, sourceReferences) ||
            !sourceReferences.CanWriteRootReferences(workflowExecutables))
            return await RejectValidationAsync(receipt, "activity.test-run.preparation-unavailable",
                "This published activity version cannot be prepared for a Test Run in this Runtime.", [], cancellationToken);

        var template = await activityTemplates.FindAsync(version.TemplateId, cancellationToken);
        var sourceReference = await sourceReferences.FindAsync(version.SourceReferenceId, cancellationToken);
        if (template is null || sourceReference is null ||
            !StringComparer.Ordinal.Equals(template.TemplateId, version.TemplateId) ||
            !StringComparer.Ordinal.Equals(template.TemplateHash, version.TemplateHash) ||
            !StringComparer.Ordinal.Equals(sourceReference.SourceReferenceId, version.SourceReferenceId) ||
            !StringComparer.Ordinal.Equals(sourceReference.ArtifactId, version.TemplateId) ||
            !StringComparer.Ordinal.Equals(sourceReference.DefinitionVersionId, version.DefinitionVersionId))
            return await RejectValidationAsync(receipt, "activity.test-run.version-material-unavailable",
                "The exact published activity material is unavailable for a Test Run.", [], cancellationToken);

        var latest = await versionPublications.FindAsync(request.VersionId, cancellationToken);
        if (latest is null || latest.Lifecycle != ActivityDefinitionVersionLifecycle.Active ||
            !StringComparer.Ordinal.Equals(latest.TemplateId, version.TemplateId) ||
            !StringComparer.Ordinal.Equals(latest.TemplateHash, version.TemplateHash) ||
            !StringComparer.Ordinal.Equals(latest.SourceReferenceId, version.SourceReferenceId))
            return await RejectValidationAsync(receipt, "activity.test-run.version-not-runnable",
                "The published activity version changed before the Test Run could be prepared.", [], cancellationToken);

        IReadOnlyDictionary<string, RuntimeInputBinding> suppliedInputs;
        try
        {
            suppliedInputs = CompileInputs(version.Contract, request.Inputs);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            return await RejectValidationAsync(receipt, exception.ErrorCode, exception.Message, exception.Diagnostics, cancellationToken);
        }

        var origin = new ActivityInvocationOrigin([
            new(ActivityInvocationOriginSegmentKind.WorkflowRoot, $"activity-test-wrapper:{definition.Id}"),
            new(ActivityInvocationOriginSegmentKind.AuthoredNode, "activity-under-test"),
            new(ActivityInvocationOriginSegmentKind.TemplateBoundary, version.DefinitionVersionId)
        ]);
        ActivityTemplatePlacement placement;
        try
        {
            placement = await placer.PlaceAsync(new(version, template, sourceReference, origin, definition.ActivityTypeKey,
                suppliedInputs, NoOutputCaptures), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ActivityTemplateMissingRequiredInputException)
        {
            return await RejectValidationAsync(receipt, "activity.test-run.required-input",
                "A required activity input has no value or default. Provide values for required inputs before running the Test Run.", [], cancellationToken);
        }
        catch (Exception)
        {
            return await RejectValidationAsync(receipt, "activity.test-run.preparation-failed",
                "The published activity version could not be prepared for a Test Run.", [], cancellationToken);
        }

        var artifactHash = hasher.ComputeHash(placement.Root);
        var artifactId = hasher.CreateArtifactId("artifact-", artifactHash);
        var wrapperDefinitionId = $"activity-test-wrapper:{definition.Id}";
        var wrapperVersionId = $"activity-test-wrapper:version:{version.DefinitionVersionId}";
        var executable = new WorkflowExecutable(
            new(artifactId, wrapperDefinitionId, wrapperVersionId, version.Version, artifactHash),
            placement.Root,
            placement.ResumeTargets,
            now,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime.scope"] = "activity-version-test-run",
                ["activity.versionId"] = version.DefinitionVersionId,
                ["activity.version"] = version.Version,
                ["activity.templateHash"] = template.TemplateHash
            },
            IncidentStrategyBuiltIns.FaultReference);
        var wrapperReference = new WorkflowExecutableSourceReference(
            $"activity-test-source-ref-{StableHash(testRunId)}",
            artifactId,
            "ActivityDefinitionVersionTestRun",
            version.DefinitionVersionId,
            version.Version,
            definition.Id,
            version.DefinitionVersionId,
            version.Version,
            now,
            null,
            WorkflowExecutableReferenceScope.TestRun,
            receipt.SourceReferenceExpiresAt,
            LayoutSidecar: placement.LayoutSidecar,
            ActivityPresentation: placement.ActivityPresentation);
        return await PersistAndDispatchAsync(receipt, executable, wrapperReference, request.CorrelationId, cancellationToken);
    }

    private async Task<ActivityDraftTestRunView> PersistAndDispatchAsync(
        ActivityDraftTestRunReceipt receipt,
        WorkflowExecutable executable,
        WorkflowExecutableSourceReference wrapperReference,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var testRunId = receipt.TestRunId;
        var artifactId = executable.Identity.ArtifactId;
        try
        {
            await workflowExecutables.SaveAsync(executable, cancellationToken);
            var storedExecutable = await workflowExecutables.FindAsync(artifactId, cancellationToken)
                ?? throw new InvalidDataException("The saved Test Run wrapper is unavailable.");
            RequireMatchingWrapper(storedExecutable, executable);

            // Each concurrent invocation owns a different lease ID. A stable run-wide ID could let one
            // writer release another writer's live grants. The source ID and expiry remain receipt-stable.
            await rootWrites.ExecuteFencedAsync([storedExecutable.Identity], $"activity-test-root:{Guid.NewGuid():N}",
                async (grants, token) => wrapperReference = await sourceReferences.SaveWithRootWriteLeasesAsync(
                    wrapperReference, storedExecutable.Identity, grants, token), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException or System.Data.Common.DbException or
            InvalidOperationException or AggregateException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ActivityDraftTestRunStorageException(testRunId, exception);
        }

        receipt = await UpdateConditionallyAsync(
            receipt,
            IsUndispatchedPreparing,
            x => x with
            {
                ArtifactId = artifactId,
                SourceReferenceId = wrapperReference.SourceReferenceId,
                Status = ActivityDraftTestRunReceiptStatus.Dispatching
            },
            cancellationToken);
        if (receipt.Status != ActivityDraftTestRunReceiptStatus.Dispatching)
            return await ProjectAsync(receipt, cancellationToken);

        return await DispatchPersistedAsync(receipt, correlationId, cancellationToken);
    }

    private static void RequireMatchingWrapper(WorkflowExecutable stored, WorkflowExecutable proposed)
    {
        if (stored.Identity.ArtifactId != proposed.Identity.ArtifactId || stored.Identity.ArtifactHash != proposed.Identity.ArtifactHash)
            throw new InvalidDataException("The saved Test Run wrapper identity conflicts with preparation.");
        // Hashes exclude node inspection metadata and do not cover every wrapper contract field.
        // Check the actual winner's complete payload, including template provenance and resume targets.
        // Only first-writer producer attribution and creation time are independent publication facts.
        var winner = JsonSerializer.SerializeToNode(stored, Elsa.Serialization.Core.JsonBaseline.Options)!.AsObject();
        var candidate = JsonSerializer.SerializeToNode(proposed, Elsa.Serialization.Core.JsonBaseline.Options)!.AsObject();
        winner.Remove("identity"); candidate.Remove("identity");
        winner.Remove("createdAt"); candidate.Remove("createdAt");
        if (!Elsa.Serialization.Core.JsonCanonicalForm.EqualsNodes(winner, candidate))
            throw new InvalidDataException("The saved Test Run wrapper content or provenance conflicts with preparation.");
    }

    private async Task<ActivityDraftTestRunView?> ResumeExistingAsync(
        ActivityDraftTestRunReceipt receipt,
        string? correlationId,
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
            return await DispatchPersistedAsync(receipt, correlationId, cancellationToken);

        return null;
    }

    private async Task<ActivityDraftTestRunView> DispatchPersistedAsync(
        ActivityDraftTestRunReceipt receipt,
        string? correlationId,
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

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.scope"] = "test-run",
            ["runtime.testRunId"] = receipt.TestRunId,
            ["runtime.sourceReferenceId"] = receipt.SourceReferenceId
        };
        var sourceVersionId = ReadActivityVersionReceiptId(receipt.DraftId);
        if (sourceVersionId is null)
        {
            metadata["activity.draftId"] = receipt.DraftId;
            metadata["activity.draftRevision"] = receipt.DraftRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            metadata["activity.versionId"] = sourceVersionId;
        }
        if (!string.IsNullOrWhiteSpace(correlationId))
            metadata["runtime.correlationId"] = correlationId;

        var partition = partitionAccessor.Current;
        var testScope = new WorkflowTestScope(
            receipt.TestRunId,
            receipt.SourceReferenceExpiresAt,
            receipt.TenantId,
            partition);
        await testScopeStore.CreateAsync(testScope, timeProvider.GetUtcNow(), cancellationToken);

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
                    inputs: null,
                    stimulusInput: null,
                    triggerNodeId: null,
                    runKind: WorkflowRunKind.TestRun,
                    sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: receipt.SourceReferenceId),
                    provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                    parentWorkflowExecutionId: null,
                    correlationId: correlationId,
                    tenantId: receipt.TenantId,
                    partition: partition,
                    authority: null,
                    startAuthority: null,
                    dispatchNestingDepth: 0,
                    testScope: testScope),
                WorkflowExecutableReferenceScope.TestRun,
                cancellationToken: cancellationToken);
            if (dispatch.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Rejected)
                await CloseTestScopeAsync(testScope.ScopeId, cancellationToken);
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
            await CloseTestScopeAsync(testScope.ScopeId, cancellationToken);
            receipt = await RuntimeRejectAsync(
                receipt,
                $"runtime.start.{exception.ReasonCode}",
                "Runtime rejected the Test Run start request.",
                cancellationToken);
        }
        catch (WorkflowExecutableReferenceRejectedException exception)
        {
            await CloseTestScopeAsync(testScope.ScopeId, cancellationToken);
            receipt = await RuntimeRejectAsync(receipt, $"runtime.reference.{exception.Reason}", "Runtime rejected the Test Run Source Reference.", cancellationToken);
        }
        catch (WorkflowExecutableNotFoundException)
        {
            await CloseTestScopeAsync(testScope.ScopeId, cancellationToken);
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

    private ValueTask<WorkflowTestScopeCloseResult> CloseTestScopeAsync(
        string scopeId,
        CancellationToken cancellationToken) =>
        testScopeStore.CloseAsync(
            new WorkflowTestScopeCloseRequest(
                scopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                timeProvider.GetUtcNow()),
            cancellationToken);

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

    public async Task<ActivityDraftTestRunView> GetVersionByIdempotencyKeyAsync(
        string versionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateRequestIdentity(idempotencyKey, null);
        var operationScope = ActivityDraftTestRunIdentity.CreateOperationScope(authorization.TenantId);
        var resourceId = ActivityVersionReceiptId(versionId);
        var receipt = await testRuns.FindByIdempotencyKeyAsync(operationScope, resourceId, idempotencyKey, cancellationToken)
                      ?? throw Reject("activity.test-run.not-found", "The requested activity Test Run was not found.");
        EnsureReceiptAuthorized(receipt);
        if (!StringComparer.Ordinal.Equals(ReadActivityVersionReceiptId(receipt.DraftId), versionId))
            throw Reject("activity.test-run.not-found", "The requested activity Test Run was not found.");
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
            ["runtime.cancellationSource"] = ReadActivityVersionReceiptId(receipt.DraftId) is null
                ? "activity-draft-test-run"
                : "activity-version-test-run"
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
        receipt = await UpdateConditionallyAsync(
            receipt,
            IsUndispatchedPreparing,
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
            outerActivityExecutionId = (await activityExecutions.ListAllAsync(receipt.WorkflowExecutionId, cancellationToken))
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

        var sourceVersionId = ReadActivityVersionReceiptId(receipt.DraftId);
        return new(
            receipt.TestRunId,
            sourceVersionId is null ? receipt.DraftId : string.Empty,
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
            receipt.UpdatedAt)
        {
            SourceVersionId = sourceVersionId
        };
    }

    private async Task<ActivityDraftTestRunReceipt> UpdateAsync(
        ActivityDraftTestRunReceipt receipt,
        Func<ActivityDraftTestRunReceipt, ActivityDraftTestRunReceipt> update,
        CancellationToken cancellationToken)
        => await UpdateConditionallyAsync(
            receipt,
            _ => true,
            update,
            cancellationToken);

    private async Task<ActivityDraftTestRunReceipt> UpdateConditionallyAsync(
        ActivityDraftTestRunReceipt receipt,
        Func<ActivityDraftTestRunReceipt, bool> canUpdate,
        Func<ActivityDraftTestRunReceipt, ActivityDraftTestRunReceipt> update,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var current = attempt == 0
                ? receipt
                : await testRuns.FindAsync(receipt.TestRunId, cancellationToken)
                  ?? throw new InvalidOperationException("The activity Test Run receipt disappeared during an update.");
            if (!canUpdate(current))
                return current;

            var next = update(current) with
            {
                Revision = current.Revision + 1,
                UpdatedAt = timeProvider.GetUtcNow()
            };
            if (await testRuns.TryUpdateAsync(next, current.Revision, cancellationToken))
                return next;
        }

        var latest = await testRuns.FindAsync(receipt.TestRunId, cancellationToken)
                     ?? throw new InvalidOperationException("The activity Test Run receipt disappeared during an update.");
        if (!canUpdate(latest))
            return latest;
        throw new InvalidOperationException("The activity Test Run receipt could not be updated after repeated concurrency conflicts.");
    }

    private static bool IsUndispatchedPreparing(ActivityDraftTestRunReceipt receipt) =>
        receipt.Status == ActivityDraftTestRunReceiptStatus.Preparing &&
        receipt.ArtifactId is null &&
        receipt.SourceReferenceId is null &&
        receipt.CommandDispatchStatus is null;

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
                hasValue = x.Value.Value.HasValue,
                value = x.Value.Value
            });
        var canonical = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new
        {
            request.DraftId,
            request.ExpectedRevision,
            request.CorrelationId,
            inputs
        }, _ => null);
        return Elsa.Serialization.Core.JsonHashInput.Hash(canonical);
    }

    private static string RequestFingerprint(StartActivityVersionTestRun request)
    {
        var inputs = (request.Inputs ?? new Dictionary<string, ActivityDraftTestRunInput>())
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new
            {
                name = x.Key,
                state = x.Value.State,
                value = x.Value.Value?.GetRawText()
            });
        return StableHash(JsonSerializer.Serialize(new
        {
            request.VersionId,
            request.CorrelationId,
            inputs
        }));
    }

    private static string ActivityVersionReceiptId(string versionId) =>
        string.Concat(ActivityVersionReceiptPrefix, versionId);

    private static string? ReadActivityVersionReceiptId(string receiptResourceId) =>
        receiptResourceId.StartsWith(ActivityVersionReceiptPrefix, StringComparison.Ordinal)
            ? receiptResourceId[ActivityVersionReceiptPrefix.Length..]
            : null;

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
            var targetType = new ValueTypeDescriptor(input.Type.Alias, input.Type.CollectionKind);
            // The contract driver encodes the boundary snapshot; it is not an external payload profile.
            var policy = ValueProtectionPolicy.InstanceInline;
            var literal = value.Value.Value.ValueKind == JsonValueKind.Null
                ? ValueEnvelope.Null(targetType, policy)
                : ValueEnvelope.Inline(targetType, value.Value.Value, policy);
            result.Add(input.ReferenceKey, new RuntimeInputBinding(
                inputKey: input.ReferenceKey,
                targetType: targetType,
                effectivePolicy: policy,
                source: RuntimeInputBindingSource.Literal,
                literal: literal,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
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
        IReadOnlyCollection<ActivityNodePresentationRecord> presentation,
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
            LayoutSidecar: new([new(versionId, origin, template.TemplateHash, records, [])]),
            ActivityPresentation: ActivityTemplatePresentation.CopyFrom(presentation, template));
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
                ActivityErrorCodes.RequestInvalid,
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
