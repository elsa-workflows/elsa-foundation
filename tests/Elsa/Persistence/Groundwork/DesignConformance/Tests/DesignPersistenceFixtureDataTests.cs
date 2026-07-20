using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
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
    public void Activity_version_can_target_a_distinct_definition_without_changing_its_scope()
    {
        var version = DesignPersistenceFixtureData.ActivityVersion(
            id: "activity-version-for-another-definition",
            scope: DesignPersistenceFixtureData.ScopeB,
            definitionId: "another-activity-definition");

        Assert.Equal("another-activity-definition", version.DefinitionId);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, version.TenantId);
    }

    [Fact]
    public void Reusable_activity_fixture_keeps_draft_and_layout_scope_revision_and_identity_aligned()
    {
        var request = DesignPersistenceFixtureData.ReusableActivityDefinition(DesignPersistenceFixtureData.ScopeB);

        Assert.Equal(DesignPersistenceFixtureData.ReusableActivityDefinitionId, request.Definition.Id);
        Assert.Equal(request.Definition.Id, request.AuthoringState.DefinitionId);
        Assert.Equal(request.Definition.Id, request.InitialDraft.DefinitionId);
        Assert.Equal(request.InitialDraft.Id, request.InitialLayout.DraftId);
        Assert.Equal(request.InitialDraft.Revision, request.InitialLayout.Revision);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, request.Definition.TenantId);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, request.AuthoringState.TenantId);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, request.InitialDraft.TenantId);
        Assert.Equal(DesignPersistenceFixtureData.ScopeB, request.InitialLayout.TenantId);
        Assert.Equal("initial", request.InitialDraft.State.Options["label"]);
        Assert.Equal("initial", Assert.Single(request.InitialLayout.Records).NodeId);
    }

    public static IEnumerable<object[]> ActivityDefinitionSnapshotProperties =>
        SnapshotPropertyCases<ActivityDesignContractSuite.ActivityDefinitionSnapshot>();

    public static IEnumerable<object[]> ActivityDefinitionVersionSnapshotProperties =>
        SnapshotPropertyCases<ActivityDesignContractSuite.ActivityDefinitionVersionSnapshot>();

    public static IEnumerable<object[]> WorkflowDefinitionSnapshotProperties =>
        SnapshotPropertyCases<WorkflowDesignContractSuite.WorkflowDefinitionSnapshot>();

    public static IEnumerable<object[]> WorkflowDefinitionDraftSnapshotProperties =>
        SnapshotPropertyCases<WorkflowDesignContractSuite.WorkflowDefinitionDraftSnapshot>();

    public static IEnumerable<object[]> WorkflowDefinitionDraftLayoutSnapshotProperties =>
        SnapshotPropertyCases<WorkflowDesignContractSuite.WorkflowDefinitionDraftLayoutSnapshot>();

    public static IEnumerable<object[]> WorkflowDefinitionVersionSnapshotProperties =>
        SnapshotPropertyCases<WorkflowDesignContractSuite.WorkflowDefinitionVersionSnapshot>();

    public static IEnumerable<object[]> WorkflowDefinitionVersionLayoutSnapshotProperties =>
        SnapshotPropertyCases<WorkflowDesignContractSuite.WorkflowDefinitionVersionLayoutSnapshot>();

    public static IEnumerable<object[]> DesignMetadataSnapshotProperties =>
        SnapshotPropertyCases<WorkflowDesignContractSuite.DesignMetadataSnapshot>();

    [Theory]
    [MemberData(nameof(ActivityDefinitionSnapshotProperties))]
    public void Activity_definition_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var definition = DesignPersistenceFixtureData.ActivityDefinition();
        var version = DesignPersistenceFixtureData.ActivityVersion();
        var before = ActivityDesignContractSuite.CanonicalSnapshot(definition, version);

        MutateProperty(definition, propertyName);
        var after = ActivityDesignContractSuite.CanonicalSnapshot(definition, version);

        AssertSnapshotPropertyMapping(definition, before, after, snapshot => snapshot.Definition, propertyName);
    }

    [Theory]
    [MemberData(nameof(ActivityDefinitionVersionSnapshotProperties))]
    public void Activity_version_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var definition = DesignPersistenceFixtureData.ActivityDefinition();
        var version = DesignPersistenceFixtureData.ActivityVersion();
        var before = ActivityDesignContractSuite.CanonicalSnapshot(definition, version);

        MutateProperty(version, propertyName);
        var after = ActivityDesignContractSuite.CanonicalSnapshot(definition, version);

        AssertSnapshotPropertyMapping(version, before, after, snapshot => snapshot.Version, propertyName);
    }

    [Theory]
    [MemberData(nameof(WorkflowDefinitionSnapshotProperties))]
    public void Workflow_definition_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var definition = DesignPersistenceFixtureData.WorkflowDefinition();
        var draft = DesignPersistenceFixtureData.WorkflowDraft();
        var layout = DesignPersistenceFixtureData.WorkflowDraftLayout();
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout);

        MutateProperty(definition, propertyName);
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout);

        AssertSnapshotPropertyMapping(definition, before, after, snapshot => snapshot.Definition, propertyName);
    }

    [Theory]
    [MemberData(nameof(WorkflowDefinitionDraftSnapshotProperties))]
    public void Workflow_draft_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var definition = DesignPersistenceFixtureData.WorkflowDefinition();
        var draft = DesignPersistenceFixtureData.WorkflowDraft();
        var layout = DesignPersistenceFixtureData.WorkflowDraftLayout();
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout);

        MutateProperty(draft, propertyName);
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout);

        AssertSnapshotPropertyMapping(draft, before, after, snapshot => snapshot.Draft, propertyName);
    }

    [Theory]
    [MemberData(nameof(WorkflowDefinitionDraftLayoutSnapshotProperties))]
    public void Workflow_draft_layout_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var definition = DesignPersistenceFixtureData.WorkflowDefinition();
        var draft = DesignPersistenceFixtureData.WorkflowDraft();
        var layout = new DraftLayoutSource { Records = DesignPersistenceFixtureData.WorkflowDraftLayout() };
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout.Records);

        MutateProperty(layout, propertyName);
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, layout.Records);

        AssertSnapshotPropertyMapping(layout, before, after, snapshot => snapshot.Layout, propertyName);
    }

    [Theory]
    [MemberData(nameof(WorkflowDefinitionVersionSnapshotProperties))]
    public void Workflow_version_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var version = DesignPersistenceFixtureData.WorkflowVersion();
        var layout = DesignPersistenceFixtureData.WorkflowVersionLayout();
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(version, layout);

        MutateProperty(version, propertyName);
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(version, layout);

        AssertSnapshotPropertyMapping(version, before, after, snapshot => snapshot.Version, propertyName);
    }

    [Theory]
    [MemberData(nameof(WorkflowDefinitionVersionLayoutSnapshotProperties))]
    public void Workflow_version_layout_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var version = DesignPersistenceFixtureData.WorkflowVersion();
        var layout = DesignPersistenceFixtureData.WorkflowVersionLayout();
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(version, layout);

        MutateProperty(layout, propertyName);
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(version, layout);

        AssertSnapshotPropertyMapping(layout, before, after, snapshot => snapshot.Layout, propertyName);
    }

    [Theory]
    [MemberData(nameof(DesignMetadataSnapshotProperties))]
    public void Design_metadata_snapshot_detects_each_included_property_drift(string propertyName)
    {
        var definition = DesignPersistenceFixtureData.WorkflowDefinition();
        var draft = DesignPersistenceFixtureData.WorkflowDraft();
        var record = Assert.Single(DesignPersistenceFixtureData.WorkflowDraftLayout());
        var before = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, [record]);
        var changedRecord = ChangedDesignMetadata(record, propertyName);
        var after = WorkflowDesignContractSuite.CanonicalSnapshot(definition, draft, [changedRecord]);

        AssertSnapshotPropertyMapping(changedRecord, before, after, snapshot => Assert.Single(snapshot.Layout.Records), propertyName);
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

    private static IEnumerable<object[]> SnapshotPropertyCases<TSnapshot>() =>
        PublicPropertyNames<TSnapshot>().Select(propertyName => new object[] { propertyName });

    private static void MutateProperty<T>(T entity, string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        Assert.True(property!.CanWrite, $"{typeof(T).Name}.{propertyName} must be writable for mutation coverage.");
        property.SetValue(entity, ChangedValue(property.PropertyType, property.GetValue(entity), propertyName));
    }

    private static object ChangedValue(Type propertyType, object? currentValue, string propertyName)
    {
        if (propertyType == typeof(string))
            return $"{currentValue ?? "null"}-drift";
        if (propertyType == typeof(DateTimeOffset))
            return ((DateTimeOffset)currentValue!).AddMinutes(1);
        if (propertyType == typeof(DateTimeOffset?))
            return (DateTimeOffset?)((DateTimeOffset?)currentValue ?? DesignPersistenceFixtureData.Epoch).AddMinutes(1);
        if (propertyType == typeof(bool))
            return !(bool)currentValue!;
        if (propertyType == typeof(ActivityExecutionType))
            return currentValue is ActivityExecutionType.Action ? ActivityExecutionType.Trigger : ActivityExecutionType.Action;
        if (propertyType == typeof(JsonElement))
            return JsonSerializer.SerializeToElement(new { propertyName, drift = true });
        if (propertyType == typeof(WorkflowDefinitionState))
            return DesignPersistenceFixtureData.WorkflowState($"drift-{propertyName}");
        if (propertyType == typeof(IEnumerable<InputDefinition>))
            return new[] { new InputDefinition("drift-input", "DriftInput", new TypeReference("String"), null, "Drift input", null, false) };
        if (propertyType == typeof(IEnumerable<OutputDefinition>))
            return new[] { new OutputDefinition("drift-output", "DriftOutput", new TypeReference("String"), null, "Drift output", null, false) };
        if (propertyType == typeof(IEnumerable<ActivityDesignFacet>))
            return new[] { new ActivityDesignFacet("drift", "1", JsonSerializer.SerializeToElement(new { value = true })) };
        if (typeof(IEnumerable<DesignMetadataRecord>).IsAssignableFrom(propertyType))
            return ChangedLayout(propertyName);

        throw new InvalidOperationException($"No deterministic mutation is defined for {propertyType} ({propertyName}).");
    }

    private static IReadOnlyCollection<DesignMetadataRecord> ChangedLayout(string propertyName) =>
        propertyName == nameof(WorkflowDesignContractSuite.WorkflowDefinitionDraftLayoutSnapshot.Records) ||
        propertyName == nameof(WorkflowDesignContractSuite.WorkflowDefinitionVersionLayoutSnapshot.Records)
            ? [new DesignMetadataRecord("drift-node", -10, -20, 320, 180, JsonSerializer.SerializeToElement(new { drift = true }))]
            : throw new InvalidOperationException($"No draft-layout mutation is defined for {propertyName}.");

    private static DesignMetadataRecord ChangedDesignMetadata(
        DesignMetadataRecord record,
        string propertyName) =>
        propertyName switch
        {
            nameof(DesignMetadataRecord.NodeId) => record with { NodeId = $"{record.NodeId}-drift" },
            nameof(DesignMetadataRecord.X) => record with { X = record.X + 1 },
            nameof(DesignMetadataRecord.Y) => record with { Y = record.Y + 1 },
            nameof(DesignMetadataRecord.Width) => record with { Width = (record.Width ?? 0) + 1 },
            nameof(DesignMetadataRecord.Height) => record with { Height = (record.Height ?? 0) + 1 },
            nameof(DesignMetadataRecord.AdditionalProperties) => record with
            {
                AdditionalProperties = JsonSerializer.SerializeToElement(new { drift = true })
            },
            _ => throw new InvalidOperationException($"No design-metadata mutation is defined for {propertyName}.")
        };

    private static void AssertSnapshotPropertyMapping<TSource, TAggregate, TSnapshot>(
        TSource source,
        TAggregate before,
        TAggregate after,
        Func<TAggregate, TSnapshot> snapshotSelector,
        string propertyName)
    {
        var sourceProperty = GetPublicProperty(typeof(TSource), propertyName);
        var beforeSnapshot = snapshotSelector(before);
        var afterSnapshot = snapshotSelector(after);
        var snapshotProperty = GetPublicProperty(typeof(TSnapshot), propertyName);

        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(sourceProperty.GetValue(source)),
            DesignPersistenceFixtureData.ResultHash(snapshotProperty.GetValue(afterSnapshot)));
        Assert.NotEqual(
            DesignPersistenceFixtureData.ResultHash(snapshotProperty.GetValue(beforeSnapshot)),
            DesignPersistenceFixtureData.ResultHash(snapshotProperty.GetValue(afterSnapshot)));
        AssertNonTargetSnapshotPropertiesAreUnchanged(beforeSnapshot, afterSnapshot, propertyName);
    }

    private static void AssertNonTargetSnapshotPropertiesAreUnchanged<TSnapshot>(
        TSnapshot beforeSnapshot,
        TSnapshot afterSnapshot,
        string targetPropertyName)
    {
        foreach (var property in typeof(TSnapshot).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name == targetPropertyName)
                continue;

            Assert.Equal(
                DesignPersistenceFixtureData.ResultHash(property.GetValue(beforeSnapshot)),
                DesignPersistenceFixtureData.ResultHash(property.GetValue(afterSnapshot)));
        }
    }

    private static PropertyInfo GetPublicProperty(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return property!;
    }

    private sealed class DraftLayoutSource
    {
        public IReadOnlyCollection<DesignMetadataRecord> Records { get; set; } = [];
    }
}
