using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Constants;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed record PublishActivityDefinitionRequest(
    string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string Version,
    string ReviewToken = "",
    string IdempotencyKey = "");

public sealed record PreflightActivityDefinitionPublicationRequest(
    string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId)
{
    public string? Version { get; init; }
}

public sealed record PublishActivityDefinitionResult(
    ActivityPublicationResult Publication,
    ActivityDefinitionVersionPublication VersionPublication,
    ExecutableActivityTemplate Template,
    WorkflowExecutableSourceReference SourceReference,
    ActivityResourceMeasurements Measurements,
    ActivityVersionDiff? Diff,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public interface IActivityDefinitionPublisher
{
    Task<ActivityPublicationPreflightView> PreflightAsync(
        PreflightActivityDefinitionPublicationRequest request,
        CancellationToken cancellationToken = default);

    Task<ActivityPublicationReceipt> PublishReviewedAsync(
        PublishActivityDefinitionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ActivityPublicationReceipt> GetReceiptAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class ActivityPublicationRejectedException(
    string errorCode,
    string message,
    IReadOnlyList<ActivityDiagnostic> diagnostics,
    bool isConflict = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
    public IReadOnlyList<ActivityDiagnostic> Diagnostics { get; } = diagnostics;
    public bool IsConflict { get; } = isConflict;
}

/// <summary>
/// Coordinates validation, exact compilation, compatibility policy, and the single cross-domain
/// commit. The commit command repeats the expected revision/head checks inside its transaction.
/// </summary>
public sealed class ActivityDefinitionPublisher(
    IActivityDefinitionStore definitions,
    IActivityDefinitionAuthoringStore authoringStore,
    IActivityDefinitionDraftStore draftStore,
    IActivityDefinitionVersionPublicationStore publicationStore,
    IActivityDefinitionLayoutStore layoutStore,
    IActivityDirectDependencyStore dependencyStore,
    IActivityDraftValidator validator,
    IActivityVersionDiffer differ,
    IActivityTemplateCompiler compiler,
    IActivityPublishingAuthorizationContext authorization,
    ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commitCommand,
    IDistributedLockProvider lockProvider,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider,
    IActivityPublicationReceiptStore? receiptStore = null,
    IEnumerable<IActivityActivationStrategy>? activityActivationStrategies = null,
    IRuntimeDurableValueStorageDriverRegistry? storageDrivers = null) : IActivityDefinitionPublisher
{
    private readonly IActivityPublicationReceiptStore _receiptStore =
        receiptStore ?? new InMemoryActivityPublicationReceiptStore();
    private readonly ActivityPublicationReviewPolicy _reviewPolicy =
        new(activityActivationStrategies, storageDrivers);

    public async Task<ActivityPublicationPreflightView> PreflightAsync(
        PreflightActivityDefinitionPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await draftStore.FindAsync(request.DraftId, cancellationToken)
                    ?? throw Reject("activity.draft.not-found", "Activity draft was not found.", [], true);
        EnsureAuthorized(draft.TenantId);
        await using var lockHandle = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(draft.DefinitionId),
            null,
            cancellationToken);

        draft = await draftStore.FindAsync(request.DraftId, cancellationToken)
                ?? throw Reject("activity.draft.not-found", "Activity draft was not found.", [], true);
        EnsureAuthorized(draft.TenantId);
        var definition = await definitions.GetAsync(draft.DefinitionId, cancellationToken);
        EnsureAuthorized(definition.TenantId);
        var authoring = await authoringStore.FindAsync(definition.Id, cancellationToken)
                        ?? throw Reject(
                            "activity.definition.authoring-not-found",
                            "Activity definition authoring state was not found.",
                            [],
                            true);
        EnsureAuthorized(authoring.TenantId);
        EnsureExpectedState(
            new(
                request.DraftId,
                request.ExpectedDraftRevision,
                request.ExpectedDefinitionHeadVersionId,
                "0.0.0"),
            draft,
            authoring);

        var layout = await layoutStore.FindDraftLayoutAsync(draft.Id, cancellationToken)
                     ?? throw Reject(
                         "activity.draft.layout-not-found",
                         "The draft layout was not found.",
                         [],
                         true);
        EnsureAuthorized(layout.TenantId);
        if (layout.Revision != draft.Revision)
            throw Reject(
                "activity.draft.stale-layout",
                "The draft layout does not match the expected draft revision.",
                [],
                true);

        var validation = await validator.ValidateAsync(
            new(definition.Id, draft.Id, draft.Revision, draft.State),
            cancellationToken);
        var head = authoring.HeadVersionId is null
            ? null
            : await publicationStore.FindAsync(authoring.HeadVersionId, cancellationToken)
              ?? throw Reject(
                  "activity.definition.head-invalid",
                  "The current definition head publication was not found.",
                  [],
                  true);
        var candidateVersionId = ActivityPublicationReviewPolicy.StableCandidateVersionId(
            definition.Id,
            draft.Id,
            draft.Revision);
        var candidateVersion = request.Version ?? ActivityPublicationReviewPolicy.ProvisionalVersion(head?.Version);
        if (!SemVer.TryParse(candidateVersion, out _))
            throw Reject(
                "activity.request.invalid",
                $"Version '{candidateVersion}' is not valid SemVer 2.0.0.",
                [Diagnostic("activity.version.invalid", $"Version '{candidateVersion}' is not valid SemVer 2.0.0.", draft)]);

        async Task<(ActivityTemplateCompilerResult Compilation, ActivityVersionDiff? Diff)> CompileCandidateAsync(string version)
        {
            var compilation = await compiler.CompileAsync(
                new(
                    definition,
                    draft,
                    candidateVersionId,
                    version,
                    ComputeLayoutBytes(layout.Records)),
                cancellationToken);
            var diff = compilation.Template is null
                ? null
                : await ComputeDiffAsync(
                    head,
                    candidateVersionId,
                    version,
                    draft,
                    layout,
                    compilation,
                    cancellationToken);
            return (compilation, diff);
        }

        var existingVersions = await publicationStore.ListByDefinitionAsync(definition.Id, cancellationToken);
        ActivityTemplateCompilerResult compilation;
        ActivityVersionDiff? diff;
        ActivityVersionBump requiredBump;
        IReadOnlyList<string> validVersions;
        string minimumVersion;
        var attemptedVersions = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (!attemptedVersions.Add(candidateVersion))
                throw Reject(
                    "activity.publication.invalid",
                    "The publication version could not be selected deterministically.",
                    [new(
                        "activity.version.selection-unstable",
                        ActivityDiagnosticSeverity.Error,
                        "Provider compilation changed the required version bump without reaching a stable suggested version.",
                        new("ActivityDraft", draft.Id, definition.Id, Revision: draft.Revision),
                        Remediation: "Run publication preflight with an explicit exact version.",
                        Metadata: new Dictionary<string, string>(StringComparer.Ordinal))]);

            var review = await CompileCandidateAsync(candidateVersion);
            compilation = review.Compilation;
            diff = review.Diff;
            requiredBump = diff?.RequiredBump ?? ActivityVersionBump.None;
            validVersions = ActivityPublicationReviewPolicy.AvailableVersionChoices(
                head?.Version,
                requiredBump,
                existingVersions);
            minimumVersion = ActivityPublicationReviewPolicy.MinimumVersion(
                head?.Version,
                requiredBump);
            if (request.Version is not null)
                break;

            var suggestedVersion = validVersions.FirstOrDefault()
                                   ?? throw Reject(
                                       "activity.version.conflict",
                                       "No suggested semantic version is available for publication.",
                                       [],
                                       true);
            if (StringComparer.Ordinal.Equals(candidateVersion, suggestedVersion))
                break;
            candidateVersion = suggestedVersion;
        }

        if (!ActivityPublicationReviewPolicy.IsVersionAtLeast(candidateVersion, minimumVersion))
            throw Reject(
                "activity.publication.invalid",
                "The requested exact version is below the reviewed minimum.",
                [new(
                    "activity.version.bump-insufficient",
                    ActivityDiagnosticSeverity.Error,
                    $"Version {candidateVersion} is below the reviewed minimum {minimumVersion}.",
                    new("ActivityDraft", draft.Id, definition.Id, Revision: draft.Revision),
                    Remediation: $"Publish as {minimumVersion} or a higher unique semantic version.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["requestedVersion"] = candidateVersion,
                        ["minimumVersion"] = minimumVersion
                    })]);
        if (SemVer.TryParse(candidateVersion, out var selectedVersion) &&
            existingVersions.Any(x =>
                SemVer.TryParse(x.Version, out var existingVersion) &&
                existingVersion == selectedVersion))
            throw Reject(
                "activity.version.conflict",
                $"Version '{candidateVersion}' already exists.",
                [],
                true);
        var diagnostics = validation.Diagnostics
            .Concat(compilation.Diagnostics)
            .Concat(_reviewPolicy.ReadinessDiagnostics(draft, compilation.Template))
            .ToArray();
        var orderedDiagnostics = ActivityDiagnosticOrderer.Order(diagnostics);
        var provider = new ActivityPublicationCapabilityReadinessView(
            "Provider",
            draft.State.Provider.ProviderKey,
            draft.State.Provider.SchemaVersion,
            compilation.Template is null ? "Unavailable" : "Available",
            compilation.Template is null ? [] : [draft.State.Provider.SchemaVersion]);
        var storage = _reviewPolicy.StorageReadiness(compilation.Template);
        var runtime = _reviewPolicy.RuntimeReadiness(compilation.Template);
        var dependencies = compilation.DirectDependencies
            .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .Select(x => new ActivityPublicationDependencyEvidenceView(
                x.DefinitionId,
                x.VersionId,
                x.Version,
                x.TemplateHash,
                x.OccurrenceId))
            .ToArray();
        var impactFirst = diff?.Changes
            .OrderBy(ActivityPublicationReviewPolicy.ImpactRank)
            .ThenBy(x => x.ChangeId, StringComparer.Ordinal)
            .ToArray() ?? [];
        var isPublishable =
            compilation.Template is not null &&
            orderedDiagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error);
        var reviewToken = ActivityPublicationReviewPolicy.ReviewToken(
            draft,
            authoring.HeadVersionId,
            candidateVersion,
            compilation.Template,
            diff,
            requiredBump,
            validVersions,
            dependencies,
            provider,
            storage,
            runtime,
            orderedDiagnostics);

        return new(
            draft.Id,
            draft.Revision,
            definition.Id,
            authoring.HeadVersionId,
            head is not null,
            reviewToken,
            isPublishable,
            minimumVersion,
            validVersions,
            diff,
            impactFirst,
            dependencies,
            provider,
            storage,
            runtime,
            orderedDiagnostics)
        {
            ReviewedVersion = candidateVersion
        };
    }

    public async Task<ActivityPublicationReceipt> PublishReviewedAsync(
        PublishActivityDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateReviewedRequest(request);
        var fingerprint = RequestFingerprint(request);
        var existing = await _receiptStore.FindAsync(
            authorization.TenantId,
            request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            EnsureAuthorized(existing.TenantId);
            return Replay(existing, fingerprint);
        }

        try
        {
            var preflight = await PreflightAsync(
                new(
                    request.DraftId,
                    request.ExpectedDraftRevision,
                    request.ExpectedDefinitionHeadVersionId)
                {
                    Version = request.Version
                },
                cancellationToken);
            if (!StringComparer.Ordinal.Equals(preflight.ReviewToken, request.ReviewToken))
                throw Reject(
                    "activity.publication.review-stale",
                    "The reviewed publication binding is stale.",
                    [new(
                        "activity.publication.review-stale",
                        ActivityDiagnosticSeverity.Error,
                        "The draft, definition head, or authoritative publication evidence changed after review.",
                        new(
                            "ActivityDraft",
                            request.DraftId,
                            preflight.DefinitionId,
                            Revision: request.ExpectedDraftRevision),
                        Remediation: "Run publication preflight again and review the current evidence.",
                        Metadata: new Dictionary<string, string>(StringComparer.Ordinal))],
                    true);
            if (!ActivityPublicationReviewPolicy.IsVersionAtLeast(
                    request.Version,
                    preflight.MinimumVersion))
                throw Reject(
                    "activity.publication.invalid",
                    "The requested exact version is below the reviewed minimum.",
                    [new(
                        "activity.version.bump-insufficient",
                        ActivityDiagnosticSeverity.Error,
                        $"Version {request.Version} is below the reviewed minimum {preflight.MinimumVersion}.",
                        new(
                            "ActivityDraft",
                            request.DraftId,
                            preflight.DefinitionId,
                            Revision: request.ExpectedDraftRevision),
                        Remediation: $"Publish as {preflight.MinimumVersion} or a higher unique semantic version.",
                        Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["requestedVersion"] = request.Version,
                            ["minimumVersion"] = preflight.MinimumVersion
                        })]);
            if (!preflight.IsPublishable)
                throw Reject(
                    "activity.publication.invalid",
                    "The reviewed activity publication is not ready.",
                    preflight.Diagnostics);

            await PublishCoreAsync(request, cancellationToken);
            return await GetReceiptAsync(request.IdempotencyKey, cancellationToken);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            var concurrentlyCompleted = await _receiptStore.FindAsync(
                authorization.TenantId,
                request.IdempotencyKey,
                cancellationToken);
            if (concurrentlyCompleted is not null)
            {
                EnsureAuthorized(concurrentlyCompleted.TenantId);
                return Replay(concurrentlyCompleted, fingerprint);
            }
            var status = exception.ErrorCode switch
            {
                "activity.publication.conflict" => ActivityPublicationReceiptStatus.OutcomeUnknown,
                var code when code.Contains("stale", StringComparison.Ordinal) =>
                    ActivityPublicationReceiptStatus.Stale,
                _ => ActivityPublicationReceiptStatus.Rejected
            };
            await StoreTerminalReceiptAsync(request, fingerprint, status, exception.ErrorCode, exception.Diagnostics, cancellationToken);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var concurrentlyCompleted = await _receiptStore.FindAsync(
                authorization.TenantId,
                request.IdempotencyKey,
                CancellationToken.None);
            if (concurrentlyCompleted is not null)
            {
                EnsureAuthorized(concurrentlyCompleted.TenantId);
                return Replay(concurrentlyCompleted, fingerprint);
            }
            await StoreTerminalReceiptAsync(
                request,
                fingerprint,
                ActivityPublicationReceiptStatus.Failed,
                "activity.operation.failed",
                [],
                cancellationToken);
            throw;
        }
    }

    public async ValueTask<ActivityPublicationReceipt> GetReceiptAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var receipt = await _receiptStore.FindAsync(
            authorization.TenantId,
            idempotencyKey,
            cancellationToken);
        if (receipt is null)
            return new(
                authorization.TenantId,
                idempotencyKey,
                "",
                ActivityPublicationReceiptStatus.OutcomeUnknown,
                "",
                0,
                null,
                "",
                "",
                null,
                "activity.publication.outcome-unknown",
                [],
                timeProvider.GetUtcNow());

        EnsureAuthorized(receipt.TenantId);
        return receipt;
    }

    private async Task<PublishActivityDefinitionResult> PublishCoreAsync(
        PublishActivityDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await draftStore.FindAsync(request.DraftId, cancellationToken)
                    ?? throw Reject("activity.draft.not-found", "Activity draft was not found.", [], true);
        EnsureAuthorized(draft.TenantId);
        await using var lockHandle = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(draft.DefinitionId),
            null,
            cancellationToken);

        draft = await draftStore.FindAsync(request.DraftId, cancellationToken)
                ?? throw Reject("activity.draft.not-found", "Activity draft was not found.", [], true);
        EnsureAuthorized(draft.TenantId);
        var definition = await definitions.GetAsync(draft.DefinitionId, cancellationToken);
        EnsureAuthorized(definition.TenantId);
        var authoring = await authoringStore.FindAsync(definition.Id, cancellationToken)
                        ?? throw Reject("activity.definition.authoring-not-found", "Activity definition authoring state was not found.", [], true);
        EnsureAuthorized(authoring.TenantId);
        EnsureExpectedState(request, draft, authoring);

        var validation = await validator.ValidateAsync(new(
            definition.Id,
            draft.Id,
            draft.Revision,
            draft.State), cancellationToken);
        if (!validation.IsValid)
            throw Reject("activity.publication.invalid", "Activity publication was rejected by validation.", validation.Diagnostics);

        if (!SemVer.TryParse(request.Version, out var requestedVersion))
            throw Reject("activity.request.invalid", $"Version '{request.Version}' is not valid SemVer 2.0.0.", [Diagnostic(
                "activity.version.invalid",
                $"Version '{request.Version}' is not valid SemVer 2.0.0.",
                draft)]);

        var existingVersions = await publicationStore.ListByDefinitionAsync(definition.Id, cancellationToken);
        if (existingVersions.Any(x => SemVer.TryParse(x.Version, out var existing) && existing == requestedVersion))
            throw Reject("activity.version.conflict", $"Version '{request.Version}' already exists.", [], true);

        var layout = await layoutStore.FindDraftLayoutAsync(draft.Id, cancellationToken)
                     ?? throw Reject("activity.draft.layout-not-found", "The draft layout was not found.", [], true);
        EnsureAuthorized(layout.TenantId);
        if (layout.Revision != draft.Revision)
            throw Reject("activity.draft.stale-layout", "The draft layout does not match the expected draft revision.", [], true);

        var versionId = NewId("activity-ver");
        var compilation = await compiler.CompileAsync(new(
            definition,
            draft,
            versionId,
            request.Version,
            ComputeLayoutBytes(layout.Records)), cancellationToken);
        if (!compilation.IsSuccessful || compilation.Template is null)
            throw Reject("activity.publication.invalid", "Activity publication was rejected during compilation.", compilation.Diagnostics);

        var head = authoring.HeadVersionId is null
            ? null
            : await publicationStore.FindAsync(authoring.HeadVersionId, cancellationToken)
              ?? throw Reject("activity.definition.head-invalid", "The current definition head publication was not found.", [], true);
        var diff = await ComputeDiffAsync(head, versionId, request.Version, draft, layout, compilation, cancellationToken);
        var semVerDiagnostic = ValidateRequestedBump(head?.Version, requestedVersion, diff?.RequiredBump ?? ActivityVersionBump.None, draft);
        if (semVerDiagnostic is not null)
            throw Reject("activity.publication.invalid", "The requested activity version is insufficient.", [semVerDiagnostic]);
        var readinessDiagnostics = _reviewPolicy.ReadinessDiagnostics(draft, compilation.Template).ToArray();

        var requiredBump = diff?.RequiredBump ?? ActivityVersionBump.None;
        var validVersions = ActivityPublicationReviewPolicy.AvailableVersionChoices(
            head?.Version,
            requiredBump,
            existingVersions);
        var dependencies = compilation.DirectDependencies
            .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .Select(x => new ActivityPublicationDependencyEvidenceView(
                x.DefinitionId,
                x.VersionId,
                x.Version,
                x.TemplateHash,
                x.OccurrenceId))
            .ToArray();
        var provider = new ActivityPublicationCapabilityReadinessView(
            "Provider",
            draft.State.Provider.ProviderKey,
            draft.State.Provider.SchemaVersion,
            "Available",
            [draft.State.Provider.SchemaVersion]);
        var diagnostics = ActivityDiagnosticOrderer.Order(
            validation.Diagnostics.Concat(compilation.Diagnostics).Concat(readinessDiagnostics));
        var currentReviewToken = ActivityPublicationReviewPolicy.ReviewToken(
            draft,
            authoring.HeadVersionId,
            request.Version,
            compilation.Template,
            diff,
            requiredBump,
            validVersions,
            dependencies,
            provider,
            _reviewPolicy.StorageReadiness(compilation.Template),
            _reviewPolicy.RuntimeReadiness(compilation.Template),
            diagnostics);
        if (!StringComparer.Ordinal.Equals(currentReviewToken, request.ReviewToken))
            throw Reject(
                "activity.publication.review-stale",
                "The reviewed publication binding is stale.",
                [],
                true);
        if (readinessDiagnostics.Length > 0)
            throw Reject(
                "activity.publication.invalid",
                "Activity publication runtime readiness checks failed.",
                readinessDiagnostics);

        var now = timeProvider.GetUtcNow();
        var sourceReferenceId = NewId("activity-source-ref");
        var sourceReference = CreateSourceReference(
            sourceReferenceId,
            definition,
            draft,
            versionId,
            request.Version,
            compilation.Template,
            layout.Records.ToArray(),
            now);
        var catalogVersion = CreateCatalogVersion(definition, draft, versionId, request.Version, compilation.Template, now);
        var publication = CreatePublication(
            definition,
            draft,
            versionId,
            request.Version,
            compilation.Template.ProviderFingerprint,
            sourceReferenceId,
            compilation.Template,
            compilation.Measurements,
            now);
        var versionLayout = new ActivityDefinitionVersionLayout
        {
            Id = versionId,
            DefinitionVersionId = versionId,
            TenantId = definition.TenantId,
            Records = layout.Records.ToArray(),
            CreatedAt = now,
            LastModifiedAt = now
        };
        var edges = compilation.DirectDependencies.Select(dependency => new ActivityDependencyEdge
        {
            Id = NewId("activity-edge"),
            TenantId = definition.TenantId,
            OwnerVersionId = versionId,
            OwnerTemplateHash = compilation.Template.TemplateHash,
            DependencyVersionId = dependency.VersionId,
            DependencyTemplateHash = dependency.TemplateHash,
            OccurrenceId = dependency.OccurrenceId,
            ParentOccurrenceId = dependency.ParentOccurrenceId,
            ChildSlotName = dependency.ChildSlotName,
            ChildIndex = dependency.ChildIndex,
            NodeOrigin = dependency.NodeOrigin.ToArray(),
            MemberUsage = dependency.MemberUsage.ToArray(),
            CreatedAt = now,
            LastModifiedAt = now
        }).ToArray();
        var receipt = new ActivityPublicationReceipt(
            authorization.TenantId,
            request.IdempotencyKey,
            RequestFingerprint(request),
            ActivityPublicationReceiptStatus.Applied,
            draft.Id,
            request.ExpectedDraftRevision,
            request.ExpectedDefinitionHeadVersionId,
            request.ReviewToken,
            request.Version,
            new(
                definition.Id,
                versionId,
                draft.Id,
                request.Version,
                compilation.Template.TemplateId,
                compilation.Template.TemplateHash,
                sourceReferenceId,
                now),
            null,
            [],
            now);

        ActivityPublicationResult committed;
        try
        {
            committed = await commitCommand.ExecuteAsync(new(
                authorization.TenantId,
                new(
                    draft.Id,
                    request.ExpectedDraftRevision,
                    definition.Id,
                    request.ExpectedDefinitionHeadVersionId,
                    catalogVersion,
                    publication,
                    versionLayout,
                    edges),
                compilation.Template,
                sourceReference,
                receipt), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Reject(
                "activity.publication.conflict",
                "Activity publication lost an expected-state or uniqueness race.",
                [],
                true,
                exception);
        }

        return new(
            committed,
            publication,
            compilation.Template,
            sourceReference,
            compilation.Measurements,
            diff,
            ActivityDiagnosticOrderer.Order(validation.Diagnostics.Concat(compilation.Diagnostics)));
    }

    private static void EnsureExpectedState(
        PublishActivityDefinitionRequest request,
        ActivityDefinitionDraft draft,
        ActivityDefinitionAuthoringState authoring)
    {
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Reject("activity.draft.not-active", "Only an active activity draft can be published.", [], true);
        if (draft.Revision != request.ExpectedDraftRevision)
            throw Reject("activity.draft.stale-revision", "The activity draft revision is stale.", [], true);
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, request.ExpectedDefinitionHeadVersionId))
            throw Reject("activity.definition.stale-head", "The activity definition head is stale.", [], true);
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw Reject("activity.definition.content-authority", "This activity definition is not Design-owned.", [], true);
    }

    private void EnsureAuthorized(string? tenantId)
    {
        if (!authorization.CanAccessTenant(tenantId))
            throw Reject(
                "activity.tenant.reference-denied",
                "The requested activity identity is outside the caller's authorized scope.",
                []);
    }

    private async ValueTask<ActivityVersionDiff> ComputeDiffAsync(
        ActivityDefinitionVersionPublication? head,
        string candidateVersionId,
        string candidateVersion,
        ActivityDefinitionDraft draft,
        ActivityDefinitionDraftLayout candidateLayout,
        ActivityTemplateCompilerResult compilation,
        CancellationToken cancellationToken)
    {
        var fromDependencies = new List<ActivityDependencyItem>();
        ActivityDefinitionVersionLayout? headLayout = null;
        if (head is not null)
        {
            var fromEdges = await dependencyStore.ListOutboundAsync(head.DefinitionVersionId, cancellationToken);
            foreach (var edge in fromEdges.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
            {
                var dependency = await publicationStore.FindAsync(edge.DependencyVersionId, cancellationToken)
                                 ?? throw Reject(
                                     "activity.dependency.version-not-found",
                                     $"Published dependency version '{edge.DependencyVersionId}' was not found while comparing the current head.",
                                     [],
                                     true);
                fromDependencies.Add(ToDependencyItem(head, dependency, edge));
            }

            headLayout = await layoutStore.FindVersionLayoutAsync(head.DefinitionVersionId, cancellationToken);
        }

        var emptyContract = new ActivityContract(draft.State.Contract.ContractSchemaVersion, [], [], []);
        return await differ.DiffAsync(new(
            head is null
                ? new("ActivityDefinitionBaseline", draft.DefinitionId)
                : new("ActivityVersion", head.DefinitionId, head.DefinitionVersionId, Version: head.Version, TemplateHash: head.TemplateHash),
            new("ActivityVersion", draft.DefinitionId, candidateVersionId, Version: candidateVersion, TemplateHash: compilation.Template!.TemplateHash),
            head?.Contract ?? emptyContract,
            draft.State.Contract,
            head?.Provider ?? draft.State.Provider,
            draft.State.Provider,
            fromDependencies,
            compilation.DirectDependencies.Select(x => ToDependencyItem(draft.DefinitionId, candidateVersionId, candidateVersion, compilation.Template.TemplateHash, x)).ToArray(),
            compilation.ProviderCompatibilityChanges,
            FromImplementation: head is null ? null : Implementation(head, headLayout),
            ToImplementation: new(
                compilation.Template.ProviderFingerprint,
                compilation.Measurements.LocalNodeCount,
                compilation.Template.ResumeTargets.Count,
                ActivityLayoutHasher.Compute(candidateLayout.Records),
                candidateLayout.Records.Count,
                compilation.Template.RuntimeRequirements
                    .Select(x => new ActivityRuntimeRequirementDeclaration(x.ConsumerKey, x.SchemaVersion))
                    .ToArray())),
            cancellationToken);
    }

    private static ActivityVersionImplementationFacts Implementation(
        ActivityDefinitionVersionPublication publication,
        ActivityDefinitionVersionLayout? layout) => new(
        publication.ProviderFingerprint,
        publication.ResourceMeasurements.LocalNodeCount,
        publication.ResumeTargetCount,
        layout is null ? null : ActivityLayoutHasher.Compute(layout.Records),
        layout?.Records.Count,
        publication.RuntimeRequirements.ToArray());

    private static ActivityDependencyItem ToDependencyItem(
        ActivityDefinitionVersionPublication owner,
        ActivityDefinitionVersionPublication dependency,
        ActivityDependencyEdge edge)
    {
        var ownerReference = ToReference(owner);
        var dependencyReference = ToReference(dependency);
        return new(
        edge.Id,
        ownerReference,
        dependencyReference,
        new(edge.OccurrenceId, edge.NodeOrigin.ToArray()),
        true,
        1,
        [ownerReference, dependencyReference]);
    }

    private static ActivityDependencyItem ToDependencyItem(
        string ownerDefinitionId,
        string ownerVersionId,
        string ownerVersion,
        string ownerTemplateHash,
        ActivityResolvedDependency dependency) => new(
        $"{ownerVersionId}:{dependency.OccurrenceId}",
        new("ActivityVersion", ownerDefinitionId, ownerVersionId, ownerVersion, TemplateHash: ownerTemplateHash),
        new("ActivityVersion", dependency.DefinitionId, dependency.VersionId, dependency.Version, TemplateHash: dependency.TemplateHash, TenantId: dependency.TenantId, Lifecycle: dependency.Lifecycle),
        new(dependency.OccurrenceId, dependency.NodeOrigin),
        true,
        1,
        []);

    private static ActivityDefinitionReference ToReference(ActivityDefinitionVersionPublication publication) => new(
        "ActivityVersion",
        publication.DefinitionId,
        publication.DefinitionVersionId,
        publication.Version,
        TemplateHash: publication.TemplateHash,
        TenantId: publication.TenantId,
        Lifecycle: publication.Lifecycle);

    private static ActivityDiagnostic? ValidateRequestedBump(
        string? baseVersionText,
        SemVer requested,
        ActivityVersionBump required,
        ActivityDefinitionDraft draft)
    {
        if (baseVersionText is null)
            return null;
        if (!SemVer.TryParse(baseVersionText, out var @base) || requested <= @base)
            return BumpDiagnostic(baseVersionText, requested.ToString(), required, "a version with greater precedence", draft);

        var sufficient = required switch
        {
            ActivityVersionBump.Major => requested.Major > @base.Major,
            ActivityVersionBump.Minor => requested.Major > @base.Major || requested.Major == @base.Major && requested.Minor > @base.Minor,
            ActivityVersionBump.Patch => requested > @base,
            _ => requested > @base
        };
        if (sufficient)
            return null;

        var minimum = required switch
        {
            ActivityVersionBump.Major => $"{@base.Major + 1}.0.0",
            ActivityVersionBump.Minor => $"{@base.Major}.{@base.Minor + 1}.0",
            _ => $"{@base.Major}.{@base.Minor}.{@base.Patch + 1}"
        };
        return BumpDiagnostic(baseVersionText, requested.ToString(), required, minimum, draft);
    }

    private static ActivityDiagnostic BumpDiagnostic(
        string baseVersion,
        string requestedVersion,
        ActivityVersionBump required,
        string minimum,
        ActivityDefinitionDraft draft) => new(
        "activity.version.bump-insufficient",
        ActivityDiagnosticSeverity.Error,
        $"Version {requestedVersion} is insufficient; the candidate requires a {required} increment from {baseVersion}.",
        new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision),
        Remediation: $"Publish as {minimum} or revise the changes.",
        Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseVersion"] = baseVersion,
            ["requestedVersion"] = requestedVersion,
            ["requiredBump"] = required.ToString(),
            ["minimumVersion"] = minimum
        });

    private static ActivityDefinitionVersion CreateCatalogVersion(
        ActivityDefinition definition,
        ActivityDefinitionDraft draft,
        string versionId,
        string version,
        ExecutableActivityTemplate template,
        DateTimeOffset now) => new(version, definition.Id)
        {
            Id = versionId,
            TenantId = definition.TenantId,
            ProviderKey = draft.State.Provider.ProviderKey,
            ProviderSchemaVersion = draft.State.Provider.SchemaVersion,
            ConsumerKey = template.Root.Descriptor.ConsumerKey,
            ConsumerSchemaVersion = template.Root.Descriptor.SchemaVersion,
            DescriptorPayload = template.Root.Descriptor.Payload,
            // Project the published public contract's inputs/outputs into the canvas descriptors the
            // authoring catalog reads. Without this a design-owned (e.g. elsa.activity-graph) version
            // surfaces no editable properties in the workflow editor, unlike CLR-scanned versions whose
            // InputDefinition/OutputDefinition projections are populated by the reconciler. See #930.
            Inputs = draft.State.Contract.Inputs.Select(ToInputDefinition).ToArray(),
            Outputs = draft.State.Contract.Outputs.Select(ToOutputDefinition).ToArray(),
            SourceKind = "ActivityDefinitionDraft",
            SourceId = draft.Id,
            Hash = template.TemplateHash,
            CreatedAt = now,
            LastModifiedAt = now
        };

    private static InputDefinition ToInputDefinition(ActivityInputContract input) => new(
        ReferenceKey: input.ReferenceKey,
        Name: input.Name,
        Type: input.Type,
        StorageDriverType: input.StorageDriverKey,
        DisplayName: string.IsNullOrWhiteSpace(input.DisplayName) ? input.Name : input.DisplayName,
        Category: input.Category,
        IsNullable: input.IsNullable,
        Description: input.Description,
        Order: input.Order,
        UiHint: input.UiHint,
        UISpecifications: input.UiSpecifications,
        IsRequired: input.IsRequired,
        DefaultValue: input.Default?.Value,
        DefaultSyntax: input.Default?.Syntax);

    private static OutputDefinition ToOutputDefinition(ActivityOutputContract output) => new(
        ReferenceKey: output.ReferenceKey,
        Name: output.Name,
        Type: output.Type,
        StorageDriverType: output.StorageDriverKey,
        DisplayName: string.IsNullOrWhiteSpace(output.DisplayName) ? output.Name : output.DisplayName,
        Category: output.Category,
        IsNullable: output.IsNullable,
        Description: output.Description,
        Order: output.Order,
        UiHint: output.UiHint,
        UISpecifications: output.UiSpecifications,
        IsRequired: output.IsRequired,
        SourceRepresentation: output.SourceRepresentation);

    private static ActivityDefinitionVersionPublication CreatePublication(
        ActivityDefinition definition,
        ActivityDefinitionDraft draft,
        string versionId,
        string version,
        string providerFingerprint,
        string sourceReferenceId,
        ExecutableActivityTemplate template,
        ActivityResourceMeasurements measurements,
        DateTimeOffset now) => new()
        {
            Id = versionId,
            TenantId = definition.TenantId,
            DefinitionVersionId = versionId,
            DefinitionId = definition.Id,
            Version = version,
            ActivityTypeKey = definition.ActivityTypeKey,
            ResolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
            SourceDraftId = draft.Id,
            SourceVersionId = draft.SourceVersionId,
            Contract = draft.State.Contract,
            Provider = draft.State.Provider,
            TemplateId = template.TemplateId,
            TemplateHash = template.TemplateHash,
            SourceReferenceId = sourceReferenceId,
            ProviderFingerprint = providerFingerprint,
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

    private static WorkflowExecutableSourceReference CreateSourceReference(
        string sourceReferenceId,
        ActivityDefinition definition,
        ActivityDefinitionDraft draft,
        string versionId,
        string version,
        ExecutableActivityTemplate template,
        IReadOnlyCollection<ActivityLayoutRecord> layout,
        DateTimeOffset now)
    {
        var records = layout.Select(ToExecutableLayoutRecord).ToArray();
        var flatLayout = layout.Select(ToWorkflowLayoutRecord).ToArray();
        var boundaryOrigin = new ActivityInvocationOrigin([
            new(ActivityInvocationOriginSegmentKind.TemplateBoundary, versionId)
        ]);
        var sidecar = new ExecutableLayoutSidecar([
            new(versionId, boundaryOrigin, template.TemplateHash, records, [])
        ]);
        return new(
            sourceReferenceId,
            template.TemplateId,
            "ActivityDefinitionVersion",
            versionId,
            version,
            definition.Id,
            versionId,
            version,
            now,
            now,
            WorkflowExecutableReferenceScope.Published,
            Layout: flatLayout,
            LayoutSidecar: sidecar);
    }

    private static ExecutableActivityLayoutRecord ToExecutableLayoutRecord(ActivityLayoutRecord record)
    {
        var (x, y, width, height) = ReadGeometry(record.Data);
        return new(record.NodeId, record.NodeId, record.NodeId, x, y, width, height, record.Data.Clone());
    }

    private static WorkflowExecutableLayoutRecord ToWorkflowLayoutRecord(ActivityLayoutRecord record)
    {
        var (x, y, width, height) = ReadGeometry(record.Data);
        return new(record.NodeId, x, y, width, height, record.Data.Clone());
    }

    private static (double X, double Y, double? Width, double? Height) ReadGeometry(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return (0, 0, null, null);
        return (
            ReadDouble(data, "x") ?? 0,
            ReadDouble(data, "y") ?? 0,
            ReadDouble(data, "width"),
            ReadDouble(data, "height"));
    }

    private static double? ReadDouble(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private static void ValidateReviewedRequest(PublishActivityDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DraftId) ||
            request.ExpectedDraftRevision <= 0 ||
            string.IsNullOrWhiteSpace(request.Version) ||
            string.IsNullOrWhiteSpace(request.ReviewToken) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 200 ||
            request.ExpectedDefinitionHeadVersionId is not null &&
            string.IsNullOrWhiteSpace(request.ExpectedDefinitionHeadVersionId) ||
            !SemVer.TryParse(request.Version, out _))
            throw Reject(
                "activity.request.invalid",
                "The reviewed publication request is malformed.",
                []);
    }

    private static ActivityPublicationReceipt Replay(
        ActivityPublicationReceipt receipt,
        string fingerprint)
    {
        if (!StringComparer.Ordinal.Equals(receipt.RequestFingerprint, fingerprint))
            throw Reject(
                "activity.publication.idempotency-conflict",
                "The idempotency key is already bound to another publication request.",
                [],
                true);
        if (receipt.Status == ActivityPublicationReceiptStatus.Applied)
            return receipt;
        throw Reject(
            receipt.ErrorCode ?? "activity.publication.outcome-unknown",
            "The idempotent publication request already has a terminal receipt.",
            receipt.Diagnostics,
            receipt.Status is ActivityPublicationReceiptStatus.Stale or
                ActivityPublicationReceiptStatus.OutcomeUnknown);
    }

    private static string RequestFingerprint(PublishActivityDefinitionRequest request) =>
        ActivityPublicationRequestFingerprint.Compute(
            request.DraftId,
            request.ExpectedDraftRevision,
            request.ExpectedDefinitionHeadVersionId,
            request.Version,
            request.ReviewToken);

    private async ValueTask StoreTerminalReceiptAsync(
        PublishActivityDefinitionRequest request,
        string fingerprint,
        ActivityPublicationReceiptStatus status,
        string errorCode,
        IReadOnlyList<ActivityDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var receipt = new ActivityPublicationReceipt(
            authorization.TenantId,
            request.IdempotencyKey,
            fingerprint,
            status,
            request.DraftId,
            request.ExpectedDraftRevision,
            request.ExpectedDefinitionHeadVersionId,
            request.ReviewToken,
            request.Version,
            null,
            errorCode,
            ActivityDiagnosticOrderer.Order(diagnostics),
            timeProvider.GetUtcNow());
        if (await _receiptStore.TryCreateAsync(receipt, cancellationToken))
            return;
        var existing = await _receiptStore.FindAsync(
            authorization.TenantId,
            request.IdempotencyKey,
            cancellationToken);
        if (existing is not null && !StringComparer.Ordinal.Equals(existing.RequestFingerprint, fingerprint))
            throw Reject(
                "activity.publication.idempotency-conflict",
                "The idempotency key is already bound to another publication request.",
                [],
                true);
    }

    private string NewId(string prefix) => $"{prefix}-{identityGenerator.Generate()}";

    private static long ComputeLayoutBytes(IEnumerable<ActivityLayoutRecord> records) => records.Sum(x =>
        (long)Encoding.UTF8.GetByteCount(x.NodeId) + Encoding.UTF8.GetByteCount(x.Data.GetRawText()));

    private static ActivityPublicationRejectedException Reject(
        string code,
        string message,
        IEnumerable<ActivityDiagnostic> diagnostics,
        bool conflict = false,
        Exception? innerException = null) => new(
        code,
        message,
        ActivityDiagnosticOrderer.Order(diagnostics),
        conflict,
        innerException);

    private static ActivityDiagnostic Diagnostic(string code, string message, ActivityDefinitionDraft draft) => new(
        code,
        ActivityDiagnosticSeverity.Error,
        message,
        new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision),
        Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
}
