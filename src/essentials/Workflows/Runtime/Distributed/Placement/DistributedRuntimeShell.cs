using CShells;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// The shell a distributed runtime instance runs in. Registries are per shell while membership is per host (spec 184,
/// FR-008), so each shell checks its own registries, publishes its own runnability entry, and runs its own join sweep.
/// </summary>
/// <param name="Name">The shell's stable name: the same across reloads of the shell, so a reload is not a first
/// activation (FR-022).</param>
/// <param name="Instance">This container's own identity: a new generation of the same shell gets a new one, so an old
/// generation that stops removes only its own runnability entry.</param>
public sealed record DistributedRuntimeShell(string Name, string Instance)
{
    /// <summary>The name a runtime composed outside CShells runs under.</summary>
    public const string DefaultName = "default";

    /// <summary>The shell of the container <paramref name="services"/> resolve from.</summary>
    public static DistributedRuntimeShell From(IServiceProvider services) =>
        new(services.GetService<ShellSettings>()?.Id.ToString() is { Length: > 0 } name ? name : DefaultName, Guid.NewGuid().ToString("N"));
}
