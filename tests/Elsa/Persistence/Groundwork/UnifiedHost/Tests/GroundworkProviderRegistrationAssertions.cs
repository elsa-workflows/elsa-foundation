using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.RegistrationTests;

internal static class GroundworkProviderRegistrationAssertions
{
    public static void AssertRepresentativeFamilyContracts(ServiceCollection services, params Type[] contracts)
    {
        foreach (var contract in contracts)
            Assert.Contains(services, descriptor => descriptor.ServiceType == contract);
    }

    public static void AssertRegistrationDiagnosticsAreSanitized(
        ServiceCollection services,
        string registrationSecret,
        string connectionString)
    {
        var diagnostics = string.Join(Environment.NewLine, services.Select(descriptor => descriptor.ToString()));
        Assert.DoesNotContain(registrationSecret, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, diagnostics, StringComparison.Ordinal);
    }
}
