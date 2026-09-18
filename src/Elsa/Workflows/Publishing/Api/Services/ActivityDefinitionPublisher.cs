using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Services;
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
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Primitives.Diagnostics;

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
        await using var context = await ResolvePublicationContextAsync(
            request.DraftId,
            request.ExpectedDraftRevision,
            request.ExpectedDefinitionHeadVersionId,
            cancellationToken);
        var (definition, draft) = (context.Definition, context.Draft);
        var candidateVersionId = ActivityPublicationReviewPolicy.StableCandidateVersionId(
            definition.Id,
            draft.Id,
            draft.Revision);
        var candidateVersion = request.Version ?? ActivityPublicationReviewPolicy.ProvisionalVersion(context.Head?.Version);
        if (!SemVer.TryParse(candidateVersion, out _))
            throw Reject(
                ActivityErrorCodes.RequestInvalid,
                $"Version '{candidateVersion}' is not valid SemVer 2.0.0.",
                [Diagnostic(ActivityErrorCodes.VersionInvalid, $"Version '{candidateVersion}' is not valid SemVer 2.0.0.", draft)]);

        PublicationCandidate candidate;
        var attemptedVersions = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (!attemptedVersions.Add(candidateVersion))
                throw Reject(
                    ActivityErrorCodes.PublicationInvalid,
                    "The publication version could not be selected deterministically.",
                    [new(
                        "activity.version.selection-unstable",
                        ActivityDiagnosticSeverity.Error,
                        "Provider compilation changed the required version bump without reaching a stable suggested version.",
                        new("ActivityDraft", draft.Id, definition.Id, Revision: draft.Revision),
                        Remediation: "Run publication preflight with an explicit exact version.",
                        Metadata: new Dictionary<string, string>(StringComparer.Ordinal))]);

            candidate = await EvaluateCandidateAsync(context, candidateVersionId, candidateVersion, cancellationToken);
            if (request.Version is not null)
                break;

            var suggestedVersion = candidate.ValidVersions.FirstOrDefault()
                                   ?? throw Reject(
                                       ActivityErrorCodes.VersionConflict,
                                       "No suggested semantic version is available for publication.",
                                       [],
                                       true);
            if (StringComparer.Ordinal.Equals(candidateVersion, suggestedVersion))
                break;
            candidateVersion = suggestedVersion;
        }

        EnsureVersionAccepted(context, candidate);
        var impactFirst = candidate.Diff?.Changes
            .OrderBy(ActivityPublicationReviewPolicy.ImpactRank)
            .ThenBy(x => x.ChangeId, StringComparer.Ordinal)
            .ToArray() ?? [];

        return new(
            draft.Id,
            draft.Revision,
            definition.Id,
            context.Authoring.HeadVersionId,
            context.Head is not null,
            candidate.ReviewToken,
            candidate.IsPublishable,
            candidate.MinimumVersion,
            candidate.ValidVersions,
            candidate.Diff,
            impactFirst,
            candidate.Dependencies,
            candidate.Provider,
            candidate.Storage,
            candidate.Runtime,
            candidate.Diagnostics)
        {
            ReviewedVersion = candidate.Version
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
                ActivityErrorCodes.PublicationConflict => ActivityPublicationReceiptStatus.OutcomeUnknown,
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
                ActivityErrorCodes.OperationFailed,
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
        // The review, the checks and the commit share one lock, so nothing can change between what was evaluated
        // and what is written.
        await using var context = await ResolvePublicationContextAsync(
            request.DraftId,
            request.ExpectedDraftRevision,
            request.ExpectedDefinitionHeadVersionId,
            cancellationToken);
        var (definition, draft, layout) = (context.Definition, context.Draft, context.Layout);
        // Publish compiles under the version id it commits. Its evidence must still reproduce the token preflight issued
        // under the stable candidate id, so evidence that shifts with the version id is refused as a stale review.
        var versionId = NewId("activity-ver");
        var candidate = await EvaluateCandidateAsync(context, versionId, request.Version, cancellationToken);
        EnsureVersionAccepted(context, candidate);
        if (!StringComparer.Ordinal.Equals(candidate.ReviewToken, request.ReviewToken))
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
                        definition.Id,
                        Revision: request.ExpectedDraftRevision),
                    Remediation: "Run publication preflight again and review the current evidence.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal))],
                true);
        if (!candidate.IsPublishable)
            throw Reject(
                ActivityErrorCodes.PublicationInvalid,
                "The reviewed activity publication is not ready.",
                candidate.Diagnostics);

        var compilation = candidate.Compilation;
        var template = compilation.Template!;
        var now = timeProvider.GetUtcNow();
        var sourceReferenceId = NewId("activity-source-ref");
        var sourceReference = CreateSourceReference(
            sourceReferenceId,
            definition,
            draft,
            versionId,
            request.Version,
            template,
            layout.Records.ToArray(),
            now);
        var catalogVersion = CreateCatalogVersion(definition, draft, versionId, request.Version, template, now);
        var publication = CreatePublication(
            definition,
            draft,
            versionId,
            request.Version,
            template.ProviderFingerprint,
            sourceReferenceId,
            template,
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
            OwnerTemplateHash = template.TemplateHash,
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
                template.TemplateId,
                template.TemplateHash,
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
                template,
                sourceReference,
                receipt), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Reject(
                ActivityErrorCodes.PublicationConflict,
                "Activity publication lost an expected-state or uniqueness race.",
                [],
                true,
                exception);
        }

        return new(
            committed,
            publication,
            template,
            sourceReference,
            compilation.Measurements,
            candidate.Diff,
            ActivityDiagnosticOrderer.Order(context.Validation.Diagnostics.Concat(compilation.Diagnostics)));
    }

    /// <summary>
    /// Loads and checks everything a publication decision reads from the stores, under the definition's publication
    /// lock, which the returned context holds until it is disposed. Preflight and publish both start here, so the
    /// state rejections they can raise, and the order they raise them in, are one list.
    /// </summary>
    private async Task<PublicationContext> ResolvePublicationContextAsync(
        string draftId,
        long expectedDraftRevision,
        string? expectedDefinitionHeadVersionId,
        CancellationToken cancellationToken)
    {
        var draft = await FindDraftAsync(draftId, cancellationToken);
        var lockHandle = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(draft.DefinitionId),
            null,
            cancellationToken);
        try
        {
            draft = await FindDraftAsync(draftId, cancellationToken);
            var definition = await definitions.GetAsync(draft.DefinitionId, cancellationToken);
            EnsureAuthorized(definition.TenantId);
            var authoring = await authoringStore.FindAsync(definition.Id, cancellationToken)
                            ?? throw Reject(
                                "activity.definition.authoring-not-found",
                                "Activity definition authoring state was not found.",
                                [],
                                true);
            EnsureAuthorized(authoring.TenantId);
            EnsureExpectedState(expectedDraftRevision, expectedDefinitionHeadVersionId, draft, authoring);

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
            var existingVersions = await publicationStore.ListByDefinitionAsync(definition.Id, cancellationToken);
            return new(lockHandle, definition, draft, authoring, layout, validation, head, existingVersions);
        }
        catch
        {
            await lockHandle.DisposeAsync();
            throw;
        }
    }

    private async Task<ActivityDefinitionDraft> FindDraftAsync(string draftId, CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindAsync(draftId, cancellationToken)
                    ?? throw Reject("activity.draft.not-found", "Activity draft was not found.", [], true);
        EnsureAuthorized(draft.TenantId);
        return draft;
    }

    /// <summary>
    /// Compiles one exact candidate version against a resolved context and derives every piece of review evidence
    /// from it: the diff, the valid version choices, readiness, diagnostics, whether it is publishable, and the review
    /// token binding all of that. Only the version id differs between the two entry points, and it is a parameter.
    /// </summary>
    private async Task<PublicationCandidate> EvaluateCandidateAsync(
        PublicationContext context,
        string versionId,
        string version,
        CancellationToken cancellationToken)
    {
        var (definition, draft, layout, head) = (context.Definition, context.Draft, context.Layout, context.Head);
        var compilation = await compiler.CompileAsync(
            new(
                definition,
                draft,
                versionId,
                version,
                ComputeLayoutBytes(layout.Records)),
            cancellationToken);
        var diff = compilation.Template is null
            ? null
            : await ComputeDiffAsync(head, versionId, version, draft, layout, compilation, cancellationToken);
        var requiredBump = diff?.RequiredBump ?? ActivityVersionBump.None;
        var validVersions = ActivityPublicationReviewPolicy.AvailableVersionChoices(
            head?.Version,
            requiredBump,
            context.ExistingVersions);
        var minimumVersion = ActivityPublicationReviewPolicy.MinimumVersion(head?.Version, requiredBump);
        var diagnostics = ActivityDiagnosticOrderer.Order(context.Validation.Diagnostics
            .Concat(compilation.Diagnostics)
            .Concat(_reviewPolicy.ReadinessDiagnostics(draft, compilation.Template)));
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
        // The validator's own verdict counts even without an error-severity diagnostic, so preflight can never call
        // publishable a draft that validation refused.
        var isPublishable =
            compilation.IsSuccessful &&
            context.Validation.IsValid &&
            diagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error);
        var reviewToken = ActivityPublicationReviewPolicy.ReviewToken(
            draft,
            context.Authoring.HeadVersionId,
            version,
            compilation.Template,
            diff,
            requiredBump,
            validVersions,
            dependencies,
            provider,
            storage,
            runtime,
            diagnostics);

        return new(
            version,
            compilation,
            diff,
            validVersions,
            minimumVersion,
            dependencies,
            provider,
            storage,
            runtime,
            diagnostics,
            isPublishable,
            reviewToken);
    }

    /// <summary>Refuses a candidate version below the reviewed minimum, or one the definition already published.</summary>
    private static void EnsureVersionAccepted(PublicationContext context, PublicationCandidate candidate)
    {
        var (definition, draft) = (context.Definition, context.Draft);
        if (!ActivityPublicationReviewPolicy.IsVersionAtLeast(candidate.Version, candidate.MinimumVersion))
            throw Reject(
                ActivityErrorCodes.PublicationInvalid,
                "The requested exact version is below the reviewed minimum.",
                [new(
                    "activity.version.bump-insufficient",
                    ActivityDiagnosticSeverity.Error,
                    $"Version {candidate.Version} is below the reviewed minimum {candidate.MinimumVersion}.",
                    new("ActivityDraft", draft.Id, definition.Id, Revision: draft.Revision),
                    Remediation: $"Publish as {candidate.MinimumVersion} or a higher unique semantic version.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["requestedVersion"] = candidate.Version,
                        ["minimumVersion"] = candidate.MinimumVersion
                    })]);
        if (SemVer.TryParse(candidate.Version, out var selectedVersion) &&
            context.ExistingVersions.Any(x =>
                SemVer.TryParse(x.Version, out var existingVersion) &&
                existingVersion == selectedVersion))
            throw Reject(
                ActivityErrorCodes.VersionConflict,
                $"Version '{candidate.Version}' already exists.",
                [],
                true);
    }

    private static void EnsureExpectedState(
        long expectedDraftRevision,
        string? expectedDefinitionHeadVersionId,
        ActivityDefinitionDraft draft,
        ActivityDefinitionAuthoringState authoring)
    {
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Reject("activity.draft.not-active", "Only an active activity draft can be published.", [], true);
        if (draft.Revision != expectedDraftRevision)
            throw Reject("activity.draft.stale-revision", "The activity draft revision is stale.", [], true);
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, expectedDefinitionHeadVersionId))
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
            DesignFacets = ToDesignFacets(draft.State.Contract),
            SourceKind = "ActivityDefinitionDraft",
            SourceId = draft.Id,
            Hash = template.TemplateHash,
            CreatedAt = now,
            LastModifiedAt = now
        };

    private static IReadOnlyCollection<ActivityDesignFacet> ToDesignFacets(
        Elsa.Activities.Design.Core.Models.ActivityContract contract)
    {
        var ports = contract.Outcomes
            .Where(outcome => outcome.IsEmitted)
            .Select(outcome => new
            {
                referenceKey = outcome.ReferenceKey,
                name = outcome.Name,
                type = "outcome"
            })
            .ToArray();
        return ports.Length == 0
            ? []
            : [new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports }))];
    }

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
                ActivityErrorCodes.RequestInvalid,
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

    /// <summary>What one publication decision read from the stores, and the lock it holds while deciding.</summary>
    private sealed record PublicationContext(
        IDistributedSynchronizationHandle Lock,
        ActivityDefinition Definition,
        ActivityDefinitionDraft Draft,
        ActivityDefinitionAuthoringState Authoring,
        ActivityDefinitionDraftLayout Layout,
        ActivityDraftValidation Validation,
        ActivityDefinitionVersionPublication? Head,
        IReadOnlyList<ActivityDefinitionVersionPublication> ExistingVersions) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Lock.DisposeAsync();
    }

    /// <summary>One compiled candidate version and the review evidence derived from it.</summary>
    private sealed record PublicationCandidate(
        string Version,
        ActivityTemplateCompilerResult Compilation,
        ActivityVersionDiff? Diff,
        IReadOnlyList<string> ValidVersions,
        string MinimumVersion,
        IReadOnlyList<ActivityPublicationDependencyEvidenceView> Dependencies,
        ActivityPublicationCapabilityReadinessView Provider,
        IReadOnlyList<ActivityPublicationCapabilityReadinessView> Storage,
        IReadOnlyList<ActivityPublicationCapabilityReadinessView> Runtime,
        IReadOnlyList<ActivityDiagnostic> Diagnostics,
        bool IsPublishable,
        string ReviewToken);
}
