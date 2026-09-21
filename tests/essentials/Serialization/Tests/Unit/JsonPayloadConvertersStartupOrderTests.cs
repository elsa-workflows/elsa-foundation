using System.Reflection;
using Xunit;
using Elsa.Serialization.SystemText.Services;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// Guards the startup ordering that every payload-deserializing startup task depends on.
/// </summary>
/// <remarks>
/// TaskManager sorts startup tasks by <c>OrderAttribute?.Order ?? 0f</c> before topological sorting. A task that
/// deserializes at boot — an artifact reconciler reading a closure envelope, a version reconciler reading a
/// definition — reads through JsonPayloadSerializer, whose options come from the registry
/// <see cref="JsonPayloadConvertersInitializingStartupTask"/> fills. Consumers sit at the default order of 0 with
/// no <c>[TaskDependency]</c> edge to it, so without a negative order here the two race in incidental DI
/// registration order, and a consumer that wins deserializes with no JsonStringEnumConverter.
///
/// That race is not hypothetical: it made twelve well-formed exported closures unreadable at boot on a real
/// server, reported as "the file is not a valid closure envelope". It is invisible to in-process tests, which
/// resolve a fully initialised serializer — which is why this is an attribute assertion rather than a
/// behavioural one.
/// </remarks>
public sealed class JsonPayloadConvertersStartupOrderTests
{
    [Fact]
    public void Converter_initialization_is_ordered_ahead_of_default_order_startup_tasks()
    {
        var order = typeof(JsonPayloadConvertersInitializingStartupTask)
            .GetCustomAttribute<OrderAttribute>();

        Assert.NotNull(order);
        Assert.True(
            order.Order < 0f,
            $"Converter initialization must sort ahead of the default order of 0, but carries {order.Order}.");
    }

    [Fact]
    public void Converter_initialization_is_a_startup_task()
    {
        // If this ever stops being an IStartupTask, the ordering guard above silently stops meaning anything.
        Assert.True(typeof(IStartupTask).IsAssignableFrom(typeof(JsonPayloadConvertersInitializingStartupTask)));
    }
}
