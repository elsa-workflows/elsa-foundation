using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Tests;

/// <summary>The design-store contract on SQLite, with the cases only SQLite can stage.</summary>
public sealed class EtlDefinitionStoreTests : EtlDefinitionStoreContract
{
    protected override async Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => await EtlDesignTestDatabase.CreateAsync();

    [Fact]
    public async Task Unrelated_publication_storage_failure_remains_visible_and_rolls_back_the_version()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);
        var artifact = Compile("orders", 1, pipeline);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER refuse_fixture_publication BEFORE INSERT ON elsa_etl_published_artifacts
            BEGIN SELECT RAISE(ABORT, 'fixture storage refusal'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact));
        Assert.Equal(0, (await store.FindDraftAsync(scope, "orders"))!.LastPublishedVersion);
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_fixture_publication");
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact)).Status);
    }

    [Fact]
    public async Task An_update_reports_its_own_revision_when_another_writer_advances_the_draft_before_the_reread()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        await using (var context = database.CreateContext())
            await Store(context).CreateDraftAsync(scope, "orders", EtlTestPipelines.Create("first"));
        // A second writer commits revision 3 between this writer's update and its re-read of the row.
        var interleave = new AfterFirstUpdate(async () =>
        {
            await using var other = database.CreateContext();
            Assert.Equal(EtlDefinitionWriteStatus.Updated,
                (await Store(other).UpdateDraftAsync(scope, "orders", 2, EtlTestPipelines.Create("theirs"))).Status);
        });

        EtlDefinitionWriteResult mine;
        await using (var context = database.CreateContext(interleave))
            mine = await Store(context).UpdateDraftAsync(scope, "orders", 1, EtlTestPipelines.Create("mine"));

        Assert.Equal((EtlDefinitionWriteStatus.Updated, 2L, "mine"), (mine.Status, mine.Draft!.Revision, mine.Draft.Pipeline.Mapping[0].Name));
        await using (var context = database.CreateContext())
            Assert.Equal(EtlDefinitionWriteStatus.Conflict,
                (await Store(context).UpdateDraftAsync(scope, "orders", mine.Draft.Revision, EtlTestPipelines.Create("again"))).Status);
    }

    [Fact]
    public async Task A_publication_that_runs_out_of_attempts_leaves_nothing_for_a_later_save_to_publish()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var busy = new BusyOnPublicationInsert();
        await using var context = database.CreateContext(busy);
        var store = Store(context);
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);

        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
            await store.PublishAsync(scope, "orders", created.Draft!.Revision, Compile("orders", 1, pipeline)));
        busy.Enabled = false;
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(scope, "unrelated", pipeline)).Status);

        Assert.True(busy.Attempts > 1);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, Compile("orders", 1, pipeline))).Status);
    }

    [Fact]
    public async Task A_publication_whose_rollback_throws_leaves_nothing_for_a_later_save_to_publish()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        using var cancellation = new CancellationTokenSource();
        // The caller's token is cancelled while the refused insert fails, so the rollback that follows throws.
        await using var context = database.CreateContext(new CancelOnCommandFailure(cancellation));
        var store = Store(context);
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER refuse_publication_and_cancel BEFORE INSERT ON elsa_etl_published_artifacts
            BEGIN SELECT RAISE(ABORT, 'fixture storage refusal'); END;
            """);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.PublishAsync(scope, "orders", created.Draft!.Revision, Compile("orders", 1, pipeline), cancellation.Token));
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_publication_and_cancel");
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(scope, "unrelated", pipeline)).Status);

        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, Compile("orders", 1, pipeline))).Status);
    }

    [Fact]
    public async Task A_cancelled_draft_create_leaves_nothing_for_a_later_save_and_can_be_retried()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        using var cancellation = new CancellationTokenSource();
        await using var context = database.CreateContext(new CancelOnDraftInsert(cancellation));
        var store = Store(context);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.CreateDraftAsync(scope, "orders", pipeline, cancellation.Token));
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(scope, "unrelated", pipeline)).Status);

        Assert.Null(await store.FindDraftAsync(scope, "orders"));
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await store.CreateDraftAsync(scope, "orders", pipeline)).Status);
    }

    [Fact]
    public async Task A_store_or_catalog_read_that_meets_a_busy_database_is_read_again()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var busy = new BusyOnNextDraftRead();
        await using var context = database.CreateContext(busy);
        Assert.Equal(EtlDefinitionWriteStatus.Created, (await Store(context).CreateDraftAsync(scope, "orders", EtlTestPipelines.Create())).Status);

        busy.Arm();
        Assert.NotNull(await Store(context).FindDraftAsync(scope, "orders"));
        busy.Arm();
        Assert.Single(await new EfEtlDefinitionCatalog(context).ListDefinitionsAsync(scope, null, 10));

        Assert.Equal(2, busy.Refusals);
    }

    /// <summary>Once armed, answers the next draft read as SQLite does a locked database, a lost race the store retries.</summary>
    private sealed class BusyOnNextDraftRead : DbCommandInterceptor
    {
        private bool _armed;
        public int Refusals { get; private set; }

        public void Arm() => _armed = true;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_armed && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("\"elsa_etl_definition_drafts\"", StringComparison.Ordinal))
            {
                _armed = false;
                Refusals++;
                throw new SqliteException("database is locked", 5);
            }
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Cancels a token as the first draft insert executes, as a caller giving up mid-write.</summary>
    private sealed class CancelOnDraftInsert(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        private bool _fired;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            CancelOnce(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            CancelOnce(command);
            return ValueTask.FromResult(result);
        }

        private void CancelOnce(DbCommand command)
        {
            if (_fired || !command.CommandText.Contains("INSERT INTO \"elsa_etl_definition_drafts\"", StringComparison.Ordinal))
                return;
            _fired = true;
            cancellation.Cancel();
        }
    }

    /// <summary>Cancels a token when a command fails, as a caller giving up while the store handles the failure.</summary>
    private sealed class CancelOnCommandFailure(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    /// <summary>Runs an action once, right after the first UPDATE this context sends.</summary>
    private sealed class AfterFirstUpdate(Func<Task> action) : DbCommandInterceptor
    {
        private bool _fired;

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!_fired && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                _fired = true;
                await action();
            }
            return result;
        }
    }

    /// <summary>Answers every publication insert as SQLite does a locked database, a lost race the store retries.</summary>
    private sealed class BusyOnPublicationInsert : DbCommandInterceptor
    {
        public bool Enabled { get; set; } = true;
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"elsa_etl_published_artifacts\"", StringComparison.Ordinal))
            {
                Attempts++;
                throw new SqliteException("database is locked", 5);
            }
            return ValueTask.FromResult(result);
        }
    }
}
