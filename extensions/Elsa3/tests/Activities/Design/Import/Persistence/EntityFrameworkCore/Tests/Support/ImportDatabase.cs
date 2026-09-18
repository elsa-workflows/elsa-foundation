using System.Collections.Concurrent;
using System.Data.Common;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// One physical database holding the import ledger and both Design lanes. Provider suites supply the three
/// provider-derived contexts; everything else — schema, command, stores, and counting — is provider-neutral.
/// </summary>
internal sealed class ImportDatabase(
    Func<IInterceptor[], Elsa3ImportDbContext> import,
    Func<IInterceptor[], ActivitiesDesignDbContext> activities,
    Func<IInterceptor[], WorkflowsDesignDbContext> workflows) : IAsyncDisposable
{
    private readonly ConcurrentBag<DbContext> created = [];

    public Elsa3ImportDbContext Import(params IInterceptor[] interceptors) => Track(import(interceptors));
    public ActivitiesDesignDbContext Activities(params IInterceptor[] interceptors) => Track(activities(interceptors));
    public WorkflowsDesignDbContext Workflows(params IInterceptor[] interceptors) => Track(workflows(interceptors));

    /// <summary>
    /// Creates every enlisted context's tables in the one database. EnsureCreated creates the database and the
    /// first model; it skips a database that already has tables, so the other two models add theirs directly.
    /// </summary>
    public async Task CreateSchemaAsync()
    {
        await using (var first = import([]))
            await first.Database.EnsureCreatedAsync();
        await using (var second = activities([]))
            await second.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await using (var third = workflows([]))
            await third.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public EfReusableActivityImportCommand Command(
        IPersistenceAccessContextAccessor access,
        TimeProvider? clock = null,
        IPayloadSerializer? serializer = null,
        IInterceptor[]? importInterceptors = null,
        IInterceptor[]? activitiesInterceptors = null,
        IInterceptor[]? workflowsInterceptors = null) =>
        new(
            Import(importInterceptors ?? []),
            Activities(activitiesInterceptors ?? []),
            Workflows(workflowsInterceptors ?? []),
            access,
            serializer ?? ImportFixtures.Serializer(),
            clock);

    public EfReusableActivityImportOperationStore OperationStore(IPersistenceAccessContextAccessor access) => new(Import(), access);

    public IReusableActivityImportOperationService Service(IPersistenceAccessContextAccessor access, TimeProvider? clock = null, IReusableActivityImportCommand? command = null)
    {
        clock ??= new MutableTimeProvider(ImportFixtures.Now);
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var importer = new ReusableActivityCollectionImporter(analyzer, ImportFixtures.Materializer(), command ?? Command(access, clock));
        return new ReusableActivityImportOperationService(OperationStore(access), importer, ImportFixtures.Options(), clock);
    }

    public async Task<LedgerCounts> CountAsync()
    {
        await using var importDb = import([]);
        await using var activitiesDb = activities([]);
        await using var workflowsDb = workflows([]);
        return new LedgerCounts(
            await importDb.Collections.CountAsync(),
            await importDb.Receipts.CountAsync(),
            await importDb.DefinitionBindings.CountAsync(),
            await activitiesDb.ActivityDefinitions.CountAsync(),
            await activitiesDb.ActivityDefinitionVersions.CountAsync(),
            await activitiesDb.ActivityDefinitionAuthoringStates.CountAsync(),
            await activitiesDb.ActivityDefinitionManagementProjections.CountAsync(),
            await activitiesDb.ActivityDesignOperations.CountAsync(),
            await workflowsDb.Definitions.CountAsync(),
            await workflowsDb.Versions.CountAsync(),
            await workflowsDb.Operations.CountAsync());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in created)
            await context.DisposeAsync();
    }

    private TContext Track<TContext>(TContext context) where TContext : DbContext
    {
        created.Add(context);
        return context;
    }
}

/// <summary>Row counts of every table an import writes, across all tenants.</summary>
internal sealed record LedgerCounts(
    int Collections,
    int Receipts,
    int Bindings,
    int ActivityDefinitions,
    int ActivityVersions,
    int Authoring,
    int DefinitionProjections,
    int ActivityOperations,
    int WorkflowDefinitions,
    int WorkflowVersions,
    int WorkflowOperations)
{
    /// <summary>Everything except the collection upload, which is written before and outside an apply.</summary>
    public bool HasNoImportWrites =>
        Receipts == 0 && Bindings == 0 && ActivityDefinitions == 0 && ActivityVersions == 0 && Authoring == 0 &&
        DefinitionProjections == 0 && ActivityOperations == 0 && WorkflowDefinitions == 0 && WorkflowVersions == 0 &&
        WorkflowOperations == 0;
}

/// <summary>Fails the shared transaction's commit before or after the provider applies it.</summary>
internal sealed class CommitFailureInterceptor(bool afterCommit) : DbTransactionInterceptor
{
    public int Commits { get; private set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (!afterCommit)
            throw new IOException("simulated connection loss before the commit reached the provider");
        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Commits++;
        if (afterCommit)
            throw new IOException("simulated lost acknowledgement after the provider committed");
        return Task.CompletedTask;
    }
}

/// <summary>Cancels the caller's token while the shared transaction commits, before or after the provider applies it.</summary>
internal sealed class CommitCancellationInterceptor(CancellationTokenSource caller, bool afterCommit) : DbTransactionInterceptor
{
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (!afterCommit)
        {
            await caller.CancelAsync();
            caller.Token.ThrowIfCancellationRequested();
        }
        return result;
    }

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (afterCommit)
        {
            await caller.CancelAsync();
            caller.Token.ThrowIfCancellationRequested();
        }
    }
}

/// <summary>Throws when a save stages a row of <typeparamref name="TEntity"/>, after earlier writes in the unit.</summary>
internal sealed class SaveFailureInterceptor<TEntity>(int failures = int.MaxValue) : SaveChangesInterceptor where TEntity : class
{
    private int remaining = failures;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<TEntity>().Any(entry => entry.State is EntityState.Added or EntityState.Modified) &&
            Interlocked.Decrement(ref remaining) >= 0)
            throw new InvalidOperationException($"simulated failure while saving {typeof(TEntity).Name}");
        return ValueTask.FromResult(result);
    }
}

/// <summary>Records the physical connection and transaction behind every command a context executes.</summary>
internal sealed class CommandCapture : DbCommandInterceptor
{
    public ConcurrentQueue<(Type Context, DbConnection Connection, DbTransaction? Transaction, string Sql)> Commands { get; } = new();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    private void Record(DbCommand command, CommandEventData eventData) =>
        Commands.Enqueue((eventData.Context!.GetType(), command.Connection!, command.Transaction, command.CommandText));
}
