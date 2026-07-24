using Elsa.Diagnostics.Persistence.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.Persistence.Extensions;

/// <summary>
/// Validates that diagnostics persistence composition selects at most one observer implementation.
/// </summary>
public sealed class DiagnosticsPersistenceObserverRegistrationValidator(IServiceCollection services)
{
    /// <summary>Returns a failed result that names every conflicting registration, or success.</summary>
    public ValidateOptionsResult Validate()
    {
        var observers = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDiagnosticsPersistenceObserver))
            .ToArray();
        if (observers.Length <= 1)
            return ValidateOptionsResult.Success;

        var selection = DiagnosticsPersistenceRegistration.FindObserverSelection(services);
        var implementations = observers
            .Select(descriptor => DescribeObserverDescriptor(descriptor, selection))
            .ToArray();
        var message =
            $"Diagnostics replacement contract '{typeof(IDiagnosticsPersistenceObserver).FullName}' has conflicting " +
            $"registrations: {string.Join(", ", implementations)}. Select one observer explicitly through " +
            $"'{nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore)}'.";
        return ValidateOptionsResult.Fail(message);
    }

    private static string DescribeObserverDescriptor(
        ServiceDescriptor descriptor,
        (ServiceDescriptor ContractDescriptor, Type ImplementationType)? selection)
    {
        if (selection is { } selected && ReferenceEquals(descriptor, selected.ContractDescriptor))
            return selected.ImplementationType.ToString();

        if (descriptor.ImplementationType is { } implementationType)
            return implementationType.ToString();

        if (descriptor.ImplementationInstance is { } implementationInstance)
            return implementationInstance.GetType().ToString();

        return $"factory registration for '{typeof(IDiagnosticsPersistenceObserver).FullName}'";
    }
}

internal sealed class DiagnosticsPersistenceObserverRegistrationOptions;

internal sealed class DiagnosticsPersistenceObserverRegistrationOptionsAdapter(
    DiagnosticsPersistenceObserverRegistrationValidator validator)
    : IValidateOptions<DiagnosticsPersistenceObserverRegistrationOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DiagnosticsPersistenceObserverRegistrationOptions options) => validator.Validate();
}
