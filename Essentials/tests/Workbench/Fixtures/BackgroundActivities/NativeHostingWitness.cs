using System.Data.Common;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workbench.Tests.BackgroundActivities;

internal static class NativeHostingWitnessRegistration
{
    public static void Register(IServiceCollection services, string? journalDirectory, string role)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(journalDirectory) || !Path.IsPathFullyQualified(journalDirectory))
            throw new InvalidOperationException("Native hosting observation requires an absolute witness journal directory.");

        var placementBackend = ExecutionPlacementStoreBackend.Find(services)
            ?? throw new InvalidOperationException("Native hosting observation requires a selected execution placement backend.");
        placementBackend.EnsureOwnsRegisteredContract(services);
        if (placementBackend.Name != ExecutionPlacementStoreBackend.EntityFramework)
            throw new InvalidOperationException("Native hosting observation requires the EF execution placement backend.");

        var commandBackend = ExecutionCommandTransportBackend.Find(services)
            ?? throw new InvalidOperationException("Native hosting observation requires a selected command transport backend.");
        commandBackend.EnsureOwnsRegisteredContract(services);
        if (commandBackend.Name != ExecutionCommandTransportBackend.EntityFramework)
            throw new InvalidOperationException("Native hosting observation requires the EF command transport backend.");

        RequireOne<IClusterMembership>(services);
        RequireOne<IWorkflowExecutionActorProvider>(services);
        RequireOne<IExecutionPlacementService>(services);

        var ownership = RequireScoped<IRuntimeExecutionOwnershipService>(services);
        var placement = RequireScoped<IExecutionPlacementStore>(services);
        var transport = RequireScoped<IExecutionCommandTransport>(services);

        services.AddOptions<NativeHostingObservationSettings>()
            .Configure<IConfiguration>((settings, configuration) => settings.Load(configuration, journalDirectory, role))
            .Validate(settings => settings.ConfigurationError is null,
                "Native hosting observation requires SQLite providers, absolute shared Data Source paths, and an absolute lock folder.")
            .ValidateOnStart();

        ReplaceScoped<IRuntimeExecutionOwnershipService>(services, ownership,
            (inner, provider, settings, ownsInner) => new NativeHostingOwnershipWitness(inner, provider, settings, ownsInner));
        var decoratedPlacement = ReplaceScoped<IExecutionPlacementStore>(services, placement,
            (inner, provider, settings, ownsInner) => new NativeHostingPlacementStoreWitness(inner, provider, settings, ownsInner));
        var decoratedTransport = ReplaceScoped<IExecutionCommandTransport>(services, transport,
            (inner, provider, settings, ownsInner) => new NativeHostingCommandTransportWitness(inner, provider, settings, ownsInner));

        ReplaceBackendMarker(services, placementBackend,
            new ExecutionPlacementStoreBackend(placementBackend.Name, decoratedPlacement));
        ReplaceBackendMarker(services, commandBackend,
            new ExecutionCommandTransportBackend(commandBackend.Name, decoratedTransport));
    }

    private static void RequireOne<TService>(IServiceCollection services)
    {
        var count = services.Count(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);
        if (count != 1)
            throw new InvalidOperationException($"Native hosting observation requires exactly one {typeof(TService).Name} registration, found {count}.");
    }

    private static ServiceDescriptor RequireScoped<TService>(IServiceCollection services)
    {
        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService)
            .ToArray();
        if (descriptors.Length != 1 || descriptors[0].Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException($"Native hosting observation requires exactly one scoped {typeof(TService).Name} registration.");
        return descriptors[0];
    }

    private static ServiceDescriptor ReplaceScoped<TService>(
        IServiceCollection services,
        ServiceDescriptor original,
        Func<TService, IServiceProvider, NativeHostingObservationSettings, bool, TService> decorate)
        where TService : class
    {
        var ownsInner = original.ImplementationInstance is null;
        var replacement = ServiceDescriptor.Scoped<TService>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<NativeHostingObservationSettings>>().Value;
            var inner = CreateInner<TService>(original, provider);
            return decorate(inner, provider, settings, ownsInner)
                   ?? throw new InvalidOperationException($"The native hosting decorator for {typeof(TService).Name} returned null.");
        });
        services.Remove(original);
        services.Add(replacement);
        return replacement;
    }

    private static TService CreateInner<TService>(ServiceDescriptor descriptor, IServiceProvider provider)
        where TService : class
    {
        var inner = descriptor.ImplementationFactory is not null
            ? descriptor.ImplementationFactory(provider)
            : descriptor.ImplementationType is not null
                ? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType)
                : descriptor.ImplementationInstance;
        return inner as TService
               ?? throw new InvalidOperationException($"The native hosting witness could not create {typeof(TService).Name}.");
    }

    private static void ReplaceBackendMarker<TBackend>(IServiceCollection services, TBackend oldBackend, TBackend newBackend)
        where TBackend : class
    {
        var marker = services.SingleOrDefault(descriptor => ReferenceEquals(descriptor.ImplementationInstance, oldBackend))
                     ?? throw new InvalidOperationException($"The native hosting witness could not find the {typeof(TBackend).Name} marker descriptor.");
        services.Remove(marker);
        if (newBackend is ExecutionPlacementStoreBackend placement)
            ExecutionPlacementStoreBackend.Register(services, placement);
        else if (newBackend is ExecutionCommandTransportBackend transport)
            ExecutionCommandTransportBackend.Register(services, transport);
        else
            throw new InvalidOperationException($"Unsupported native hosting backend marker {typeof(TBackend).Name}.");
    }
}

internal sealed class NativeHostingObservationSettings
{
    private const string FeatureRoot = "CShells:Shells:default:Features";

    public string JournalDirectory { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string RuntimeProvider { get; private set; } = string.Empty;
    public string MembershipProvider { get; private set; } = string.Empty;
    public string PlacementProvider { get; private set; } = string.Empty;
    public string CommandTransportProvider { get; private set; } = string.Empty;
    public string ConfiguredRuntimeDataSource { get; private set; } = string.Empty;
    public string ConfiguredMembershipDataSource { get; private set; } = string.Empty;
    public string ConfiguredPlacementDataSource { get; private set; } = string.Empty;
    public string ConfiguredCommandTransportDataSource { get; private set; } = string.Empty;
    public string LocksFolderPath { get; private set; } = string.Empty;
    public string? ConfigurationError { get; private set; }

    public void Load(IConfiguration configuration, string journalDirectory, string role)
    {
        JournalDirectory = journalDirectory;
        Role = role;

        RuntimeProvider = FeatureValue(configuration, "WorkflowsRuntimeEntityFrameworkCore", "Provider") ?? "Sqlite";
        MembershipProvider = configuration["Elsa:Cluster:Membership:EntityFrameworkCore:Provider"] ?? "Sqlite";
        PlacementProvider = FeatureValue(configuration, "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "Provider") ?? "Sqlite";
        CommandTransportProvider = FeatureValue(configuration, "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence", "Provider") ?? "Sqlite";

        var runtimeConnection = FeatureConnection(configuration, "WorkflowsRuntimeEntityFrameworkCore", "Elsa");
        var membershipConnection = NamedConnection(configuration,
            configuration["Elsa:Cluster:Membership:EntityFrameworkCore:ConnectionString"],
            configuration["Elsa:Cluster:Membership:EntityFrameworkCore:ConnectionName"]);
        var placementConnection = FeatureConnection(configuration, "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "Elsa");
        var commandTransportConnection = FeatureConnection(configuration, "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence", "Elsa");

        if (!TryGetSqliteDataSource(RuntimeProvider, runtimeConnection, out var runtimeDataSource) ||
            !TryGetSqliteDataSource(MembershipProvider, membershipConnection, out var membershipDataSource) ||
            !TryGetSqliteDataSource(PlacementProvider, placementConnection, out var placementDataSource) ||
            !TryGetSqliteDataSource(CommandTransportProvider, commandTransportConnection, out var commandTransportDataSource))
        {
            ConfigurationError = "A configured provider is not SQLite or an absolute Data Source was not available.";
            return;
        }

        ConfiguredRuntimeDataSource = runtimeDataSource!;
        ConfiguredMembershipDataSource = membershipDataSource!;
        ConfiguredPlacementDataSource = placementDataSource!;
        ConfiguredCommandTransportDataSource = commandTransportDataSource!;
        LocksFolderPath = configuration["CShells:Shells:default:Features:FileSystemDistributedLocking:LocksFolderPath"] ?? string.Empty;
        if (!Path.IsPathFullyQualified(LocksFolderPath))
            ConfigurationError = "The file-system distributed lock folder must be an absolute path.";
    }

    private static string? FeatureValue(IConfiguration configuration, string feature, string setting) =>
        configuration[$"{FeatureRoot}:{feature}:{setting}"];

    private static string? FeatureConnection(IConfiguration configuration, string feature, string defaultConnectionName)
    {
        var connectionString = FeatureValue(configuration, feature, "ConnectionString");
        var connectionName = FeatureValue(configuration, feature, "ConnectionName");
        return NamedConnection(configuration, connectionString, connectionName ?? defaultConnectionName);
    }

    private static string? NamedConnection(IConfiguration configuration, string? connectionString, string? connectionName)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;
        return configuration.GetConnectionString(string.IsNullOrWhiteSpace(connectionName) ? "Elsa" : connectionName);
    }

    private static bool TryGetSqliteDataSource(string provider, string? connectionString, out string? dataSource)
    {
        dataSource = null;
        if (!string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            if (builder.Keys.Cast<string>().Any(key => !key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) &&
                                                       !key.Equals("Pooling", StringComparison.OrdinalIgnoreCase)))
                return false;
            if (!builder.TryGetValue("Data Source", out var value))
                return false;

            var path = Convert.ToString(value);
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                return false;
            dataSource = Path.GetFullPath(path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal abstract class NativeHostingOwnedService<TService>(TService inner, IServiceProvider services, NativeHostingObservationSettings settings, bool ownsInner)
    : IDisposable, IAsyncDisposable
    where TService : class
{
    private int _disposed;

    protected TService Inner { get; } = inner;
    protected IServiceProvider Services { get; } = services;
    protected NativeHostingObservationSettings Settings { get; } = settings;
    internal Type BackendType => Inner.GetType();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || !ownsInner)
            return;
        if (Inner is IDisposable disposable)
            disposable.Dispose();
        else if (Inner is IAsyncDisposable)
            throw new InvalidOperationException($"{typeof(TService).Name} requires asynchronous disposal.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || !ownsInner)
            return;
        if (Inner is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (Inner is IDisposable disposable)
            disposable.Dispose();
    }
}

internal sealed class NativeHostingOwnershipWitness(
    IRuntimeExecutionOwnershipService inner,
    IServiceProvider services,
    NativeHostingObservationSettings settings,
    bool ownsInner)
    : NativeHostingOwnedService<IRuntimeExecutionOwnershipService>(inner, services, settings, ownsInner), IRuntimeExecutionOwnershipService
{
    public async ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        var lease = await Inner.AcquireAsync(workflowExecutionId, cancellationToken);
        await NativeHostingWitnessRecorder.TryRecordCompositionAsync(Services, Settings);
        NativeHostingWitnessRecorder.Write(Settings, new
        {
            Stage = "ownership-acquired",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = Settings.Role,
            LocalHostId = NativeHostingWitnessRecorder.ReadLocalHostId(Services),
            ExecutionId = lease.WorkflowExecutionId,
            LeaseId = lease.LeaseId,
            OwnerId = lease.OwnerId,
            FencingToken = lease.FencingToken,
            LeaseExpiresAt = lease.ExpiresAt
        });
        return lease;
    }

    public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
        Inner.HeartbeatAsync(lease, cancellationToken);

    public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
        Inner.ReleaseAsync(lease, cancellationToken);

    public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default) =>
        Inner.EnsureCurrentAsync(workflowExecutionId, fencingToken, cancellationToken);
}

internal sealed class NativeHostingPlacementStoreWitness(
    IExecutionPlacementStore inner,
    IServiceProvider services,
    NativeHostingObservationSettings settings,
    bool ownsInner)
    : NativeHostingOwnedService<IExecutionPlacementStore>(inner, services, settings, ownsInner), IExecutionPlacementStore
{
    public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        Inner.FindAsync(workflowExecutionId, cancellationToken);

    public async ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var result = await Inner.TryClaimAsync(claim, now, cancellationToken);
        await NativeHostingWitnessRecorder.TryRecordCompositionAsync(Services, Settings);
        var membership = await NativeHostingWitnessRecorder.ReadFreshMembershipAsync(Services, cancellationToken);
        NativeHostingWitnessRecorder.Write(Settings, new
        {
            Stage = "placement-claimed",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = Settings.Role,
            LocalHostId = membership.LocalHostId,
            ExecutionId = claim.WorkflowExecutionId,
            ClaimantOwnerId = claim.OwnerId,
            ClaimOutcome = result.Outcome.ToString(),
            PlacementOwnerId = result.Lease.OwnerId,
            PlacementToken = result.Lease.PlacementToken,
            PlacementExpiresAt = result.Lease.ExpiresAt,
            FreshMembershipProviderType = membership.ProviderType,
            FreshMembershipProviderKind = membership.ProviderKind,
            FreshMembershipReadMode = membership.ReadMode,
            FreshMembershipJudgedAt = membership.JudgedAt,
            FreshMembershipMembers = membership.Members,
            FreshMembershipError = membership.Error
        });
        return result;
    }

    public ValueTask<ExecutionPlacementLease?> TryRenewAsync(ExecutionPlacementLease held, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        Inner.TryRenewAsync(held, now, expiresAt, cancellationToken);

    public async ValueTask<bool> ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default)
    {
        var released = await Inner.ReleaseAsync(lease, cancellationToken);
        // This independent post-release snapshot describes the fleet, not the read that authorized the release.
        var membership = await NativeHostingWitnessRecorder.ReadFreshMembershipAsync(Services, cancellationToken);
        NativeHostingWitnessRecorder.Write(Settings, new
        {
            Stage = "placement-released",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = Settings.Role,
            LocalHostId = membership.LocalHostId,
            ExecutionId = lease.WorkflowExecutionId,
            PlacementOwnerId = lease.OwnerId,
            PlacementToken = lease.PlacementToken,
            PlacementExpiresAt = lease.ExpiresAt,
            ReleaseResult = released,
            FreshMembershipProviderType = membership.ProviderType,
            FreshMembershipProviderKind = membership.ProviderKind,
            FreshMembershipReadMode = membership.ReadMode,
            FreshMembershipJudgedAt = membership.JudgedAt,
            FreshMembershipMembers = membership.Members,
            FreshMembershipError = membership.Error
        });
        return released;
    }

    public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default) =>
        Inner.ListOwnedAsync(request, cancellationToken);
}

internal sealed class NativeHostingCommandTransportWitness(
    IExecutionCommandTransport inner,
    IServiceProvider services,
    NativeHostingObservationSettings settings,
    bool ownsInner)
    : NativeHostingOwnedService<IExecutionCommandTransport>(inner, services, settings, ownsInner), IExecutionCommandTransport
{
    public async ValueTask<ExecutionCommandTransportItem> SendAsync(
        string workflowExecutionId,
        WorkflowExecutionCommandEnvelope envelope,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var membershipBefore = await NativeHostingWitnessRecorder.ReadFreshMembershipAsync(Services, cancellationToken);
        var placementBefore = await NativeHostingWitnessRecorder.ReadPlacementAsync(Services, workflowExecutionId, cancellationToken);
        var item = await Inner.SendAsync(workflowExecutionId, envelope, now, cancellationToken);
        var placementAfter = await NativeHostingWitnessRecorder.ReadPlacementAsync(Services, workflowExecutionId, cancellationToken);
        var membershipAfter = await NativeHostingWitnessRecorder.ReadFreshMembershipAsync(Services, cancellationToken);

        NativeHostingWitnessRecorder.Write(Settings, new
        {
            Stage = "transport-sent",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = Settings.Role,
            LocalHostId = membershipAfter.LocalHostId ?? membershipBefore.LocalHostId,
            CommandTransportProviderType = Inner.GetType().FullName ?? Inner.GetType().Name,
            PlacementServiceBeforeProviderType = placementBefore.ProviderType,
            PlacementServiceAfterProviderType = placementAfter.ProviderType,
            ExecutionId = item.WorkflowExecutionId,
            CommandKind = item.Envelope.Command.Kind.ToString(),
            CommandSource = NativeHostingWitnessRecorder.ReadCommandSource(item.Envelope),
            EnvelopeId = item.Envelope.EnvelopeId,
            TransportItemId = item.TransportItemId,
            CommandEnqueuedAt = item.Envelope.Command.EnqueuedAt,
            TransportEnqueuedAt = item.EnqueuedAt,
            PlacementOwnerId = placementBefore.OwnerId,
            PlacementToken = placementBefore.PlacementToken,
            PlacementExpiresAt = placementBefore.ExpiresAt,
            PlacementBeforeError = placementBefore.Error,
            PlacementAfterOwnerId = placementAfter.OwnerId,
            PlacementAfterToken = placementAfter.PlacementToken,
            PlacementAfterExpiresAt = placementAfter.ExpiresAt,
            PlacementAfterError = placementAfter.Error,
            MembershipBeforeProviderType = membershipBefore.ProviderType,
            MembershipBeforeProviderKind = membershipBefore.ProviderKind,
            MembershipBeforeReadMode = membershipBefore.ReadMode,
            MembershipBeforeJudgedAt = membershipBefore.JudgedAt,
            MembershipBeforeMembers = membershipBefore.Members,
            MembershipBeforeError = membershipBefore.Error,
            MembershipAfterProviderType = membershipAfter.ProviderType,
            MembershipAfterProviderKind = membershipAfter.ProviderKind,
            MembershipAfterReadMode = membershipAfter.ReadMode,
            MembershipAfterJudgedAt = membershipAfter.JudgedAt,
            MembershipAfterMembers = membershipAfter.Members,
            MembershipAfterError = membershipAfter.Error
        });
        return item;
    }

    public async ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        var items = await Inner.LeaseAsync(workflowExecutionId, ownerId, now, leaseDuration, maxItems, cancellationToken);
        foreach (var item in items)
        {
            NativeHostingWitnessRecorder.Write(Settings, new
            {
                Stage = "transport-leased",
                RecordedAt = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                Role = Settings.Role,
                LocalHostId = NativeHostingWitnessRecorder.ReadLocalHostId(Services),
                ExecutionId = item.WorkflowExecutionId,
                TransportItemId = item.TransportItemId,
                CommandKind = item.Envelope.Command.Kind.ToString(),
                CommandSource = NativeHostingWitnessRecorder.ReadCommandSource(item.Envelope),
                CommandEnqueuedAt = item.Envelope.Command.EnqueuedAt,
                TransportEnqueuedAt = item.EnqueuedAt,
                EnvelopeId = item.Envelope.EnvelopeId,
                LeaseOwnerId = item.LeasedByOwnerId,
                LeaseToken = item.LeaseToken,
                LeaseExpiresAt = item.LeaseExpiresAt
            });
        }
        return items;
    }

    public async ValueTask<bool> AckAsync(
        string workflowExecutionId,
        string transportItemId,
        string ownerId,
        long leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var acked = await Inner.AckAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);
        NativeHostingWitnessRecorder.Write(Settings, new
        {
            Stage = "transport-acked",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = Settings.Role,
            LocalHostId = NativeHostingWitnessRecorder.ReadLocalHostId(Services),
            ExecutionId = workflowExecutionId,
            TransportItemId = transportItemId,
            LeaseOwnerId = ownerId,
            LeaseToken = leaseToken,
            AckResult = acked
        });
        return acked;
    }

    public ValueTask<bool> ReleaseLeaseAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Inner.ReleaseLeaseAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);

    public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> PeekAsync(string workflowExecutionId, DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default) =>
        Inner.PeekAsync(workflowExecutionId, now, maxItems, cancellationToken);

    public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> ListLeasedAsync(string ownerId, DateTimeOffset now, int maxItems, string? workflowExecutionId = null, CancellationToken cancellationToken = default) =>
        Inner.ListLeasedAsync(ownerId, now, maxItems, workflowExecutionId, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default) =>
        Inner.ListPendingExecutionIdsAsync(now, maxItems, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int maxItems, int skip, CancellationToken cancellationToken = default) =>
        Inner.ListPendingExecutionIdsAsync(now, maxItems, skip, cancellationToken);

    public ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        Inner.CountPendingAsync(workflowExecutionId, cancellationToken);
}

internal static class NativeHostingWitnessRecorder
{
    private const string RecoverySourceMetadataKey = "runtime.recovery.source";

    public static async Task TryRecordCompositionAsync(IServiceProvider services, NativeHostingObservationSettings settings)
    {
        try
        {
            var membership = await ReadFreshMembershipAsync(services, CancellationToken.None);
            var actorProviderType = ResolveTypeName<IWorkflowExecutionActorProvider>(services);
            var placementServiceType = ResolveTypeName<IExecutionPlacementService>(services);
            var placementStore = services.GetRequiredService<IExecutionPlacementStore>();
            var commandTransport = services.GetRequiredService<IExecutionCommandTransport>();
            var placementBackend = services.GetRequiredService<ExecutionPlacementStoreBackend>();
            var commandBackend = services.GetRequiredService<ExecutionCommandTransportBackend>();
            var runtimeDatabase = ReadEfDatabase<RuntimeDbContext>(services);
            var membershipDatabase = ReadEfDatabase<ClusterMembershipDbContext>(services);
            var placementDatabase = ReadEfDatabase<ExecutionPlacementDbContext>(services);
            var commandTransportDatabase = ReadEfDatabase<ExecutionCommandTransportDbContext>(services);

            Write(settings, new
            {
                Stage = "composition",
                RecordedAt = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                Role = settings.Role,
                LocalHostId = membership.LocalHostId,
                ActorProviderType = actorProviderType,
                MembershipProviderType = membership.ProviderType,
                MembershipProviderKind = membership.ProviderKind,
                PlacementServiceType = placementServiceType,
                PlacementStoreBackend = placementBackend.Name,
                PlacementStoreBackendType = BackendTypeName(placementStore),
                CommandTransportBackend = commandBackend.Name,
                CommandTransportBackendType = BackendTypeName(commandTransport),
                FreshMembershipReadMode = membership.ReadMode,
                FreshMembershipJudgedAt = membership.JudgedAt,
                FreshMembershipMembers = membership.Members,
                FreshMembershipError = membership.Error,
                RuntimeProvider = runtimeDatabase.ProviderName,
                RuntimeDataSource = runtimeDatabase.DataSource,
                RuntimeDbConnectionType = runtimeDatabase.ConnectionType,
                RuntimeObservationError = runtimeDatabase.Error,
                ConfiguredRuntimeProvider = settings.RuntimeProvider,
                ConfiguredRuntimeDataSource = settings.ConfiguredRuntimeDataSource,
                MembershipProvider = membershipDatabase.ProviderName,
                MembershipDataSource = membershipDatabase.DataSource,
                MembershipDbConnectionType = membershipDatabase.ConnectionType,
                MembershipObservationError = membershipDatabase.Error,
                ConfiguredMembershipProvider = settings.MembershipProvider,
                ConfiguredMembershipDataSource = settings.ConfiguredMembershipDataSource,
                PlacementProvider = placementDatabase.ProviderName,
                PlacementDataSource = placementDatabase.DataSource,
                PlacementDbConnectionType = placementDatabase.ConnectionType,
                PlacementObservationError = placementDatabase.Error,
                ConfiguredPlacementProvider = settings.PlacementProvider,
                ConfiguredPlacementDataSource = settings.ConfiguredPlacementDataSource,
                CommandTransportProvider = commandTransportDatabase.ProviderName,
                CommandTransportDataSource = commandTransportDatabase.DataSource,
                CommandTransportDbConnectionType = commandTransportDatabase.ConnectionType,
                CommandTransportObservationError = commandTransportDatabase.Error,
                ConfiguredCommandTransportProvider = settings.CommandTransportProvider,
                ConfiguredCommandTransportDataSource = settings.ConfiguredCommandTransportDataSource,
                LocksFolderPath = settings.LocksFolderPath
            });
        }
        catch (Exception exception)
        {
            Write(settings, new
            {
                Stage = "composition",
                RecordedAt = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                Role = settings.Role,
                LocalHostId = ReadLocalHostId(services),
                CompositionError = exception.GetType().FullName
            });
        }
    }

    private static NativeHostingEfDatabaseObservation ReadEfDatabase<TContext>(IServiceProvider services)
        where TContext : DbContext
    {
        // Inspect the registered context's provider connection without opening it or serializing its connection string.
        string? providerName = null;
        string? connectionType = null;
        try
        {
            var context = services.GetRequiredService<TContext>();
            providerName = context.Database.ProviderName;
            if (!string.Equals(providerName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
                return new NativeHostingEfDatabaseObservation(providerName, null, null, "NonSqliteProvider");

            var connection = context.Database.GetDbConnection();
            connectionType = connection.GetType().FullName ?? connection.GetType().Name;
            var dataSource = connection.DataSource;
            if (string.IsNullOrWhiteSpace(dataSource) || !Path.IsPathFullyQualified(dataSource))
                return new NativeHostingEfDatabaseObservation(providerName, null, connectionType, "DataSourceNotAbsolute");

            return new NativeHostingEfDatabaseObservation(providerName, Path.GetFullPath(dataSource), connectionType, null);
        }
        catch (Exception exception)
        {
            return new NativeHostingEfDatabaseObservation(providerName, null, connectionType, exception.GetType().FullName);
        }
    }

    public static async Task<NativeHostingMembershipObservation> ReadFreshMembershipAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IClusterMembership? membership = null;
        string? localHostId = null;
        try
        {
            membership = services.GetRequiredService<IClusterMembership>();
            localHostId = membership.GetLocalStanding().Identity.HostId;
            var fleet = await membership.ReadFleetAsync(FleetReadMode.Fresh, cancellationToken);
            return new NativeHostingMembershipObservation(
                TypeName(membership),
                membership.ProviderKind.ToString(),
                localHostId,
                fleet.ReadMode.ToString(),
                fleet.JudgedAt,
                fleet.Members.Select(member => new NativeHostingMembershipMember(
                    member.Identity.HostId,
                    member.Status.ToString(),
                    member.IsLive,
                    member.IsDisplaced,
                    member.LastHeartbeatAt,
                    member.ExpiryPeriod)).ToArray(),
                null);
        }
        catch (Exception exception)
        {
            return new NativeHostingMembershipObservation(
                TypeName(membership),
                membership?.ProviderKind.ToString(),
                localHostId,
                FleetReadMode.Fresh.ToString(),
                null,
                null,
                exception.GetType().FullName);
        }
    }

    public static async Task<NativeHostingPlacementObservation> ReadPlacementAsync(IServiceProvider services, string executionId, CancellationToken cancellationToken)
    {
        IExecutionPlacementService? placementService = null;
        try
        {
            placementService = services.GetRequiredService<IExecutionPlacementService>();
            var lease = await placementService.FindOwnerAsync(executionId, cancellationToken);
            return lease is null
                ? new NativeHostingPlacementObservation(TypeName(placementService), null, null, null, null)
                : new NativeHostingPlacementObservation(TypeName(placementService), lease.OwnerId, lease.PlacementToken, lease.ExpiresAt, null);
        }
        catch (Exception exception)
        {
            return new NativeHostingPlacementObservation(TypeName(placementService), null, null, null, exception.GetType().FullName);
        }
    }

    public static string? ReadLocalHostId(IServiceProvider services)
    {
        try
        {
            return services.GetRequiredService<IClusterMembership>().GetLocalStanding().Identity.HostId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? ReadCommandSource(WorkflowExecutionCommandEnvelope envelope)
    {
        if (envelope.Command.Metadata.TryGetValue("source", out var commandSource))
            return commandSource;
        if (envelope.Metadata.TryGetValue("source", out var envelopeSource))
            return envelopeSource;
        if (envelope.Command.Metadata.TryGetValue(RecoverySourceMetadataKey, out var recoveryCommandSource))
            return recoveryCommandSource;
        return envelope.Metadata.TryGetValue(RecoverySourceMetadataKey, out var recoveryEnvelopeSource)
            ? recoveryEnvelopeSource
            : null;
    }

    public static void Write<T>(NativeHostingObservationSettings settings, T record)
    {
        var path = Path.Combine(settings.JournalDirectory, $"native-hosting.{settings.Role}.{Guid.NewGuid():N}.json");
        try
        {
            BackgroundWitnessJournal.TryWriteJsonAtomically(path, record);
        }
        catch (Exception)
        {
            // Instrumentation failures must not change a runtime result.
        }
    }

    private static string? ResolveTypeName<T>(IServiceProvider services)
    {
        try
        {
            return TypeName(services.GetRequiredService<T>());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BackendTypeName(object service) => service switch
    {
        NativeHostingOwnedService<IExecutionPlacementStore> placement => placement.BackendType.FullName ?? placement.BackendType.Name,
        NativeHostingOwnedService<IExecutionCommandTransport> transport => transport.BackendType.FullName ?? transport.BackendType.Name,
        _ => TypeName(service) ?? string.Empty
    };

    private static string? TypeName(object? value) => value?.GetType().FullName ?? value?.GetType().Name;
}

internal sealed record NativeHostingMembershipObservation(
    string? ProviderType,
    string? ProviderKind,
    string? LocalHostId,
    string ReadMode,
    DateTimeOffset? JudgedAt,
    NativeHostingMembershipMember[]? Members,
    string? Error);

internal sealed record NativeHostingMembershipMember(
    string HostId,
    string Status,
    bool IsLive,
    bool IsDisplaced,
    DateTimeOffset LastHeartbeatAt,
    TimeSpan ExpiryPeriod);

internal sealed record NativeHostingPlacementObservation(
    string? ProviderType,
    string? OwnerId,
    long? PlacementToken,
    DateTimeOffset? ExpiresAt,
    string? Error);

internal sealed record NativeHostingEfDatabaseObservation(
    string? ProviderName,
    string? DataSource,
    string? ConnectionType,
    string? Error);
