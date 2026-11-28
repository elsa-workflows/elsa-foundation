using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

public static partial class PostgresPreviewHosting
{
    /// <summary>Selects the private authored standalone Event store for one ordinary publisher scope.</summary>
    public static KernelDb CreateAuthoredStandaloneEventQualificationContext(DbContextOptions<KernelDb> options,
        string actorType, bool protectsStarts, Contract.BoundScope publisherScope)
    {
        ArgumentNullException.ThrowIfNull(publisherScope);
        if (publisherScope.IsHostIsolated)
            throw new ArgumentException("An isolated scope cannot publish standalone Events.", nameof(publisherScope));
        RootPublicEventHost.RequireConfiguredActorType(actorType);
        return KernelDb.ForPostgresAuthoredStandaloneEventQualification(options,
            new(actorType, protectsStarts) { AuthoredStandaloneTenant = publisherScope.Tenant });
    }

    /// <summary>Installs or verifies the private authored standalone Event store.</summary>
    public static Task InstallAuthoredStandaloneEventQualificationAsync(DbContextOptions<KernelDb> options, string actorType,
        bool protectsStarts, Contract.BoundScope publisherScope, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreateAuthoredStandaloneEventQualificationContext(options, actorType, protectsStarts, publisherScope),
            cancellationToken);

    /// <summary>Selects the private PostgreSQL profile for prepared standalone Events and held responses.</summary>
    public static KernelDb CreateRetainedStandaloneSourcesQualificationContext(DbContextOptions<KernelDb> options,
        string actorType, Contract.BoundScope publisherScope)
    {
        ArgumentNullException.ThrowIfNull(publisherScope);
        if (publisherScope.IsHostIsolated)
            throw new ArgumentException("An isolated scope cannot publish standalone Events.", nameof(publisherScope));
        RootPublicEventHost.RequireConfiguredActorType(actorType);
        return KernelDb.ForPostgresRetainedStandaloneSourcesQualification(options,
            new(actorType, true) { AuthoredStandaloneTenant = publisherScope.Tenant });
    }

    /// <summary>Installs or verifies the private prepared standalone sources profile.</summary>
    public static Task InstallRetainedStandaloneSourcesQualificationAsync(DbContextOptions<KernelDb> options, string actorType,
        Contract.BoundScope publisherScope, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreateRetainedStandaloneSourcesQualificationContext(options, actorType, publisherScope),
            cancellationToken);

    /// <summary>Binds a reader and acknowledgement edge to the installed ordinary publisher scope.</summary>
    public static Contract.IAuthoredStandaloneEventOutbox BindAuthoredStandaloneEvents(Func<KernelDb> open, Contract.BoundScope scope)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(scope);
        return new PostgresRootAdmission(open, TimeSpan.Zero).BindAuthoredStandaloneEvents(scope);
    }
}
