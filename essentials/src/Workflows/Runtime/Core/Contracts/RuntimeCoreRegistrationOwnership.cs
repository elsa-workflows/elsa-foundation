using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Recognizes the Runtime implementation's replaceable default registrations by
/// <see cref="RuntimeDefaultRegistrationAttribute"/>, so neither a caller with a similar type name nor a renamed default
/// changes what a provider backend may replace.
/// </summary>
internal static class RuntimeCoreRegistrationOwnership
{
    public static bool IsDefault(Type? type) =>
        type?.IsDefined(typeof(RuntimeDefaultRegistrationAttribute), inherit: false) == true;

    /// <summary>A factory lambda compiles into a closure type nested in the composition root that declares it.</summary>
    public static bool IsCoreFactory(ServiceDescriptor descriptor) =>
        descriptor.ImplementationFactory?.Method.DeclaringType is { } type &&
        (IsDefault(type) || IsDefault(type.DeclaringType));
}
