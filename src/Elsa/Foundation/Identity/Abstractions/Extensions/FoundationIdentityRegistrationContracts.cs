using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Abstractions.Extensions;

public sealed record FoundationIdentityReplacementRegistration(
    Type ContractType,
    Type ImplementationType,
    ServiceDescriptor? Descriptor = null);

internal sealed record FoundationIdentityResultHandlerRegistration;

internal sealed record FoundationPermissionAuthorizationHandlerRegistration;

internal sealed record FoundationIdentityPolicyProviderRegistration;

internal sealed record FoundationIdentityRegistrationState(IServiceCollection Services);

internal sealed class FoundationIdentityRegistrationValidator(FoundationIdentityRegistrationState state)
    : IValidateOptions<FoundationIdentityOptions>
{
    private static readonly Type[] ReplacementContracts =
    [
        typeof(IPermissionEvaluator),
        typeof(IPermissionAuthorizationService),
        typeof(IPermissionPolicyNameFormatter),
        typeof(IPermissionCatalog)
    ];

    public ValidateOptionsResult Validate(string? name, FoundationIdentityOptions options)
    {
        var failures = new List<string>();
        var invalidAuthenticationTypes = options.NormalizedAuthenticationTypes
            .Where(type => string.IsNullOrWhiteSpace(type) || !string.Equals(type, type.Trim(), StringComparison.Ordinal))
            .ToArray();
        if (invalidAuthenticationTypes.Length > 0)
        {
            failures.Add($"Normalized authentication types must be non-empty and cannot contain leading or trailing whitespace. " +
                         $"Invalid values: {string.Join(", ", invalidAuthenticationTypes.Select(type => $"'{type}'"))}.");
        }

        var markers = state.Services
            .Where(descriptor => descriptor.ServiceType == typeof(FoundationIdentityReplacementRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<FoundationIdentityReplacementRegistration>()
            .ToArray();

        foreach (var contract in ReplacementContracts)
        {
            var descriptors = state.Services.Where(descriptor => descriptor.ServiceType == contract).ToArray();
            var contractMarkers = markers.Where(marker => marker.ContractType == contract).ToArray();
            if (descriptors.Length != 1 || contractMarkers.Length != 1 ||
                !DescriptorMatches(descriptors.SingleOrDefault(), contractMarkers.SingleOrDefault()))
            {
                failures.Add($"Replacement contract '{contract.FullName}' must have exactly one tagged implementation. " +
                             $"Descriptors: {Describe(descriptors)}. Markers: {Describe(contractMarkers.Select(x => x.ImplementationType))}.");
            }
        }

        foreach (var contract in markers.Select(marker => marker.ContractType)
                     .Where(contract => !ReplacementContracts.Contains(contract))
                     .Distinct())
        {
            var descriptors = state.Services.Where(descriptor => descriptor.ServiceType == contract).ToArray();
            var contractMarkers = markers.Where(marker => marker.ContractType == contract).ToArray();
            if (contract.GetCustomAttributes(typeof(ReplacementContractAttribute), inherit: false).Length == 0 ||
                descriptors.Length != 1 || contractMarkers.Length != 1 ||
                !DescriptorMatches(descriptors.SingleOrDefault(), contractMarkers.SingleOrDefault()))
            {
                failures.Add($"Replacement contract '{contract.FullName}' must have exactly one tagged implementation. " +
                             $"Descriptors: {Describe(descriptors)}. Markers: {Describe(contractMarkers.Select(x => x.ImplementationType))}.");
            }
        }

        var resultHandlers = state.Services.Where(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler)).ToArray();
        var resultHandlerMarkers = state.Services.Count(descriptor => descriptor.ServiceType == typeof(FoundationIdentityResultHandlerRegistration));
        if (resultHandlers.Length != 1 || resultHandlerMarkers != 1 ||
            resultHandlers[0].ImplementationType != typeof(PermissionAuthorizationMiddlewareResultHandler))
        {
            failures.Add($"Authorization result handler contract '{typeof(IAuthorizationMiddlewareResultHandler).FullName}' must be owned by " +
                         $"'{typeof(PermissionAuthorizationMiddlewareResultHandler).FullName}' with exactly one Foundation registration marker. " +
                         $"Descriptors: {Describe(resultHandlers)}. Markers: {resultHandlerMarkers}.");
        }

        var policyProviders = state.Services.Where(descriptor => descriptor.ServiceType == typeof(IAuthorizationPolicyProvider)).ToArray();
        var policyProviderMarkers = state.Services.Count(descriptor => descriptor.ServiceType == typeof(FoundationIdentityPolicyProviderRegistration));
        if (policyProviders.Length != 1 || policyProviderMarkers != 1 ||
            policyProviders[0].ImplementationType != typeof(RequirePermissionPolicyProvider))
        {
            failures.Add($"Authorization policy provider contract '{typeof(IAuthorizationPolicyProvider).FullName}' must be owned by " +
                         $"'{typeof(RequirePermissionPolicyProvider).FullName}' with exactly one Foundation registration marker. " +
                         $"Descriptors: {Describe(policyProviders)}. Markers: {policyProviderMarkers}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool DescriptorMatches(
        ServiceDescriptor? descriptor,
        FoundationIdentityReplacementRegistration? marker)
    {
        if (descriptor is null || marker is null)
            return false;

        return marker.Descriptor is not null
            ? ReferenceEquals(descriptor, marker.Descriptor)
            : descriptor.ImplementationType == marker.ImplementationType ||
              descriptor.ImplementationInstance?.GetType() == marker.ImplementationType;
    }

    internal static string Describe(IEnumerable<ServiceDescriptor> descriptors) =>
        Describe(descriptors.Select(descriptor => descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType() ?? descriptor.ServiceType));

    private static string Describe(IEnumerable<Type> types) =>
        string.Join(", ", types.Select(type => type.FullName).OrderBy(x => x, StringComparer.Ordinal));
}
