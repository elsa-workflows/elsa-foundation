using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity.Differential;

/// <summary>The behavioural dimensions the #646 differential drives against <see cref="ITenantMembershipStore"/>.</summary>
internal enum TenantMembershipDimension
{
    ConcurrencyConflictShape,
    ProducerOrdering,
    NullAndDefaultMaterialization,
    RollbackVisibility,
    RestartObservation,
    IdempotentReplay
}

/// <summary>A normalized, provider-neutral record of what one comparand did.</summary>
internal sealed record TenantMembershipObservation(TenantMembershipDimension Dimension, IReadOnlyDictionary<string, string> Facts)
{
    public string Canonical => string.Join("; ", Facts.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));

    public override string ToString() => Canonical;
}

/// <summary>
/// One comparand for <see cref="ITenantMembershipStore"/>, the only Elsa IAM contract that is both
/// dual-stack and admissible: <c>iam-user</c>, <c>iam-role</c> and <c>iam-external-identity</c> are
/// <c>externally-blocked</c> in the coverage ledger, and the ASP.NET Core Identity oracle tree is frozen.
/// </summary>
/// <remarks>
/// Both sides run real file-backed SQLite. The EF side reaches it through the parameterless
/// <c>AddFoundationAspNetCoreIdentityEntityFrameworkCore()</c> registration, which is deliberate: the
/// provider call lives in <c>src/</c>, so no EF registration token enters this test project and the
/// fail-closed EF surface ratchet stays green.
/// </remarks>
internal abstract class TenantMembershipDifferentialTarget : IAsyncDisposable
{
    public const string TenantId = "tenant-differential";

    protected TenantMembershipDifferentialTarget(string identity) => Identity = identity;

    public string Identity { get; }

    public abstract Task<ITenantMembershipStore> OpenAsync();

    /// <summary>
    /// Materializes a user the membership can reference. Groundwork's membership store resolves the
    /// owning user document and rejects an orphan membership; EF's has no such link and will persist
    /// one happily. Probes seed first so the remaining dimensions compare membership behaviour rather
    /// than re-observing that one precondition difference six times.
    /// </summary>
    public abstract Task SeedUserAsync(string userId);

    public abstract Task CloseAsync();

    public abstract ValueTask DisposeAsync();

    public static TenantMembershipDifferentialTarget EfCore() => new EfCoreTarget();

    public static TenantMembershipDifferentialTarget Groundwork() => new GroundworkTarget();

    protected sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    protected static UserRecord User(string userId) =>
        new(userId, TenantId, userId, $"{userId}@example.test", userId, UserStatus.Active,
            ResourceOwnership.Foundation, new HashSet<string>(), new HashSet<string>());

    private sealed class EfCoreTarget() : TenantMembershipDifferentialTarget("efcore.sqlite")
    {
        private ServiceProvider? _provider;
        private IServiceScope? _scope;

        public override async Task<ITenantMembershipStore> OpenAsync()
        {
            var services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore();
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
            var db = _scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            return new EfCoreTenantMembershipStore(db);
        }

        public override async Task SeedUserAsync(string userId)
        {
            var db = _scope!.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            await new EfCoreUserStore(db).SaveAsync(User(userId));
        }

        public override Task CloseAsync()
        {
            _scope?.Dispose();
            _scope = null;
            _provider?.Dispose();
            _provider = null;
            return Task.CompletedTask;
        }

        public override async ValueTask DisposeAsync()
        {
            // The registration hard-codes a fixed data source, so the database must be dropped rather
            // than a per-target file deleted. TenantMembershipStoreDifferentialTests serializes access.
            if (_scope is null)
                await OpenAsync();

            var db = _scope!.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            await db.Database.EnsureDeletedAsync();
            await CloseAsync();
        }
    }

    private sealed class GroundworkTarget() : TenantMembershipDifferentialTarget("groundwork.sqlite")
    {
        private SqliteGroundworkProviderDriver? _driver;
        private GroundworkProviderClient? _client;

        public override async Task<ITenantMembershipStore> OpenAsync()
        {
            if (_driver is null)
            {
                _driver = new();
                await _driver.InitializeAsync(CancellationToken.None);
                await _driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()], CancellationToken.None);
            }

            _client = await _driver.OpenPhysicalClientAsync(
                DocumentStoreAccess.Scoped(new StorageScope(TenantId)),
                CancellationToken.None);

            return new GroundworkTenantMembershipStore(_client.DocumentStore, Accessor);
        }

        public override async Task SeedUserAsync(string userId) =>
            await new GroundworkUserStore(_client!.DocumentStore, Accessor, _client.BoundedDocumentStore)
                .SaveAsync(User(userId));

        private static IPersistenceAccessContextAccessor Accessor { get; } =
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(TenantId)));

        public override async Task CloseAsync()
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }
        }

        public override async ValueTask DisposeAsync()
        {
            await CloseAsync();

            if (_driver is not null)
            {
                await _driver.DisposeAsync();
                _driver = null;
            }
        }
    }
}

/// <summary>
/// Drives one identical operation sequence per dimension. The contract is two methods, so the probes
/// concentrate on how each stack represents a membership's two string sets — which is where EF's
/// newline-joined, case-insensitively-split storage is most likely to differ from Groundwork's.
/// </summary>
internal static class TenantMembershipDifferentialProbes
{
    public static Func<TenantMembershipDifferentialTarget, Task<TenantMembershipObservation>> For(TenantMembershipDimension dimension) => dimension switch
    {
        TenantMembershipDimension.ConcurrencyConflictShape => ConcurrencyConflictShapeAsync,
        TenantMembershipDimension.ProducerOrdering => ProducerOrderingAsync,
        TenantMembershipDimension.NullAndDefaultMaterialization => NullAndDefaultMaterializationAsync,
        TenantMembershipDimension.RollbackVisibility => RollbackVisibilityAsync,
        TenantMembershipDimension.RestartObservation => RestartObservationAsync,
        TenantMembershipDimension.IdempotentReplay => IdempotentReplayAsync,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    /// <summary>Two writers claim the same (tenant, user) key; records who wins and whether either failed.</summary>
    private static async Task<TenantMembershipObservation> ConcurrencyConflictShapeAsync(TenantMembershipDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        await target.SeedUserAsync("user-contested");

        var first = await FailureShapeAsync(() => store.SaveAsync(Membership("user-contested", ["role-first"])).AsTask());
        var second = await FailureShapeAsync(() => store.SaveAsync(Membership("user-contested", ["role-second"])).AsTask());
        var read = await store.FindAsync(TenantMembershipDifferentialTarget.TenantId, "user-contested");

        var facts = new Dictionary<string, string>
        {
            ["first-save"] = first,
            ["second-save"] = second,
            ["surviving-roles"] = Describe(read?.RoleIds),
            ["last-writer-wins"] = read is not null && read.RoleIds.Contains("role-second") ? "true" : "false"
        };

        await target.CloseAsync();
        return new(TenantMembershipDimension.ConcurrencyConflictShape, facts);
    }

    /// <summary>Concurrent writers on distinct keys: records loss and readback fidelity.</summary>
    private static async Task<TenantMembershipObservation> ProducerOrderingAsync(TenantMembershipDifferentialTarget target)
    {
        const int count = 12;
        var store = await target.OpenAsync();
        for (var i = 0; i < count; i++)
            await target.SeedUserAsync($"user-{i:D2}");

        // Drive the seam the way a concurrent caller would: one store instance, many writers.
        var concurrent = await FailureShapeAsync(() => Task.WhenAll(Enumerable.Range(0, count)
            .Select(i => Task.Run(async () => await store.SaveAsync(Membership($"user-{i:D2}", [$"role-{i:D2}"]))))
            .ToArray()));

        // Whatever the concurrent attempt did, re-drive it serially so the readback below measures
        // durable outcome rather than how far the concurrent attempt happened to get.
        for (var i = 0; i < count; i++)
            await store.SaveAsync(Membership($"user-{i:D2}", [$"role-{i:D2}"]));

        var found = new List<string>();
        for (var i = 0; i < count; i++)
        {
            if (await store.FindAsync(TenantMembershipDifferentialTarget.TenantId, $"user-{i:D2}") is { } record)
                found.Add(Describe(record.RoleIds));
        }

        var facts = new Dictionary<string, string>
        {
            ["written"] = count.ToString(),
            ["readable"] = found.Count.ToString(),
            ["loss"] = found.Count == count ? "none" : "present",
            ["roles-intact"] = found.Count(f => f.StartsWith("role-", StringComparison.Ordinal)).ToString(),
            ["concurrent-writes-through-one-instance"] = concurrent
        };

        await target.CloseAsync();
        return new(TenantMembershipDimension.ProducerOrdering, facts);
    }

    /// <summary>
    /// Round-trips the awkward set contents: empty sets, case-differing entries, surrounding whitespace,
    /// and an embedded newline. EF joins on '\n' and splits case-insensitively, so this is where the two
    /// representations are most likely to disagree.
    /// </summary>
    private static async Task<TenantMembershipObservation> NullAndDefaultMaterializationAsync(TenantMembershipDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        foreach (var userId in new[] { "user-empty", "user-case", "user-space" })
            await target.SeedUserAsync(userId);

        await store.SaveAsync(new(TenantMembershipDifferentialTarget.TenantId, "user-empty", TenantMembershipStatus.Suspended,
            new HashSet<string>(), new HashSet<string>()));
        await store.SaveAsync(new(TenantMembershipDifferentialTarget.TenantId, "user-case", TenantMembershipStatus.Active,
            new HashSet<string>(StringComparer.Ordinal) { "Role-Alpha", "role-alpha" }, new HashSet<string>(StringComparer.Ordinal) { "perm" }));
        await store.SaveAsync(new(TenantMembershipDifferentialTarget.TenantId, "user-space", TenantMembershipStatus.Active,
            new HashSet<string>(StringComparer.Ordinal) { " padded " }, new HashSet<string>(StringComparer.Ordinal) { "multi\nline" }));

        var empty = await store.FindAsync(TenantMembershipDifferentialTarget.TenantId, "user-empty");
        var caseVariant = await store.FindAsync(TenantMembershipDifferentialTarget.TenantId, "user-case");
        var spaced = await store.FindAsync(TenantMembershipDifferentialTarget.TenantId, "user-space");

        var facts = new Dictionary<string, string>
        {
            ["empty-sets-readable"] = empty is null ? "false" : "true",
            ["empty-role-count"] = empty?.RoleIds.Count.ToString() ?? "<absent>",
            ["suspended-status"] = empty?.Status.ToString() ?? "<absent>",
            ["case-variant-role-count"] = caseVariant?.RoleIds.Count.ToString() ?? "<absent>",
            ["case-variants-collapsed"] = caseVariant is null ? "<absent>" : caseVariant.RoleIds.Count < 2 ? "true" : "false",
            ["padded-role"] = spaced is null ? "<absent>" : spaced.RoleIds.Contains(" padded ") ? "preserved" : "trimmed-or-lost",
            ["embedded-newline-permission-count"] = spaced?.DirectPermissions.Count.ToString() ?? "<absent>",
            ["embedded-newline-intact"] = spaced is null ? "<absent>" : spaced.DirectPermissions.Contains("multi\nline") ? "true" : "false"
        };

        await target.CloseAsync();
        return new(TenantMembershipDimension.NullAndDefaultMaterialization, facts);
    }

    /// <summary>A rejected write must leave the previously committed record exactly as it was.</summary>
    private static async Task<TenantMembershipObservation> RollbackVisibilityAsync(TenantMembershipDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        await target.SeedUserAsync("user-stable");
        await store.SaveAsync(Membership("user-stable", ["role-committed"]));

        // A cross-tenant write is the one both stacks can be asked to reject at this seam.
        var foreignWrite = await FailureShapeAsync(() => store.SaveAsync(
            new("tenant-other", "user-stable", TenantMembershipStatus.Active,
                new HashSet<string> { "role-foreign" }, new HashSet<string>())).AsTask());

        var read = await store.FindAsync(TenantMembershipDifferentialTarget.TenantId, "user-stable");

        var facts = new Dictionary<string, string>
        {
            ["foreign-tenant-write"] = foreignWrite,
            ["original-record-intact"] = read is not null && read.RoleIds.Contains("role-committed") && read.RoleIds.Count == 1 ? "true" : "false",
            ["original-roles"] = Describe(read?.RoleIds)
        };

        await target.CloseAsync();
        return new(TenantMembershipDimension.RollbackVisibility, facts);
    }

    /// <summary>A committed membership must survive closing and reopening the store over the same storage.</summary>
    private static async Task<TenantMembershipObservation> RestartObservationAsync(TenantMembershipDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        await target.SeedUserAsync("user-restart");
        await store.SaveAsync(Membership("user-restart", ["role-a", "role-b"], TenantMembershipStatus.Suspended));
        await target.CloseAsync();

        var reopened = await target.OpenAsync();
        var read = await reopened.FindAsync(TenantMembershipDifferentialTarget.TenantId, "user-restart");

        var facts = new Dictionary<string, string>
        {
            ["survived-restart"] = read is null ? "false" : "true",
            ["roles-after-restart"] = Describe(read?.RoleIds),
            ["status-after-restart"] = read?.Status.ToString() ?? "<absent>"
        };

        await target.CloseAsync();
        return new(TenantMembershipDimension.RestartObservation, facts);
    }

    /// <summary>Saving the identical record twice must be an upsert, not a duplicate or a failure.</summary>
    private static async Task<TenantMembershipObservation> IdempotentReplayAsync(TenantMembershipDifferentialTarget target)
    {
        var store = await target.OpenAsync();
        await target.SeedUserAsync("user-replay");
        var record = Membership("user-replay", ["role-x"]);

        var first = await FailureShapeAsync(() => store.SaveAsync(record).AsTask());
        var replay = await FailureShapeAsync(() => store.SaveAsync(record).AsTask());
        var read = await store.FindAsync(TenantMembershipDifferentialTarget.TenantId, "user-replay");

        var facts = new Dictionary<string, string>
        {
            ["first-save"] = first,
            ["replayed-save"] = replay,
            ["roles-after-replay"] = Describe(read?.RoleIds),
            ["record-readable"] = read is null ? "false" : "true"
        };

        await target.CloseAsync();
        return new(TenantMembershipDimension.IdempotentReplay, facts);
    }

    private static async Task<string> FailureShapeAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return "accepted";
        }
        catch (Exception exception)
        {
            return $"threw:{exception.GetType().Name}";
        }
    }

    private static string Describe(IReadOnlySet<string>? values) =>
        values is null ? "<absent>" : values.Count == 0 ? "<empty>" : string.Join(",", values.OrderBy(v => v, StringComparer.Ordinal));

    private static TenantMembershipRecord Membership(string userId, string[] roleIds, TenantMembershipStatus status = TenantMembershipStatus.Active) =>
        new(TenantMembershipDifferentialTarget.TenantId, userId, status,
            new HashSet<string>(roleIds, StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>
/// Recorded surface and dispositions for the <see cref="ITenantMembershipStore"/> differential.
/// Executable half of <c>specs/094-harden-groundwork-stores/divergence-ledger.md</c>.
/// </summary>
internal static class TenantMembershipDivergenceLedger
{
    private static readonly IReadOnlyDictionary<TenantMembershipDimension, string[]> Compared =
        new Dictionary<TenantMembershipDimension, string[]>
        {
            [TenantMembershipDimension.ConcurrencyConflictShape] =
                ["first-save", "last-writer-wins", "second-save", "surviving-roles"],
            [TenantMembershipDimension.ProducerOrdering] =
                ["concurrent-writes-through-one-instance", "loss", "readable", "roles-intact", "written"],
            [TenantMembershipDimension.NullAndDefaultMaterialization] =
            [
                "case-variant-role-count", "case-variants-collapsed", "embedded-newline-intact",
                "embedded-newline-permission-count", "empty-role-count", "empty-sets-readable",
                "padded-role", "suspended-status"
            ],
            [TenantMembershipDimension.RollbackVisibility] =
                ["foreign-tenant-write", "original-record-intact", "original-roles"],
            [TenantMembershipDimension.RestartObservation] =
                ["roles-after-restart", "status-after-restart", "survived-restart"],
            [TenantMembershipDimension.IdempotentReplay] =
                ["first-save", "record-readable", "replayed-save", "roles-after-replay"]
        };

    private static readonly IReadOnlyDictionary<TenantMembershipDimension, string[]> Divergences =
        new Dictionary<TenantMembershipDimension, string[]>
        {
            [TenantMembershipDimension.ConcurrencyConflictShape] = [],
            // EF's store holds one DbContext, which is not thread-safe, so concurrent writers through
            // a single instance fault; Groundwork's store accepts them. Disposition ContractIsGroundwork.
            [TenantMembershipDimension.ProducerOrdering] = ["concurrent-writes-through-one-instance"],
            // EF stores both string sets as one newline-joined column and splits it back
            // case-insensitively with entries trimmed and empties dropped. That encoding is lossy in
            // three separate ways; Groundwork round-trips the set exactly. Disposition
            // ContractIsGroundwork on all four facts — EF's behaviour is a storage artifact.
            [TenantMembershipDimension.NullAndDefaultMaterialization] =
            [
                "case-variant-role-count", "case-variants-collapsed", "embedded-newline-intact",
                "embedded-newline-permission-count", "padded-role"
            ],
            // EF's membership store filters only on the record's own tenant/user columns and happily
            // writes a membership for a tenant outside the caller's scope; Groundwork's scope guard
            // rejects it before provider I/O. Disposition ContractIsGroundwork.
            [TenantMembershipDimension.RollbackVisibility] = ["foreign-tenant-write"],
            [TenantMembershipDimension.RestartObservation] = [],
            [TenantMembershipDimension.IdempotentReplay] = []
        };

    public const string SurfaceDigest = "42f863e9213e8bef8c012cba81867f5fbac698df8f2bd830ceb2c0aa456af098";

    public static IReadOnlyCollection<string> ComparedFactsFor(TenantMembershipDimension dimension) =>
        Compared.TryGetValue(dimension, out var facts) ? facts : [];

    public static IReadOnlyCollection<string> RecordedDivergencesFor(TenantMembershipDimension dimension) =>
        Divergences.TryGetValue(dimension, out var facts) ? facts : [];

    public static IEnumerable<TenantMembershipDimension> Dimensions =>
        Enum.GetValues<TenantMembershipDimension>().OrderBy(d => d.ToString(), StringComparer.Ordinal);
}
