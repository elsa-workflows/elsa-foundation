using Elsa.Workflows.Publishing.Core.Contracts;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Shared read-only catalog projections of a validated public contract and compiled provider output.</summary>
public sealed class ActivityCatalogProjection : IActivityCatalogProjection
{
    public ActivityDefinitionVersion CatalogVersion(ActivityDefinition definition, ActivityContract contract, ActivityProviderManifest provider,
        string versionId, string version, string sourceKind, string sourceId, ExecutableActivityTemplate template, DateTimeOffset now) => new(version, definition.Id)
        {
            Id = versionId,
            TenantId = definition.TenantId,
            ProviderKey = provider.ProviderKey,
            ProviderSchemaVersion = provider.SchemaVersion,
            ConsumerKey = template.Root.Descriptor.ConsumerKey,
            ConsumerSchemaVersion = template.Root.Descriptor.SchemaVersion,
            DescriptorPayload = template.Root.Descriptor.Payload,
            // Project the published public contract's inputs/outputs into the canvas descriptors the
            // authoring catalog reads. Without this a design-owned (e.g. elsa.activity-graph) version
            // surfaces no editable properties in the workflow editor, unlike CLR-scanned versions whose
            // InputDefinition/OutputDefinition projections are populated by the reconciler. See #930.
            Inputs = contract.Inputs.Select(ToInputDefinition).ToArray(),
            Outputs = contract.Outputs.Select(ToOutputDefinition).ToArray(),
            DesignFacets = ToDesignFacets(contract),
            SourceKind = sourceKind,
            SourceId = sourceId,
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

    public ActivityDefinitionVersionPublication Publication(ActivityDefinition definition, ActivityContract contract, ActivityProviderManifest provider,
        string versionId, string version, string providerFingerprint, string sourceReferenceId, ExecutableActivityTemplate template,
        ActivityResourceMeasurements measurements, DateTimeOffset now, ActivityDefinitionVersionResolutionKind resolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary,
        string? sourceDraftId = null, string? sourceVersionId = null) => new()
        {
            Id = versionId,
            TenantId = definition.TenantId,
            DefinitionVersionId = versionId,
            DefinitionId = definition.Id,
            Version = version,
            ActivityTypeKey = definition.ActivityTypeKey,
            ResolutionKind = resolutionKind,
            SourceDraftId = sourceDraftId,
            SourceVersionId = sourceVersionId,
            Contract = contract,
            Provider = provider,
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

}
