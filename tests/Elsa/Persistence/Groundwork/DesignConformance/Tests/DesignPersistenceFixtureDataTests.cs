using System.Reflection;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Pins the two oracle properties a provider leaf cannot reveal: the result hash is canonical over member
/// order, and every public entity property is either hashed by its snapshot or named as an intentional omission.
/// </summary>
public class DesignPersistenceFixtureDataTests
{
    [Fact]
    public void Result_hash_is_canonical_over_member_order_and_sensitive_to_content()
    {
        var reordered = DesignPersistenceFixtureData.ResultHash(new Dictionary<string, object?>
        {
            ["z"] = 1,
            ["nested"] = new Dictionary<string, object?> { ["b"] = true, ["a"] = new[] { "first", "second" } },
            ["a"] = "value"
        });
        var ordered = DesignPersistenceFixtureData.ResultHash(new Dictionary<string, object?>
        {
            ["a"] = "value",
            ["nested"] = new Dictionary<string, object?> { ["a"] = new[] { "first", "second" }, ["b"] = true },
            ["z"] = 1
        });

        Assert.Equal(64, ordered.Length);
        Assert.Equal(ordered, reordered);
        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(new { Zeta = 1, Alpha = "value" }),
            DesignPersistenceFixtureData.ResultHash(new { Alpha = "value", Zeta = 1 }));
        Assert.NotEqual(
            DesignPersistenceFixtureData.ResultHash(new Dictionary<string, object?> { ["state"] = "published", ["version"] = 1 }),
            DesignPersistenceFixtureData.ResultHash(new Dictionary<string, object?> { ["state"] = "published", ["version"] = 2 }));
    }

    [Fact]
    public void Canonical_snapshots_account_for_every_public_design_entity_property()
    {
        // RowNumber, navigation properties, relational *Source shadows, and the retired DescriptorType
        // column are provider representations rather than logical public-read content (FR-010).
        AssertSnapshotCoverage<ActivityDefinition, ActivityDesignContractSuite.ActivityDefinitionSnapshot>(
            nameof(ActivityDefinition.RowNumber));
        AssertSnapshotCoverage<ActivityDefinitionVersion, ActivityDesignContractSuite.ActivityDefinitionVersionSnapshot>(
            nameof(ActivityDefinitionVersion.RowNumber),
            nameof(ActivityDefinitionVersion.Definition),
            "DescriptorType",
            nameof(ActivityDefinitionVersion.DescriptorPayloadSource),
            nameof(ActivityDefinitionVersion.InputsSource),
            nameof(ActivityDefinitionVersion.OutputsSource),
            nameof(ActivityDefinitionVersion.DesignFacetsSource));
        AssertSnapshotCoverage<WorkflowDefinition, WorkflowDesignContractSuite.WorkflowDefinitionSnapshot>(
            nameof(WorkflowDefinition.RowNumber));
        AssertSnapshotCoverage<WorkflowDefinitionDraft, WorkflowDesignContractSuite.WorkflowDefinitionDraftSnapshot>(
            nameof(WorkflowDefinitionDraft.RowNumber),
            nameof(WorkflowDefinitionDraft.WorkflowDefinition),
            nameof(WorkflowDefinitionDraft.StateSource));

        // The draft read port exposes only logical layout records; sibling identity/FK/timestamps exist
        // only in the temporary relational representation and are intentionally outside the oracle hash.
        AssertSnapshotCoverage<WorkflowDefinitionDraftLayout, WorkflowDesignContractSuite.WorkflowDefinitionDraftLayoutSnapshot>(
            nameof(WorkflowDefinitionDraftLayout.RowNumber),
            nameof(WorkflowDefinitionDraftLayout.Id),
            nameof(WorkflowDefinitionDraftLayout.CreatedAt),
            nameof(WorkflowDefinitionDraftLayout.LastModifiedAt),
            nameof(WorkflowDefinitionDraftLayout.TenantId),
            nameof(WorkflowDefinitionDraftLayout.WorkflowDefinitionDraftId),
            nameof(WorkflowDefinitionDraftLayout.WorkflowDefinitionDraft));
        AssertSnapshotCoverage<WorkflowDefinitionVersion, WorkflowDesignContractSuite.WorkflowDefinitionVersionSnapshot>(
            nameof(WorkflowDefinitionVersion.RowNumber),
            nameof(WorkflowDefinitionVersion.Definition),
            nameof(WorkflowDefinitionVersion.StateSource));
        AssertSnapshotCoverage<WorkflowDefinitionVersionLayout, WorkflowDesignContractSuite.WorkflowDefinitionVersionLayoutSnapshot>(
            nameof(WorkflowDefinitionVersionLayout.RowNumber),
            nameof(WorkflowDefinitionVersionLayout.WorkflowDefinitionVersion));
        AssertSnapshotCoverage<DesignMetadataRecord, WorkflowDesignContractSuite.DesignMetadataSnapshot>();
    }

    private static void AssertSnapshotCoverage<TEntity, TSnapshot>(params string[] intentionalOmissions)
    {
        var snapshotProperties = PublicPropertyNames<TSnapshot>();

        Assert.Empty(snapshotProperties.Intersect(intentionalOmissions, StringComparer.Ordinal));
        Assert.Equal(
            PublicPropertyNames<TEntity>(),
            snapshotProperties.Concat(intentionalOmissions).Order(StringComparer.Ordinal).ToArray());
    }

    private static string[] PublicPropertyNames<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
