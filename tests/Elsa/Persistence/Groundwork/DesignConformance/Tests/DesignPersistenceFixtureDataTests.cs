using System.Reflection;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignPersistenceFixtureDataTests
{
    [Fact]
    public void Equivalent_fixture_values_have_a_stable_result_hash()
    {
        var first = DesignPersistenceFixtureData.ResultHash(DesignPersistenceFixtureData.WorkflowDefinition());
        var second = DesignPersistenceFixtureData.ResultHash(DesignPersistenceFixtureData.WorkflowDefinition());

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Equivalent_dictionary_payloads_with_different_insertion_orders_have_the_same_result_hash()
    {
        var first = new Dictionary<string, object?>
        {
            ["z"] = 1,
            ["nested"] = new Dictionary<string, object?> { ["b"] = true, ["a"] = new[] { "first", "second" } },
            ["a"] = "value"
        };
        var second = new Dictionary<string, object?>
        {
            ["a"] = "value",
            ["nested"] = new Dictionary<string, object?> { ["a"] = new[] { "first", "second" }, ["b"] = true },
            ["z"] = 1
        };

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(first), DesignPersistenceFixtureData.ResultHash(second));
    }

    [Fact]
    public void Equivalent_object_payloads_with_different_property_orders_have_the_same_result_hash()
    {
        var first = new { Zeta = 1, Alpha = "value" };
        var second = new { Alpha = "value", Zeta = 1 };

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(first), DesignPersistenceFixtureData.ResultHash(second));
    }

    [Fact]
    public void Semantically_different_payloads_have_different_result_hashes()
    {
        var first = new Dictionary<string, object?> { ["state"] = "published", ["version"] = 1 };
        var second = new Dictionary<string, object?> { ["state"] = "published", ["version"] = 2 };

        Assert.NotEqual(DesignPersistenceFixtureData.ResultHash(first), DesignPersistenceFixtureData.ResultHash(second));
    }

    [Fact]
    public void Payload_serializer_options_are_returned_as_independent_copies()
    {
        var serializer = new DesignPersistenceFixtureData.DeterministicPayloadSerializer();
        var mutated = serializer.GetOptions();
        mutated.WriteIndented = true;

        Assert.False(serializer.GetOptions().WriteIndented);
        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }),
            DesignPersistenceFixtureData.ResultHash(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
    }

    [Fact]
    public void Fixture_scopes_remain_distinct()
    {
        var first = DesignPersistenceFixtureData.WorkflowDefinition(DesignPersistenceFixtureData.ScopeA);
        var second = DesignPersistenceFixtureData.WorkflowDefinition(DesignPersistenceFixtureData.ScopeB);

        Assert.NotEqual(first.TenantId, second.TenantId);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Layout_fixture_is_bound_to_the_fixed_workflow_version()
    {
        var layout = DesignPersistenceFixtureData.WorkflowVersionLayout();

        Assert.Equal(DesignPersistenceFixtureData.WorkflowVersionLayoutId, layout.Id);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowVersionId, layout.WorkflowDefinitionVersionId);
    }

    [Fact]
    public void Workflow_state_uses_the_fixed_activity_version_identity()
    {
        var state = DesignPersistenceFixtureData.WorkflowState();

        Assert.NotNull(state.RootActivity);
        Assert.Equal(DesignPersistenceFixtureData.ActivityVersionId, state.RootActivity!.ActivityVersionId);
    }

    [Fact]
    public void Reconciliation_candidate_keeps_its_definition_identity_and_scope()
    {
        var candidate = DesignPersistenceFixtureData.ReconciledActivityVersion(DesignPersistenceFixtureData.ScopeB);

        Assert.NotNull(candidate.Definition);
        Assert.Equal(DesignPersistenceFixtureData.ReconciledActivityDefinitionId, candidate.DefinitionId);
        Assert.Equal(candidate.DefinitionId, candidate.Definition!.Id);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, candidate.TenantId);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, candidate.Definition.TenantId);
    }

    [Fact]
    public void Activity_canonical_snapshot_detects_definition_description_drift()
    {
        var definition = DesignPersistenceFixtureData.ActivityDefinition();
        var version = DesignPersistenceFixtureData.ActivityVersion();
        var before = ActivityDesignContractSuite.CanonicalSnapshot(definition, version);

        definition.Description = "changed after the first snapshot";
        var after = ActivityDesignContractSuite.CanonicalSnapshot(definition, version);

        Assert.NotEqual(
            DesignPersistenceFixtureData.ResultHash(before),
            DesignPersistenceFixtureData.ResultHash(after));
    }

    [Fact]
    public void Workflow_canonical_snapshot_detects_definition_deletion_reason_drift()
    {
        var definition = DesignPersistenceFixtureData.WorkflowDefinition();
        var draft = DesignPersistenceFixtureData.WorkflowDraft();
        var layout = DesignPersistenceFixtureData.WorkflowDraftLayout();
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout);

        definition.DeletedReason = "changed after the first snapshot";
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout);

        Assert.NotEqual(
            DesignPersistenceFixtureData.ResultHash(before),
            DesignPersistenceFixtureData.ResultHash(after));
    }

    [Fact]
    public void Workflow_version_canonical_snapshot_detects_source_draft_drift()
    {
        var version = DesignPersistenceFixtureData.WorkflowVersion();
        var layout = DesignPersistenceFixtureData.WorkflowVersionLayout();
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(version, layout);

        version = new WorkflowDefinitionVersion(version.DefinitionId, version.Version)
        {
            Id = version.Id,
            TenantId = version.TenantId,
            CreatedAt = version.CreatedAt,
            LastModifiedAt = version.LastModifiedAt,
            SourceCreatedAt = version.SourceCreatedAt,
            SourceDraftId = "different-source-draft",
            State = version.State
        };
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(version, layout);

        Assert.NotEqual(
            DesignPersistenceFixtureData.ResultHash(before),
            DesignPersistenceFixtureData.ResultHash(after));
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

    [Fact]
    public void Deterministic_identity_generator_fails_when_a_scenario_exceeds_its_declared_inputs()
    {
        var generator = new DesignPersistenceFixtureData.DeterministicIdentityGenerator(["one"]);

        Assert.Equal("one", generator.Generate());
        Assert.Throws<InvalidOperationException>(generator.Generate);
    }

    private static void AssertSnapshotCoverage<TEntity, TSnapshot>(params string[] intentionalOmissions)
    {
        var entityProperties = PublicPropertyNames<TEntity>();
        var snapshotProperties = PublicPropertyNames<TSnapshot>();
        var omissions = intentionalOmissions.ToHashSet(StringComparer.Ordinal);

        Assert.Empty(snapshotProperties.Intersect(omissions, StringComparer.Ordinal));
        Assert.Equal(
            entityProperties,
            snapshotProperties
                .Concat(omissions)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static string[] PublicPropertyNames<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
