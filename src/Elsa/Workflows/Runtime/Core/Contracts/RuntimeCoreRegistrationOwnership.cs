using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Identifies the exact Runtime core factory, not a caller with a similar type name.</summary>
internal static class RuntimeCoreRegistrationOwnership
{
    private const string CoreRegistrationType = "Elsa.Workflows.Runtime.Core.Extensions.RuntimeCoreServiceCollectionExtensions";

    public static bool IsCoreFactory(ServiceDescriptor descriptor)
    {
        var type = descriptor.ImplementationFactory?.Method.DeclaringType;
        return type?.FullName == CoreRegistrationType || type?.DeclaringType?.FullName == CoreRegistrationType;
    }
}
