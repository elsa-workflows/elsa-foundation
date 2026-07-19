using System.Security.Cryptography;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Services;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Closes source-owned activity catalog versions into the same immutable publication/template model
/// used by Design-owned providers. The source remains sole content authority; customization forks to
/// a new Design-owned definition through the normal authoring API.
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

        await commitCommand.ExecuteAsync(new(
            persistedDefinition,
            authoring,
            catalogVersion,
            publication,
            layout,
            template,
            sourceReference), cancellationToken);
    }

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
        [new("Done", "Done", true)]);

    private static string StableId(string prefix, string value) =>
        $"{prefix}-{Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))}";
}
