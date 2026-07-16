using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public class ActivityConstructorRegistryTests
{
    [Fact] // SC-005 / FR-006
    public void Add_SecondConstructorForSameConsumerAndSchema_Throws()
    {
        var registry = new ActivityConstructorRegistry();
        registry.Add(new FakeConstructorA("Some.Descriptor"));

        var ex = Assert.Throws<DuplicateActivityConstructorException>(
            () => registry.Add(new FakeConstructorB("Some.Descriptor")));

        Assert.Equal("Some.Descriptor", ex.ConsumerKey);
        Assert.Equal("1", ex.SchemaVersion);
    }

    [Fact]
    public void Add_SecondInstanceOfSameConstructorTypeForSameConsumerAndSchema_Throws()
    {
        var registry = new ActivityConstructorRegistry();
        registry.Add(new FakeConstructorA("test.consumer"));

        var ex = Assert.Throws<DuplicateActivityConstructorException>(
            () => registry.Add(new FakeConstructorA("test.consumer")));

        Assert.Equal("test.consumer", ex.ConsumerKey);
        Assert.Equal("1", ex.SchemaVersion);
    }

    [Fact]
    public void Add_MultiSchemaConflict_DoesNotPartiallyRegisterEarlierSchemas()
    {
        var registry = new ActivityConstructorRegistry();
        registry.Add(new FakeConstructorA("test.consumer", "2"));

        Assert.Throws<DuplicateActivityConstructorException>(
            () => registry.Add(new FakeConstructorB("test.consumer", "1", "2")));

        Assert.Throws<UnsupportedActivityDescriptorSchemaException>(() => registry.Resolve("test.consumer", "1"));
    }

    [Fact]
    public void Add_DistinctConsumerKeys_AllResolvable()
    {
        var registry = new ActivityConstructorRegistry();
        var a = new FakeConstructorA("Type.A");
        var b = new FakeConstructorB("Type.B");

        registry.AddAll([a, b]);

        Assert.Same(a, registry.Resolve("Type.A", "1"));
        Assert.Same(b, registry.Resolve("Type.B", "1"));
    }

    [Fact]
    public void Resolve_UnregisteredConsumer_Throws()
    {
        var registry = new ActivityConstructorRegistry();

        var ex = Assert.Throws<UnknownActivityConsumerException>(() => registry.Resolve("not.registered", "1"));
        Assert.Equal("not.registered", ex.ConsumerKey);
        Assert.Equal("1", ex.SchemaVersion);
    }

    [Fact]
    public void Resolve_UnsupportedSchema_ThrowsWithSupportedSchemas()
    {
        var registry = new ActivityConstructorRegistry();
        registry.Add(new FakeConstructorA("test.consumer", "1", "2"));

        var ex = Assert.Throws<UnsupportedActivityDescriptorSchemaException>(() => registry.Resolve("test.consumer", "3"));

        Assert.Equal(["1", "2"], ex.SupportedSchemaVersions);
    }
}
