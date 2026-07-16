using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Expressions.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public class ClrActivityConstructorTests
{
    private static JsonPayloadSerializer Serializer() => new(new JsonPayloadConverterRegistry());

    [Fact] // US4 / SC-003 — CLR round-trip with no Kind string
    public async Task Create_ClrDescriptor_ConstructsActivityAndBindsInput()
    {
        // Arrange: the CLR descriptor is the stable-alias ClrActivityDescriptor; the payload is serialized via
        // the canonical payload serializer (the same one the constructor reads with) and the activity type is
        // registered in the well-known type registry so its alias resolves — no assembly name/version anywhere.
        var serializer = Serializer();
        var payload = ClrConstruction.Payload(serializer, typeof(WriteLine));

        var registry = new ActivityConstructorRegistry();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        registry.Add(ClrConstruction.Constructor(serviceProvider, serializer, typeof(WriteLine)));
        var factory = new ActivityFactory(registry);

        var inputs = new Dictionary<string, InputArgument>
        {
            ["Text"] = new InputArgument<string>(new MemoryBlockReference())
        };

        // Act
        var activity = await factory.Create(
            new RuntimeActivityDescriptor(ClrConstruction.ConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, payload),
            inputs,
            null);

        // Assert
        var writeLine = Assert.IsType<WriteLine>(activity);
        Assert.NotNull(writeLine.Text);
    }

    [Fact] // descriptor type is derived from typeof(TDescriptor) — never hand-authored
    public void ClrConstructor_UsesStableConsumerKeyAndSchema()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var constructor = ClrConstruction.Constructor(serviceProvider, Serializer());

        Assert.Equal(WellKnownRuntimeActivityConsumers.ClrActivity, constructor.ConsumerKey);
        Assert.Contains(RuntimeActivityDescriptor.InitialSchemaVersion, constructor.SupportedSchemaVersions);
    }

    [Fact] // an alias that resolves to no registered CLR type is a loud failure, not a silent object
    public async Task Create_UnknownAlias_ThrowsUnknownActivityType()
    {
        var serializer = Serializer();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var registry = new ActivityConstructorRegistry();
        // Constructor with an empty registry: the WriteLine alias resolves to nothing.
        registry.Add(ClrConstruction.Constructor(serviceProvider, serializer));
        var factory = new ActivityFactory(registry);

        await Assert.ThrowsAsync<UnknownActivityTypeException>(() =>
            factory.Create(ClrConstruction.Descriptor(serializer, typeof(WriteLine)), null, null).AsTask());
    }
}
