using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Api.Tests;

/// <summary>
/// A host may subclass the public feature class. Its request handlers come from the Expressions API assembly either
/// way, never from the assembly the subclass lives in.
/// </summary>
public sealed class ExpressionsApiFeatureTests
{
    [Fact]
    public void A_subclass_registers_the_same_services_as_the_feature()
    {
        Assert.Equal(Registrations(new ExpressionsApiFeature()), Registrations(new HostExpressionsApiFeature()));
    }

    private static string[] Registrations(ExpressionsApiFeature feature)
    {
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        return services
            .Select(descriptor => $"{descriptor.ServiceType} -> {descriptor.ImplementationType}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class HostExpressionsApiFeature : ExpressionsApiFeature;
}
