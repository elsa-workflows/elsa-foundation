using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public class ClrActivityConstructorTests
{
    private static JsonPayloadSerializer Serializer() => new(new JsonPayloadConverterRegistry());

    [Fact] // US4 / SC-003 — CLR round-trip with no Kind string
    public async Task Create_ClrDescriptor_ConstructsActivityAndBindsInput()
    {
        // Arrange: the CLR descriptor IS TypeInformation; payload is the activity type's TypeInformation,
        // serialized via the canonical payload serializer (the same one the constructor reads with).
        var serializer = Serializer();
        var descriptor = TypeInformation.FromType(typeof(WriteLine));
        var payload = serializer.SerializeToElement(descriptor);

        var registry = new ActivityConstructorRegistry();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        registry.Add(new ClrActivityConstructor(serviceProvider, new ActivityArgumentBinder(), serializer));
        var factory = new ActivityFactory(registry);

        var inputs = new Dictionary<string, InputArgument>
        {
            ["Text"] = new InputArgument<string>(new MemoryBlockReference())
        };

        // Act
        var activity = await factory.Create(typeof(TypeInformation).FullName!, payload, inputs, null);

        // Assert
        var writeLine = Assert.IsType<WriteLine>(activity);
        Assert.NotNull(writeLine.Text);
    }

    [Fact] // descriptor type is derived from typeof(TDescriptor) — never hand-authored
    public void ClrConstructor_DescriptorType_IsTypeInformationFullName()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var constructor = new ClrActivityConstructor(serviceProvider, new ActivityArgumentBinder(), Serializer());

        Assert.Equal("Elsa.Primitives.Models.TypeInformation", constructor.DescriptorType);
    }
}
