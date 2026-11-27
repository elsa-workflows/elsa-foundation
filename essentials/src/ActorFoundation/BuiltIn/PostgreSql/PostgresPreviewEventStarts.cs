using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

public static partial class PostgresPreviewHosting
{
    internal static KernelDb CreateKeyedStartsQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootKeyedStartsQualification(options);

    internal static Task InstallKeyedStartsQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreateKeyedStartsQualificationContext(options), cancellationToken);

    /// <summary>Selects the dedicated Event start qualification format.</summary>
    public static KernelDb CreateEventStartsQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootEventStartsQualification(options);

    /// <summary>Installs or verifies a dedicated Event start qualification database.</summary>
    public static Task InstallEventStartsQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreateEventStartsQualificationContext(options), cancellationToken);

    /// <summary>Selects the dedicated Event receipt-retention qualification format.</summary>
    public static KernelDb CreateEventStartReceiptsQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootEventStartReceiptsQualification(options);

    /// <summary>Installs or verifies a dedicated Event receipt-retention database.</summary>
    public static Task InstallEventStartReceiptsQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreateEventStartReceiptsQualificationContext(options), cancellationToken);

    /// <summary>Selects the dedicated ordered Event start qualification format.</summary>
    public static KernelDb CreateEventStartScanQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootEventStartScanQualification(options);

    /// <summary>Installs or verifies the ordered Event start qualification database.</summary>
    public static Task InstallEventStartScanQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreateEventStartScanQualificationContext(options), cancellationToken);

    /// <summary>Selects the private profile for qualifying inline promotion and ordered Event delivery together.</summary>
    public static KernelDb CreateSharedActivationQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootSharedActivationQualification(options);

    /// <summary>Installs or verifies the dedicated combined qualification database.</summary>
    public static Task InstallSharedActivationQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreateSharedActivationQualificationContext(options), cancellationToken);

    /// <summary>Selects the private profile that binds public Event start authority to the installed actor host.</summary>
    public static KernelDb CreatePublicEventHostQualificationContext(DbContextOptions<KernelDb> options, string actorType, bool protectsStarts)
    {
        PostgresRootAdmission.RequireActorType(actorType);
        return KernelDb.ForPostgresRootPublicEventHostQualification(options, new(actorType, protectsStarts));
    }

    /// <summary>Installs or verifies a PostgreSQL Event host with the specified actor type and start protection.</summary>
    public static Task InstallPublicEventHostQualificationAsync(DbContextOptions<KernelDb> options, string actorType, bool protectsStarts,
        CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreatePublicEventHostQualificationContext(options, actorType, protectsStarts), cancellationToken);

    /// <summary>Whether the selected PostgreSQL profile supports Event-triggered starts.</summary>
    public static bool TakesEventStarts(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootEventStarts;
    }

    /// <summary>Whether the selected PostgreSQL profile supports Event start receipt retention.</summary>
    public static bool RetainsEventStartReceipts(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootEventStartReceipts;
    }

    /// <summary>Whether the selected PostgreSQL profile supports ordered Event replay.</summary>
    public static bool TakesOrderedEventScan(Func<KernelDb> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        using var db = create();
        return db.UsesRootEventStartScan;
    }

    /// <summary>Binds trusted Event registration and relay to the selected database and tenant.</summary>
    public static IEventStartAdmissions BindEventStarts(Func<KernelDb> create, Contract.BoundScope scope, PostgresDirectRequests? requests = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(scope);
        requests?.RequireEventBinding(create);
        using var db = create();
        if (!db.UsesRootEventStarts) throw new StoreFormatException("The selected PostgreSQL profile does not support Event starts.");
        return new EventStarts(new PostgresRootAdmission(create, TimeSpan.Zero, pressure: requests?.Pressure), scope.Tenant);
    }

    private sealed class EventStarts(PostgresRootAdmission admission, string tenant) : IOrderedEventStartAdmissions
    {
        public Task<EventRegistrationResult> ApplyAsync(string slot, long revision, IReadOnlyList<EventStartRegistration> registrations, CancellationToken cancellationToken) =>
            admission.ApplyEventStartsAsync(tenant, slot, revision, registrations, cancellationToken);

        public Task<IReadOnlyList<EventStartTarget>> ListAsync(string? after, CancellationToken cancellationToken) =>
            admission.ListEventStartsAsync(tenant, after, cancellationToken);

        public Task<long> ReadPendingTailAsync(CancellationToken cancellationToken) =>
            admission.ReadEventStartTailAsync(tenant, cancellationToken);

        public Task<IReadOnlyList<EventStartScanItem>> ReadPendingPageAsync(long after, long through, int limit, CancellationToken cancellationToken) =>
            admission.ReadEventStartPageAsync(tenant, after, through, limit, cancellationToken);

        public Task<Contract.Admission> AdmitAsync(string target, Func<EventStartTarget, Contract.StartSpec> start,
            ProtectRootStartAsync protect, CancellationToken cancellationToken) =>
            admission.AdmitEventStartAsync(tenant, target, start, protect, cancellationToken);

        public Task<int> PruneSettledAsync(CancellationToken cancellationToken) =>
            admission.PruneSettledEventStartsAsync(tenant, cancellationToken);

        public Task<bool> ConfirmDrainedAsync(string slot, long revision, CancellationToken cancellationToken) =>
            admission.ConfirmEventStartRegistrationDrainedAsync(tenant, slot, revision, cancellationToken);
    }
}
