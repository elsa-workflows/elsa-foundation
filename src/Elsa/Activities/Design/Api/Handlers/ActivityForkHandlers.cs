using System.Security.Cryptography;
using System.Text;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Contracts;
using Microsoft.Extensions.Options;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

/// <summary>
/// Deep authoring module for the review-before-apply fork flow. It owns canonical request binding,
/// provider revalidation, reservation lifecycle, and safe receipt projection behind three public
/// operations; persistence adapters own only durable exact reads and the final atomic commit.
/// </summary>
public sealed class ActivityForkService(
    IActivityDefinitionAuthoringStore authoringStore,
    IActivityDefinitionVersionPublicationStore publicationStore,
    IActivityDefinitionLayoutStore layoutStore,
    IActivityForkStore forkStore,
    ISaveActivityForkCandidateCommand saveCandidate,
    IPruneActivityForkCandidatesCommand pruneCandidates,
    IApplyActivityForkCandidateCommand applyCandidate,
    IActivityProviderRegistry providers,
    ActivityContractAuthoringValidator contractAuthoringValidator,
    IActivityTypeKeyPolicy typeKeyPolicy,
    IIdentityGenerator identityGenerator,
    IActivityForkCandidateIdCodec candidateIdCodec,
    IOptions<ActivityForkReservationOptions> options,
    TimeProvider timeProvider,
    IActivityAuthoringContext context)
{
    private readonly ActivityForkReservationOptions _options = ValidateOptions(options.Value);

    public async Task<ActivityForkPreviewView> PreviewAsync(
        PreviewReusableActivityFork command,
        CancellationToken cancellationToken)
    {
        EnsureCanFork(command.TargetProviderKey);
        EnsureBoundedIdentity(command.IdempotencyKey, "idempotencyKey");
        var presentation = NormalizePresentation(command.Category, command.DisplayName, command.Description);
        var sourceAuthoring = await RequiredAuthoringAsync(command.DefinitionId, cancellationToken);
        EnsureVisible(sourceAuthoring.TenantId);
        if (sourceAuthoring.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource)
            throw Conflict("activity.fork.source-authority", "Activity fork source is not source-owned", "Only an exact source-owned activity version can be forked.");

        var source = await RequiredPublicationAsync(command.SourceVersionId, cancellationToken);
        if (!StringComparer.Ordinal.Equals(source.DefinitionId, command.DefinitionId))
            throw NotFound("activity.version.not-found", "Activity version not found", "The exact source version was not found for this definition.");
        EnsureVisible(source.TenantId);

        var targetProvider = ResolveProvider(source.Provider, command.TargetProviderKey, command.TargetProviderSchemaVersion);
        var migration = await targetProvider.MigrateAsync(
            new(source.Provider, command.TargetProviderSchemaVersion),
            cancellationToken);
        var targetManifest = RequireMigration(
            source.Provider,
            command.TargetProviderKey,
            command.TargetProviderSchemaVersion,
            migration);
        EnsureAuthorableProvider(targetProvider, targetManifest);

        var now = timeProvider.GetUtcNow();
        var definitionId = NewId("activity-def");
        var draftId = NewId("activity-draft");
        EnsureAuthorableContract(source.Contract, new("ActivityDraft", draftId, definitionId, Revision: 1));
        var sourceLayout = await layoutStore.FindVersionLayoutAsync(source.DefinitionVersionId, cancellationToken)
            ?? throw OutcomeUnknown("The exact source version layout is unavailable.");

        var definition = new ActivityDefinition
        {
            Id = definitionId,
            TenantId = context.TenantId,
            ActivityTypeKey = typeKeyPolicy.Generate(presentation.DisplayName, definitionId),
            Category = presentation.Category,
            DisplayName = presentation.DisplayName,
            Description = presentation.Description,
            CreatedAt = now,
            LastModifiedAt = now
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = NewId("activity-authoring"),
            TenantId = context.TenantId,
            DefinitionId = definitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
            ForkedFrom = new(command.DefinitionId, source.DefinitionVersionId, source.Version),
            CreatedAt = now,
            LastModifiedAt = now
        };
        var draft = new ActivityDefinitionDraft
        {
            Id = draftId,
            TenantId = context.TenantId,
            DefinitionId = definitionId,
            Revision = 1,
            SourceVersionId = source.DefinitionVersionId,
            Status = ActivityDefinitionDraftStatus.Active,
            State = new(source.Contract, targetManifest, new Dictionary<string, string>()),
            CreatedAt = now,
            LastModifiedAt = now
        };
        var layout = new ActivityDefinitionDraftLayout
        {
            Id = NewId("activity-draft-layout"),
            TenantId = context.TenantId,
            DraftId = draftId,
            Revision = 1,
            Records = sourceLayout.Records.ToList(),
            CreatedAt = now,
            LastModifiedAt = now
        };

        var sourceProviderFingerprint = ActivityProviderManifestFingerprint.Compute(source.Provider);
        var targetProviderFingerprint = ActivityProviderManifestFingerprint.Compute(targetManifest);
        var sourceContractFingerprint = ActivityForkMaterialFingerprint.Compute(source.Contract);
        var targetContractFingerprint = ActivityForkMaterialFingerprint.Compute(draft.State.Contract);
        var accessBindingFingerprint = AccessBindingFingerprint();
        var expiresAt = now.Add(_options.Lifetime);
        var requestFingerprint = Fingerprint(
            command.IdempotencyKey,
            command.DefinitionId,
            source.DefinitionVersionId,
            source.Version,
            source.Lifecycle.ToString(),
            sourceProviderFingerprint,
            presentation.Category,
            presentation.DisplayName,
            presentation.Description,
            targetManifest.ProviderKey,
            targetManifest.SchemaVersion,
            targetProviderFingerprint,
            sourceContractFingerprint,
            targetContractFingerprint,
            accessBindingFingerprint);
        var reservationId = ActivityForkCandidateIdentity.Compute(
            context.TenantId,
            context.ActorId,
            command.IdempotencyKey);
        var candidateId = candidateIdCodec.Encode(new(reservationId, requestFingerprint, expiresAt));
        var candidate = new ActivityForkCandidate
        {
            Id = reservationId,
            CandidateId = candidateId,
            PreviewIdempotencyKey = command.IdempotencyKey,
            TenantId = context.TenantId,
            RequestFingerprint = requestFingerprint,
            AccessBindingFingerprint = accessBindingFingerprint,
            ActorId = context.ActorId,
            AuthorizationProfile = context.AuthorizationProfile,
            SourceDefinitionId = source.DefinitionId,
            SourceVersionId = source.DefinitionVersionId,
            SourceVersion = source.Version,
            SourceLifecycle = source.Lifecycle,
            SourceProviderFingerprint = sourceProviderFingerprint,
            TargetProviderFingerprint = targetProviderFingerprint,
            ReservedDefinition = definition,
            ReservedAuthoringState = authoring,
            ReservedDraft = draft,
            ReservedLayout = layout,
            MigrationDiagnostics = ActivityDiagnosticOrderer.Order(migration.Diagnostics).ToList(),
            SourceContractFingerprint = sourceContractFingerprint,
            TargetContractFingerprint = targetContractFingerprint,
            ExpiresAt = expiresAt,
            RetainUntil = expiresAt.Add(_options.Retention),
            RetentionKey = ActivityForkCandidateIdentity.RetentionKey(expiresAt.Add(_options.Retention)),
            Status = ActivityForkCandidateStatus.Reserved,
            CreatedAt = now,
            LastModifiedAt = now
        };

        try
        {
            await pruneCandidates.ExecuteAsync(now, cancellationToken: cancellationToken);
            candidate = await saveCandidate.ExecuteAsync(new(candidate), cancellationToken);
        }
        catch (ActivityForkPreviewIdempotencyConflictException exception)
        {
            throw Conflict(
                "activity.fork.preview-idempotency-conflict",
                "Activity fork preview idempotency conflict",
                "The preview idempotency key is already bound to different normalized fork material.",
                exception);
        }
        catch (ActivityForkPreviewExpiredException exception)
        {
            throw new ActivityAuthoringException(
                410,
                "activity.fork.preview-expired",
                "Activity fork preview expired",
                "The preview idempotency key belongs to an expired reviewed reservation and cannot allocate replacement identities.",
                innerException: exception);
        }
        catch (InvalidOperationException exception)
        {
            throw OutcomeUnknown("The fork reservation could not be durably established.", exception);
        }

        return ToPreview(candidate, source, presentation);
    }

    public async Task<ActivityForkReceiptView> ApplyAsync(
        ApplyReusableActivityFork command,
        CancellationToken cancellationToken)
    {
        EnsureBoundedIdentity(command.IdempotencyKey, "idempotencyKey");
        EnsureFingerprint(command.RequestFingerprint);
        ActivityForkCandidateIdState candidateId;
        try
        {
            candidateId = candidateIdCodec.Decode(command.CandidateId);
        }
        catch (ActivityForkCandidateIdInvalidException exception)
        {
            throw Stale("The reviewed fork candidate identity is malformed or was altered.", exception);
        }
        if (!StringComparer.Ordinal.Equals(candidateId.RequestFingerprint, command.RequestFingerprint))
            throw Stale("The supplied request fingerprint is not bound to this candidate.");

        var existingReceipt = await forkStore.FindReceiptAsync(ReceiptId(command.IdempotencyKey), cancellationToken);
        if (existingReceipt is not null)
        {
            EnsureReceiptMatchesCommand(existingReceipt, command);
            return ToReceipt(existingReceipt, ActivityForkOutcomeView.AlreadyApplied);
        }

        var candidate = await forkStore.FindCandidateAsync(candidateId.ReservationId, cancellationToken)
            ?? throw NotFound("activity.fork.candidate-not-found", "Activity fork candidate not found", "The reviewed fork candidate was not found.");
        EnsureCandidateBinding(candidate);
        if (!StringComparer.Ordinal.Equals(candidate.CandidateId, command.CandidateId) ||
            !StringComparer.Ordinal.Equals(candidate.RequestFingerprint, command.RequestFingerprint) ||
            candidate.ExpiresAt != candidateId.ExpiresAt)
            throw Stale("The reviewed fork material changed after preview.");
        if (candidate.ExpiresAt <= timeProvider.GetUtcNow())
            throw Expired();

        if (candidate.Status == ActivityForkCandidateStatus.Applied)
        {
            if (!StringComparer.Ordinal.Equals(candidate.AppliedIdempotencyKey, command.IdempotencyKey))
                throw Stale("The fork candidate was already consumed by another operation identity.");
            var existing = await RequiredReceiptAsync(command.IdempotencyKey, cancellationToken);
            EnsureReceiptMatchesCommand(existing, command);
            return ToReceipt(existing, ActivityForkOutcomeView.AlreadyApplied);
        }

        await RevalidateAsync(candidate, cancellationToken);
        try
        {
            var result = await applyCandidate.ExecuteAsync(new(
                candidate.Id,
                candidate.RequestFingerprint,
                candidate.AccessBindingFingerprint,
                candidate.ActorId,
                candidate.AuthorizationProfile,
                command.IdempotencyKey,
                ReceiptId(command.IdempotencyKey),
                timeProvider.GetUtcNow()), cancellationToken);
            return ToReceipt(
                result.Receipt,
                result.AlreadyApplied ? ActivityForkOutcomeView.AlreadyApplied : ActivityForkOutcomeView.Applied);
        }
        catch (ActivityForkIdempotencyConflictException exception)
        {
            throw Conflict("activity.fork.idempotency-conflict", "Activity fork idempotency conflict", "The idempotency key is already bound to different reviewed fork material.", exception);
        }
        catch (ActivityForkCandidateStaleException exception)
        {
            throw Stale("The reviewed fork candidate changed or was consumed before apply.", exception);
        }
        catch (ActivityForkCollisionException exception)
        {
            throw Conflict(ActivityErrorCodes.ForkCollision, "Activity fork identity collision", "A reserved target identity or activity type key is no longer available. Create a new preview.", exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var reconciled = await forkStore.FindReceiptAsync(ReceiptId(command.IdempotencyKey), cancellationToken);
            if (reconciled is not null)
            {
                EnsureReceiptMatchesCommand(reconciled, command);
                return ToReceipt(reconciled, ActivityForkOutcomeView.AlreadyApplied);
            }
            throw OutcomeUnknown("The server cannot prove whether the fork apply committed. Query the fork status before retrying.", exception);
        }
    }

    public async Task<ActivityForkReceiptView> GetStatusAsync(
        GetReusableActivityForkStatus request,
        CancellationToken cancellationToken)
    {
        EnsureBoundedIdentity(request.IdempotencyKey, "idempotencyKey");
        var receipt = await RequiredReceiptAsync(request.IdempotencyKey, cancellationToken);
        EnsureReceiptBinding(receipt);
        return ToReceipt(receipt, ActivityForkOutcomeView.Applied);
    }

    private async Task RevalidateAsync(ActivityForkCandidate candidate, CancellationToken cancellationToken)
    {
        EnsureCanFork(candidate.ReservedDraft.State.Provider.ProviderKey);
        var sourceAuthoring = await RequiredAuthoringAsync(candidate.SourceDefinitionId, cancellationToken);
        EnsureVisible(sourceAuthoring.TenantId);
        if (sourceAuthoring.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource)
            throw Stale("The source definition content authority changed after preview.");
        var source = await RequiredPublicationAsync(candidate.SourceVersionId, cancellationToken);
        EnsureVisible(source.TenantId);
        if (!StringComparer.Ordinal.Equals(source.DefinitionId, candidate.SourceDefinitionId) ||
            !StringComparer.Ordinal.Equals(source.Version, candidate.SourceVersion) ||
            source.Lifecycle != candidate.SourceLifecycle ||
            !StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(source.Provider), candidate.SourceProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(
                ActivityForkMaterialFingerprint.Compute(source.Contract),
                candidate.SourceContractFingerprint))
            throw Stale("The exact source binding no longer matches the reviewed preview.");

        var targetProvider = ResolveProvider(
            source.Provider,
            candidate.ReservedDraft.State.Provider.ProviderKey,
            candidate.ReservedDraft.State.Provider.SchemaVersion);
        var migration = await targetProvider.MigrateAsync(
            new(source.Provider, candidate.ReservedDraft.State.Provider.SchemaVersion),
            cancellationToken);
        var manifest = RequireMigration(
            source.Provider,
            candidate.ReservedDraft.State.Provider.ProviderKey,
            candidate.ReservedDraft.State.Provider.SchemaVersion,
            migration);
        EnsureAuthorableProvider(targetProvider, manifest);
        EnsureAuthorableContract(
            source.Contract,
            new("ActivityDraft", candidate.ReservedDraft.Id, candidate.ReservedDefinition.Id, Revision: 1));
        if (!StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(manifest), candidate.TargetProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(
                ActivityForkMaterialFingerprint.Compute(candidate.ReservedDraft.State.Contract),
                candidate.TargetContractFingerprint))
            throw Stale("The provider migration or target contract changed after preview.");
    }

    private ActivityForkPreviewView ToPreview(
        ActivityForkCandidate candidate,
        ActivityDefinitionVersionPublication source,
        ActivityForkPresentationView presentation) => new(
        candidate.CandidateId,
        candidate.RequestFingerprint,
        ActivityForkCandidateLifecycleView.Reserved,
        new(candidate.AccessBindingFingerprint),
        new(
            candidate.SourceDefinitionId,
            candidate.SourceVersionId,
            candidate.SourceVersion,
            candidate.SourceLifecycle,
            source.Provider.ProviderKey,
            source.Provider.SchemaVersion,
            candidate.SourceProviderFingerprint),
        presentation,
        new(
            candidate.ReservedDefinition.Id,
            candidate.ReservedDefinition.ActivityTypeKey,
            candidate.ReservedDraft.Id,
            candidate.ReservedDraft.State.Provider.ProviderKey,
            candidate.ReservedDraft.State.Provider.SchemaVersion,
            candidate.TargetProviderFingerprint,
            candidate.ReservedDraft.State.Contract.ToView()),
        new(
            source.Provider.ProviderKey,
            source.Provider.SchemaVersion,
            candidate.ReservedDraft.State.Provider.ProviderKey,
            candidate.ReservedDraft.State.Provider.SchemaVersion,
            candidate.TargetProviderFingerprint,
            candidate.MigrationDiagnostics.ToArray()),
        new(
            candidate.SourceContractFingerprint,
            candidate.TargetContractFingerprint,
            StringComparer.Ordinal.Equals(candidate.SourceContractFingerprint, candidate.TargetContractFingerprint),
            []),
        candidate.CreatedAt,
        candidate.ExpiresAt);

    private static ActivityForkReceiptView ToReceipt(
        ActivityForkReceipt receipt,
        ActivityForkOutcomeView outcome) => new(
        receipt.IdempotencyKey,
        receipt.PublicCandidateId,
        receipt.RequestFingerprint,
        outcome,
        new(receipt.AccessBindingFingerprint),
        new(
            receipt.Definition.Id,
            receipt.Definition.ActivityTypeKey,
            receipt.Definition.TenantId,
            receipt.Definition.Category,
            receipt.Definition.DisplayName ?? receipt.Definition.ActivityTypeKey,
            receipt.Definition.Description,
            receipt.AuthoringState.ContentAuthority,
            receipt.AuthoringState.ForkedFrom,
            null,
            null),
        new(
            receipt.Draft.Id,
            receipt.Draft.DefinitionId,
            receipt.Draft.Revision,
            receipt.Draft.SourceVersionId,
            receipt.Draft.Status,
            receipt.Draft.State.Provider.ProviderKey,
            receipt.Draft.State.Provider.SchemaVersion,
            receipt.Draft.LastModifiedAt,
            receipt.Draft.PresentationLabel),
        receipt.AppliedAt);

    private IActivityProvider ResolveProvider(
        ActivityProviderManifest source,
        string targetProviderKey,
        string targetSchemaVersion)
    {
        try
        {
            return providers.Resolve(targetProviderKey, targetSchemaVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw MigrationUnsupported(source, targetProviderKey, targetSchemaVersion, [], exception);
        }
    }

    private static ActivityProviderManifest RequireMigration(
        ActivityProviderManifest source,
        string targetProviderKey,
        string targetSchemaVersion,
        ActivityManifestMigration migration)
    {
        if (migration.Manifest is null ||
            migration.Diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            throw MigrationUnsupported(source, targetProviderKey, targetSchemaVersion, migration.Diagnostics);
        if (!StringComparer.Ordinal.Equals(migration.Manifest.ProviderKey, targetProviderKey) ||
            !StringComparer.Ordinal.Equals(migration.Manifest.SchemaVersion, targetSchemaVersion))
            throw MigrationUnsupported(source, targetProviderKey, targetSchemaVersion, migration.Diagnostics);
        return migration.Manifest;
    }

    private static void EnsureAuthorableProvider(IActivityProvider provider, ActivityProviderManifest manifest)
    {
        if (provider.AuthoringCapabilities.ManifestSchemas.All(x =>
                !StringComparer.Ordinal.Equals(x.SchemaVersion, manifest.SchemaVersion) || !x.IsAuthorable))
            throw new ActivityAuthoringException(
                422,
                "activity.provider.schema-not-authorable",
                "Activity provider schema is not authorable",
                "The target provider schema is unavailable for mutable authoring.");
    }

    private void EnsureAuthorableContract(ActivityContract contract, ActivityDiagnosticSubject subject)
    {
        var diagnostics = contractAuthoringValidator.Validate(contract, subject);
        if (diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            throw new ActivityAuthoringException(
                422,
                "activity.contract.capability-rejected",
                "Activity contract is not authorable",
                "The target contract contains facts outside the activated authoring capability catalog.",
                diagnostics);
    }

    private async Task<ActivityDefinitionAuthoringState> RequiredAuthoringAsync(
        string definitionId,
        CancellationToken cancellationToken) =>
        await authoringStore.FindAsync(definitionId, cancellationToken)
        ?? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.");

    private async Task<ActivityDefinitionVersionPublication> RequiredPublicationAsync(
        string versionId,
        CancellationToken cancellationToken) =>
        await publicationStore.FindAsync(versionId, cancellationToken)
        ?? throw NotFound("activity.version.not-found", "Activity version not found", "The requested activity version was not found.");

    private async Task<ActivityForkReceipt> RequiredReceiptAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await forkStore.FindReceiptAsync(ReceiptId(idempotencyKey), cancellationToken)
        ?? throw NotFound("activity.fork.receipt-not-found", "Activity fork receipt not found", "No durable fork outcome exists for this operation identity.");

    private void EnsureCanFork(string targetProviderKey)
    {
        if (!context.CanManageActivityDefinitions || !context.CanAuthorProvider(targetProviderKey))
            throw Forbidden("The caller is not authorized to fork activity definitions.");
        if (string.IsNullOrWhiteSpace(context.ActorId))
            throw Forbidden("A stable authenticated actor identity is required to reserve an activity fork.");
    }

    private void EnsureCandidateBinding(ActivityForkCandidate candidate)
    {
        EnsureVisible(candidate.TenantId);
        if (!StringComparer.Ordinal.Equals(candidate.ActorId, context.ActorId) ||
            !StringComparer.Ordinal.Equals(candidate.AuthorizationProfile, context.AuthorizationProfile) ||
            !StringComparer.Ordinal.Equals(candidate.AccessBindingFingerprint, AccessBindingFingerprint()))
            throw Forbidden("The reviewed fork candidate belongs to a different caller or access profile.");
    }

    private void EnsureReceiptBinding(ActivityForkReceipt receipt)
    {
        EnsureVisible(receipt.TenantId);
        if (!StringComparer.Ordinal.Equals(receipt.ActorId, context.ActorId) ||
            !StringComparer.Ordinal.Equals(receipt.AuthorizationProfile, context.AuthorizationProfile) ||
            !StringComparer.Ordinal.Equals(receipt.AccessBindingFingerprint, AccessBindingFingerprint()))
            throw Forbidden("The durable fork receipt belongs to a different caller or access profile.");
    }

    private void EnsureReceiptMatchesCommand(
        ActivityForkReceipt receipt,
        ApplyReusableActivityFork command)
    {
        EnsureReceiptBinding(receipt);
        if (!StringComparer.Ordinal.Equals(receipt.PublicCandidateId, command.CandidateId) ||
            !StringComparer.Ordinal.Equals(receipt.RequestFingerprint, command.RequestFingerprint))
            throw Conflict(
                "activity.fork.idempotency-conflict",
                "Activity fork idempotency conflict",
                "The idempotency key is already bound to different reviewed fork material.");
    }

    private void EnsureVisible(string? tenantId)
    {
        if (tenantId is not null && !StringComparer.Ordinal.Equals(tenantId, context.TenantId))
            throw Forbidden("The requested activity identity is outside the caller tenant scope.", "activity.tenant.reference-denied");
    }

    private string AccessBindingFingerprint() => Fingerprint(
        context.TenantId ?? "<global>",
        context.ActorId,
        context.AuthorizationProfile);

    private string NewId(string prefix) => $"{prefix}-{identityGenerator.Generate()}";

    private string ReceiptId(string idempotencyKey) =>
        ActivityForkReceiptIdentity.Compute(context.TenantId, context.ActorId, idempotencyKey);

    private static ActivityForkPresentationView NormalizePresentation(
        string category,
        string displayName,
        string? description) => new(
        NormalizeRequired(category, "category", 200),
        NormalizeRequired(displayName, "displayName", 200),
        NormalizeOptional(description, "description", 2000));

    private static string NormalizeRequired(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw BadRequest($"'{name}' is required.");
        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length > maximumLength)
            throw BadRequest($"'{name}' must not exceed {maximumLength} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, string name, int maximumLength)
    {
        if (value is null)
            return null;
        var normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length > maximumLength)
            throw BadRequest($"'{name}' must not exceed {maximumLength} characters.");
        return normalized;
    }

    private static void EnsureBoundedIdentity(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.IndexOf('\0') >= 0)
            throw BadRequest($"'{name}' must contain between 1 and 200 safe characters.");
    }

    private static void EnsureFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 71 ||
            !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            value[7..].Any(x => !Uri.IsHexDigit(x)))
            throw BadRequest("'requestFingerprint' is invalid.");
    }

    private static string Fingerprint(params string?[] values)
    {
        var material = string.Concat(values.Select(value =>
        {
            var normalized = value ?? "<null>";
            return $"{Encoding.UTF8.GetByteCount(normalized)}:{normalized}";
        }));
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }

    private static ActivityForkReservationOptions ValidateOptions(ActivityForkReservationOptions value)
    {
        if (value.Lifetime <= TimeSpan.Zero || value.Lifetime > TimeSpan.FromHours(24))
            throw new InvalidOperationException("Activity fork reservation lifetime must be between zero and 24 hours.");
        if (value.Retention <= TimeSpan.Zero || value.Retention > TimeSpan.FromDays(30))
            throw new InvalidOperationException("Activity fork reservation retention must be between zero and 30 days.");
        return value;
    }

    private static ActivityAuthoringException MigrationUnsupported(
        ActivityProviderManifest source,
        string targetProviderKey,
        string targetSchema,
        IReadOnlyList<ActivityDiagnostic> diagnostics,
        Exception? inner = null) => new(
        422,
        "activity.provider.migration-unsupported",
        "Activity provider migration is unsupported",
        $"No deterministic provider migration is available from '{source.ProviderKey}/{source.SchemaVersion}' to '{targetProviderKey}/{targetSchema}'.",
        ActivityDiagnosticOrderer.Order(diagnostics),
        inner);

    private static ActivityAuthoringException BadRequest(string detail) =>
        new(400, ActivityErrorCodes.RequestInvalid, "Invalid activity fork request", detail);

    private static ActivityAuthoringException Forbidden(string detail, string code = ActivityErrorCodes.AuthorizationDenied) =>
        new(403, code, "Activity fork is forbidden", detail);

    private static ActivityAuthoringException NotFound(string code, string title, string detail) =>
        new(404, code, title, detail);

    private static ActivityAuthoringException Conflict(string code, string title, string detail, Exception? inner = null) =>
        new(409, code, title, detail, innerException: inner);

    private static ActivityAuthoringException Stale(string detail, Exception? inner = null) =>
        new(409, "activity.fork.candidate-stale", "Activity fork candidate is stale", detail, innerException: inner);

    private static ActivityAuthoringException Expired() =>
        new(410, "activity.fork.candidate-expired", "Activity fork candidate expired", "Create and review a new fork preview before applying.");

    private static ActivityAuthoringException OutcomeUnknown(string detail, Exception? inner = null) =>
        new(500, "activity.fork.outcome-unknown", "Activity fork outcome is unknown", detail, innerException: inner);
}

public sealed class PreviewReusableActivityForkHandler(ActivityForkService service)
    : ICommandHandler<PreviewReusableActivityFork, ActivityForkPreviewView>
{
    public Task<ActivityForkPreviewView> Handle(
        PreviewReusableActivityFork command,
        CancellationToken cancellationToken) =>
        service.PreviewAsync(command, cancellationToken);
}

public sealed class ApplyReusableActivityForkHandler(ActivityForkService service)
    : ICommandHandler<ApplyReusableActivityFork, ActivityForkReceiptView>
{
    public Task<ActivityForkReceiptView> Handle(
        ApplyReusableActivityFork command,
        CancellationToken cancellationToken) =>
        service.ApplyAsync(command, cancellationToken);
}

public sealed class GetReusableActivityForkStatusHandler(ActivityForkService service)
    : IRequestHandler<GetReusableActivityForkStatus, ActivityForkReceiptView>
{
    public Task<ActivityForkReceiptView> Handle(
        GetReusableActivityForkStatus request,
        CancellationToken cancellationToken) =>
        service.GetStatusAsync(request, cancellationToken);
}
