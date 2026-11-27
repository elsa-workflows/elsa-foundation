using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public sealed partial class SqliteEmbeddedFoundation
{
    /// <summary>Composes the dedicated Event start profile with its trusted held-root protection.</summary>
    public static SqliteEmbeddedFoundation ForEventStartsQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart) =>
        new(databasePath, node, actorType, StoreFormat.RootEventStartsQualification, protectRootStart);

    /// <summary>Composes the dedicated Event receipt-retention profile.</summary>
    public static SqliteEmbeddedFoundation ForEventStartReceiptsQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootEventStartReceiptsQualification, protectRootStart);
    }

    /// <summary>Composes the dedicated ordered Event start qualification profile.</summary>
    public static SqliteEmbeddedFoundation ForEventStartScanQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootEventStartScanQualification, protectRootStart);
    }

    /// <summary>Composes the private profile for qualifying inline promotion and ordered Event delivery together.</summary>
    public static SqliteEmbeddedFoundation ForSharedActivationQualification(string databasePath, string node, string actorType,
        ProtectRootStartAsync protectRootStart)
    {
        ArgumentNullException.ThrowIfNull(protectRootStart);
        return new(databasePath, node, actorType, StoreFormat.RootSharedActivationQualification, protectRootStart);
    }

    /// <summary>Binds trusted Event registration and relay to the selected file and tenant.</summary>
    public IEventStartAdmissions BindEventStarts(Contract.BoundScope scope) => new BoundEventStarts(EventStarts, scope);

    private sealed class BoundEventStarts(SqliteEmbeddedHosting.SqliteDirectRequests requests, Contract.BoundScope scope) : IOrderedEventStartAdmissions
    {
        public Task<EventRegistrationResult> ApplyAsync(string slot, long revision, IReadOnlyList<EventStartRegistration> registrations, CancellationToken cancellationToken) =>
            requests.ApplyEventStartsAsync(scope, slot, revision, registrations, cancellationToken);

        public Task<IReadOnlyList<EventStartTarget>> ListAsync(string? after, CancellationToken cancellationToken) =>
            requests.ListEventStartsAsync(scope, after, cancellationToken);

        public Task<long> ReadPendingTailAsync(CancellationToken cancellationToken) =>
            requests.ReadEventStartTailAsync(scope, cancellationToken);

        public Task<IReadOnlyList<EventStartScanItem>> ReadPendingPageAsync(long after, long through, int limit, CancellationToken cancellationToken) =>
            requests.ReadEventStartPageAsync(scope, after, through, limit, cancellationToken);

        public Task<Contract.Admission> AdmitAsync(string target, Func<EventStartTarget, Contract.StartSpec> start,
            ProtectRootStartAsync protect, CancellationToken cancellationToken) =>
            requests.AdmitEventStartAsync(scope, target, start, protect, cancellationToken);

        public Task<int> PruneSettledAsync(CancellationToken cancellationToken) =>
            requests.PruneSettledEventStartsAsync(scope, cancellationToken);

        public Task<bool> ConfirmDrainedAsync(string slot, long revision, CancellationToken cancellationToken) =>
            requests.ConfirmEventStartRegistrationDrainedAsync(scope, slot, revision, cancellationToken);
    }
}
