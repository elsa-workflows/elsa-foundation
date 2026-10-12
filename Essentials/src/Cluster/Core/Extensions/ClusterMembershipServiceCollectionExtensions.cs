using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.Core.Extensions;

/// <summary>
/// How a membership provider is selected. Exactly one is active per host process (FR-001; ADR 0078, invariant 1):
/// the in-process default yields to one durable provider, and two durable providers fail whatever order they are
/// composed in, never resolved by last-write-wins.
/// </summary>
/// <remarks>
/// A durable provider is composed once, on the host container (ADR 0076 D9; spec 183, Decisions, Q20). The host
/// starts only if its membership settings are valid, exactly one provider owns <see cref="IClusterMembership"/>, and a
/// durable provider has an explicit host id (FR-003a). The startup check runs before any hosted service starts, so a
/// refused provider never attempts to join.
/// </remarks>
public static class ClusterMembershipServiceCollectionExtensions
{
    /// <summary>
    /// Selects a durable provider. The in-process default, if already registered, yields to it; any other provider, or
    /// any <see cref="IClusterMembership"/> registered directly, is refused with a diagnostic naming both.
    /// </summary>
    public static IServiceCollection AddClusterMembershipProvider(this IServiceCollection services, ClusterMembershipProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.Kind != ClusterProviderKind.Durable)
            throw new ArgumentException(
                $"Provider '{registration.Name}' is not durable. The in-process provider is the default and is registered with {nameof(TryAddClusterMembershipDefault)}.",
                nameof(registration));

        var registered = Registrations(services);
        var competitor = registered.FirstOrDefault(existing => existing.Kind == ClusterProviderKind.Durable)?.Name
                         ?? UnownedMembershipDescriptors(services, registered).Select(Describe).FirstOrDefault();
        if (competitor is not null)
            throw new ClusterMembershipConfigurationException(ConflictMessage(competitor, registration.Name));

        foreach (var yielding in registered)
        {
            services.Remove(yielding.MembershipDescriptor);
            foreach (var marker in services.Where(descriptor => ReferenceEquals(descriptor.ImplementationInstance, yielding)).ToArray())
                services.Remove(marker);
        }

        return Register(services, registration);
    }

    /// <summary>
    /// Registers the in-process default unless <see cref="IClusterMembership"/> is already registered. Consumers call
    /// this, so a host that composes nothing about clustering is a cluster of one.
    /// </summary>
    public static IServiceCollection TryAddClusterMembershipDefault(this IServiceCollection services, ClusterMembershipProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.Kind != ClusterProviderKind.InProcess)
            throw new ArgumentException(
                $"Provider '{registration.Name}' is durable. A durable provider replaces the default through {nameof(AddClusterMembershipProvider)}.",
                nameof(registration));

        return MembershipDescriptors(services).Length == 0 ? Register(services, registration) : services;
    }

    private static IServiceCollection Register(IServiceCollection services, ClusterMembershipProviderRegistration registration)
    {
        services.Add(registration.MembershipDescriptor);
        services.AddSingleton(registration);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ClusterMembershipComposition)))
            return services;

        var composition = new ClusterMembershipComposition(services);
        services.AddSingleton(composition);
        services.AddSingleton<IValidateOptions<ClusterMembershipOptions>>(new ClusterMembershipOptionsValidator(composition));
        services.AddOptions<ClusterMembershipOptions>().ValidateOnStart();
        return services;
    }

    internal static ClusterMembershipProviderRegistration[] Registrations(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(ClusterMembershipProviderRegistration) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ClusterMembershipProviderRegistration>()
            .ToArray();

    internal static ServiceDescriptor[] MembershipDescriptors(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(IClusterMembership) && !descriptor.IsKeyedService).ToArray();

    internal static IEnumerable<ServiceDescriptor> UnownedMembershipDescriptors(IServiceCollection services, IEnumerable<ClusterMembershipProviderRegistration> registrations) =>
        MembershipDescriptors(services)
            .Where(descriptor => !registrations.Any(registration => ReferenceEquals(registration.MembershipDescriptor, descriptor)));

    internal static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName
        ?? descriptor.ImplementationInstance?.GetType().FullName
        ?? "a factory registration";

    internal static string ConflictMessage(string first, string second) =>
        $"Cluster membership provider '{second}' cannot be composed beside '{first}': a host has exactly one membership " +
        "provider, and a second one is never resolved by last-write-wins. Remove one of them.";
}

/// <summary>The service collection a host composed its membership provider in, read again by the startup check.</summary>
internal sealed record ClusterMembershipComposition(IServiceCollection Services);

/// <summary>
/// The startup check: valid settings, exactly one provider owning <see cref="IClusterMembership"/>, and an explicit
/// host id for a durable provider.
/// </summary>
internal sealed class ClusterMembershipOptionsValidator(ClusterMembershipComposition composition) : IValidateOptions<ClusterMembershipOptions>
{
    public ValidateOptionsResult Validate(string? name, ClusterMembershipOptions options)
    {
        var failures = DescribeSettings(options).Concat(DescribeComposition(options)).ToArray();
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> DescribeSettings(ClusterMembershipOptions options)
    {
        const string section = ClusterMembershipOptions.SectionName;
        if (options.HostId is not null && ClusterHostIdConstraints.Describe(options.HostId) is { } hostIdProblem)
            yield return $"{section}:{nameof(options.HostId)} is invalid. {hostIdProblem}";
        if (options.HeartbeatInterval <= TimeSpan.Zero)
            yield return $"{section}:{nameof(options.HeartbeatInterval)} must be positive; it is {options.HeartbeatInterval}.";
        if (options.ExpiryPeriod < options.HeartbeatInterval * 3)
            yield return $"{section}:{nameof(options.ExpiryPeriod)} must be at least three heartbeat intervals, so one lost heartbeat does not " +
                         $"make a member lapse; it is {options.ExpiryPeriod} against a heartbeat interval of {options.HeartbeatInterval}.";
        if (options.SkewAllowance < TimeSpan.Zero)
            yield return $"{section}:{nameof(options.SkewAllowance)} must not be negative; it is {options.SkewAllowance}.";
    }

    private IEnumerable<string> DescribeComposition(ClusterMembershipOptions options)
    {
        var services = composition.Services;
        var registrations = ClusterMembershipServiceCollectionExtensions.Registrations(services);
        if (registrations.Length == 0)
        {
            yield return "The membership provider's registration was removed after it was composed.";
            yield break;
        }

        if (registrations.Length > 1)
        {
            yield return ClusterMembershipServiceCollectionExtensions.ConflictMessage(registrations[0].Name, registrations[1].Name);
            yield break;
        }

        var selected = registrations[0];
        var descriptors = ClusterMembershipServiceCollectionExtensions.MembershipDescriptors(services);
        var others = descriptors.Where(descriptor => !ReferenceEquals(descriptor, selected.MembershipDescriptor)).ToArray();
        if (others.Length > 0)
            yield return ClusterMembershipServiceCollectionExtensions.ConflictMessage(selected.Name, string.Join(", ", others.Select(ClusterMembershipServiceCollectionExtensions.Describe)));
        else if (descriptors.Length == 0)
            yield return $"Membership provider '{selected.Name}' no longer registers {nameof(IClusterMembership)}.";

        if (selected.Kind == ClusterProviderKind.Durable && options.HostId is null)
            yield return $"Cluster membership provider '{selected.Name}' is durable and requires an explicit host id: set " +
                         $"{ClusterMembershipOptions.SectionName}:{nameof(options.HostId)}. The machine-name default applies only to the in-process provider.";
    }
}
