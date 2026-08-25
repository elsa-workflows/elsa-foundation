using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Query.Model;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDefinitionVersionStoreTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    private sealed record Fixture(GroundworkWorkflowDefinitionVersionStore Versions, DesignGroundworkTestPersistence Raw);

    private static Fixture Seeded(IEnumerable<WorkflowDefinitionVersion> versions, IEnumerable<WorkflowDefinition>? definitions = null)
    {
        var raw = new DesignGroundworkTestPersistence();
        raw.RecordQueries = true;
        foreach (var version in versions) raw.SeedVersion(version);
        foreach (var definition in definitions ?? []) raw.SeedDefinition(definition);
        var definitionsStore = new GroundworkWorkflowDefinitionStore(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor);
        return new Fixture(new GroundworkWorkflowDefinitionVersionStore(
            raw, definitionsStore, Payloads, DesignGroundworkTestAccess.DefaultAccessContextAccessor), raw);
    }

    private static WorkflowDefinitionState EmptyState() => new([], null, [], [], null);
    private static WorkflowDefinitionVersion Version(string id, string definitionId, string version) =>
        new(definitionId, version) { Id = id, State = EmptyState() };

    [Fact]
    public async Task FindById_round_trips_state_and_excludes_navigation()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0")]);
        using (fixture.Raw)
        {
            var result = await fixture.Versions.FindByIdAsync("v1");
            Assert.NotNull(result);
            Assert.Equal("def1", result!.DefinitionId);
            Assert.Equal("1.0.0", result.Version);
            Assert.NotNull(result.State);
            Assert.Empty(result.State.Variables);
            Assert.Null(result.Definition);
        }
    }

    [Fact]
    public void Stored_document_omits_persistence_artifacts()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0")]);
        using (fixture.Raw)
        {
            var values = Assert.Single(fixture.Raw.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind));
            var json = values.Values[WorkflowsDesignStorageManifest.ContentField] switch
            {
                System.Text.Json.JsonElement element => element.GetRawText(),
                string text => text,
                _ => throw new InvalidOperationException()
            };
            Assert.Contains("\"state\"", json);
            Assert.DoesNotContain("stateSource", json);
            Assert.DoesNotContain("rowNumber", json);
            Assert.DoesNotContain("\"definition\":", json);
        }
    }

    [Fact]
    public async Task GetWithDefinition_loads_owner_via_second_read()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0")], [new WorkflowDefinition { Id = "def1", Name = "Order Processing" }]);
        using (fixture.Raw)
            Assert.Equal("Order Processing", (await fixture.Versions.GetWithDefinitionAsync("v1")).Definition?.Name);
    }

    [Fact]
    public async Task GetWithDefinition_throws_when_version_absent()
    {
        var fixture = Seeded([]);
        using (fixture.Raw) await Assert.ThrowsAsync<EntityNotFoundException>(() => fixture.Versions.GetWithDefinitionAsync("missing"));
    }

    [Fact]
    public async Task FindLatestVersion_resolves_highest_semver()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0"), Version("v2", "def1", "2.0.0"), Version("v3", "def1", "1.5.0")]);
        using (fixture.Raw) Assert.Equal("v2", (await fixture.Versions.FindLatestVersionAsync("def1"))?.Id);
    }

    [Fact]
    public async Task FindLatestVersion_returns_null_for_unknown_definition()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0")]);
        using (fixture.Raw) Assert.Null(await fixture.Versions.FindLatestVersionAsync("other"));
    }

    [Fact]
    public async Task ListByDefinition_returns_only_that_definitions_versions()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0"), Version("v2", "def1", "2.0.0"), Version("v3", "def2", "1.0.0")]);
        using (fixture.Raw) Assert.Equal(["v1", "v2"], (await fixture.Versions.ListByDefinitionAsync("def1")).Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Exists_is_true_for_known_sort_key()
    {
        var version = Version("v1", "def1", "1.0.0");
        var fixture = Seeded([version]);
        using (fixture.Raw) Assert.True(await fixture.Versions.ExistsAsync("def1", version.SemVerSortKey));
    }

    [Fact]
    public async Task Exists_is_false_for_unknown_sort_key()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0")]);
        using (fixture.Raw) Assert.False(await fixture.Versions.ExistsAsync("def1", "9999999999"));
    }

    [Fact]
    public async Task Version_reads_use_their_exact_named_routes_and_result_operations()
    {
        var fixture = Seeded([Version("v1", "def1", "1.0.0")]);
        using (fixture.Raw)
        {
            Assert.NotNull(await fixture.Versions.FindLatestVersionAsync("def1"));
            var latest = Assert.Single(fixture.Raw.Queries);
            Assert.Equal(WorkflowsDesignStorageManifest.LatestVersionByDefinitionIndex, latest.IndexName);
            Assert.Equal(
                [
                    WorkflowsDesignStorageManifest.VersionSemVerSortKeyField,
                    WorkflowsDesignStorageManifest.VersionIdField
                ],
                latest.Request.Order.Select(term => term.Column.Name));
            Assert.Single(await fixture.Versions.ListByDefinitionAsync("def1"));
            var list = Assert.Single(fixture.Raw.Queries.Skip(1));
            Assert.Equal(WorkflowsDesignStorageManifest.VersionByDefinitionIndex, list.IndexName);
            Assert.Equal(
                [
                    WorkflowsDesignStorageManifest.VersionDefinitionIdField,
                    WorkflowsDesignStorageManifest.VersionSemVerSortKeyField,
                    WorkflowsDesignStorageManifest.VersionIdField
                ],
                list.Request.Order.Select(term => term.Column.Name));
            Assert.True(await fixture.Versions.ExistsAsync("def1", Version("v1", "def1", "1.0.0").SemVerSortKey));
            var exists = Assert.Single(fixture.Raw.Queries.Skip(2));
            Assert.Equal(WorkflowsDesignStorageManifest.VersionByDefinitionAndSortKeyIndex, exists.IndexName);
            Assert.IsType<Predicate.And>(exists.Request.Where);
        }
    }

    [Fact]
    public async Task Version_reads_honor_cancellation()
    {
        var fixture = Seeded([]);
        using (fixture.Raw)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Versions.FindByIdAsync("missing", cts.Token));
        }
    }

    [Fact]
    public async Task GetAsync_throws_when_absent()
    {
        var fixture = Seeded([]);
        using (fixture.Raw)
            await Assert.ThrowsAsync<EntityNotFoundException>(() => fixture.Versions.GetAsync("missing"));
    }
}
