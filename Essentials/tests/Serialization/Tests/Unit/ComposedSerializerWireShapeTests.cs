using Elsa.Events;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.Testing;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// The JSON a payload becomes through the serializer the public <see cref="SerializationFeature"/> composes, once the
/// feature's startup has run: enum names, <see cref="TimeSpan"/> text, <see cref="Type"/> aliases, and sorted members and
/// dictionary keys. The serializer, the converters, the source that contributes them, the handler that collects them and
/// the startup task that publishes for them are all internal, so this pins that how they are found and built leaves
/// the wire shape exactly as it was. The startup runs through the real inline event publisher: if the task stops
/// publishing, or the publisher stops reaching the handler, no converter is registered and the shape changes.
/// </summary>
public sealed class ComposedSerializerWireShapeTests
{
    [Fact]
    public async Task A_payload_keeps_its_wire_shape_through_the_composed_feature()
    {
        var services = SerializationServices.Compose();
        new EventsFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(string), "String");

        await using (var scope = provider.CreateAsyncScope())
            foreach (var startupTask in scope.ServiceProvider.GetServices<IStartupTask>())
                await startupTask.ExecuteAsync(CancellationToken.None);

        var payload = new WirePayload(
            Zeta: "last",
            Day: DayOfWeek.Monday,
            Every: TimeSpan.FromSeconds(90),
            Kind: typeof(string),
            Counts: new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 });
        var json = provider.GetRequiredService<IPayloadSerializer>().Serialize(payload);

        Assert.Equal("""{"counts":{"a":1,"b":2},"day":"Monday","every":"00:01:30","kind":"String","zeta":"last"}""", json);
    }

    private sealed record WirePayload(string Zeta, DayOfWeek Day, TimeSpan Every, Type Kind, Dictionary<string, int> Counts);
}
