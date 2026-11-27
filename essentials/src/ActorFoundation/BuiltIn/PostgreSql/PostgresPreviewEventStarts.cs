using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

public static partial class PostgresPreviewHosting
{
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

    /// <summary>Binds trusted Event registration and relay to the selected qualification database and tenant.</summary>
    public static IEventStartAdmissions BindEventStarts(Func<KernelDb> create, Contract.BoundScope scope, PostgresDirectRequests? requests = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(scope);
        requests?.RequireEventBinding(create);
        using var db = create();
        if (!db.UsesRootEventStarts) throw new StoreFormatException("Event starts require their private qualification profile.");
        return new EventStarts(new PostgresRootAdmission(create, TimeSpan.Zero, pressure: requests?.Pressure), scope.Tenant);
    }

    private sealed class EventStarts(PostgresRootAdmission admission, string tenant) : IEventStartAdmissions
    {
        public Task<EventRegistrationResult> ApplyAsync(string slot, long revision, IReadOnlyList<EventStartRegistration> registrations, CancellationToken cancellationToken) =>
            admission.ApplyEventStartsAsync(tenant, slot, revision, registrations, cancellationToken);

        public Task<IReadOnlyList<EventStartTarget>> ListAsync(string? after, CancellationToken cancellationToken) =>
            admission.ListEventStartsAsync(tenant, after, cancellationToken);

        public Task<Contract.Admission> AdmitAsync(string target, Func<EventStartTarget, Contract.StartSpec> start,
            ProtectRootStartAsync protect, CancellationToken cancellationToken) =>
            admission.AdmitEventStartAsync(tenant, target, start, protect, cancellationToken);

        public Task<int> PruneSettledAsync(CancellationToken cancellationToken) =>
            admission.PruneSettledEventStartsAsync(tenant, cancellationToken);

        public Task<bool> ConfirmDrainedAsync(string slot, long revision, CancellationToken cancellationToken) =>
            admission.ConfirmEventStartRegistrationDrainedAsync(tenant, slot, revision, cancellationToken);
    }
}
