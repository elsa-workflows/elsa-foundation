using System.Collections.Concurrent;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// The runnability entries of every shell in this process whose distributed runtime is active (spec 184, FR-008), for
/// the one <see cref="WorkflowRuntimeRunnabilitySource"/> the host's membership publishes from.
/// </summary>
/// <remarks>
/// <para>
/// Membership is per host and registries are per shell, so the host registers one instance of this registry and every
/// shell container, built from copies of the host's registrations, shares it. Each shell's placement pump records its
/// entry when its runtime activates and on every sweep where its registries changed, and removes it when the shell
/// stops. A shell only ever touches its own container instance's entry, so an old generation that stops during a
/// reload leaves the new generation's entry in place.
/// </para>
/// <para>
/// Entries are snapshots the shell computed from its own registries: a publish never calls into a shell, which may be
/// stopping. A snapshot lags its registries by at most one sweep, which only ever misleads a diagnostic, because a member
/// never consults the section for its own claims (FR-011).
/// </para>
/// </remarks>
public sealed class ShellRunnabilityRegistry
{
    private readonly ConcurrentDictionary<string, RunnabilityEntry> _entries = new(StringComparer.Ordinal);

    public IReadOnlyList<RunnabilityEntry> Entries =>
        _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();

    /// <summary>Records the entry of <paramref name="shell"/>, and returns whether it differs from the one before.</summary>
    public bool Record(DistributedRuntimeShell shell, RunnabilityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(entry);
        var previous = _entries.TryGetValue(shell.Instance, out var existing) ? existing : null;
        _entries[shell.Instance] = entry;
        return !entry.Equals(previous);
    }

    /// <summary>Removes the entry of <paramref name="shell"/>, and returns whether it had one.</summary>
    public bool Remove(DistributedRuntimeShell shell) => _entries.TryRemove(shell.Instance, out _);
}

/// <summary>The one source of the runnability section (spec 184, FR-008; spec 183, FR-014).</summary>
public sealed class WorkflowRuntimeRunnabilitySource(ShellRunnabilityRegistry registry) : IMemberReportSource<RunnabilitySection>
{
    public ValueTask<RunnabilitySection> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RunnabilitySection(registry.Entries));
    }
}

/// <summary>
/// Builds a shell's runnability entry from exactly the registries <see cref="IRuntimeRequirementChecker"/> reads, so the
/// entry and the checker agree on every requirement (spec 184, FR-008): every runtime consumer capability with its
/// supported schema versions, the durable-value storage-driver registry's keys, and every alias the type registry
/// resolves. The type registry resolves by alias only, with no CLR type-name fallback, so its registered aliases are
/// exactly the ones the checker accepts.
/// </summary>
public static class RunnabilityEntryFactory
{
    /// <summary>The entry of the shell whose services <paramref name="services"/> resolve from.</summary>
    /// <remarks>
    /// The database identity is the one the shell's Runtime EF module read from its finalization record (spec 181,
    /// FR-001). Before the module has read one, and in a shell with no Runtime EF module, the entry names none, which
    /// applies to every database: the conservative direction.
    /// </remarks>
    public static RunnabilityEntry Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var consumers = services.GetServices<IRuntimeActivityConsumerCapability>()
            .Select(capability => new RunnableConsumer(capability.ConsumerKey, capability.SupportedSchemaVersions));
        var drivers = services.GetService<IRuntimeDurableValueStorageDriverRegistry>()?.DriverKeys ?? [];
        var typeRegistry = services.GetService<IWellKnownTypeRegistry>();
        var aliases = typeRegistry is null
            ? []
            : typeRegistry.ListTypes()
                .Select(type => typeRegistry.TryGetAlias(type, out var alias) ? alias : null)
                .Where(alias => !string.IsNullOrWhiteSpace(alias) && typeRegistry.TryGetTypeOrDefault(alias, out _))
                .Select(alias => alias!);
        return new RunnabilityEntry(consumers, drivers, aliases, services.GetService<IRuntimeSchemaFinalization>()?.DatabaseIdentity);
    }
}

public static class WorkflowRuntimeRunnabilityServiceCollectionExtensions
{
    /// <summary>
    /// Reports what this host's workflow runtimes can activate through its cluster membership (spec 184, FR-008). Compose
    /// it once, on the host container and never per shell, beside the membership provider, so every shell of the process
    /// records its entry in one registry and the host's report carries all of them. It also registers the in-process
    /// membership default unless a provider is already registered.
    /// </summary>
    /// <remarks>
    /// A shell that runs the distributed runtime without this on its host still publishes its own entry through the
    /// in-process default. A host that composes a durable provider must compose this too, or its report carries no
    /// runnability section and other members read it as able to run nothing: a diagnostic that over-reports, never a
    /// wrong claim, because a member only ever claims on its own registries (FR-011).
    /// </remarks>
    public static IServiceCollection AddWorkflowRuntimeRunnabilityReport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // An instance, not a type: every shell container is built from copies of these registrations, and a copied
        // instance registration is the one object all of them share.
        services.TryAddSingleton(new ShellRunnabilityRegistry());
        return services.AddShellRunnabilitySource();
    }

    /// <summary>What a shell's distributed runtime needs to publish its entry, whether or not its host composed the
    /// report: without the host's registry, the shell has its own and publishes through the in-process default.</summary>
    internal static IServiceCollection AddShellRunnabilitySource(this IServiceCollection services)
    {
        services.TryAddSingleton<ShellRunnabilityRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemberReportSource<RunnabilitySection>, WorkflowRuntimeRunnabilitySource>());
        return services.TryAddInProcessClusterMembership();
    }
}
