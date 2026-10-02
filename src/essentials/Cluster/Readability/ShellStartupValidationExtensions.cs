using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.Readability;

public static class ShellStartupValidationExtensions
{
    /// <summary>
    /// Makes every shell run the options validators registered with <c>ValidateOnStart</c> in its own container as it
    /// activates, so a failing one refuses the activation. Composed once, on the host container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generic host runs <see cref="IStartupValidator"/> as it starts, against its own container only. CShells builds
    /// each shell container separately and runs nothing but <see cref="IShellInitializer"/>s when a shell activates, so a
    /// check a shell feature registers with <c>ValidateOnStart</c> is otherwise never run there (#2331): a real options
    /// type is then validated at its first use, often a request, and a marker options type nothing resolves is never
    /// validated at all.
    /// </para>
    /// <para>
    /// CShells copies this registration into every shell container, where it resolves that container's validator. That
    /// validator also holds the checks the host registered, which CShells copied in too, so each of those runs again per
    /// shell, against the options the shell's own container resolves: a shell feature may configure the same options.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddShellStartupValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Any(descriptor => descriptor.ServiceType == typeof(ShellStartupValidation))
            ? services
            : services.AddShellInitializer<ShellStartupValidation>(LifecyclePhase.Prepare, ShellStartupValidation.PrepareOrder);
    }
}

/// <summary>
/// Runs a shell container's <see cref="IStartupValidator"/> first in the <see cref="LifecyclePhase.Prepare"/> phase,
/// below the lowest order a shipped feature registers at, so a composition the shell's validators refuse fails the
/// activation before a provider binding is checked or a migration runs.
/// </summary>
/// <param name="validator">Absent when nothing in the container registered a check with <c>ValidateOnStart</c>.</param>
public sealed class ShellStartupValidation(IStartupValidator? validator = null) : IShellInitializer
{
    /// <summary>Ahead of the EF provider-binding check (-100) and the module migrators (0) of the same phase.</summary>
    internal const int PrepareOrder = -1000;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        validator?.Validate();
        return Task.CompletedTask;
    }
}
