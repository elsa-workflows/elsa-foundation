using Elsa.Workflows.Publishing.Services;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Services;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Closes source-owned activity catalog versions into the same immutable publication/template model
/// used by Design-owned providers. The source remains sole content authority; customization forks to
/// a new Design-owned definition through the normal authoring API. Nodes reconciling the same source
/// converge: a commit that loses to an identical publication counts as published.
/// </summary>
public sealed class SourceOwnedActivityVersionPublisher(
    ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commitCommand,
    IActivityDefinitionVersionPublicationStore publicationStore,
    ExecutableNodeCompiler nodeCompiler,
    TimeProvider timeProvider) : IActivitySourceVersionPublisher
{
    public async Task PublishAsync(
        IActivityDefinition definition,
        IActivityDefinitionVersion version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StringComparer.Ordinal.Equals(version.ProviderKey, WellKnownActivityContentAuthorities.Design) ||
            StringComparer.Ordinal.Equals(version.SourceKind, "ActivityDefinitionDraft"))
            throw new InvalidOperationException("The source-owned publication bridge cannot publish Design-owned activity content.");
        if (await publicationStore.FindAsync(version.Id, cancellationToken) is not null)
            return;
        var now = timeProvider.GetUtcNow();
        var catalogVersion = ActivityDefinitionVersion.From(version);
        var persistedDefinition = ActivityDefinition.From(definition);
        catalogVersion.Definition = persistedDefinition;

        var root = nodeCompiler.CompileSourceOwnedRoot(catalogVersion);
        var resumeTargets = nodeCompiler.BuildResumeTargets(root)
            .ToDictionary(
                x => x.Key,
                x => x.Value with { LocalResumeTargetId = x.Value.LocalResumeTargetId ?? x.Value.ResumeTargetId },
                StringComparer.Ordinal);
        var providerFingerprint = ExecutableActivityTemplateBehaviorHasher.ComputeCanonicalValueHash(new
        {
            version.ProviderKey,
            version.ProviderSchemaVersion,
            version.ConsumerKey,
            version.ConsumerSchemaVersion,
            descriptor = version.DescriptorPayload
        });
        var templateHash = ExecutableActivityTemplateBehaviorHasher.Compute(
            root,
            resumeTargets,
            [],
            [],
            [new(version.ConsumerKey, version.ConsumerSchemaVersion)],
            [],
            providerFingerprint,
            new Dictionary<string, string>(StringComparer.Ordinal));
        var templateId = $"activity-template-{templateHash["sha256:".Length..]}";
        var template = new ExecutableActivityTemplate(
            templateId,
            templateHash,
            root,
            resumeTargets,
            [],
            [],
            [new(version.ConsumerKey, version.ConsumerSchemaVersion)],
            providerFingerprint,
            new Dictionary<string, string>(StringComparer.Ordinal),
            now);
        var sourceReferenceId = StableId("activity-source-ref", version.Id);
        var sourceReference = new WorkflowExecutableSourceReference(
            sourceReferenceId,
            templateId,
            version.SourceKind,
            version.SourceId,
            version.Version,
            definition.Id,
            version.Id,
            version.Version,
            now,
            now,
            WorkflowExecutableReferenceScope.Published);
        var contract = ToContract(version);
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = version.Id,
            TenantId = catalogVersion.TenantId,
            DefinitionVersionId = version.Id,
            DefinitionId = definition.Id,
            Version = version.Version,
            ActivityTypeKey = definition.ActivityTypeKey,
            ResolutionKind = ActivityDefinitionVersionResolutionKind.AuthorableActivity,
            Contract = contract,
            Provider = new(version.ProviderKey, version.ProviderSchemaVersion, version.DescriptorPayload),
            TemplateId = templateId,
            TemplateHash = templateHash,
            SourceReferenceId = sourceReferenceId,
            ProviderFingerprint = providerFingerprint,
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = [new(version.ConsumerKey, version.ConsumerSchemaVersion)],
            ResourceMeasurements = new(
                LocalNodeCount: 1,
                ClosedNodeCount: 1,
                DependencyCount: 0,
                MaximumObservedAuthoredDepth: 1,
                DescriptorBytes: version.DescriptorPayload.GetRawText().Length,
                LayoutBytes: 0,
                EstimatedDurableBoundarySlots: version.Inputs.LongCount() + version.Outputs.LongCount()),
            ResumeTargetCount = resumeTargets.Count,
            Lifecycle = ActivityDefinitionVersionLifecycle.Active,
            PublishedAt = now,
            CreatedAt = now,
            LastModifiedAt = now
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = StableId("activity-authority", definition.Id),
            TenantId = catalogVersion.TenantId,
            DefinitionId = definition.Id,
            ContentAuthority = new(ActivityContentAuthorityKind.ProviderSource, version.ProviderKey, version.SourceId),
            HeadVersionId = version.Id,
            RecommendedVersionId = version.Id,
            CreatedAt = now,
            LastModifiedAt = now
        };
        var layout = new ActivityDefinitionVersionLayout
        {
            Id = version.Id,
            TenantId = catalogVersion.TenantId,
            DefinitionVersionId = version.Id,
            Records = [],
            CreatedAt = now,
            LastModifiedAt = now
        };

        try
        {
            await commitCommand.ExecuteAsync(new(
                persistedDefinition,
                authoring,
                catalogVersion,
                publication,
                layout,
                template,
                sourceReference), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Another node reconciling the same source can commit this publication after the check above. The commit
            // then refuses it as already published, or loses a uniqueness race to it (#2189). When what is stored is
            // the publication this call was about to write, the version is published, which is all this call promises.
            // Anything else is a real failure and keeps the commit's own exception.
            if (!IsIdentical(await publicationStore.FindAsync(version.Id, cancellationToken), publication))
                throw;
        }
    }

    /// <summary>
    /// Whether <paramref name="stored"/> is the publication <paramref name="candidate"/> describes. Every persisted member
    /// must be equal except the ones that do not describe what was published:
    /// <list type="bullet">
    /// <item>the clock readings (<c>PublishedAt</c>, <c>CreatedAt</c>, <c>LastModifiedAt</c>) and the <c>RowNumber</c>
    /// ordinal hint, which each node fills in for itself;</item>
    /// <item>the <c>Lifecycle</c>, which moves only after publication, so a retired version is still the one published.</item>
    /// </list>
    /// A member added later is compared unless it is listed here, so a new difference fails rather than passes.
    /// </summary>
    private static bool IsIdentical(ActivityDefinitionVersionPublication? stored, ActivityDefinitionVersionPublication candidate) =>
        stored is not null && StringComparer.Ordinal.Equals(PublishedContent(stored), PublishedContent(candidate));

    private static string PublishedContent(ActivityDefinitionVersionPublication publication)
    {
        var members = JsonSerializer.SerializeToNode(publication)!.AsObject();
        foreach (var member in MembersNotCompared)
            members.Remove(member);
        return ExecutableActivityTemplateBehaviorHasher.ComputeCanonicalValueHash(members);
    }

    private static readonly string[] MembersNotCompared =
    [
        nameof(ActivityDefinitionVersionPublication.PublishedAt),
        nameof(ActivityDefinitionVersionPublication.CreatedAt),
        nameof(ActivityDefinitionVersionPublication.LastModifiedAt),
        nameof(ActivityDefinitionVersionPublication.RowNumber),
        nameof(ActivityDefinitionVersionPublication.Lifecycle)
    ];

    private static Elsa.Activities.Design.Core.Models.ActivityContract ToContract(IActivityDefinitionVersion version) => new(
        "1",
        version.Inputs.Select(input => new Elsa.Activities.Design.Core.Models.ActivityInputContract(
            input.ReferenceKey,
            input.Name,
            input.Type,
            input.IsRequired,
            input.IsNullable,
            input.DefaultValue is { } value ? new(input.DefaultSyntax ?? "Literal", value) : null,
            WellKnownRuntimeDurableValueStorageDrivers.Json,
            DisplayName: input.DisplayName,
            Description: input.Description,
            Category: input.Category,
            Order: input.Order,
            UiHint: input.UiHint,
            UiSpecifications: input.UISpecifications)).ToArray(),
        version.Outputs.Select(output => new ActivityOutputContract(
            output.ReferenceKey,
            output.Name,
            output.Type,
            output.IsRequired,
            output.IsNullable,
            WellKnownRuntimeDurableValueStorageDrivers.Json,
            DisplayName: output.DisplayName,
            Description: output.Description,
            Category: output.Category,
            Order: output.Order,
            UiHint: output.UiHint,
            UiSpecifications: output.UISpecifications,
            SourceRepresentation: output.SourceRepresentation)).ToArray(),
        ToOutcomes(version));

    private static IReadOnlyList<ActivityOutcomeContract> ToOutcomes(IActivityDefinitionVersion version)
    {
        var outcomes = new List<ActivityOutcomeContract>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var facet in version.DesignFacets.Where(x =>
                     StringComparer.Ordinal.Equals(x.Kind, "elsa.outcomes") &&
                     x.Payload.ValueKind == JsonValueKind.Object &&
                     x.Payload.TryGetProperty("ports", out var ports) &&
                     ports.ValueKind == JsonValueKind.Array))
        {
            foreach (var port in facet.Payload.GetProperty("ports").EnumerateArray())
            {
                if (port.ValueKind != JsonValueKind.Object ||
                    !port.TryGetProperty("name", out var nameProperty) ||
                    nameProperty.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(nameProperty.GetString()))
                    continue;

                var name = nameProperty.GetString()!;
                var referenceKey = port.TryGetProperty("referenceKey", out var referenceProperty) &&
                                   referenceProperty.ValueKind == JsonValueKind.String &&
                                   !string.IsNullOrWhiteSpace(referenceProperty.GetString())
                    ? referenceProperty.GetString()!
                    : name;
                if (seen.Add(referenceKey))
                    outcomes.Add(new(referenceKey, name, true));
            }
        }

        return outcomes.Count == 0 ? [new("Done", "Done", true)] : outcomes;
    }

    private static string StableId(string prefix, string value) =>
        $"{prefix}-{Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))}";
}
