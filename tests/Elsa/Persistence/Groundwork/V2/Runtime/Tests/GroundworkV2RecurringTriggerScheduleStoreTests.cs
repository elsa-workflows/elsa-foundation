using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RecurringTriggerScheduleStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_v2_store_implements_the_public_recurring_schedule_contract()
    {
        Assert.Contains(
            typeof(IRecurringTriggerScheduleStore),
            typeof(GroundworkV2RecurringTriggerScheduleStore).GetInterfaces());
    }

    [Fact]
    public async Task Sqlite_round_trips_due_cas_delete_and_restart()
    {
        var first = NativeProviderRuntime.Create("sqlite", null);
        first.DeleteSqliteFilesOnDispose = false;
        var store = first.Store("tenant-a");
        var early = Schedule("artifact-a", "early", -5);
        var sameA = Schedule("artifact-a", "a", -2);
        var sameB = Schedule("artifact-a", "b", -2);
        var future = Schedule("artifact-a", "future", 10);
        var inactive = Schedule("artifact-a", "inactive", -3) with { IsActive = false };
        await store.SaveAsync(early);
        await store.SaveAsync(sameA);
        await store.SaveAsync(sameB);
        await store.SaveAsync(future);
        await store.SaveAsync(inactive);
        var retained = Schedule("artifact-retained", "node", 30);
        await store.SaveAsync(retained);

        Assert.Equal(
            [early.ScheduleId, sameA.ScheduleId, sameB.ScheduleId],
            (await store.ListDueAsync(Now, 10)).Select(schedule => schedule.ScheduleId));
        Assert.Equal(
            [early.ScheduleId, sameA.ScheduleId],
            (await store.ListDueAsync(Now, 2)).Select(schedule => schedule.ScheduleId));

        Assert.False(await store.TryAdvanceAsync(early.ScheduleId, Now.AddMinutes(-4), Now));
        Assert.True(await store.TryAdvanceAsync(early.ScheduleId, early.NextOccurrence, Now));
        Assert.False(await store.TryAdvanceAsync(early.ScheduleId, early.NextOccurrence, Now));
        Assert.Equal(Now, (await store.FindAsync(early.ScheduleId))!.NextOccurrence);

        await store.DeleteByArtifactAsync("artifact-a");
        Assert.Equal([retained.ScheduleId],
            (await store.ListDueAsync(Now.AddHours(1), 10)).Select(schedule => schedule.ScheduleId));
        await store.DeleteAsync("missing-schedule");

        var database = first.SqlitePath!;
        await first.DisposeAsync();

        // A second store instance over the same provider proves that the schedule survives adapter restart.
        await using var restarted = NativeProviderRuntime.Create("sqlite", database);
        var restartedStore = restarted.Store("tenant-a");
        Assert.Null(await restartedStore.FindAsync(early.ScheduleId));
        Assert.Equal(retained, await restartedStore.FindAsync(retained.ScheduleId));
    }

    [Fact]
    public async Task Sqlite_publication_lifecycle_uses_bounded_pages_and_cleans_projection_state()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var old = Schedule("artifact-old", "old", 1, "pub-old", "slot-a");
        var oldSecond = Schedule("artifact-old", "old-2", 2, "pub-old", "slot-a");
        var replacement = Schedule("artifact-new", "new", 1, "pub-new", "slot-b");

        await store.PreparePublicationAsync("pub-old", [old, oldSecond]);
        await store.PreparePublicationAsync("pub-new", [replacement]);
        var firstPage = await store.ListByPublicationPageAsync(
            new RecurringTriggerSchedulePublicationPageQuery("pub-old", 1));
        Assert.Single(firstPage.Items);
        Assert.NotNull(firstPage.NextContinuationToken);
        var secondPage = await store.ListByPublicationPageAsync(
            new RecurringTriggerSchedulePublicationPageQuery("pub-old", 1, firstPage.NextContinuationToken));
        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextContinuationToken);
        Assert.All((await store.ListByPublicationAsync("pub-old")), schedule => Assert.False(schedule.IsActive));

        await store.ActivatePublicationAsync("pub-old", null);
        Assert.Equal(2, (await store.ListDueAsync(Now.AddHours(1), 10)).Count);
        await store.ActivatePublicationAsync("pub-new", "pub-old");
        Assert.True(Assert.Single(await store.ListByPublicationAsync("pub-new")).IsActive);
        Assert.All(await store.ListByPublicationAsync("pub-old"), schedule => Assert.False(schedule.IsActive));

        await store.DeleteByPublicationAsync("pub-new");
        Assert.Empty(await store.ListByPublicationAsync("pub-new"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivatePublicationAsync("pub-new", null).AsTask());
    }

    [Fact]
    public async Task Active_publication_can_be_replaced_after_its_runtime_cursor_advances()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var old = Schedule("artifact-old", "node-old", -1, "pub-old", "slot-a");
        var replacement = Schedule("artifact-new", "node-new", 1, "pub-new", "slot-b");

        await store.PreparePublicationAsync("pub-old", [old]);
        await store.ActivatePublicationAsync("pub-old", null);
        var advanced = old.NextOccurrence.AddMinutes(5);
        Assert.True(await store.TryAdvanceAsync(old.ScheduleId, old.NextOccurrence, advanced));

        await store.PreparePublicationAsync("pub-new", [replacement]);
        await store.ActivatePublicationAsync("pub-new", "pub-old");

        Assert.True(Assert.Single(await store.ListByPublicationAsync("pub-new")).IsActive);
        var deactivated = Assert.Single(await store.ListByPublicationAsync("pub-old"));
        Assert.False(deactivated.IsActive);
        Assert.Equal(advanced, deactivated.NextOccurrence);
    }

    [Fact]
    public async Task Exhausted_active_schedule_can_be_deleted_before_replacement_and_prepared_drift_is_refused()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var old = Schedule("artifact-old", "node-old", -1, "pub-old", "slot-a");
        var replacement = Schedule("artifact-new", "node-new", 1, "pub-new", "slot-b");

        await store.PreparePublicationAsync("pub-old", [old]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(old.ScheduleId).AsTask());
        await store.ActivatePublicationAsync("pub-old", null);
        await store.DeleteAsync(old.ScheduleId);

        await store.PreparePublicationAsync("pub-new", [replacement]);
        await store.ActivatePublicationAsync("pub-new", "pub-old");
        Assert.True(Assert.Single(await store.ListByPublicationAsync("pub-new")).IsActive);
        Assert.Empty(await store.ListByPublicationAsync("pub-old"));
    }

    [Fact]
    public async Task Direct_save_cannot_activate_or_mutate_a_prepared_publication_schedule()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var schedule = Schedule("artifact", "node", -1, "publication", "slot");

        await store.PreparePublicationAsync("publication", [schedule]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(schedule).AsTask());
        Assert.Empty(await store.ListDueAsync(Now, 10));
        Assert.False(Assert.Single(await store.ListByPublicationAsync("publication")).IsActive);

        await store.ActivatePublicationAsync("publication", null);
        Assert.True(Assert.Single(await store.ListByPublicationAsync("publication")).IsActive);
    }

    [Fact]
    public async Task Exhausted_publication_schedule_identity_cannot_be_reused_as_a_direct_schedule()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var schedule = Schedule("artifact", "node", -1, "publication", "slot");

        await store.PreparePublicationAsync("publication", [schedule]);
        await store.ActivatePublicationAsync("publication", null);
        await store.DeleteAsync(schedule.ScheduleId);

        var forged = schedule with { PublicationId = null, SlotId = null };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(forged).AsTask());
        Assert.Null(await store.FindAsync(schedule.ScheduleId));
    }

    [Fact]
    public async Task Deterministic_fan_out_schedule_identities_remain_supported()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var direct = Schedule("artifact-direct", "node", -1) with
        {
            ScheduleId = RecurringTriggerSchedule.BuildFanOutId("artifact-direct", "node", "sha256:node")
        };
        var published = Schedule("artifact-published", "node", 1, "publication", "slot") with
        {
            ScheduleId = RecurringTriggerSchedule.BuildFanOutId(
                "publication",
                "artifact-published",
                "node",
                "sha256:node")
        };

        await store.SaveAsync(direct);
        await store.PreparePublicationAsync("publication", [published]);

        Assert.Equal(direct, await store.FindAsync(direct.ScheduleId));
        Assert.Equal(published with { IsActive = false }, Assert.Single(
            await store.ListByPublicationAsync("publication")));
    }

    [Fact]
    public async Task Activation_refuses_a_tampered_nonblank_immutable_fingerprint_without_partial_writes()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var schedule = Schedule("artifact", "node", -1, "publication", "slot");
        await store.PreparePublicationAsync("publication", [schedule]);

        var stateSession = runtime.Open(
            ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind,
            "tenant-a");
        var stateId = $"recurringSchedules:{"publication".Length}:publication";
        var stateEntry = stateSession.Read(GroundworkRuntimeRowStore.Key(stateId));
        Assert.NotNull(stateEntry);
        var values = stateEntry.Values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var content = values[ElsaRuntimeV2StorageManifest.ContentField] switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            _ => throw new XunitException("Projection state content was not JSON.")
        };
        var stateJson = JsonNode.Parse(content)!.AsObject();
        stateJson["scheduleFingerprints"]!.AsObject()[schedule.ScheduleId] = new string('A', 64);
        values[ElsaRuntimeV2StorageManifest.ContentField] = stateJson.ToJsonString();
        Assert.Equal(
            WriteOutcomeStatus.Upserted,
            stateSession.Upsert(new StorageValues(values), WriteOptions.Unconditional).Status);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ActivatePublicationAsync("publication", null).AsTask());
        Assert.Empty(await store.ListDueAsync(Now, 10));
        Assert.False(Assert.Single(await store.ListByPublicationAsync("publication")).IsActive);
    }

    [Fact]
    public async Task Artifact_cleanup_removes_an_exhausted_publication_state_and_allows_reprepare()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var exhausted = Schedule("artifact", "old", -1, "publication", "slot-old");

        await store.PreparePublicationAsync("publication", [exhausted]);
        await store.ActivatePublicationAsync("publication", null);
        await store.DeleteAsync(exhausted.ScheduleId);
        await store.DeleteByArtifactAsync("artifact");

        var replacement = Schedule("artifact", "new", 1, "publication", "slot-new");
        await store.PreparePublicationAsync("publication", [replacement]);
        var prepared = Assert.Single(await store.ListByPublicationAsync("publication"));
        Assert.Equal(replacement.ScheduleId, prepared.ScheduleId);
        Assert.False(prepared.IsActive);
    }

    [Fact]
    public async Task Preparation_refuses_mixed_artifact_publications_before_any_write()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var first = Schedule("artifact-a", "first", 1, "publication", "slot-a");
        var second = Schedule("artifact-b", "second", 1, "publication", "slot-b");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.PreparePublicationAsync("publication", [first, second]).AsTask());

        Assert.Empty(await store.ListByPublicationAsync("publication"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivatePublicationAsync("publication", null).AsTask());
    }

    [Fact]
    public async Task Empty_publications_preserve_the_publication_lifecycle()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");

        await store.PreparePublicationAsync("empty-publication", []);
        await store.ActivatePublicationAsync("empty-publication", null);
        Assert.Empty(await store.ListByPublicationAsync("empty-publication"));

        await store.DeleteByPublicationAsync("empty-publication");
        await store.PreparePublicationAsync("empty-publication", []);
    }

    [Fact]
    public async Task Save_republish_is_idempotent_but_does_not_overwrite_a_competing_schedule()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var original = await store.SaveAsync(Schedule("artifact", "node", 1, expression: "PT1M"));
        var same = await store.SaveAsync(original);
        Assert.Equal(original, same);
        var replacement = await store.SaveAsync(original with { Expression = "PT2M", NextOccurrence = Now.AddMinutes(2) });
        Assert.Equal("PT2M", replacement.Expression);
        Assert.Equal("PT2M", (await store.FindAsync(original.ScheduleId))!.Expression);

        var forgedValues = GroundworkV2RecurringTriggerScheduleStorageConventions.Values(
                original with { Expression = "PT3M", NextOccurrence = Now.AddMinutes(3) })
            .Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        forgedValues[ElsaRuntimeV2StorageManifest.IdField] = original.ScheduleId;
        forgedValues[ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField] = original.ScheduleId;
        Assert.Equal(
            WriteOutcomeStatus.Upserted,
            runtime.Open(
                    ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind,
                    "tenant-a")
                .Upsert(new StorageValues(forgedValues), WriteOptions.Unconditional)
                .Status);
        Assert.Equal("PT3M", (await store.FindAsync(original.ScheduleId))!.Expression);
    }

    [Fact]
    public async Task Separator_and_long_identities_are_injective_and_forged_rows_fail_closed()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var first = Schedule("artifact:a", "node", -1);
        var second = Schedule("artifact", "a:node", -1);
        var longArtifact = new string('a', 50);
        var longNode = new string('n', 50);
        var longSchedule = Schedule(longArtifact, longNode, -1);
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(longSchedule);
        Assert.NotEqual(first.ScheduleId, second.ScheduleId);
        Assert.NotNull(await store.FindAsync(first.ScheduleId));
        Assert.NotNull(await store.FindAsync(second.ScheduleId));
        Assert.NotNull(await store.FindAsync(longSchedule.ScheduleId));

        var foreign = Schedule("foreign", "node", -1);
        var values = GroundworkV2RecurringTriggerScheduleStorageConventions.Values(foreign)
            .Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values[ElsaRuntimeV2StorageManifest.IdField] = first.ScheduleId;
        Assert.Equal(
            WriteOutcomeStatus.Upserted,
            runtime.Open(ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind, "tenant-a")
                .Upsert(new StorageValues(values), WriteOptions.Unconditional)
                .Status);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => store.FindAsync(first.ScheduleId).AsTask());
        Assert.Contains("projection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Schema_projection_scope_and_continuation_guards_are_explicit()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var schedule = Schedule("artifact", "node", -1);
        var values = GroundworkV2RecurringTriggerScheduleStorageConventions.Values(schedule)
            .Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values[ElsaRuntimeV2StorageManifest.SchemaVersionField] = "0.9.0";
        runtime.InsertRaw(new StorageValues(values), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a").FindAsync(schedule.ScheduleId).AsTask());

        var source = new CountingSource(ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind));
        foreach (var context in new[]
                 {
                     PersistenceAccessContext.Global,
                     PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test"))
                 })
        {
            var scopedStore = new GroundworkV2RecurringTriggerScheduleStore(source, new FixedAccessor(context));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scopedStore.FindAsync(schedule.ScheduleId).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => scopedStore.ListDueAsync(Now, 1).AsTask());
        }
        Assert.Equal(0, source.OpenCount);

        var row = GroundworkV2RecurringTriggerScheduleStorageConventions.Values(schedule);
        var cycling = new CyclingSession(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind),
            row,
            ["cycle", "cycle"]);
        var cycleStore = new GroundworkV2RecurringTriggerScheduleStore(
            new FakeSource(cycling, cycling.Unit),
            new FixedAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var cycleException = await Assert.ThrowsAsync<InvalidDataException>(() =>
            cycleStore.ListByPublicationAsync("publication").AsTask());
        Assert.Contains("continuation", cycleException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, cycling.QueryCount);
    }

    [Fact]
    public async Task Due_and_page_queries_use_declared_projections_and_order()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind);
        var requests = new List<QueryRequest>();
        var source = new RecordingSource(unit, requests);
        var store = new GroundworkV2RecurringTriggerScheduleStore(
            source,
            new FixedAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await store.ListDueAsync(Now, 17);
        await store.ListByArtifactPageAsync(new RecurringTriggerScheduleArtifactPageQuery("artifact", 13, "next"));
        Assert.Equal(2, requests.Count);
        var due = requests[0];
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIdField
            ],
            due.Order.Select(term => term.Column.Name));
        var conjunction = Assert.IsType<Predicate.And>(due.Where);
        Assert.Contains(conjunction.Terms, term =>
            term is Predicate.Equal equality &&
            equality.Column.Name == ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleIsActiveField);
        Assert.Contains(conjunction.Terms, term =>
            term is Predicate.Range range &&
            range.Column.Name == ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleNextOccurrenceField);
        Assert.Equal(17, due.Paging.Limit);
        Assert.Equal(13, requests[1].Paging.Limit);
        Assert.Equal("next", requests[1].Paging.ContinuationToken);
    }

    [Fact]
    public async Task Public_page_queries_normalize_invalid_provider_continuations()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind);
        var session = new InvalidCursorSession(unit);
        var store = new GroundworkV2RecurringTriggerScheduleStore(
            new FakeSource(session, unit),
            new FixedAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        var publication = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ListByPublicationPageAsync(
                new RecurringTriggerSchedulePublicationPageQuery("publication", 10, "invalid")).AsTask());
        var artifact = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ListByArtifactPageAsync(
                new RecurringTriggerScheduleArtifactPageQuery("artifact", 10, "invalid")).AsTask());

        Assert.Equal("continuationToken", publication.ParamName);
        Assert.Equal("continuationToken", artifact.ParamName);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_schedule_contract(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} recurring-schedule gate.");
        await using var runtime = NativeProviderRuntime.Create(providerName, connectionString);
        var store = runtime.Store("tenant-native");
        var schedule = Schedule("artifact-native", "node-native", -1);
        Assert.Equal(schedule, await store.SaveAsync(schedule));
        Assert.Equal(schedule, await store.FindAsync(schedule.ScheduleId));
        Assert.Equal([schedule.ScheduleId], (await store.ListDueAsync(Now, 1)).Select(item => item.ScheduleId));
        Assert.True(await store.TryAdvanceAsync(schedule.ScheduleId, schedule.NextOccurrence, Now));

        var current = Schedule("artifact-current", "node-current", -1, "pub-current", "slot-current");
        var replacement = Schedule("artifact-next", "node-next", 1, "pub-next", "slot-next");
        await store.PreparePublicationAsync("pub-current", [current]);
        await store.ActivatePublicationAsync("pub-current", null);
        var advanced = current.NextOccurrence.AddMinutes(5);
        Assert.True(await store.TryAdvanceAsync(current.ScheduleId, current.NextOccurrence, advanced));
        await store.PreparePublicationAsync("pub-next", [replacement]);
        await store.ActivatePublicationAsync("pub-next", "pub-current");
        Assert.True(Assert.Single(await store.ListByPublicationAsync("pub-next")).IsActive);
        var deactivated = Assert.Single(await store.ListByPublicationAsync("pub-current"));
        Assert.False(deactivated.IsActive);
        Assert.Equal(advanced, deactivated.NextOccurrence);

        var exhausted = Schedule("artifact-exhausted", "node-exhausted", -1, "pub-exhausted", "slot-exhausted");
        await store.PreparePublicationAsync("pub-exhausted", [exhausted]);
        await store.ActivatePublicationAsync("pub-exhausted", null);
        await store.DeleteAsync(exhausted.ScheduleId);
        await store.DeleteByArtifactAsync(exhausted.ArtifactId);
        var republished = Schedule("artifact-exhausted", "node-republished", 1, "pub-exhausted", "slot-republished");
        await store.PreparePublicationAsync("pub-exhausted", [republished]);
        Assert.False(Assert.Single(await store.ListByPublicationAsync("pub-exhausted")).IsActive);
    }

    private static RecurringTriggerSchedule Schedule(
        string artifactId,
        string nodeId,
        int nextOffsetMinutes,
        string? publicationId = null,
        string? slotId = null,
        string expression = "PT5M") =>
        new(
            publicationId is null
                ? RecurringTriggerSchedule.BuildId(artifactId, nodeId)
                : RecurringTriggerSchedule.BuildId(publicationId, artifactId, nodeId),
            artifactId,
            nodeId,
            "Timer",
            $"sha256:{nodeId}",
            RecurringScheduleKind.Interval,
            expression,
            Now.AddMinutes(nextOffsetMinutes),
            Now,
            publicationId,
            slotId,
            true);

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private sealed class NativeProviderRuntime : IAsyncDisposable
    {
        private readonly IStorageProviderConnection connection;
        private readonly DirectSource source;
        private readonly StorageUnit scheduleUnit;
        private readonly StorageUnit stateUnit;
        private bool disposed;

        private NativeProviderRuntime(
            IStorageProviderConnection connection,
            DirectSource source,
            StorageUnit scheduleUnit,
            StorageUnit stateUnit,
            string? sqlitePath)
        {
            this.connection = connection;
            this.source = source;
            this.scheduleUnit = scheduleUnit;
            this.stateUnit = stateUnit;
            SqlitePath = sqlitePath;
        }

        public string? SqlitePath { get; }

        public bool DeleteSqliteFilesOnDispose { get; set; } = true;

        public static NativeProviderRuntime Create(string providerName, string? connectionString)
        {
            string? sqlitePath = null;
            if (providerName == "sqlite")
            {
                sqlitePath = connectionString is null
                    ? Path.Combine(Path.GetTempPath(), $"elsa-recurring-v2-{Guid.NewGuid():N}.db")
                    : connectionString.Replace("Data Source=", string.Empty, StringComparison.Ordinal);
                connectionString = $"Data Source={sqlitePath}";
            }

            var connection = CreateConnection(providerName, connectionString!);
            var declaredSchedule = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind);
            var declaredState = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind);
            var scheduleUnit = PhysicalUnit(providerName, declaredSchedule);
            var stateUnit = PhysicalUnit(providerName, declaredState);
            connection.Schema.Apply(scheduleUnit);
            connection.Schema.Apply(stateUnit);
            var units = new Dictionary<string, StorageUnit>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind] = scheduleUnit,
                [ElsaRuntimeV2StorageManifest.PublicationProjectionStateDocumentKind] = stateUnit
            };
            units[scheduleUnit.Id.Value] = scheduleUnit;
            units[stateUnit.Id.Value] = stateUnit;
            return new NativeProviderRuntime(
                connection,
                new DirectSource(connection, units),
                scheduleUnit,
                stateUnit,
                sqlitePath);
        }

        public GroundworkV2RecurringTriggerScheduleStore Store(string scope) =>
            new(source, new FixedAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope(scope))));

        public IStorageSession Open(string unitId, string scope) =>
            connection.OpenSession(
                unitId == ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind
                    ? scheduleUnit
                    : stateUnit,
                StorageAccess.Scoped(new StorageScope(scope)));

        public void InsertRaw(StorageValues values, string scope)
        {
            var outcome = Open(ElsaRuntimeV2StorageManifest.RecurringTriggerScheduleDocumentKind, scope)
                .Insert(values, WriteOptions.CreateOnly);
            Assert.Equal(WriteOutcomeStatus.Inserted, outcome.Status);
        }

        public ValueTask DisposeAsync()
        {
            if (disposed)
                return ValueTask.CompletedTask;
            disposed = true;
            connection.Dispose();
            if (SqlitePath is not null && DeleteSqliteFilesOnDispose)
            {
                foreach (var path in new[]
                         {
                             SqlitePath,
                             $"{SqlitePath}-shm",
                             $"{SqlitePath}-wal",
                             $"{SqlitePath}-journal",
                             $"{SqlitePath}.schema.lock"
                         })
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }

            return ValueTask.CompletedTask;
        }

        private static StorageUnit PhysicalUnit(string providerName, StorageUnit declared)
        {
            if (providerName == "sqlite")
                return declared;

            var suffix = Guid.NewGuid().ToString("N")[..12];
            return declared with
            {
                Id = new StorageUnitId($"{declared.Id.Value}-{suffix}"),
                Name = $"{declared.Name}_{suffix}"
            };
        }
    }

    private sealed class DirectSource(
        IStorageProviderConnection connection,
        IReadOnlyDictionary<string, StorageUnit> units) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(Resolve(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(Resolve).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => Resolve(unitId);

        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        private StorageUnit Resolve(string unitId) => units[unitId];
    }

    private sealed class FixedAccessor(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }

    private sealed class CountingSource(StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            throw new InvalidOperationException("provider open should not occur");
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class FakeSource(IStorageSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) => session;

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class RecordingSource(
        StorageUnit unit,
        ICollection<QueryRequest> requests) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new EmptySession(unit, requests);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class EmptySession(StorageUnit unit, ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            requests.Add(request);
            return new QueryMaterializedResult([], null, null);
        }

        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }

    private sealed class CyclingSession(
        StorageUnit unit,
        StorageValues row,
        IReadOnlyList<string> tokens) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public int QueryCount { get; private set; }
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            new([row.Values], null, tokens[Math.Min(QueryCount++, tokens.Count - 1)]);
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }

    private sealed class InvalidCursorSession(StorageUnit unit) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            throw new FormatException("invalid continuation");
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) => providerName switch
    {
        "sqlite" => new SqliteProviderFactory().Create(connectionString),
        "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
        "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
        "mongodb" => new MongoProviderFactory().Create(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
    };
}
