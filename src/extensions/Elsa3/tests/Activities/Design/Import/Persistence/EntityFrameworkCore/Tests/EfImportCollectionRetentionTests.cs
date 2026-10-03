using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support.ImportFixtures;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The retention of an uploaded collection (#2330). The stored upload holds the uploaded document verbatim, literal
/// credentials included, so it is deleted from the import ledger when its apply is decided or its lifetime runs out.
/// Run against the EF Core ledger and both EF Core Design lanes on one SQLite database.
/// </summary>
public sealed class EfImportCollectionRetentionTests : IAsyncLifetime
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private static readonly ReusableActivityImportAccessScope Scope = new(TenantA, "user-a");
    private readonly MutableAccess access = MutableAccess.Tenant(TenantA);
    private readonly MutableTimeProvider clock = new(Now);

    /// <summary>When an upload made at <see cref="Now"/> expires.</summary>
    private static DateTimeOffset Expiry => Now.Add(Options().Value.CollectionLifetime);
    private SqliteImportHarness harness = null!;

    private ImportDatabase Db => harness.Database;

    public async Task InitializeAsync() => harness = await SqliteImportHarness.CreateAsync();

    public async Task DisposeAsync() => await harness.DisposeAsync();

    [Fact]
    public async Task A_completed_apply_deletes_the_upload_and_a_replay_still_answers_from_the_receipt()
    {
        var service = Db.Service(access, clock);
        var (handle, planId) = await UploadAsync(service);

        var applied = await service.ApplyAsync(handle, planId, ["a-v1"], "consumed", Scope);

        Assert.Equal(0, (await Db.CountAsync()).Collections);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () => await service.AnalyzeAsync(handle, 0, 10, Scope));
        var replayed = await Db.Service(access, clock).ApplyAsync(handle, planId, ["a-v1"], "consumed", Scope);
        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported, replayed.Status);
        Assert.Equal(applied.ReceiptId, replayed.ReceiptId);
        Assert.Equal(applied.ReceiptId, (await service.GetStatusAsync("consumed", Scope)).ReceiptId);
    }

    [Fact]
    public async Task A_replay_deletes_the_upload_of_an_apply_that_committed_and_stopped_before_its_delete()
    {
        var stopped = Db.Service(access, clock, new StopsAfterCommit(Db.Command(access, clock)));
        var (handle, planId) = await UploadAsync(stopped);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stopped.ApplyAsync(handle, planId, ["a-v1"], "stopped", Scope));
        var committed = await Db.CountAsync();
        Assert.Equal((1, 1), (committed.Collections, committed.Receipts));

        var replayed = await Db.Service(access, clock).ApplyAsync(handle, planId, ["a-v1"], "stopped", Scope);

        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported, replayed.Status);
        Assert.Equal(0, (await Db.CountAsync()).Collections);
    }

    [Fact]
    public async Task An_apply_whose_caller_cancelled_after_the_commit_became_durable_still_deletes_the_upload()
    {
        using var caller = new CancellationTokenSource();
        var service = Db.Service(access, clock, Db.Command(access, clock, importInterceptors: [new CommitCancellationInterceptor(caller, afterCommit: true)]));
        var (handle, planId) = await UploadAsync(service);

        var applied = await service.ApplyAsync(handle, planId, ["a-v1"], "cancelled-late", Scope, caller.Token);

        Assert.True(caller.IsCancellationRequested);
        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, applied.Status);
        Assert.Equal(0, (await Db.CountAsync()).Collections);
    }

    /// <summary>
    /// An apply that ends outside the outcomes its caller can continue from refuses the upload's content. An
    /// <see cref="ArgumentException"/> stands in for such a refusal here; the credential-literal refusal of spec 188
    /// is one.
    /// </summary>
    [Fact]
    public async Task A_refusal_of_the_uploads_content_deletes_the_upload_and_reaches_the_caller_unchanged()
    {
        var refusal = new ArgumentException("The upload holds a literal on a credential input.");
        var service = Db.Service(access, clock, new ThrowingCommand(() => refusal));
        var (handle, planId) = await UploadAsync(service);

        var thrown = await Assert.ThrowsAsync<ArgumentException>(async () => await service.ApplyAsync(handle, planId, ["a-v1"], "refused", Scope));

        Assert.Same(refusal, thrown);
        var counts = await Db.CountAsync();
        Assert.Equal(0, counts.Collections);
        Assert.True(counts.HasNoImportWrites);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () => await service.AnalyzeAsync(handle, 0, 10, Scope));
    }

    [Theory]
    [InlineData("stale plan")]
    [InlineData("non-closed selection")]
    [InlineData("idempotency conflict")]
    [InlineData("identity collision")]
    [InlineData("persistence failure")]
    [InlineData("schema write refusal")]
    [InlineData("cancellation")]
    public async Task An_outcome_the_caller_can_continue_from_keeps_the_upload(string outcome)
    {
        // The first two are refused by the importer itself; the rest are what the commit port can throw.
        Exception? failure = outcome switch
        {
            "stale plan" or "non-closed selection" => null,
            "idempotency conflict" => new ReusableActivityImportIdempotencyConflictException("keeps"),
            "identity collision" => new ReusableActivityImportCollisionException("The identity is owned by different content."),
            "persistence failure" => new ReusableActivityImportPersistenceException("commit", "keeps", new IOException("connection lost")),
            "schema write refusal" => new EfSchemaWriteRefusedException("Elsa3Import", "1", "2"),
            "cancellation" => new OperationCanceledException(),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        var service = Db.Service(access, clock, failure is null ? null : new ThrowingCommand(() => failure));
        // Workflow b depends on a, so selecting b alone is a non-closed selection.
        var (handle, planId) = await UploadAsync(service, Workflow("a", "a-v1", 1, true, Leaf("root")), Workflow("b", "b-v1", 1, true, Reference("b-to-a", "a-v1")));

        var thrown = await Record.ExceptionAsync(async () => await service.ApplyAsync(
            handle,
            outcome == "stale plan" ? "stale-plan" : planId,
            outcome == "non-closed selection" ? ["b-v1"] : ["a-v1", "b-v1"],
            "keeps",
            Scope));

        if (failure is null)
            Assert.IsType<ReusableActivityImportValidationException>(thrown);
        else
            Assert.Same(failure, thrown);
        Assert.Equal(1, (await Db.CountAsync()).Collections);
        Assert.Equal(2, (await service.AnalyzeAsync(handle, 0, 10, Scope)).Items.Count);
    }

    [Fact]
    public async Task A_malformed_apply_request_is_refused_before_the_upload_is_read_and_keeps_it()
    {
        var service = Db.Service(access, clock);
        var (handle, _) = await UploadAsync(service);

        await Assert.ThrowsAsync<ArgumentException>(async () => await service.ApplyAsync(handle, " ", ["a-v1"], "blank-plan", Scope));

        Assert.Equal(1, (await Db.CountAsync()).Collections);
    }

    /// <summary>
    /// A delete that fails never replaces the outcome the caller asked for: the receipt, the replayed receipt, the
    /// refusal and the 410 stand, the failure is logged, and the row stays for the replay or the sweep to remove.
    /// </summary>
    [Theory]
    [InlineData("completed apply")]
    [InlineData("replay")]
    [InlineData("content refusal")]
    [InlineData("expired read")]
    public async Task A_delete_that_fails_leaves_the_callers_outcome_in_place(string path)
    {
        var refusal = new ArgumentException("The upload holds a literal on a credential input.");
        ReusableActivityImportReceipt? receipt = null;
        var store = new FailingDeleteStore(Db.OperationStore(access), () => path == "replay" ? receipt : null);
        var logger = new CapturingLogger();
        var service = Service(store, new StubImporter(() => path == "content refusal" ? throw refusal : receipt!), logger);
        var (handle, _) = await UploadAsync(service);
        receipt = Receipt(handle);
        if (path == "expired read")
            clock.Advance(Options().Value.CollectionLifetime);

        var thrown = await Record.ExceptionAsync(async () => await service.ApplyAsync(handle, "plan", ["a-v1"], "keeps", Scope));

        switch (path)
        {
            case "completed apply" or "replay":
                Assert.Null(thrown);
                break;
            case "content refusal":
                Assert.Same(refusal, thrown);
                break;
            default:
                Assert.IsType<ReusableActivityImportExpiredException>(thrown);
                break;
        }
        Assert.Equal(1, (await Db.CountAsync()).Collections);
        Assert.Contains(handle, Assert.Single(logger.Errors));
    }

    /// <summary>
    /// The outcome is decided when the delete runs, so a caller that cancels after a refusal (a client that
    /// disconnects) does not leave the refused upload, and the literal it may hold, in the ledger.
    /// </summary>
    [Fact]
    public async Task A_caller_that_cancels_after_a_content_refusal_still_gets_the_upload_deleted()
    {
        using var caller = new CancellationTokenSource();
        var refusal = new ArgumentException("The upload holds a literal on a credential input.");
        var service = Service(Db.OperationStore(access), new StubImporter(() =>
        {
            caller.Cancel();
            throw refusal;
        }), new CapturingLogger());
        var (handle, planId) = await UploadAsync(service);

        var thrown = await Record.ExceptionAsync(async () => await service.ApplyAsync(handle, planId, ["a-v1"], "cancelled", Scope, caller.Token));

        Assert.Same(refusal, thrown);
        Assert.Equal(0, (await Db.CountAsync()).Collections);
    }

    [Fact]
    public async Task The_read_that_finds_an_upload_expired_deletes_it()
    {
        var service = Db.Service(access, clock);
        var (handle, _) = await UploadAsync(service);
        clock.Advance(Options().Value.CollectionLifetime);

        await Assert.ThrowsAsync<ReusableActivityImportExpiredException>(async () => await service.AnalyzeAsync(handle, 0, 10, Scope));

        Assert.Equal(0, (await Db.CountAsync()).Collections);
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () => await service.AnalyzeAsync(handle, 0, 10, Scope));
    }

    [Fact]
    public async Task A_collection_is_deleted_only_in_its_exact_tenant_and_user_scope()
    {
        var (handle, _) = await UploadAsync(Db.Service(access, clock));
        var store = Db.OperationStore(access);

        Assert.False(await store.DeleteCollectionAsync(handle, new(TenantA, "user-b")));
        Assert.False(await store.DeleteCollectionAsync("another-handle", Scope));
        await Assert.ThrowsAsync<ReusableActivityImportNotFoundException>(async () => await store.DeleteCollectionAsync(handle, new(TenantB, "user-a")));
        Assert.Equal(1, (await Db.CountAsync()).Collections);

        Assert.True(await store.DeleteCollectionAsync(handle, Scope));
        Assert.False(await store.DeleteCollectionAsync(handle, Scope));
        Assert.Equal(0, (await Db.CountAsync()).Collections);
    }

    [Fact]
    public async Task The_expiry_sweep_deletes_an_upload_at_its_expiry_and_not_before()
    {
        var handle = await UploadAsync(TenantA, "user-a");
        var store = Db.OperationStore(access);

        Assert.Equal(0, await store.DeleteExpiredCollectionsAsync(Expiry.AddTicks(-1), 10));
        Assert.Equal([handle], await StoredAsync(handle));

        Assert.Equal(1, await store.DeleteExpiredCollectionsAsync(Expiry, 10));
        Assert.Empty(await StoredAsync(handle));
    }

    [Fact]
    public async Task The_expiry_sweep_deletes_only_the_ambient_persistence_scopes_uploads()
    {
        var foreign = await UploadAsync(TenantB, "user-a");

        Assert.Equal(0, await Db.OperationStore(access).DeleteExpiredCollectionsAsync(Expiry, 10));
        Assert.Equal([foreign], await StoredAsync(foreign));

        Assert.Equal(1, await Db.OperationStore(MutableAccess.Tenant(TenantB)).DeleteExpiredCollectionsAsync(Expiry, 10));
    }

    [Fact]
    public async Task The_expiry_sweep_leaves_a_row_at_a_schema_version_this_build_does_not_read()
    {
        var skewed = await UploadAsync(TenantA, "user-a");
        await using (var import = Db.Import())
            await import.Collections.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, "99.0.0"));

        Assert.Equal(0, await Db.OperationStore(access).DeleteExpiredCollectionsAsync(Expiry, 10));

        Assert.Equal([skewed], await StoredAsync(skewed));
    }

    [Fact]
    public async Task The_expiry_sweep_deletes_every_users_uploads_oldest_first_in_bounded_batches()
    {
        var oldest = await UploadAsync(TenantA, "user-a");
        clock.Advance(TimeSpan.FromMinutes(10));
        var newer = await UploadAsync(TenantA, "user-b");
        var asOf = Expiry.AddMinutes(10);
        var store = Db.OperationStore(access);

        Assert.Equal(1, await store.DeleteExpiredCollectionsAsync(asOf, 1));
        Assert.Equal([newer], await StoredAsync(oldest, newer));

        Assert.Equal(1, await store.DeleteExpiredCollectionsAsync(asOf, 10));
        Assert.Equal(0, await store.DeleteExpiredCollectionsAsync(asOf, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.DeleteExpiredCollectionsAsync(asOf, 0));
    }

    [Fact]
    public async Task The_recurring_sweep_deletes_expired_uploads_in_every_persistence_scope_and_keeps_unexpired_ones()
    {
        var expiredA = await UploadAsync(TenantA, "user-a");
        var expiredB = await UploadAsync(TenantB, "user-b");
        clock.Advance(Options().Value.CollectionLifetime);
        var unexpired = await UploadAsync(TenantA, "user-a");
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceScopeSource>(new TenantScopes(TenantA, TenantB));
        services.AddPersistenceCore();
        services.AddScoped<IReusableActivityImportOperationStore>(provider =>
            Db.OperationStore(provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
        await using var provider = services.BuildServiceProvider();
        var sweep = new ExpiredImportCollectionSweepTask(
            provider.GetRequiredService<IPersistenceScopeRunner>(),
            Options(),
            clock,
            NullLogger<ExpiredImportCollectionSweepTask>.Instance);

        await sweep.ExecuteAsync(CancellationToken.None);

        Assert.Equal([unexpired], await StoredAsync(expiredA, expiredB, unexpired));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(15, 0)]
    public void The_recurring_sweep_refuses_a_sweep_interval_or_batch_size_that_is_not_positive(int intervalMinutes, int batchSize)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ReusableActivityImportOptions
        {
            ExpiredCollectionSweepInterval = TimeSpan.FromMinutes(intervalMinutes),
            ExpiredCollectionSweepBatchSize = batchSize
        });

        Assert.Throws<InvalidOperationException>(() =>
            new ExpiredImportCollectionSweepTask(new NoScopes(), options, clock, NullLogger<ExpiredImportCollectionSweepTask>.Instance));
    }

    private ReusableActivityImportOperationService Service(IReusableActivityImportOperationStore store, IReusableActivityCollectionImporter importer, CapturingLogger logger) =>
        new(store, importer, Options(), clock, logger);

    /// <summary>The receipt of applying <c>a-v1</c> from <paramref name="handle"/> under key <c>keeps</c>, so a replay of that request matches it.</summary>
    private static ReusableActivityImportReceipt Receipt(string handle) => new(
        "receipt", handle, "plan", "keeps",
        ReusableActivityImportOperationService.SelectionFingerprint(handle, "plan", ["a-v1"], Scope),
        Scope, ReusableActivityImportReceiptStatus.Applied, Now, []);

    private async Task<(string Handle, string PlanId)> UploadAsync(IReusableActivityImportOperationService service, params Elsa3WorkflowDefinition[] definitions)
    {
        var upload = await service.UploadAsync(Json(definitions.Length == 0 ? [Workflow("a", "a-v1", 1, true, Leaf("root"))] : definitions), null, Scope);
        return (upload.CollectionHandle, (await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope)).PlanId);
    }

    private async Task<string> UploadAsync(string tenantId, string userId) =>
        (await Db.Service(MutableAccess.Tenant(tenantId), clock)
            .UploadAsync(Json(Workflow("a", "a-v1", 1, true, Leaf("root"))), null, new(tenantId, userId))).CollectionHandle;

    /// <summary>The <paramref name="handles"/> still in the ledger, in the order given.</summary>
    private async Task<string[]> StoredAsync(params string[] handles)
    {
        await using var import = Db.Import();
        var stored = await import.Collections.AsNoTracking().Select(row => row.HandleHash).ToListAsync();
        return handles.Where(handle => stored.Contains(EfRelationalIdentity.Hash(handle))).ToArray();
    }

    /// <summary>An importer whose apply is the given outcome; analysis is the real one, so the plan check still runs.</summary>
    private sealed class StubImporter(Func<ReusableActivityImportReceipt> apply) : IReusableActivityCollectionImporter
    {
        public ValueTask<ReusableActivityImportPlan> AnalyzeAsync(ReusableActivityImportCollection collection, CancellationToken cancellationToken = default) =>
            new ReusableActivityCollectionAnalyzer().AnalyzeAsync(collection, cancellationToken);

        public ValueTask<ReusableActivityImportApplyResult> ApplyAsync(ReusableActivityImportApplyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReusableActivityImportApplyResult(request.PlanId, [], false, apply()));
    }

    /// <summary>
    /// The real store, with a ledger whose delete fails with an unwrapped error, and with <paramref name="priorReceipt"/>
    /// as the receipt every key already has.
    /// </summary>
    private sealed class FailingDeleteStore(IReusableActivityImportOperationStore inner, Func<ReusableActivityImportReceipt?> priorReceipt) : IReusableActivityImportOperationStore
    {
        public ValueTask<bool> TryCreateCollectionAsync(ReusableActivityImportCollectionHandle collection, CancellationToken cancellationToken = default) => inner.TryCreateCollectionAsync(collection, cancellationToken);
        public ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(string handle, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => inner.FindCollectionAsync(handle, accessScope, cancellationToken);
        public ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(string idempotencyKey, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => ValueTask.FromResult(priorReceipt());
        public ValueTask<bool> DeleteCollectionAsync(string handle, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The provider connection was lost.");
        public ValueTask<int> DeleteExpiredCollectionsAsync(DateTimeOffset expiresAtOrBefore, int maxCount, CancellationToken cancellationToken = default) => inner.DeleteExpiredCollectionsAsync(expiresAtOrBefore, maxCount, cancellationToken);
    }

    /// <summary>Records the formatted message of every error the service logs.</summary>
    private sealed class CapturingLogger : ILogger<ReusableActivityImportOperationService>
    {
        public List<string> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    private sealed class ThrowingCommand(Func<Exception> failure) : IReusableActivityImportCommand
    {
        public ValueTask<ReusableActivityImportCommitResult> CommitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken = default) =>
            throw failure();
    }

    /// <summary>Commits durably and then stops, as a host does that dies between the commit and the upload's delete.</summary>
    private sealed class StopsAfterCommit(IReusableActivityImportCommand inner) : IReusableActivityImportCommand
    {
        public async ValueTask<ReusableActivityImportCommitResult> CommitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken = default)
        {
            await inner.CommitAsync(mutation, cancellationToken);
            throw new OperationCanceledException("The host stopped after the commit.");
        }
    }

    private sealed class NoScopes : IPersistenceScopeRunner
    {
        public ValueTask RunAsync(Func<PersistenceScope, PersistenceOperationScope, CancellationToken, ValueTask> operation, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class TenantScopes(params string[] tenantIds) : IPersistenceScopeSource
    {
        public ValueTask<IReadOnlyList<PersistenceScope>> GetScopesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PersistenceScope>>(tenantIds.Select(tenantId => new PersistenceScope(tenantId)).ToArray());
    }
}
