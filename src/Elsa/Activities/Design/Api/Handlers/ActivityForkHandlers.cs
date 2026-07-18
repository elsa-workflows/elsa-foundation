using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions FingerprintJson = new(JsonSerializerDefaults.Web);
    private readonly ActivityForkReservationOptions _options = ValidateOptions(options.Value);

    public async Task<ActivityForkPreviewView> PreviewAsync(
        PreviewReusableActivityFork command,
        CancellationToken cancellationToken)
    {
        EnsureCanFork(command.TargetProviderKey);
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
        var sourceContractFingerprint = Fingerprint(source.Contract);
        var targetContractFingerprint = Fingerprint(draft.State.Contract);
        var accessBindingFingerprint = AccessBindingFingerprint();
        var expiresAt = now.Add(_options.Lifetime);
        var requestFingerprint = Fingerprint(
            command.DefinitionId,
            source.DefinitionVersionId,
            source.Version,
            sourceProviderFingerprint,
            presentation.Category,
            presentation.DisplayName,
            presentation.Description,
            targetManifest.ProviderKey,
            targetManifest.SchemaVersion,
            targetProviderFingerprint,
            sourceContractFingerprint,
            targetContractFingerprint,
            definition.Id,
            definition.ActivityTypeKey,
            draft.Id,
            accessBindingFingerprint);
        var candidateId = candidateIdCodec.Encode(new(NewId("activity-fork"), requestFingerprint, expiresAt));
        var candidate = new ActivityForkCandidate
        {
            Id = candidateId,
            TenantId = context.TenantId,
            RequestFingerprint = requestFingerprint,
            AccessBindingFingerprint = accessBindingFingerprint,
            ActorId = context.ActorId,
            AuthorizationProfile = context.AuthorizationProfile,
            SourceDefinitionId = source.DefinitionId,
            SourceVersionId = source.DefinitionVersionId,
            SourceVersion = source.Version,
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
            Status = ActivityForkCandidateStatus.Reserved,
            CreatedAt = now,
            LastModifiedAt = now
        };

        try
        {
            await saveCandidate.ExecuteAsync(new(candidate), cancellationToken);
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

        var candidate = await forkStore.FindCandidateAsync(command.CandidateId, cancellationToken)
            ?? throw NotFound("activity.fork.candidate-not-found", "Activity fork candidate not found", "The reviewed fork candidate was not found.");
        EnsureCandidateBinding(candidate);
        if (!StringComparer.Ordinal.Equals(candidate.RequestFingerprint, command.RequestFingerprint))
            throw Stale("The reviewed fork material changed after preview.");
        if (candidate.ExpiresAt <= timeProvider.GetUtcNow())
            throw Expired();

        if (candidate.Status == ActivityForkCandidateStatus.Applied)
        {
            if (!StringComparer.Ordinal.Equals(candidate.AppliedIdempotencyKey, command.IdempotencyKey))
                throw Stale("The fork candidate was already consumed by another operation identity.");
            var existing = await RequiredReceiptAsync(command.IdempotencyKey, cancellationToken);
            EnsureReceiptBinding(existing);
            return ToReceipt(candidate, existing, ActivityForkOutcomeView.AlreadyApplied);
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
                candidate,
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
            throw Conflict("activity.fork.collision", "Activity fork identity collision", "A reserved target identity or activity type key is no longer available. Create a new preview.", exception);
        }
        catch (Exception exception)
        {
            var reconciled = await forkStore.FindReceiptAsync(ReceiptId(command.IdempotencyKey), cancellationToken);
            if (reconciled is not null)
            {
                EnsureReceiptBinding(reconciled);
                return ToReceipt(candidate, reconciled, ActivityForkOutcomeView.AlreadyApplied);
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
        var candidate = await forkStore.FindCandidateAsync(receipt.CandidateId, cancellationToken)
            ?? throw OutcomeUnknown("The durable fork receipt refers to an unavailable candidate.");
        EnsureCandidateBinding(candidate);
        return ToReceipt(candidate, receipt, ActivityForkOutcomeView.Applied);
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
            !StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(source.Provider), candidate.SourceProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(Fingerprint(source.Contract), candidate.SourceContractFingerprint))
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
            !StringComparer.Ordinal.Equals(Fingerprint(candidate.ReservedDraft.State.Contract), candidate.TargetContractFingerprint))
            throw Stale("The provider migration or target contract changed after preview.");
    }

    private ActivityForkPreviewView ToPreview(
        ActivityForkCandidate candidate,
        ActivityDefinitionVersionPublication source,
        ActivityForkPresentationView presentation) => new(
        candidate.Id,
        candidate.RequestFingerprint,
        ActivityForkCandidateLifecycleView.Reserved,
        new(candidate.AccessBindingFingerprint),
        new(
            candidate.SourceDefinitionId,
            candidate.SourceVersionId,
            candidate.SourceVersion,
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
        ActivityForkCandidate candidate,
        ActivityForkReceipt receipt,
        ActivityForkOutcomeView outcome) => new(
        receipt.IdempotencyKey,
        receipt.CandidateId,
        receipt.RequestFingerprint,
        outcome,
        new(receipt.AccessBindingFingerprint),
        new(
            candidate.ReservedDefinition.Id,
            candidate.ReservedDefinition.ActivityTypeKey,
            candidate.ReservedDefinition.TenantId,
            candidate.ReservedDefinition.Category,
            candidate.ReservedDefinition.DisplayName ?? candidate.ReservedDefinition.ActivityTypeKey,
            candidate.ReservedDefinition.Description,
            candidate.ReservedAuthoringState.ContentAuthority,
            candidate.ReservedAuthoringState.ForkedFrom,
            null,
            null),
        new(
            candidate.ReservedDraft.Id,
            candidate.ReservedDraft.DefinitionId,
            candidate.ReservedDraft.Revision,
            candidate.ReservedDraft.SourceVersionId,
            candidate.ReservedDraft.Status,
            candidate.ReservedDraft.State.Provider.ProviderKey,
            candidate.ReservedDraft.State.Provider.SchemaVersion,
            candidate.ReservedDraft.LastModifiedAt,
            candidate.ReservedDraft.PresentationLabel),
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

    private static string Fingerprint(object value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, FingerprintJson);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant()}";
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
        new(400, "activity.request.invalid", "Invalid activity fork request", detail);

    private static ActivityAuthoringException Forbidden(string detail, string code = "activity.authorization.denied") =>
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
