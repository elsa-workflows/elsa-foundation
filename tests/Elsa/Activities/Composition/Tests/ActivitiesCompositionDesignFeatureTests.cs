using Elsa.Activities.Composition.Design;
using Elsa.Activities.Composition.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

/// <summary>
/// G27 registration test (T031, Design half): the Design-side Composition feature contributes both the
/// usable-as-activity workflow source (the §2.7 adapter port) and the reconciliation source that the
/// universal reconciliation handler discovers from DI.
/// </summary>
public sealed class ActivitiesCompositionDesignFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersWorkflowSourceAndReconciliationSource()
    {
        var services = new ServiceCollection();

        new ActivitiesCompositionDesignFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IUsableAsActivityWorkflowSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IActivityReconciliationSource));
    }
}
