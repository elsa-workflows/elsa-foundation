using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// G27 registration test (spec 006 T025): the Primitives feature contributes the CLR activity constructor
/// against the <see cref="IActivityConstructor"/> contract and the feature-internal
/// <see cref="ActivityArgumentBinder"/>.
/// </summary>
public sealed class ActivitiesPrimitivesFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersClrConstructorAndBinder()
    {
        var services = new ServiceCollection();

        new ActivitiesPrimitivesFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IActivityConstructor));
        Assert.Contains(services, d => d.ServiceType == typeof(ActivityArgumentBinder));
    }
}
