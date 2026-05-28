using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Resolvers;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US3 — IActivityFactory + IActivityImplementationResolver behaviour.
/// </summary>
public sealed class ActivityFactoryTests
{
    [Fact]
    public async Task UnknownKind_Throws_ActivityResolutionException()
    {
        // Elsa §E2.6.1 — domain-failure path. The factory throws a typed
        // ActivityResolutionException, NOT a system exception, when no resolver is registered.
        var registry = new ActivityImplementationResolverRegistry();
        var factory = new ActivityFactory(new ServiceCollection().BuildServiceProvider(), registry);

        // Workflow descriptor has Kind="Workflow"; nothing registered → resolution fails.
        var descriptor = new WorkflowImplementationDescriptor("wf-1", 1);

        await Assert.ThrowsAsync<ActivityResolutionException>(async () =>
            await factory.Create(descriptor, [], [], CancellationToken.None));
    }

    [Fact]
    public void RegisterAll_DuplicateKindWithDifferentResolverType_Throws()
    {
        var registry = new ActivityImplementationResolverRegistry();
        var clr = new ClrActivityImplementationResolver();
        var other = new FakeClrResolver();

        registry.RegisterAll([clr]);

        Assert.Throws<InvalidOperationException>(() => registry.RegisterAll([other]));
    }

    [Fact]
    public async Task ClrDescriptor_ResolvesToTheWrappedType()
    {
        // The CLR resolver loads the type wrapped in the descriptor; the factory activates it
        // via ActivatorUtilities. Use a TypeInformation pointing at a real loadable type
        // present in this test assembly.
        var registry = new ActivityImplementationResolverRegistry();
        registry.RegisterAll([new ClrActivityImplementationResolver()]);

        var services = new ServiceCollection().BuildServiceProvider();
        var factory = new ActivityFactory(services, registry);

        var typeInfo = TypeInformation.FromType<NoopActivity>();
        var descriptor = new ClrImplementationDescriptor(typeInfo);

        var activity = await factory.Create(descriptor, [], [], CancellationToken.None);

        Assert.IsType<NoopActivity>(activity);
    }

    private sealed class FakeClrResolver : IActivityImplementationResolver<ClrImplementationDescriptor>
    {
        public string Kind => "Clr";
        public Type Resolve(ClrImplementationDescriptor descriptor) => typeof(object);
    }
}
