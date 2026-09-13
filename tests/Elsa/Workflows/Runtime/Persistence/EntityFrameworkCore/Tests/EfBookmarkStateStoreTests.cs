using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfBookmarkStateStoreTests
{
    [Fact]
    public async Task Saves_round_trips_composite_identity_scope_and_lossless_payload()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var state = State("wf-1", "same", "HttpEndpoint", "hash-a");
        await fixture.Store.SaveAsync(state);
        await fixture.Store.SaveAsync(State("wf-2", "same", "HttpEndpoint", "hash-b"));
        await fixture.Store.SaveAsync(State("wf-null", "null-payload", "Event", "hash-null") with { Payload = null });

        var result = await fixture.Store.FindAsync("wf-1", "same");
        Assert.NotNull(result);
        Assert.Equal("/orders", result!.Payload!.Value.GetProperty("path").GetString());
        Assert.Equal("é", result.Payload.Value.GetProperty("value").GetString());
        Assert.Equal(state.Metadata, result.Metadata);
        Assert.Equal(state.CreatedAt, result.CreatedAt);
        Assert.Equal(state.ExpiresAt, result.ExpiresAt);
        Assert.Equal("hash-b", (await fixture.Store.FindAsync("wf-2", "same"))!.StimulusHash);
        Assert.Null((await fixture.Store.FindAsync("wf-null", "null-payload"))!.Payload);

        await using var otherScope = await fixture.ReopenAsync("tenant-b");
        Assert.Null(await otherScope.Store.FindAsync("wf-1", "same"));
    }

    [Fact]
    public async Task Save_canonicalizes_payload_projection_without_losing_explicit_json_null()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        using var document = JsonDocument.Parse("{ \"path\" : \"/orders\", \"value\" : \"é\" }");
        var nonCanonical = State("wf-json", "non-canonical", "Event", "hash-json") with
        {
            Payload = document.RootElement.Clone()
        };
        var explicitNull = State("wf-json", "json-null", "Event", "hash-null") with
        {
            Payload = JsonSerializer.SerializeToElement<object?>(null)
        };

        await fixture.Store.SaveAsync(nonCanonical);
        await fixture.Store.SaveAsync(explicitNull);

        var roundTrip = await fixture.Store.FindAsync("wf-json", "non-canonical");
        Assert.Equal("/orders", roundTrip!.Payload!.Value.GetProperty("path").GetString());
        Assert.Equal("é", roundTrip.Payload.Value.GetProperty("value").GetString());
        var nullRoundTrip = await fixture.Store.FindAsync("wf-json", "json-null");
        Assert.True(nullRoundTrip!.Payload.HasValue);
        Assert.Equal(JsonValueKind.Null, nullRoundTrip.Payload.Value.ValueKind);
        Assert.Equal("null", (await fixture.Context.Set<BookmarkStateEntity>().SingleAsync(row => row.BookmarkId == "json-null")).PayloadJson);
    }

    [Fact]
    public async Task Distinct_malformed_utf16_scopes_remain_isolated()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-\uD800");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Event", "hash-a"));

        await using var otherScope = await fixture.ReopenAsync("tenant-\uD801");
        Assert.Null(await otherScope.Store.FindAsync("wf-1", "bm-1"));
    }

    [Fact]
    public async Task Pages_are_bounded_forward_and_stably_ordered_for_workflow_and_stimulus()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        foreach (var id in new[] { "c", "a", "b" })
            await fixture.Store.SaveAsync(State("wf-1", id, "Event", "same"));
        await fixture.Store.SaveAsync(State("wf-2", "a", "Event", "same") with { ExpiresAt = DateTimeOffset.UnixEpoch });

        var first = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 2));
        Assert.Equal(new[] { "a", "b" }, first.Items.Select(x => x.BookmarkId));
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 2, first.NextContinuationToken));
        Assert.Equal(new[] { "c" }, second.Items.Select(x => x.BookmarkId));
        Assert.Null(second.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListPageAsync(
            new BookmarkStatePageQuery("wf-2", 2, first.NextContinuationToken)).AsTask());

        var index = (IBookmarkStimulusIndex)fixture.Store;
        var stimulus = await index.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "same", 2));
        Assert.Equal(new[] { "wf-1:a", "wf-1:b" }, stimulus.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
        Assert.NotNull(stimulus.NextContinuationToken);
        var stimulusSecond = await index.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "same", 2, stimulus.NextContinuationToken));
        Assert.Equal(new[] { "wf-1:c", "wf-2:a" }, stimulusSecond.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
        Assert.Equal(DateTimeOffset.UnixEpoch, stimulusSecond.Items[^1].ExpiresAt);
        var type = await index.ListByStimulusTypePageAsync(new BookmarkStimulusTypePageQuery("Event", 2));
        Assert.Equal(new[] { "wf-1:a", "wf-1:b" }, type.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
        await Assert.ThrowsAsync<ArgumentException>(() => index.ListByStimulusTypePageAsync(
            new BookmarkStimulusTypePageQuery("OtherEvent", 2, type.NextContinuationToken)).AsTask());
    }

    [Fact]
    public async Task Workflow_continuation_cannot_be_replayed_for_another_workflow()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Store.SaveAsync(State("wf-1", "a", "Event", "one"));
        await fixture.Store.SaveAsync(State("wf-1", "b", "Event", "two"));
        await fixture.Store.SaveAsync(State("wf-2", "a", "Event", "three"));
        await fixture.Store.SaveAsync(State("wf-2", "b", "Event", "four"));

        var first = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 1));
        var other = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-2", 1));
        var tampered = JsonNode.Parse(Utf8.GetString(Convert.FromBase64String(first.NextContinuationToken!)))!.AsObject();
        var otherCursor = JsonNode.Parse(Utf8.GetString(Convert.FromBase64String(other.NextContinuationToken!)))!.AsObject();
        tampered["workflowExecutionId"] = otherCursor["workflowExecutionId"]!.GetValue<string>();
        tampered["workflowOrderKey"] = otherCursor["workflowOrderKey"]!.GetValue<string>();
        var token = Convert.ToBase64String(Utf8.GetBytes(tampered.ToJsonString()));

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListPageAsync(
            new BookmarkStatePageQuery("wf-1", 1, token)).AsTask());
    }

    [Fact]
    public async Task Paging_uses_ordinal_unicode_order_even_when_ids_share_prefixes()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        foreach (var id in new[] { "a\0\0", "a", "Z", "a\0" })
            await fixture.Store.SaveAsync(State("wf-ordinal", id, "Event", id));

        var page = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-ordinal", 10));

        Assert.Equal(new[] { "Z", "a", "a\0", "a\0\0" }, page.Items.Select(x => x.BookmarkId));
    }

    [Fact]
    public async Task Stimulus_continuations_are_bound_to_unambiguous_type_and_hash_pairs()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "a\0b", "c"));
        await fixture.Store.SaveAsync(State("wf-2", "bm-2", "a\0b", "c"));
        await fixture.Store.SaveAsync(State("wf-3", "bm-3", "a", "b\0c"));

        var first = await fixture.Store.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("a\0b", "c", 1));
        Assert.NotNull(first.NextContinuationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery("a", "b\0c", 1, first.NextContinuationToken)).AsTask());
    }

    [Fact]
    public async Task Upsert_find_delete_and_restart_preserve_exact_identity()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Event", "one"));
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Timer", "two"));
        Assert.Equal("Timer", (await fixture.Store.FindAsync("wf-1", "bm-1"))!.StimulusType);
        Assert.True(await fixture.Store.DeleteAsync("wf-1", "bm-1"));
        Assert.False(await fixture.Store.DeleteAsync("wf-1", "bm-1"));

        await using var reopened = await fixture.ReopenAsync("tenant-a");
        Assert.Null(await reopened.Store.FindAsync("wf-1", "bm-1"));
    }

    [Fact]
    public async Task Accepts_contract_maximum_identity_and_projection_lengths()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var workflow = new string('w', 128);
        var bookmark = new string('b', 128);
        var stimulusType = new string('t', 256);
        var stimulusHash = new string('h', 450);
        var metadataKey = new string('m', 450);
        await fixture.Store.SaveAsync(State(workflow, bookmark, stimulusType, stimulusHash, new Dictionary<string, string> { [metadataKey] = "value" }));

        var result = await fixture.Store.FindAsync(workflow, bookmark);
        Assert.Equal(stimulusHash, result!.StimulusHash);
        Assert.Equal("value", result.Metadata[metadataKey]);
    }

    [Fact]
    public async Task Rejects_invalid_inputs_and_continuations_before_database_access()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FindAsync(" ", "x").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FindAsync(new string('w', 129), "x").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListByStimulusPageAsync(new BookmarkStimulusPageQuery(new string('t', 257), "hash")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf", 1, "not-base64")).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf", 501)).AsTask());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Store.FindAsync("wf", "x", cancelled.Token).AsTask());
    }

    [Fact]
    public async Task Paging_observes_cancellation_before_scope_and_continuation_validation()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf", 1, "not-base64"), cancelled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Store.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "hash", 1, "not-base64"), cancelled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Store.ListByStimulusTypePageAsync(new BookmarkStimulusTypePageQuery("Event", 1, "not-base64"), cancelled.Token).AsTask());

        var globalStore = new EfBookmarkStateStore(fixture.Context, new Accessor(PersistenceAccessContext.Global));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            globalStore.ListPageAsync(new BookmarkStatePageQuery("wf"), cancelled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            globalStore.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "hash"), cancelled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            globalStore.ListByStimulusTypePageAsync(new BookmarkStimulusTypePageQuery("Event"), cancelled.Token).AsTask());
    }

    [Fact]
    public async Task Rejects_privileged_scoped_access_before_database_access()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var privilegedStore = new EfBookmarkStateStore(
            fixture.Context,
            new Accessor(PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), new PersistenceAccessPurpose("maintenance"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => privilegedStore.FindAsync("wf", "bookmark").AsTask());
    }

    [Fact]
    public async Task Clears_tracker_after_a_concurrency_failure_and_can_reuse_the_context()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new FaultingBookmarkStateDbContext(new DbContextOptionsBuilder<FaultingBookmarkStateDbContext>().UseSqlite(connection).Options);
        await using (context)
        {
            await context.Database.EnsureCreatedAsync();
            var store = new EfBookmarkStateStore(context, new Accessor("tenant-a"));
            context.FailNextSave = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(State("wf", "bookmark", "Event", "hash")).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
            await store.SaveAsync(State("wf", "bookmark", "Event", "hash"));
            Assert.NotNull(await store.FindAsync("wf", "bookmark"));
        }
    }

    [Fact]
    public async Task Provider_failures_are_normalized_at_every_store_boundary()
    {
        await using (var save = await FailingProviderFixture.CreateAsync(new ThrowingSaveInterceptor()))
        {
            var exception = await Assert.ThrowsAsync<BookmarkStateEntityFrameworkPersistenceException>(() =>
                save.Store.SaveAsync(State("wf", "save", "Event", "hash")).AsTask());
            Assert.Equal("saving", exception.Operation);
        }

        await using (var read = await FailingProviderFixture.CreateAsync(new ThrowingReaderInterceptor()))
        {
            var find = await Assert.ThrowsAsync<BookmarkStateEntityFrameworkPersistenceException>(() =>
                read.Store.FindAsync("wf", "find").AsTask());
            Assert.Equal("finding", find.Operation);

            var delete = await Assert.ThrowsAsync<BookmarkStateEntityFrameworkPersistenceException>(() =>
                read.Store.DeleteAsync("wf", "delete").AsTask());
            Assert.Equal("deleting", delete.Operation);

            var list = await Assert.ThrowsAsync<BookmarkStateEntityFrameworkPersistenceException>(() =>
                read.Store.ListPageAsync(new BookmarkStatePageQuery("wf", 1)).AsTask());
            Assert.Equal("listing", list.Operation);
        }
    }

    [Fact]
    public async Task Delete_returns_false_for_a_wrapped_transient_write_conflict()
    {
        await using var database = await FileDatabase.CreateAsync();
        await using (var seed = database.Open())
            await seed.Store.SaveAsync(State("wf", "delete-transient", "Event", "hash"));

        await using var deleting = database.Open(new ThrowingTransientDeleteInterceptor());

        Assert.False(await deleting.Store.DeleteAsync("wf", "delete-transient"));
        Assert.Empty(deleting.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Concurrent_creates_and_updates_have_one_deterministic_loser()
    {
        await using var database = await FileDatabase.CreateAsync();

        var createBarrier = new SaveBarrierInterceptor(2);
        await using (var left = database.Open(createBarrier))
        await using (var right = database.Open(createBarrier))
        {
            var outcomes = await Task.WhenAll(
                Capture(left.Store.SaveAsync(State("wf-create", "bm", "Event", "left"))),
                Capture(right.Store.SaveAsync(State("wf-create", "bm", "Event", "right"))));

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        }

        await using (var seed = database.Open())
            await seed.Store.SaveAsync(State("wf-update", "bm", "Event", "seed"));

        var updateBarrier = new SaveBarrierInterceptor(2);
        await using (var left = database.Open(updateBarrier))
        await using (var right = database.Open(updateBarrier))
        {
            var outcomes = await Task.WhenAll(
                Capture(left.Store.SaveAsync(State("wf-update", "bm", "Event", "left"))),
                Capture(right.Store.SaveAsync(State("wf-update", "bm", "Event", "right"))));

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        }
    }

    [Fact]
    public async Task Stale_delete_returns_false_and_does_not_remove_the_successor()
    {
        await using var database = await FileDatabase.CreateAsync();
        await using (var seed = database.Open())
            await seed.Store.SaveAsync(State("wf", "bm", "Event", "seed"));

        var pause = new PausingSaveInterceptor();
        await using var deleting = database.Open(pause);
        await using var updating = database.Open();
        var delete = deleting.Store.DeleteAsync("wf", "bm").AsTask();
        await pause.Entered;
        await updating.Store.SaveAsync(State("wf", "bm", "Event", "successor"));
        pause.Release();

        Assert.False(await delete);
        Assert.Equal("successor", (await updating.Store.FindAsync("wf", "bm"))!.StimulusHash);
    }

    [Fact]
    public async Task External_transaction_rollback_leaves_no_bookmark()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await fixture.Store.SaveAsync(State("wf-rollback", "bm", "Event", "hash"));
            await transaction.RollbackAsync();
        }

        fixture.Context.ChangeTracker.Clear();
        Assert.Null(await fixture.Store.FindAsync("wf-rollback", "bm"));
    }

    [Fact]
    public async Task Corrupt_projection_fails_closed_instead_of_disappearing_from_lookup()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var state = State("wf-1", "bm-1", "Event", "hash");
        await fixture.Store.SaveAsync(state);
        var id = await fixture.Context.Bookmarks.Select(row => row.Id).SingleAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_bookmark_state SET StimulusType = 'Corrupt' WHERE Id = {0}", id);
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("wf-1", "bm-1").AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => ((IBookmarkStimulusIndex)fixture.Store).ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "hash")).AsTask());
    }

    [Fact]
    public async Task Corrupt_encoded_scope_fails_closed_as_invalid_persisted_data()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Event", "hash"));
        var id = await fixture.Context.Bookmarks.Select(row => row.Id).SingleAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_bookmark_state SET ScopeKey = 'not-base64' WHERE Id = {0}", id);
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("wf-1", "bm-1").AsTask());
    }

    [Fact]
    public async Task Both_public_interfaces_resolve_to_the_same_scoped_instance()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeBookmarksEntityFrameworkCore(new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>(), scope.ServiceProvider.GetRequiredService<IBookmarkStimulusIndex>());
    }

    [Fact]
    public async Task Shell_feature_registers_the_selected_provider_and_owned_store()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new RuntimeBookmarksEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<BookmarkStateSqliteDbContext>(scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>());
        Assert.IsType<EfBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
    }

    [Fact]
    public void Shell_feature_exposes_manifest_metadata_and_the_sqlite_default()
    {
        var feature = new RuntimeBookmarksEntityFrameworkCoreFeature();
        Assert.Equal("Sqlite", feature.Provider);

        foreach (var propertyName in new[]
                 {
                     nameof(RuntimeBookmarksEntityFrameworkCoreFeature.Provider),
                     nameof(RuntimeBookmarksEntityFrameworkCoreFeature.ConnectionString),
                     nameof(RuntimeBookmarksEntityFrameworkCoreFeature.ConnectionName)
                 })
        {
            var property = typeof(RuntimeBookmarksEntityFrameworkCoreFeature).GetProperty(propertyName);
            Assert.Contains(property!.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        }

        var connectionString = typeof(RuntimeBookmarksEntityFrameworkCoreFeature)
            .GetProperty(nameof(RuntimeBookmarksEntityFrameworkCoreFeature.ConnectionString));
        var setting = Assert.Single(connectionString!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        Assert.Contains(setting.NamedArguments,
            argument => argument.MemberName == "Secret" && argument.TypedValue.Value is true);
    }

    [Fact]
    public void Shell_feature_is_derivable_and_allows_configuration_override()
    {
        Assert.False(typeof(RuntimeBookmarksEntityFrameworkCoreFeature).IsSealed);
        Assert.True(typeof(RuntimeBookmarksEntityFrameworkCoreFeature)
            .GetMethod(nameof(RuntimeBookmarksEntityFrameworkCoreFeature.ConfigureServices))!.IsVirtual);
        Assert.IsAssignableFrom<RuntimeBookmarksEntityFrameworkCoreFeature>(new DerivedFeature());
    }

    [Fact]
    public void Remove_owned_artifacts_rolls_back_public_and_auxiliary_descriptors_when_cleanup_fails()
    {
        var services = new ServiceCollection();
        var state = ServiceDescriptor.Singleton<IBookmarkStateStore, CustomBookmarkStateStore>();
        var index = ServiceDescriptor.Singleton<IBookmarkStimulusIndex, CustomBookmarkStimulusIndex>();
        var auxiliary = ServiceDescriptor.Singleton<object>(new object());
        ((ICollection<ServiceDescriptor>)services).Add(state);
        ((ICollection<ServiceDescriptor>)services).Add(index);
        ((ICollection<ServiceDescriptor>)services).Add(auxiliary);
        var backend = new BookmarkStateStoreBackend("entity-framework", state, index, collection =>
        {
            collection.Remove(auxiliary);
            collection.Remove(state);
            throw new InvalidOperationException("cleanup failed");
        });
        BookmarkStateStoreBackend.Register(services, backend);
        var snapshot = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => backend.RemoveOwnedArtifacts(services));
        Assert.Equal(snapshot, services);
        backend.EnsureOwnsRegisteredContract(services);
    }

    [Fact]
    public void Named_connection_rejects_an_empty_configured_value_when_the_context_is_resolved()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:runtime"] = "   "
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddRuntimeBookmarksEntityFrameworkCore(new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionName = "runtime"
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<BookmarkStateSqliteDbContext>());
        Assert.Contains("not found or was empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_is_idempotent_and_refuses_custom_ownership()
    {
        var options = new RuntimeBookmarksEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" };
        var repeated = new ServiceCollection();
        repeated.AddWorkflowRuntime();
        repeated.AddRuntimeBookmarksEntityFrameworkCore(options);
        repeated.AddRuntimeBookmarksEntityFrameworkCore(options);
        Assert.Single(repeated, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(repeated, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
        Assert.Throws<InvalidOperationException>(() => repeated.AddRuntimeBookmarksEntityFrameworkCore(
            new RuntimeBookmarksEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=other.db" }));

        var custom = new ServiceCollection();
        custom.AddSingleton<IBookmarkStateStore, CustomBookmarkStateStore>();
        Assert.Throws<InvalidOperationException>(() => custom.AddRuntimeBookmarksEntityFrameworkCore(options));

        var explicitInMemory = new ServiceCollection();
        explicitInMemory.AddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        Assert.Throws<InvalidOperationException>(() => explicitInMemory.AddRuntimeBookmarksEntityFrameworkCore(options));

        var customContext = new ServiceCollection();
        customContext.AddScoped<BookmarkStateSqliteDbContext>(_ => throw new NotSupportedException());
        Assert.Throws<InvalidOperationException>(() => customContext.AddRuntimeBookmarksEntityFrameworkCore(options));
        Assert.DoesNotContain(customContext, descriptor =>
            descriptor.ImplementationInstance is RuntimeBookmarksEntityFrameworkCoreOptions);

        var customIndex = new ServiceCollection();
        customIndex.AddWorkflowRuntime();
        customIndex.AddSingleton<IBookmarkStimulusIndex, CustomBookmarkStimulusIndex>();
        Assert.Throws<InvalidOperationException>(() => customIndex.AddRuntimeBookmarksEntityFrameworkCore(options));

        var defaultIndex = new ServiceCollection();
        defaultIndex.AddWorkflowRuntime();
        BookmarkStateStoreBackend.TryRegisterDefaultStimulusIndex(defaultIndex);
        defaultIndex.AddRuntimeBookmarksEntityFrameworkCore(options);
        Assert.Single(defaultIndex, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(defaultIndex, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));

    }

    [Fact]
    public void Combined_runtime_ef_registration_rejects_incompatible_options_in_either_order()
    {
        var bookmarks = new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=bookmarks.db"
        };
        var artifacts = new RuntimeArtifactsEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=artifacts.db"
        };

        var bookmarksFirst = new ServiceCollection();
        bookmarksFirst.AddWorkflowRuntime();
        bookmarksFirst.AddRuntimeBookmarksEntityFrameworkCore(bookmarks);
        Assert.Throws<InvalidOperationException>(() => bookmarksFirst.AddRuntimeArtifactsEntityFrameworkCore(artifacts));

        var artifactsFirst = new ServiceCollection();
        artifactsFirst.AddWorkflowRuntime();
        artifactsFirst.AddRuntimeArtifactsEntityFrameworkCore(artifacts);
        Assert.Throws<InvalidOperationException>(() => artifactsFirst.AddRuntimeBookmarksEntityFrameworkCore(bookmarks));
    }

    [Fact]
    public void Combined_runtime_ef_registration_rejects_different_default_connections_in_either_order()
    {
        foreach (var register in new[] { "bookmarks-first", "artifacts-first" })
        {
            var services = new ServiceCollection();
            services.AddWorkflowRuntime();
            if (register == "bookmarks-first")
            {
                services.AddRuntimeBookmarksEntityFrameworkCore(new());
                Assert.Throws<InvalidOperationException>(() => services.AddRuntimeArtifactsEntityFrameworkCore(new()));
            }
            else
            {
                services.AddRuntimeArtifactsEntityFrameworkCore(new());
                Assert.Throws<InvalidOperationException>(() => services.AddRuntimeBookmarksEntityFrameworkCore(new()));
            }
        }
    }

    [Fact]
    public async Task Combined_runtime_ef_registration_accepts_matching_options_in_either_order()
    {
        var bookmarks = new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=shared.db"
        };
        var artifacts = new RuntimeArtifactsEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=shared.db"
        };

        foreach (var register in new[] { "bookmarks-first", "artifacts-first" })
        {
            var services = new ServiceCollection();
            services.AddWorkflowRuntime();
            if (register == "bookmarks-first")
            {
                services.AddRuntimeBookmarksEntityFrameworkCore(bookmarks);
                services.AddRuntimeArtifactsEntityFrameworkCore(artifacts);
            }
            else
            {
                services.AddRuntimeArtifactsEntityFrameworkCore(artifacts);
                services.AddRuntimeBookmarksEntityFrameworkCore(bookmarks);
            }

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            Assert.IsAssignableFrom<BookmarkStateDbContext>(scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>());
        }
    }

    private static async Task<Exception?> Capture(ValueTask<BookmarkState> operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static BookmarkState State(string workflow, string bookmark, string stimulusType, string hash, Dictionary<string, string>? metadata = null) => new(
        bookmark, workflow, "activity-1", "node-1", "resume-1", stimulusType, hash,
        JsonSerializer.SerializeToElement(new { path = "/orders", value = "é" }),
        metadata ?? new Dictionary<string, string> { ["tag"] = "one", ["unicode"] = "é" },
        new DateTimeOffset(2026, 9, 13, 10, 15, 16, 123, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 9, 14, 10, 15, 16, 123, TimeSpan.FromHours(-5)));

    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    private sealed class DerivedFeature : RuntimeBookmarksEntityFrameworkCoreFeature
    {
        public override void ConfigureServices(IServiceCollection services) { }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string scope;
        public BookmarkStateSqliteDbContext Context { get; }
        public EfBookmarkStateStore Store { get; }

        private Fixture(SqliteConnection connection, string scope, BookmarkStateSqliteDbContext context)
        {
            this.connection = connection;
            this.scope = scope;
            Context = context;
            Store = new EfBookmarkStateStore(context, new Accessor(scope));
        }

        public static async Task<Fixture> CreateAsync(string scope)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, scope, context);
        }

        public async Task<Fixture> ReopenAsync(string requestedScope)
        {
            await Context.DisposeAsync();
            var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            return new Fixture(connection, requestedScope, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public Accessor(string value) : this(PersistenceAccessContext.Scoped(new PersistenceScope(value))) { }

        public Accessor(PersistenceAccessContext current) => Current = current;

        public PersistenceAccessContext Current { get; }
    }

    private sealed class FaultingBookmarkStateDbContext(DbContextOptions<FaultingBookmarkStateDbContext> options) : BookmarkStateDbContext(options)
    {
        public bool FailNextSave { get; set; }

        protected override void ConfigureProvider(ModelBuilder modelBuilder) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new DbUpdateConcurrencyException("test concurrency failure");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class FileDatabase : IAsyncDisposable
    {
        private FileDatabase(string path) => Path = path;

        private string Path { get; }

        public static async Task<FileDatabase> CreateAsync()
        {
            var database = new FileDatabase(System.IO.Path.Join(System.IO.Path.GetTempPath(), $"elsa-runtime-bookmarks-{Guid.NewGuid():N}.db"));
            await using var fixture = database.Open();
            await fixture.Context.Database.EnsureCreatedAsync();
            return database;
        }

        public FileFixture Open(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>()
                .UseSqlite($"Data Source={Path}")
                .AddInterceptors(interceptor is null ? [] : [interceptor])
                .Options;
            var context = new BookmarkStateSqliteDbContext(options);
            return new FileFixture(context, new EfBookmarkStateStore(context, new Accessor("tenant-a")));
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(Path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FileFixture(BookmarkStateSqliteDbContext context, EfBookmarkStateStore store) : IAsyncDisposable
    {
        public BookmarkStateSqliteDbContext Context { get; } = context;
        public EfBookmarkStateStore Store { get; } = store;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class SaveBarrierInterceptor(int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int remaining = participants;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref remaining) == 0)
                release.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class PausingSaveInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;
        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("provider save failure");
    }

    private sealed class ThrowingTransientDeleteInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException(
                "wrapped provider delete failure",
                new SqliteException("database is locked", 5, 5));
    }

    private sealed class ThrowingReaderInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result) =>
            throw new SqliteException("provider read failure", 1, 1);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            throw new SqliteException("provider read failure", 1, 1);
    }

    private sealed class FailingProviderFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly BookmarkStateSqliteDbContext context;
        public EfBookmarkStateStore Store { get; }

        private FailingProviderFixture(SqliteConnection connection, BookmarkStateSqliteDbContext context)
        {
            this.connection = connection;
            this.context = context;
            Store = new EfBookmarkStateStore(context, new Accessor("tenant-a"));
        }

        public static async Task<FailingProviderFixture> CreateAsync(IInterceptor interceptor)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor)
                .Options);
            await context.Database.EnsureCreatedAsync();
            return new FailingProviderFixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class CustomBookmarkStateStore : IBookmarkStateStore
    {
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CustomBookmarkStimulusIndex : IBookmarkStimulusIndex
    {
        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(BookmarkStimulusPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(BookmarkStimulusTypePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
