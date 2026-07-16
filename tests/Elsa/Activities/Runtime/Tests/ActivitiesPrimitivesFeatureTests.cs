using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The Primitives feature contributes the transient CLR activation boundary.
/// </summary>
public sealed class ActivitiesPrimitivesFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersClrActivator()
    {
        var services = new ServiceCollection();

        new ActivitiesPrimitivesFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IActivityActivator));
    }
}
