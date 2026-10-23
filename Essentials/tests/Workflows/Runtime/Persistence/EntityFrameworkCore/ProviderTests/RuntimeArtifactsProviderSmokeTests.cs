using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeArtifactsPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_runtime_artifacts_model_crud_query_and_concurrency() =>
        RuntimeArtifactsProviderSmoke.RunAsync(fixture, "PostgreSql", connection => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task PostgreSql_doomed_reference_delete_loses_to_a_restore_between_its_read_and_its_delete() =>
        RuntimeArtifactsProviderSmoke.RunDoomedDeleteRaceAsync(fixture, "PostgreSql", (connection, interceptors) => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options));

    [SkippableFact]
    public Task PostgreSql_a_guard_that_expires_during_its_delete_cannot_delete_under_a_late_lease() =>
        RuntimeArtifactsProviderSmoke.RunGuardExpiryDuringDeleteRaceAsync(fixture, "PostgreSql", (connection, interceptors) => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options));

    [SkippableFact]
    public Task PostgreSql_concurrent_root_write_lease_holders_of_one_artifact_all_succeed() =>
        RuntimeArtifactsProviderSmoke.RunConcurrentRootWriteLeaseHoldersAsync(fixture, "PostgreSql", connection => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options));

    [SkippableFact]
    public Task PostgreSql_concurrent_idempotent_executable_saves_reconcile_a_winner_before_coordination_read() =>
        RuntimeArtifactsProviderSmoke.RunExecutableCoordinationRaceAsync(fixture, "PostgreSql", (connection, interceptors) => new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options));
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeArtifactsSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_runtime_artifacts_model_crud_query_and_concurrency() =>
        RuntimeArtifactsProviderSmoke.RunAsync(fixture, "SqlServer", connection => new RuntimeSqlServerDbContext(
            new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task SqlServer_a_guard_that_expires_during_its_delete_cannot_delete_under_a_late_lease() =>
        RuntimeArtifactsProviderSmoke.RunGuardExpiryDuringDeleteRaceAsync(fixture, "SqlServer", (connection, interceptors) => new RuntimeSqlServerDbContext(
            new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(interceptors).Options));

    [SkippableFact]
    public Task SqlServer_concurrent_root_write_lease_holders_of_one_artifact_all_succeed() =>
        RuntimeArtifactsProviderSmoke.RunConcurrentRootWriteLeaseHoldersAsync(fixture, "SqlServer", connection => new RuntimeSqlServerDbContext(
            new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options));
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeArtifactsMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_runtime_artifacts_model_crud_query_and_concurrency() =>
        RuntimeArtifactsProviderSmoke.RunAsync(fixture, "MySql", connection => new RuntimeMySqlDbContext(
            new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task MySql_a_guard_that_expires_during_its_delete_cannot_delete_under_a_late_lease() =>
        RuntimeArtifactsProviderSmoke.RunGuardExpiryDuringDeleteRaceAsync(fixture, "MySql", (connection, interceptors) => new RuntimeMySqlDbContext(
            new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).AddInterceptors(interceptors).Options));

    [SkippableFact]
    public Task MySql_concurrent_root_write_lease_holders_of_one_artifact_all_succeed() =>
        RuntimeArtifactsProviderSmoke.RunConcurrentRootWriteLeaseHoldersAsync(fixture, "MySql", connection => new RuntimeMySqlDbContext(
            new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options));
}

internal static class RuntimeArtifactsProviderSmoke
{
    private const string SigningKey = "ef-runtime-provider-smoke-signing-key-32-bytes";
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, RuntimeDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var connectionString = fixture.ConnectionString;
        var scope = $"provider-artifacts-{Guid.NewGuid():N}";
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = SigningKey,
            AllowEphemeralDevelopmentKey = false
        }));

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var executable = new EfWorkflowExecutableStore(context, new FixedAccessor(scope));
            var template = new EfExecutableActivityTemplateStore(context, new FixedAccessor(scope), codec);
            var source = new EfWorkflowExecutableSourceReferenceStore(context, new FixedAccessor(scope), codec);

            await executable.SaveAsync(Executable("artifact-a"));
            Assert.NotNull(await executable.FindAsync("artifact-a"));
            await executable.SaveAsync(Executable("artifact-b"));
            var firstExecutablePage = await executable.ListPageAsync(new RuntimeStorePageRequest(1));
            var secondExecutablePage = await executable.ListPageAsync(new RuntimeStorePageRequest(1, firstExecutablePage.NextContinuationToken));
            Assert.Single(firstExecutablePage.Items);
            Assert.Single(secondExecutablePage.Items);
            Assert.Null(secondExecutablePage.NextContinuationToken);
            Assert.Equal(
                new[] { "artifact-a", "artifact-b" },
                new[] { firstExecutablePage.Items[0].Identity.ArtifactId, secondExecutablePage.Items[0].Identity.ArtifactId }.Order(StringComparer.Ordinal));
            var maximumWidthArtifactId = new string('x', RuntimeArtifactEfModule.IdentityMaximumLength);
            await executable.SaveAsync(Executable(maximumWidthArtifactId));
            Assert.NotNull(await executable.FindAsync(maximumWidthArtifactId));
            await template.SaveAsync(Template("template-a", "template-hash-a"));
            Assert.NotNull(await template.FindByHashAsync("template-hash-a"));
            await template.SaveAsync(Template("template-case-upper", "ABC"));
            await template.SaveAsync(Template("template-case-lower", "abc"));
            Assert.Equal("template-case-lower", (await template.FindByHashAsync("abc"))!.TemplateId);
            await source.SaveAsync(Reference("reference-a", "artifact-a", "definition-version-a"));
            Assert.NotNull(await source.FindAsync("reference-a"));

            var maxValueNow = DateTimeOffset.MaxValue;
            await source.SaveAsync(Reference("permanent-expiry", "permanent-expiry-artifact", "definition-version-expiry"));
            await source.SaveAsync(Reference("explicit-max-expiry", "explicit-max-expiry-artifact", "definition-version-expiry") with { ExpiresAt = maxValueNow });
            var liveAtMaxValue = await source.ListPageAsync(new WorkflowExecutableSourceReferencePageQuery(liveOnly: true, now: maxValueNow, limit: 10));
            Assert.Contains(liveAtMaxValue.Items, item => item.SourceReferenceId == "permanent-expiry");
            Assert.DoesNotContain(liveAtMaxValue.Items, item => item.SourceReferenceId == "explicit-max-expiry");
            var unreferencedAtMaxValue = await source.ListUnreferencedArtifactIdsAsync(
                new WorkflowExecutableArtifactCandidateBatch(["permanent-expiry-artifact", "explicit-max-expiry-artifact"]),
                maxValueNow);
            Assert.Equal(["explicit-max-expiry-artifact"], unreferencedAtMaxValue);
            var deletedAtMaxValue = await source.DeleteExpiredOrRetiredAsync(new WorkflowExecutableSourceReferenceCleanupBatch(10), maxValueNow);
            Assert.Contains("explicit-max-expiry", deletedAtMaxValue);
            Assert.NotNull(await source.FindAsync("permanent-expiry"));
            Assert.Null(await source.FindAsync("explicit-max-expiry"));

            var page = await source.ListByDefinitionVersionPageAsync(
                new WorkflowExecutableSourceReferenceDefinitionVersionPageQuery("definition-version-a", 1));
            Assert.Equal("reference-a", Assert.Single(page.Items).SourceReferenceId);
            Assert.Null(page.NextContinuationToken);

            var secondScope = scope + "-second";
            var secondSource = new EfWorkflowExecutableSourceReferenceStore(context, new FixedAccessor(secondScope), codec);
            await secondSource.SaveAsync(Reference("reference-b", "artifact-b", "definition-version-a"));
            var across = new EfWorkflowExecutableSourceReferenceStore(
                context,
                new FixedAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("provider-smoke-export"))),
                codec);
            var firstAcrossPage = await across.ListByDefinitionVersionPageAsync(new("definition-version-a", 1));
            var secondAcrossPage = await across.ListByDefinitionVersionPageAsync(new("definition-version-a", 1, firstAcrossPage.NextContinuationToken));
            Assert.Single(firstAcrossPage.Items);
            Assert.Single(secondAcrossPage.Items);
            Assert.Null(secondAcrossPage.NextContinuationToken);
            Assert.Equal(["reference-a", "reference-b"], new[] { firstAcrossPage.Items[0].SourceReferenceId, secondAcrossPage.Items[0].SourceReferenceId }.Order(StringComparer.Ordinal));

            await executable.SaveAsync(Executable("rollback-existing"));
            var coordination = await context.WorkflowExecutableCoordinations.SingleAsync(x => x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("rollback-existing"));
            context.WorkflowExecutableCoordinations.Remove(coordination);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await Assert.ThrowsAsync<InvalidDataException>(() => executable.SaveBatchAsync(
                [Executable("rollback-new"), Executable("rollback-existing")]).AsTask());
            Assert.Null(await executable.FindAsync("rollback-new"));
        }

        var concurrentReference = Reference("concurrent-reference", "artifact-concurrent", "definition-version-concurrent");
        await using (var leftContext = createContext(connectionString))
        await using (var rightContext = createContext(connectionString))
        {
            var outcomes = await Task.WhenAll(
                Capture(new EfWorkflowExecutableSourceReferenceStore(leftContext, new FixedAccessor(scope), codec).SaveAsync(concurrentReference)),
                Capture(new EfWorkflowExecutableSourceReferenceStore(rightContext, new FixedAccessor(scope), codec).SaveAsync(concurrentReference)));
            Assert.Single(outcomes, outcome => outcome is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        }
    }

    /// <summary>
    /// A restore commits through a second connection after the collector re-read the doomed reference and before its
    /// delete is saved. The delete must lose to the revision and incarnation fence and report that it did.
    /// </summary>
    public static async Task RunDoomedDeleteRaceAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, IInterceptor[], RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"provider-doomed-race-{Guid.NewGuid():N}";
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = SigningKey,
            AllowEphemeralDevelopmentKey = false
        }));
        var retired = Reference("doomed-race", "doomed-race-artifact", "doomed-race-version").Retire(CreatedAt, "replaced");
        var restored = retired with { DeletedAt = null, DeletedReason = null };

        await using var seedContext = createContext(fixture.ConnectionString, []);
        await seedContext.Database.EnsureCreatedAsync();
        await new EfWorkflowExecutableSourceReferenceStore(seedContext, new FixedAccessor(scope), codec).SaveAsync(retired);

        await using var activationContext = createContext(fixture.ConnectionString, []);
        var activation = new EfWorkflowExecutableSourceReferenceStore(activationContext, new FixedAccessor(scope), codec);
        await using var collectorContext = createContext(
            fixture.ConnectionString,
            [new RestoreBeforeSaveInterceptor(async () => Assert.True(await activation.TryRestoreAsync(retired, restored)))]);
        var collector = new EfWorkflowExecutableSourceReferenceStore(collectorContext, new FixedAccessor(scope), codec);
        var snapshot = await collector.FindAsync(retired.SourceReferenceId);

        Assert.False(await collector.TryDeleteDoomedAsync(snapshot!, DateTimeOffset.UtcNow));

        Assert.Null((await activation.FindAsync(retired.SourceReferenceId))!.DeletedAt);
    }

    /// <summary>
    /// A guarded delete validates its guard and finds no lease, then the guard expires before the delete commits. An
    /// acquirer whose clock says the guard expired commits a lease meanwhile. At most one may win: either the lease is
    /// refused, or the delete is, and an artifact a returned lease protects is never deleted under it.
    /// </summary>
    public static async Task RunGuardExpiryDuringDeleteRaceAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, IInterceptor[], RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"provider-guard-expiry-{Guid.NewGuid():N}";
        const string artifactId = "provider-guard-expiry";
        var start = DateTimeOffset.UtcNow;

        await using var seedContext = createContext(fixture.ConnectionString, []);
        await seedContext.Database.EnsureCreatedAsync();
        var seed = new EfWorkflowExecutableStore(seedContext, new FixedAccessor(scope));
        await seed.SaveAsync(Executable(artifactId));
        var guard = await seed.TryBeginDeletionAsync(artifactId, "delete", start.AddMinutes(1), start);
        Assert.NotNull(guard);

        await using var acquirerContext = createContext(fixture.ConnectionString, []);
        var acquirer = new EfWorkflowExecutableStore(acquirerContext, new FixedAccessor(scope));
        WorkflowExecutableRootWriteLease? lease = null;
        var interleaving = new BeforeLeaseRowsDeletedInTransaction(async () =>
            lease = await acquirer.TryAcquireRootWriteLeaseAsync(artifactId, "late", start.AddMinutes(3), start.AddMinutes(2)));
        await using var collectorContext = createContext(fixture.ConnectionString, [interleaving]);
        var collector = new EfWorkflowExecutableStore(collectorContext, new FixedAccessor(scope));

        var deleted = await collector.DeleteAsync(guard!, start.AddSeconds(30));

        Assert.Equal(1, interleaving.Callbacks);
        Assert.False(deleted && lease is not null, "The delete committed under a lease the acquirer was granted.");
        if (lease is not null)
        {
            Assert.NotNull(await acquirer.FindAsync(artifactId));
            Assert.True(await acquirer.RenewRootWriteLeaseAsync(lease, start.AddMinutes(4), start.AddMinutes(2)));
        }
    }

    public static async Task RunExecutableCoordinationRaceAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, IInterceptor[], RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"provider-executable-race-{Guid.NewGuid():N}";
        var candidate = Executable("provider-coordination-read-race");

        await using var winnerContext = createContext(fixture.ConnectionString, []);
        await winnerContext.Database.EnsureCreatedAsync();
        var winner = new EfWorkflowExecutableStore(winnerContext, new FixedAccessor(scope));
        var interleaving = new BeforeExecutableCoordinationRead(
            () => winner.SaveAsync(candidate).AsTask());
        await using var contenderContext = createContext(fixture.ConnectionString, [interleaving]);
        var contender = new EfWorkflowExecutableStore(contenderContext, new FixedAccessor(scope));

        await contender.SaveAsync(candidate);

        Assert.Equal(1, interleaving.Callbacks);
        Assert.NotNull(interleaving.TriggeredCommand);
        Assert.Contains(
            RuntimeArtifactEfModule.WorkflowExecutableCoordinationTableName,
            interleaving.TriggeredCommand!,
            StringComparison.OrdinalIgnoreCase);

        var persisted = await contender.FindAsync(candidate.Identity.ArtifactId);
        Assert.NotNull(persisted);
        Assert.Equal(candidate.Identity.ArtifactId, persisted!.Identity.ArtifactId);
        Assert.Equal(candidate.Identity.ArtifactHash, persisted.Identity.ArtifactHash);
        Assert.NotEmpty(persisted.Nodes);
        Assert.Empty(contenderContext.ChangeTracker.Entries());

        await using var verificationContext = createContext(fixture.ConnectionString, []);
        var artifactRow = await verificationContext.WorkflowExecutables
            .AsNoTracking()
            .SingleAsync(x =>
                x.ScopeKey == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(scope) &&
                x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(candidate.Identity.ArtifactId));
        var coordinationRow = await verificationContext.WorkflowExecutableCoordinations
            .AsNoTracking()
            .SingleAsync(x =>
                x.ScopeKey == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(scope) &&
                x.ArtifactId == Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(candidate.Identity.ArtifactId));
        Assert.Equal(artifactRow.IncarnationId, coordinationRow.IncarnationId);
    }


    /// <summary>
    /// #2538: every concurrent execution of one published workflow takes and releases its own root-write lease on
    /// the same artifact for each checkpoint commit. Real parallel holders must all succeed; none may fault because
    /// other holders wrote their own leases first.
    /// </summary>
    public static async Task RunConcurrentRootWriteLeaseHoldersAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, RuntimeDbContext> createContext)
    {
        const int holders = 32;
        const int cyclesPerHolder = 5;
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"provider-lease-holders-{Guid.NewGuid():N}";
        var artifactId = "provider-concurrent-lease-holders";

        await using (var seedContext = createContext(fixture.ConnectionString))
        {
            await seedContext.Database.EnsureCreatedAsync();
            await new EfWorkflowExecutableStore(seedContext, new FixedAccessor(scope)).SaveAsync(Executable(artifactId));
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, holders).Select(holder => Task.Run(async () =>
        {
            await using var context = createContext(fixture.ConnectionString);
            var store = new EfWorkflowExecutableStore(context, new FixedAccessor(scope));
            await start.Task;
            for (var cycle = 0; cycle < cyclesPerHolder; cycle++)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var lease = await store.TryAcquireRootWriteLeaseAsync(artifactId, $"holder-{holder}-cycle-{cycle}", now.AddMinutes(1), now);
                    if (lease is null)
                    {
                        failures.Add($"holder {holder} cycle {cycle}: acquire returned null without a deletion guard");
                        continue;
                    }

                    await store.ReleaseRootWriteLeaseAsync(lease);
                }
                catch (Exception exception) when (IsCatchable(exception))
                {
                    failures.Add($"holder {holder} cycle {cycle}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(tasks);

        Assert.True(failures.IsEmpty, $"{failures.Count} of {holders * cyclesPerHolder} lease cycles failed:{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(10))}");

        // Every lease was released, so a deletion guard is grantable now; cancel it to leave the artifact usable.
        await using var verificationContext = createContext(fixture.ConnectionString);
        var verifier = new EfWorkflowExecutableStore(verificationContext, new FixedAccessor(scope));
        var verifiedAt = DateTimeOffset.UtcNow;
        var guard = await verifier.TryBeginDeletionAsync(artifactId, "verify-no-live-leases", verifiedAt.AddMinutes(1), verifiedAt);
        Assert.NotNull(guard);
        Assert.True(await verifier.CancelDeletionAsync(guard!));
    }

    /// <summary>Runs a callback once, just before a guarded delete removes the artifact's lease rows inside its transaction.</summary>
    private sealed class BeforeLeaseRowsDeletedInTransaction(Func<Task> callback) : DbCommandInterceptor
    {
        private int callbacks;

        public int Callbacks => Volatile.Read(ref callbacks);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.Transaction is not null &&
                command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(RuntimeArtifactEfModule.WorkflowExecutableRootWriteLeaseTableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref callbacks, 1) == 0)
                await callback();
            return result;
        }
    }

    private sealed class RestoreBeforeSaveInterceptor(Func<Task> restore) : SaveChangesInterceptor
    {
        private int invoked;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref invoked, 1) == 0)
                await restore();
            return result;
        }
    }

    private static WorkflowExecutable Executable(string artifactId)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new WorkflowExecutable(new WorkflowExecutableIdentity(artifactId, "definition", "version", "1", $"hash-{artifactId}"), node, new Dictionary<string, WorkflowExecutableResumeTarget>(), CreatedAt, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ExecutableActivityTemplate Template(string id, string hash)
    {
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>());
        return new ExecutableActivityTemplate(id, hash, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "fingerprint", new Dictionary<string, string>(), CreatedAt);
    }

    private static WorkflowExecutableSourceReference Reference(string id, string artifact, string definitionVersion) => new(
        id, artifact, "WorkflowDefinition", "definition", "1", "definition", definitionVersion, "1", CreatedAt, CreatedAt, WorkflowExecutableReferenceScope.Published);

    private static async Task<Exception?> Capture(ValueTask operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception) when (IsCatchable(exception))
        {
            return exception;
        }
    }

    private static bool IsCatchable(Exception exception) =>
        exception is not (OutOfMemoryException or
            StackOverflowException or
            AccessViolationException or
            AppDomainUnloadedException or
            BadImageFormatException or
            CannotUnloadAppDomainException or
            InvalidProgramException);

    private sealed class FixedAccessor : IPersistenceAccessContextAccessor
    {
        public FixedAccessor(string scope) : this(PersistenceAccessContext.Scoped(new PersistenceScope(scope))) { }
        public FixedAccessor(PersistenceAccessContext current) => Current = current;
        public PersistenceAccessContext Current { get; }
    }
}
