using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Admits all contributed v2 units during both plain-host and shell startup and reuses non-owning
/// sessions from the selected provider connection for each unit and access context. On-demand admission
/// keeps direct test hosts safe without weakening startup admission in production.
/// </summary>
public sealed class GroundworkStorageSessionSource(
    IServiceProvider services,
    GroundworkStorageUnitRegistry registry) :
    IGroundworkStorageSessionSource,
    IGroundworkStorageCapabilitySource,
    IHostedService,
    IShellInitializer
{
    private readonly Lock admissionGate = new();
    private readonly Lock sessionGate = new();

    /// <summary>
    /// One observer for the source's lifetime, forwarded to every session and unit of work this source
    /// opens. Lifetime-scoped rather than per-call for two reasons that reinforce each other: sessions are
    /// cached, so a per-call observer would let the second caller of a cached session silently inherit the
    /// first caller's observer — the same shape as the retired first-staged-write lookup — and putting the
    /// observer in the cache key would multiply cached sessions (and their connections) per observer for
    /// nothing any consumer wants. Resolved lazily because this source is constructed by DI before the
    /// host has decided whether anything observes at all; null means unobserved, which is the production
    /// default.
    /// </summary>
    private readonly Lazy<IProviderCommandObserver?> observer =
        new(() => services.GetService<IProviderCommandObserver>());
    private readonly HashSet<(string Target, string Fingerprint)> admitted = [];
    private readonly Dictionary<
        (string Target, string UnitId, string Fingerprint, StorageAccess Access),
        IStorageSession> sessions = [];

    public Task StartAsync(CancellationToken cancellationToken) => AdmitAllAsync(cancellationToken);

    public Task InitializeAsync(CancellationToken cancellationToken = default) => AdmitAllAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        var registration = registry.Require(unitId, targetName);
        var connection = RequireConnection(registration.TargetName);
        Admit(connection, registration);
        var key = (
            registration.TargetName,
            registration.Unit.Id.Value,
            registration.Fingerprint,
            access);
        lock (sessionGate)
        {
            if (sessions.TryGetValue(key, out var session))
                return session;

            session = connection.OpenSession(registration.Unit, access, observer.Value);
            sessions.Add(key, session);
            return session;
        }
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
        // The observed overload sits beside the unobserved ones; both forwarding points matter, because the
        // checkpoint commit path issues its provider commands through units of work, not sessions — an
        // observer forwarded only at OpenSession would count zero for exactly the workload it exists to
        // measure, while still reporting itself present and exact.
        return connection.BeginUnitOfWork(access, options, observer.Value, units.Select(candidate => candidate.Unit).ToArray());
    }

    public StorageUnit Unit(string unitId, string? targetName = null) =>
        registry.Require(unitId, targetName).Unit;

    public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) =>
        RequireConnection(GroundworkTargetNames.Normalize(targetName)).Capabilities;

    private Task AdmitAllAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in registry.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Admit(RequireConnection(registration.TargetName), registration, revalidate: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies a unit to its target. <paramref name="revalidate"/> bypasses the admitted-set memo so the
    /// declaration is checked against the live schema again.
    /// </summary>
    /// <remarks>
    /// The memo exists for <see cref="GetSession"/>, which admits on every session open and must not pay a
    /// schema round-trip each time. It is wrong for the explicit startup and initialize entry points: those
    /// are how a host asks whether its store is usable, and a memo answers from the last time it looked
    /// rather than from the database. Schema that drifted after first admission -- an index dropped by hand,
    /// a restore from an older dump -- would then be reported ready while every query route that needs the
    /// missing index is already broken. Applying is idempotent against an unchanged schema, so re-checking
    /// costs one pass and is what makes readiness mean anything.
    /// </remarks>
    private void Admit(
        IStorageProviderConnection connection,
        GroundworkStorageUnitRegistration registration,
        bool revalidate = false)
    {
        var key = (registration.TargetName, registration.Fingerprint);
        lock (admissionGate)
        {
            if (!revalidate && admitted.Contains(key))
                return;
            try
            {
                connection.Schema.Apply(registration.Unit);
            }
            catch (Exception exception)
            {
                admitted.Remove(key);
                throw new InvalidOperationException(
                    $"Groundwork unit '{registration.Unit.Id.Value}' failed admission on target " +
                    $"'{registration.TargetName}': {exception.Message}",
                    exception);
            }

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
