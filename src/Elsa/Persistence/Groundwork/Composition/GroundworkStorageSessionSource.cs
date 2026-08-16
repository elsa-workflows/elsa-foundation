using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Admits all contributed v2 units during both plain-host and shell startup and opens non-owning
/// sessions from the selected provider connection. On-demand admission keeps direct test hosts safe
/// without weakening startup admission in production.
/// </summary>
public sealed class GroundworkStorageSessionSource(
    IServiceProvider services,
    GroundworkStorageUnitRegistry registry) :
    IGroundworkStorageSessionSource,
    IHostedService,
    IShellInitializer
{
    private readonly Lock gate = new();
    private readonly HashSet<(string Target, string Fingerprint)> admitted = [];

    public Task StartAsync(CancellationToken cancellationToken) => AdmitAllAsync(cancellationToken);

    public Task InitializeAsync(CancellationToken cancellationToken = default) => AdmitAllAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        var registration = registry.Require(unitId, targetName);
        var connection = RequireConnection(registration.TargetName);
        Admit(connection, registration);
        return connection.OpenSession(registration.Unit, access);
    }

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(unitIds);
        if (unitIds.Count == 0)
            throw new ArgumentException("At least one declared storage unit is required.", nameof(unitIds));

        var target = GroundworkTargetNames.Normalize(targetName);
        var connection = RequireConnection(target);
        var units = unitIds.Select(unitId => registry.Require(unitId, target)).ToArray();
        foreach (var registration in units)
            Admit(connection, registration);
        return connection.BeginUnitOfWork(access, options, units.Select(candidate => candidate.Unit).ToArray());
    }

    public StorageUnit Unit(string unitId, string? targetName = null) =>
        registry.Require(unitId, targetName).Unit;

    private Task AdmitAllAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in registry.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Admit(RequireConnection(registration.TargetName), registration);
        }

        return Task.CompletedTask;
    }

    private void Admit(
        IStorageProviderConnection connection,
        GroundworkStorageUnitRegistration registration)
    {
        var key = (registration.TargetName, registration.Fingerprint);
        lock (gate)
        {
            if (admitted.Contains(key))
                return;
            connection.Schema.Apply(registration.Unit);
            admitted.Add(key);
        }
    }

    private IStorageProviderConnection RequireConnection(string targetName)
    {
        var keyed = services.GetKeyedService<IStorageProviderConnection>(targetName);
        if (keyed is not null)
            return keyed;
        if (GroundworkTargetNames.IsDefault(targetName) &&
            services.GetService<IStorageProviderConnection>() is { } @default)
        {
            return @default;
        }

        throw new InvalidOperationException(
            $"Groundwork target '{targetName}' has no v2 provider connection. " +
            "Register the selected provider connection before admitting storage units.");
    }
}
