using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Store;
using Groundwork.Testing;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

/// <summary>
/// Isolated public Groundwork v2 storage used by the ASP.NET Core Identity contract suite.
/// The fixture owns one provider connection and admits the same 17 Identity units as production.
/// </summary>
internal sealed class AspNetCoreIdentityTestPersistence : IGroundworkStorageSessionSource, IDisposable
{
    private readonly Lock sessionGate = new();
    private readonly Dictionary<(string UnitId, StorageAccess Access), IStorageSession> sessions = [];
    private readonly IReadOnlyDictionary<string, StorageUnit> units = IdentityV2StorageManifest.CreateUnits()
        .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

    public AspNetCoreIdentityTestPersistence()
        : this(new SerializingStorageProviderConnection(
            new InMemoryProviderFactory().Create($"identity-aspnet-tests:{Guid.NewGuid():N}")))
    {
    }

    public AspNetCoreIdentityTestPersistence(IStorageProviderConnection connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        foreach (var unit in units.Values)
            Connection.Schema.Apply(unit);
    }

    public IStorageProviderConnection Connection { get; }

    public GroundworkIdentityRowStore Rows(IPersistenceAccessContextAccessor access) =>
        new(this, access);

    public IReadOnlyList<GroundworkIdentityRow> Snapshot(string unitId, string scope) =>
        Rows(new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope))))
            .Query(unitId, new GroundworkIdentityRowQuery(
                IdentityV2StorageManifest.IdField,
                GroundworkIdentityRowComparison.GreaterThanOrEqual,
                string.Empty,
                IdentityV2StorageManifest.IdField,
                Take: 100_000));

    public GroundworkIdentityRow? Read(string unitId, string id, string scope) =>
        Rows(new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope))))
            .Read(unitId, id);

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
    {
        var key = (unitId, access);
        lock (sessionGate)
        {
            if (sessions.TryGetValue(key, out var session))
                return session;
            session = Connection.OpenSession(Unit(unitId, targetName), access);
            sessions.Add(key, session);
            return session;
        }
    }

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null) =>
        Connection.BeginUnitOfWork(
            access,
            options,
            unitIds.Select(id => Unit(id, targetName)).ToArray());

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

    public void Dispose() => Connection.Dispose();

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}

/// <summary>DI-safe view for hosts that are restarted while the test-owned connection remains open.</summary>
internal sealed class NonDisposingStorageProviderConnection(IStorageProviderConnection inner)
    : IStorageProviderConnection
{
    public IProviderCatalog Catalog => inner.Catalog;
    public ISchemaCoordinator Schema => inner.Schema;
    public IReadOnlyList<CapabilityDescriptor> Capabilities => inner.Capabilities;

    public IStorageSession OpenSession(StorageUnit unit, StorageAccess access) =>
        inner.OpenSession(unit, access);

    public IStorageSession OpenSession(StorageUnit unit, StorageAccess access, IProviderCommandObserver? observer = null) =>
        inner.OpenSession(unit, access, observer);

    public IOwnedStorageSession OpenOwnedSession(
        StorageUnit unit,
        StorageAccess access,
        IProviderCommandObserver? observer = null) =>
        inner.OpenOwnedSession(unit, access, observer);

    public IUnitOfWork BeginUnitOfWork(StorageAccess access, params StorageUnit[] units) =>
        inner.BeginUnitOfWork(access, units);

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        params StorageUnit[] units) =>
        inner.BeginUnitOfWork(access, options, units);

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IProviderCommandObserver? observer,
        params StorageUnit[] units) =>
        inner.BeginUnitOfWork(access, options, observer, units);

    public void Dispose()
    {
        // The owning AspNetCoreIdentityTestPersistence is disposed by the test after all hosts stop.
    }
}

/// <summary>
/// Models a provider that serializes transaction admission while leaving all row preconditions to
/// Groundwork. The seeding stress contract uses it to exercise two independent hosts without
/// depending on the in-memory provider's intentionally fail-fast snapshot-overlap exception.
/// Native independent-client linearization remains covered separately by the SQLite contract.
/// </summary>
internal sealed class SerializingStorageProviderConnection(IStorageProviderConnection inner)
    : IStorageProviderConnection
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public IProviderCatalog Catalog => inner.Catalog;
    public ISchemaCoordinator Schema => inner.Schema;
    public IReadOnlyList<CapabilityDescriptor> Capabilities => inner.Capabilities;

    public IStorageSession OpenSession(StorageUnit unit, StorageAccess access) =>
        inner.OpenSession(unit, access);

    public IStorageSession OpenSession(StorageUnit unit, StorageAccess access, IProviderCommandObserver? observer = null) =>
        inner.OpenSession(unit, access, observer);

    public IOwnedStorageSession OpenOwnedSession(
        StorageUnit unit,
        StorageAccess access,
        IProviderCommandObserver? observer = null) =>
        inner.OpenOwnedSession(unit, access, observer);

    public IUnitOfWork BeginUnitOfWork(StorageAccess access, params StorageUnit[] units) =>
        Begin(access, null, null, units);

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        params StorageUnit[] units) =>
        Begin(access, options, null, units);

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IProviderCommandObserver? observer,
        params StorageUnit[] units) =>
        Begin(access, options, observer, units);

    public void Dispose()
    {
        inner.Dispose();
        gate.Dispose();
    }

    private IUnitOfWork Begin(
        StorageAccess access,
        BatchWriteOptions? options,
        IProviderCommandObserver? observer,
        StorageUnit[] units)
    {
        gate.Wait();
        try
        {
            var unitOfWork = observer is not null
                ? inner.BeginUnitOfWork(access, options ?? BatchWriteOptions.Default, observer, units)
                : options is null
                    ? inner.BeginUnitOfWork(access, units)
                    : inner.BeginUnitOfWork(access, options, units);
            return new SerializingUnitOfWork(unitOfWork, gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    private sealed class SerializingUnitOfWork(IUnitOfWork inner, SemaphoreSlim gate) : IUnitOfWork
    {
        private int released;

        public IStorageSession OpenSession(StorageUnit unit) => inner.OpenSession(unit);
        public void Stage(RowWrite write) => inner.Stage(write);
        public BatchWriteSummary Commit() => Terminal(inner.Commit);
        public BatchWriteReport CommitWithOutcomes() => Terminal(inner.CommitWithOutcomes);

        public async ValueTask<BatchWriteReport> CommitWithOutcomesAsync(
            CancellationToken cancellationToken = default) =>
            await TerminalAsync(() => inner.CommitWithOutcomesAsync(cancellationToken));

        public async ValueTask<BatchWriteSummary> CommitAsync(
            CancellationToken cancellationToken = default) =>
            await TerminalAsync(() => inner.CommitAsync(cancellationToken));

        public void Rollback()
        {
            try
            {
                inner.Rollback();
            }
            finally
            {
                Release();
            }
        }

        public void Dispose()
        {
            try
            {
                inner.Dispose();
            }
            finally
            {
                Release();
            }
        }

        private T Terminal<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            finally
            {
                Release();
            }
        }

        private async ValueTask<T> TerminalAsync<T>(Func<ValueTask<T>> operation)
        {
            try
            {
                return await operation();
            }
            finally
            {
                Release();
            }
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                gate.Release();
        }
    }
}
