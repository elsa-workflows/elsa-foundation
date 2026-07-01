using Elsa.Activities.Composition.Design;
using Elsa.Activities.Design.Reconciliation.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

/// <summary>
/// G27 registration test (T031, Design half): the Design-side Composition feature contributes its
/// reconciliation source against the <see cref="IActivityReconciliationSource"/> contract so the
/// universal reconciliation handler discovers it from DI.
/// </summary>
public sealed class ActivitiesCompositionDesignFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersWorkflowActivityReconciliationSource()
    {
        var services = new ServiceCollection();

        new ActivitiesCompositionDesignFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IActivityReconciliationSource));
    }
}
