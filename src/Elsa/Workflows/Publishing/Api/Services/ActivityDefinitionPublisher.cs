using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Api.Contracts;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed record PublishActivityDefinitionRequest(
    string DraftId,
    long ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string Version);

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
    Task<PublishActivityDefinitionResult> PublishAsync(
        PublishActivityDefinitionRequest request,
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
    ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commitCommand,
    IDistributedLockProvider lockProvider,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider) : IActivityDefinitionPublisher
{
    public async Task<PublishActivityDefinitionResult> PublishAsync(
        PublishActivityDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await draftStore.FindAsync(request.DraftId, cancellationToken)
                    ?? throw Reject("activity.draft.not-found", "Activity draft was not found.", [], true);
        EnsureAuthorized(draft.TenantId);
        await using var lockHandle = await lockProvider.AcquireLockAsync(DefinitionLockKey(draft.DefinitionId), null, cancellationToken);

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
            CreatedAt = now,
            LastModifiedAt = now
        }).ToArray();

        ActivityPublicationResult committed;
        try
        {
            committed = await commitCommand.ExecuteAsync(new(
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
                sourceReference), cancellationToken);
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
            SourceKind = "ActivityDefinitionDraft",
            SourceId = draft.Id,
            Hash = template.TemplateHash,
            CreatedAt = now,
            LastModifiedAt = now
        };

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

    private string NewId(string prefix) => $"{prefix}-{identityGenerator.Generate()}";

    private static long ComputeLayoutBytes(IEnumerable<ActivityLayoutRecord> records) => records.Sum(x =>
        (long)Encoding.UTF8.GetByteCount(x.NodeId) + Encoding.UTF8.GetByteCount(x.Data.GetRawText()));

    private static string DefinitionLockKey(string definitionId) =>
        $"elsa:activities:design:publication:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definitionId)))}";

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
