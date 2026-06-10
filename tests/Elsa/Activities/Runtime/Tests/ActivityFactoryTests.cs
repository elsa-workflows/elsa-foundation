using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
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

    [Fact]
    public async Task Create_LoadsContributedConstructorsIntoEmptyRegistry()
    {
        var constructor = new FakeConstructorA("Dispatch.Target");
        var factory = new ActivityFactory(new ActivityConstructorRegistry(), [constructor]);

        var activity = await factory.Create("Dispatch.Target", default, null, null);

        Assert.Same(constructor.Returned, activity);
    }

    [Fact]
    public async Task Create_LoadsContributedConstructorsOnlyOncePerFactory()
    {
        var constructor = new FakeConstructorA("Dispatch.Target");
        var registry = new CountingActivityConstructorRegistry();
        var factory = new ActivityFactory(registry, [constructor]);

        await factory.Create("Dispatch.Target", default, null, null);
        await factory.Create("Dispatch.Target", default, null, null);

        Assert.Equal(1, registry.AddAllCalls);
    }

    [Fact] // US2 edge case / FR-005
    public async Task Create_UnknownDescriptorType_Throws()
    {
        var factory = new ActivityFactory(new ActivityConstructorRegistry());

        await Assert.ThrowsAsync<UnknownDescriptorTypeException>(
            async () => await factory.Create("Nope", default, null, null));
    }

    private sealed class CountingActivityConstructorRegistry : IActivityConstructorRegistry
    {
        private readonly ActivityConstructorRegistry _inner = new();

        public int AddAllCalls { get; private set; }

        public void Add(IActivityConstructor constructor) => _inner.Add(constructor);

        public void AddAll(IEnumerable<IActivityConstructor> constructors)
        {
            AddAllCalls++;
            _inner.AddAll(constructors);
        }

        public IActivityConstructor Resolve(string descriptorType) => _inner.Resolve(descriptorType);
    }
}
