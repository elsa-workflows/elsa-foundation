using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public class ActivityConstructorRegistryTests
{
    [Fact] // SC-005 / FR-006
    public void Add_SecondConstructorForSameDescriptorType_Throws()
    {
        var registry = new ActivityConstructorRegistry();
        registry.Add(new FakeConstructorA("Some.Descriptor"));

        var ex = Assert.Throws<DuplicateActivityConstructorException>(
            () => registry.Add(new FakeConstructorB("Some.Descriptor")));

        Assert.Equal("Some.Descriptor", ex.DescriptorType);
    }

    [Fact]
    public void Add_DistinctDescriptorTypes_AllResolvable()
    {
        var registry = new ActivityConstructorRegistry();
        var a = new FakeConstructorA("Type.A");
        var b = new FakeConstructorB("Type.B");

        registry.AddAll([a, b]);

        Assert.Same(a, registry.Resolve("Type.A"));
        Assert.Same(b, registry.Resolve("Type.B"));
    }

    [Fact]
    public void Resolve_UnregisteredDescriptorType_Throws()
    {
        var registry = new ActivityConstructorRegistry();

        var ex = Assert.Throws<UnknownDescriptorTypeException>(() => registry.Resolve("Not.Registered"));
        Assert.Equal("Not.Registered", ex.DescriptorType);
    }
}
