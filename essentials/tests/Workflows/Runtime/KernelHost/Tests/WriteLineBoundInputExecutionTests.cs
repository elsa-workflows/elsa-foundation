using Kernel.Foundation.Contracts;
using Kernel.Native;
using Xunit;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class WriteLineBoundInputExecutionTests
{
    [Fact]
    public async Task WriteLine_hydrates_plain_text_without_an_argument_or_memory_wrapper()
    {
        var line = Compiled.WriteLine("line", Compiled.Literal("text", "Hello World!", "String"));
        var workflow = NativeExecutableMapper.Map(Compiled.Executable(line));
        using var writer = new StringWriter();

        var turn = await NativeTurn.StartAsync(workflow, new NativeExecutionOptions { ConsoleOutput = writer });
        var output = writer.ToString();

        Assert.Equal(ActorStatus.Finished, turn.Status);
        Assert.Equal("Hello World!", output.Trim());
    }
}
