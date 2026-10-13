using Elsa.Events.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// The JSON a payload becomes through the serializer the public <see cref="SerializationFeature"/> composes, with the
/// converters its built-in source contributes: enum names, <see cref="TimeSpan"/> text, <see cref="Type"/> aliases,
/// and sorted members and dictionary keys. The serializer, the converters and the source behind it are internal, so
/// this pins that how they are found and built leaves the wire shape exactly as it was.
/// </summary>
public sealed class ComposedSerializerWireShapeTests
{
    [Fact]
    public async Task A_payload_keeps_its_wire_shape_through_the_composed_feature()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        new SerializationFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(string), "String");

        // What the converter-initializing startup task does once its event reaches the feature's one handler.
        using (var scope = provider.CreateScope())
        {
            var contribution = new JsonPayloadConvertersInitializing();
            foreach (var handler in scope.ServiceProvider.GetServices<IEventHandler<JsonPayloadConvertersInitializing>>())
                await handler.Handle(contribution, CancellationToken.None);
            provider.GetRequiredService<JsonPayloadConverterRegistry>().RegisterAll(contribution.Converters);
        }

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
