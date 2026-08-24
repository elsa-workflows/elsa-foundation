using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;

namespace Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests;

internal static class TemporalProjectionData
{
    public static ActivityManagementDefinitionChange DefinitionChange(
        string id,
        string? tenantId,
        string displayName,
        DateTimeOffset changedAt)
    {
        var definition = DefinitionEntity(id, tenantId, displayName, changedAt);
        return new(
            definition,
            new ActivityDefinitionAuthoringState
            {
                Id = $"authoring-{id}",
                DefinitionId = id,
                TenantId = tenantId,
                ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.activity-graph"),
                CreatedAt = changedAt,
                LastModifiedAt = changedAt
            });
    }

    public static ActivityDefinition DefinitionEntity(
        string id,
        string? tenantId,
        string displayName,
        DateTimeOffset changedAt) => new()
    {
        Id = id,
        TenantId = tenantId,
        ActivityTypeKey = $"tests.{id}",
        Category = "Tests",
        DisplayName = displayName,
        Description = "Safe summary",
        CreatedAt = changedAt,
        LastModifiedAt = changedAt
    };

    public static ActivityDefinitionDraft Draft(
        string id,
        string definitionId,
        string label,
        ActivityDefinitionDraftStatus status,
        string providerKey,
        DateTimeOffset changedAt,
        string? tenantId = null) => new()
    {
        Id = id,
        DefinitionId = definitionId,
        TenantId = tenantId,
        Revision = 1,
        Status = status,
        PresentationLabel = label,
        State = new(
            EmptyContract(),
            new(providerKey, "1", Json("{\"internalValue\":\"not projected\"}")),
            new Dictionary<string, string>()),
        CreatedAt = changedAt,
        LastModifiedAt = changedAt
    };

    public static ActivityDefinitionVersionPublication Version(
        string id,
        string definitionId,
        string version,
        string providerKey,
        DateTimeOffset publishedAt,
        string? tenantId,
        ActivityDefinitionVersionLifecycle lifecycle = ActivityDefinitionVersionLifecycle.Active) => new()
    {
        Id = $"publication-{id}",
        TenantId = tenantId,
        DefinitionVersionId = id,
        DefinitionId = definitionId,
        Version = version,
        ActivityTypeKey = "tests.filtered",
        Contract = EmptyContract(),
        Provider = new(providerKey, "1", Json("{\"internalValue\":\"not projected\"}")),
        TemplateId = $"template-{id}",
        TemplateHash = $"hash-{id}",
        SourceReferenceId = $"source-{id}",
        ProviderFingerprint = $"fingerprint-{id}",
        DirectDependencyCount = 0,
        ClosedTemplateCount = 1,
        RuntimeRequirements = [],
        Lifecycle = lifecycle,
        PublishedAt = publishedAt,
        CreatedAt = publishedAt,
        LastModifiedAt = publishedAt
    };

    public static T Deserialize<T>(ActivityDesignDocument document) where T : Elsa.Primitives.Entities.Entity =>
        JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<T>>(
            document.ContentJson,
            GroundworkActivitiesDesignJson.Options)!.Entity;

    private static ActivityContract EmptyContract() => new("1", [], [], []);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
