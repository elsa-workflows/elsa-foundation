using Elsa.Serialization.Testing;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// <see cref="SerializationServices"/> hands out services from containers it never disposes. That holds only while
/// the composition registers nothing a container would have to dispose; this fails the day it does.
/// </summary>
public sealed class SerializationServicesTests
{
    [Fact]
    public void The_composition_registers_nothing_a_container_would_have_to_dispose()
    {
        var services = SerializationServices.Compose();
        Assert.DoesNotContain(services, descriptor => descriptor.IsKeyedService);

        // A container disposes only what it builds, so a supplied instance (the null logger factory) is not its to dispose.
        var built = services.Where(descriptor => descriptor.ImplementationInstance is null).ToList();
        Assert.All(built, descriptor => Assert.True(
            descriptor.ImplementationType is not null,
            $"{descriptor.ServiceType} is registered through a factory, which hides whether what it builds is disposable."));
        Assert.DoesNotContain(built, descriptor =>
            typeof(IDisposable).IsAssignableFrom(descriptor.ImplementationType) ||
            typeof(IAsyncDisposable).IsAssignableFrom(descriptor.ImplementationType));
    }
}
