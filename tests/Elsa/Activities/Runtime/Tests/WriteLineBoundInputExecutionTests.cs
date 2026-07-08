using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Expressions.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end guard for the WriteLine "blank line" bug: an authored Text value that survives compilation
/// as a runtime input binding must, once materialized and seeded, be what WriteLine actually prints.
/// Before the fix the authored text never reached the binding, so WriteLine ran with a null Text and
/// printed a blank line with no incident.
/// </summary>
// Shares a collection with other Console.Out-capturing tests so xUnit does not run them in parallel
// (Console.SetOut is process-global; concurrent capture would interleave output).
[Collection("ConsoleCapture")]
public sealed class WriteLineBoundInputExecutionTests
{
    [Fact]
    public async Task WriteLine_PrintsAuthoredText_WhenBoundInputIsMaterializedAndSeeded()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

        var registry = new ActivityConstructorRegistry();
        registry.Add(ClrConstruction.Constructor(serviceProvider, serializer, typeof(WriteLine)));
        var factory = new ActivityFactory(registry);

        var textArgument = new InputArgument<string>(new MemoryBlockReference("write-hello:Text"));
        var activity = await factory.Create(
            ClrConstruction.DescriptorType,
            ClrConstruction.Payload(serializer, typeof(WriteLine)),
            new Dictionary<string, InputArgument> { ["Text"] = textArgument },
            outputs: null);

        var writeLine = Assert.IsType<WriteLine>(activity);
        var context = new SimpleActivityExecutionContext(serviceProvider, writeLine, CancellationToken.None);
        RuntimeActivityInputMemory.Seed(context, [new RuntimeMaterializedActivityInput("Text", textArgument, "Hello World!")]);

        var output = await CaptureConsoleAsync(() => ((IActivity)writeLine).ExecuteAsync(context));

        Assert.Equal("Hello World!", output.Trim());
    }

    private static async Task<string> CaptureConsoleAsync(Func<ValueTask> action)
    {
        var original = Console.Out;
        await using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }
}
