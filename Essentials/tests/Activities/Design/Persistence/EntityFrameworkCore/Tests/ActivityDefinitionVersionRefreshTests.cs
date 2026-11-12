using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivityDefinitionVersionRefreshTests
{
    [Fact]
    public async Task Sqlite_palette_hints_refresh_existing_catalog_without_changing_executable_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var stores = new EfActivityDesignStores(db);
        await ReconcileAsync(stores, CreateVersion(MessageInput()));
        var before = await ReadRowAsync(db);
        var previous = await db.ActivityDefinitionVersions.AsNoTracking().SingleAsync();
        var hinted = CreateVersion(MessageInput(), [PaletteFacet("bpmn")]);
        Assert.Equal(previous.Hash, hinted.Hash);

        await ReconcileAsync(stores, hinted);
        var after = await ReadRowAsync(db);
        var stored = await db.ActivityDefinitionVersions.AsNoTracking().SingleAsync();
        Assert.Contains("bpmn", stored.DesignFacetsSource);
        Assert.Equal(previous.Id, stored.Id);
        Assert.Equal(previous.Hash, stored.Hash);
        Assert.Equal(previous.InputsSource, stored.InputsSource);
        Assert.Equal(previous.OutputsSource, stored.OutputsSource);
        Assert.Equal(previous.DescriptorPayloadSource, stored.DescriptorPayloadSource);
        Assert.Equal(previous.SourceKind, stored.SourceKind);
        Assert.Equal(previous.SourceId, stored.SourceId);
        Assert.False(before.Token.SequenceEqual(after.Token));

        await ReconcileAsync(stores, hinted);
        var again = await ReadRowAsync(db);
        Assert.Equal(after.LastModifiedAt, again.LastModifiedAt);
        Assert.True(after.Token.SequenceEqual(again.Token));
    }

    [Fact]
    public async Task Sqlite_palette_refresh_is_idempotent_and_rejects_stale_or_non_palette_writes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var stores = new EfActivityDesignStores(db);
        await SeedAsync(db, "same-hash", [MessageInput()]);
        var previous = await db.ActivityDefinitionVersions.AsNoTracking().SingleAsync();
        var incoming = Incoming("same-hash", [MessageInput()]);
        incoming.DesignFacets = [PaletteFacet("bpmn")];
        var operation = new DesignOperationKey("palette-refresh");
        await stores.RefreshPaletteHintsAsync(operation, incoming, previous.DesignFacetsSource);
        var once = await ReadRowAsync(db);
        await stores.RefreshPaletteHintsAsync(operation, incoming, previous.DesignFacetsSource);
        var twice = await ReadRowAsync(db);
        Assert.True(once.Token.SequenceEqual(twice.Token));

        incoming.DesignFacets = [PaletteFacet("flowchart")];
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stores.RefreshPaletteHintsAsync(operation, incoming, previous.DesignFacetsSource));
        incoming.DesignFacets = [new ActivityDesignFacet("elsa.other", "1", JsonSerializer.SerializeToElement(new { changed = true }))];
        await Assert.ThrowsAsync<InvalidOperationException>(() => stores.RefreshPaletteHintsAsync(operation, incoming, previous.DesignFacetsSource));
        Assert.Contains("bpmn", (await db.ActivityDefinitionVersions.AsNoTracking().SingleAsync()).DesignFacetsSource);
    }

    [Fact]
    public async Task Sqlite_input_label_and_help_refresh_preserves_the_existing_version_reference()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var stores = new EfActivityDesignStores(db);
        await ReconcileAsync(stores, CreateVersion(MessageInput()));
        var previous = await db.ActivityDefinitionVersions.AsNoTracking().SingleAsync();
        await ReconcileAsync(stores, CreateVersion(MessageInput() with { DisplayName = "Start new workflows", Description = "Starts a new instance." }, [PaletteFacet("bpmn")]));
        var stored = await db.ActivityDefinitionVersions.AsNoTracking().SingleAsync();
        Assert.Equal(previous.Id, stored.Id);
        Assert.Equal(previous.DescriptorPayloadSource, stored.DescriptorPayloadSource);
        Assert.NotEqual(previous.Hash, stored.Hash);
        Assert.Contains("Start new workflows", stored.InputsSource);
        Assert.Contains("bpmn", stored.DesignFacetsSource);
    }

    private static ActivityDesignFacet PaletteFacet(string mode) => new("elsa.activity-palette", "1",
        JsonSerializer.SerializeToElement(new { containerModes = new[] { mode } }));

    [Fact]
    public async Task Sqlite_tier_only_reconcile_rewrites_hash_inputs_token_and_timestamp_then_is_a_noop()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var stores = new EfActivityDesignStores(db);
        var unset = MessageInput();

        await ReconcileAsync(stores, CreateVersion(unset));
        var before = await ReadRowAsync(db);
        Assert.Null(Assert.Single(before.Inputs).Tier);
        Assert.False(string.IsNullOrWhiteSpace(before.Hash));

        await Task.Delay(TimeSpan.FromMilliseconds(5));
        await ReconcileAsync(stores, CreateVersion(unset with { Tier = ActivityInputTiers.Optional }));
        var refreshed = await ReadRowAsync(db);
        Assert.Equal(ActivityInputTiers.Optional, Assert.Single(refreshed.Inputs).Tier);
        Assert.NotEqual(before.Hash, refreshed.Hash);
        Assert.NotEqual(before.LastModifiedAt, refreshed.LastModifiedAt);
        Assert.False(before.Token.SequenceEqual(refreshed.Token));

        await ReconcileAsync(stores, CreateVersion(unset with { Tier = ActivityInputTiers.Optional }));
        var again = await ReadRowAsync(db);
        Assert.Equal(refreshed.Hash, again.Hash);
        Assert.Equal(ActivityInputTiers.Optional, Assert.Single(again.Inputs).Tier);
        Assert.Equal(refreshed.LastModifiedAt, again.LastModifiedAt);
        Assert.True(refreshed.Token.SequenceEqual(again.Token));
    }

    [Fact]
    public async Task Sqlite_stale_expected_hash_succeeds_when_the_row_already_has_the_incoming_hash()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var stores = new EfActivityDesignStores(db);
        await SeedAsync(db, hash: "old-hash", inputs: [MessageInput()]);
        var incoming = Incoming("new-hash", [MessageInput() with { Tier = ActivityInputTiers.Optional }]);

        await stores.RefreshAsync(new DesignOperationKey("first-writer"), incoming, "old-hash");
        Assert.Equal("new-hash", (await ReadRowAsync(db)).Hash);

        await stores.RefreshAsync(new DesignOperationKey("stale-winner"), incoming, "old-hash");
        var stored = await ReadRowAsync(db);
        Assert.Equal("new-hash", stored.Hash);
        Assert.Equal(ActivityInputTiers.Optional, Assert.Single(stored.Inputs).Tier);
    }

    [Fact]
    public async Task Sqlite_stale_expected_hash_throws_when_the_row_has_a_different_hash()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var stores = new EfActivityDesignStores(db);
        await SeedAsync(db, hash: "old-hash", inputs: [MessageInput()]);
        await stores.RefreshAsync(
            new DesignOperationKey("first-writer"),
            Incoming("new-hash", [MessageInput() with { Tier = ActivityInputTiers.Optional }]),
            "old-hash");

        var ex = await Assert.ThrowsAsync<ActivityVersionHashMismatchException>(() =>
            stores.RefreshAsync(
                new DesignOperationKey("stale-conflict"),
                Incoming("other-hash", [MessageInput() with { Tier = ActivityInputTiers.Setting }]),
                "old-hash"));

        Assert.Equal("new-hash", ex.PersistedHash);
        Assert.Equal("other-hash", ex.IncomingHash);
        var stored = await ReadRowAsync(db);
        Assert.Equal("new-hash", stored.Hash);
        Assert.Equal(ActivityInputTiers.Optional, Assert.Single(stored.Inputs).Tier);
    }

    private static async Task ReconcileAsync(EfActivityDesignStores stores, IActivityDefinitionVersion version)
    {
        await new ActivityVersionReconciler(
            NullLogger<ActivityVersionReconciler>.Instance,
            new DefaultActivityDefinitionHasher(),
            new DirectPublisher(version),
            Microsoft.Extensions.Options.Options.Create(new ActivityVersionReconcilerOptions()),
            stores,
            stores,
            stores,
            stores,
            stores).Reconcile(CancellationToken.None);
    }

    private static IActivityDefinitionVersion CreateVersion(InputDefinition input, IReadOnlyCollection<ActivityDesignFacet>? facets = null)
    {
        var identities = new SequentialIdentityGenerator();
        var definition = new ActivityDefinitionFactory(identities).Create(
            "Acme.Activities.Greet",
            "Acme",
            description: "same",
            id: "def-greet");
        return new ActivityDefinitionVersionFactory(identities, new DefaultActivityDefinitionHasher()).Create(
            definition,
            "1.0.0",
            "elsa.clr-activity",
            "1",
            "elsa.clr-activity",
            "1",
            JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            "test",
            "source",
            [input],
            [],
            facets ?? [],
            id: "ver-greet-1");
    }

    private static async Task SeedAsync(ActivitiesDesignSqliteDbContext db, string hash, InputDefinition[] inputs)
    {
        var definition = new ActivityDefinition
        {
            Id = "d1",
            TenantId = "tenant-a",
            ActivityTypeKey = "Acme.Activities.Greet",
            Category = "Acme"
        };
        var version = Incoming(hash, inputs);
        version.Definition = definition;
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionVersions.Add(version);
        version.InputsSource = JsonSerializer.Serialize(inputs, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static ActivityDefinitionVersion Incoming(string hash, InputDefinition[] inputs) =>
        new("1.0.0", "d1")
        {
            Id = "v1",
            TenantId = "tenant-a",
            ProviderKey = "provider",
            ProviderSchemaVersion = "1",
            ConsumerKey = "consumer",
            ConsumerSchemaVersion = "1",
            SourceKind = "test",
            SourceId = "v1",
            Hash = hash,
            Inputs = inputs
        };

    private static async Task<VersionRow> ReadRowAsync(ActivitiesDesignSqliteDbContext db)
    {
        db.ChangeTracker.Clear();
        var row = await db.ActivityDefinitionVersions.AsNoTracking().SingleAsync();
        var token = await db.ActivityDefinitionVersions.AsNoTracking()
            .Select(x => EF.Property<byte[]>(x, "ConcurrencyToken"))
            .SingleAsync();
        return new VersionRow(
            row.Hash,
            row.LastModifiedAt,
            token ?? [],
            JsonSerializer.Deserialize<IReadOnlyList<InputDefinition>>(
                row.InputsSource ?? "[]",
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []);
    }

    private static InputDefinition MessageInput() =>
        new(
            "message",
            "Message",
            new TypeReference("String"),
            StorageDriverType: null,
            DisplayName: "Message",
            Category: null,
            IsNullable: false);

    private sealed record VersionRow(
        string? Hash,
        DateTimeOffset LastModifiedAt,
        byte[] Token,
        IReadOnlyList<InputDefinition> Inputs);

    private sealed class DirectPublisher(IActivityDefinitionVersion version) : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is ActivityVersionsReconciling reconciling)
                reconciling.Versions.Add(version);
            return Task.CompletedTask;
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => "generated";
    }
}
