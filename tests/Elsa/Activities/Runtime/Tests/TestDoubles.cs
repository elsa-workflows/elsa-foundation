using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>A constructor test-double returning a fixed activity. Used to assert registry + factory dispatch.</summary>
internal sealed class FakeConstructorA(string descriptorType) : IActivityConstructor
{
    public IActivity Returned { get; } = new WriteLine();
    public string DescriptorType { get; } = descriptorType;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken) => new(Returned);
}

/// <summary>A second, distinct constructor type with the same descriptor type — to trip the dup-guard.</summary>
internal sealed class FakeConstructorB(string descriptorType) : IActivityConstructor
{
    public string DescriptorType { get; } = descriptorType;

    public ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken) => new((IActivity)new WriteLine());
}

/// <summary>
/// Redirects <see cref="Console.Out"/> for the duration of an action and returns what was written.
/// Shared by the console-parity activity tests (WriteLine/WriteLines). <see cref="Console.SetOut"/> is
/// process-global, so callers must share the <c>"ConsoleCapture"</c> xUnit collection to serialize.
/// </summary>
internal static class ConsoleCapture
{
    public static async Task<string> RunAsync(Func<ValueTask> action)
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
