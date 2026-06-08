using System.Text.Json;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public class ActivityFactoryTests
{
    [Fact] // US2 / FR-005
    public async Task Create_DispatchesToRegisteredConstructor()
    {
        var registry = new ActivityConstructorRegistry();
        var constructor = new FakeConstructorA("Dispatch.Target");
        registry.Add(constructor);
        var factory = new ActivityFactory(registry);

        var activity = await factory.Create("Dispatch.Target", default, null, null);

        Assert.Same(constructor.Returned, activity);
    }

    [Fact] // US2 edge case / FR-005
    public async Task Create_UnknownDescriptorType_Throws()
    {
        var factory = new ActivityFactory(new ActivityConstructorRegistry());

        await Assert.ThrowsAsync<UnknownDescriptorTypeException>(
            async () => await factory.Create("Nope", default, null, null));
    }
}
