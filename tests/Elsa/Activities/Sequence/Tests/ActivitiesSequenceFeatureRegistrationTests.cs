using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Design;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Sequence.Tests;

/// <summary>
/// T128: Sequence ships as two packages. The design half owns the authored-structure handler and may reference
/// <c>Elsa.Workflows.Design.Core</c>; the runtime half owns execution scheduling and must not. These tests pin
/// which half registers what, so a service drifting back across the boundary fails here rather than only in the
/// runtime-only closure guard.
/// </summary>
public sealed class ActivitiesSequenceFeatureRegistrationTests
{
    [Fact]
    public void DesignFeature_RegistersSequenceStructureHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesSequenceDesignFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var handler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(global::Elsa.Activities.Sequence.Activities.Sequence.StructureKind, handler.Kind);
        Assert.Equal(global::Elsa.Activities.Sequence.Activities.Sequence.StructureSchemaVersion, handler.SchemaVersion);
        Assert.Equal(typeof(Models.SequenceAuthoredStructure), handler.AuthoredPayloadType);
    }

    [Fact]
    public void RuntimeFeature_RegistersSchedulingServicesAndNoStructureHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesSequenceRuntimeFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IOperatorActivitySchedulingCapabilityProvider>());
        Assert.Single(provider.GetServices<IOperatorActivitySchedulingPolicy>());
        Assert.Single(provider.GetServices<IReplaySafeSuccessorRoutingProbe>());

        // The runtime half must not carry the authored-structure handler; that is the design half's job.
        Assert.Empty(provider.GetServices<IActivityStructureHandler>());
    }
}
