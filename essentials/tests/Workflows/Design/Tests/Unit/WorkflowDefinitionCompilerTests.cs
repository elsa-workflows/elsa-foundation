using System.Text.Json;
using Elsa.Workflows.Design.Core.Authoring;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class WorkflowDefinitionCompilerTests
{
    [Fact]
    public void Compiler_builds_once_per_definition_version_and_is_deterministic_across_instances()
    {
        var compiler = new WorkflowDefinitionCompiler();
        var firstDefinition = new DeterministicWorkflow();

        var first = compiler.Compile(firstDefinition, "version-1");
        var repeated = compiler.Compile(firstDefinition, "version-1");
        var secondVersion = compiler.Compile(firstDefinition, "version-2");
        var equivalent = compiler.Compile(new DeterministicWorkflow(), "version-1");

        Assert.Same(first, repeated);
        Assert.NotSame(first, secondVersion);
        Assert.Equal(2, firstDefinition.BuildCount);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(equivalent));
    }

    private sealed record Request(string Message);
    private sealed record Result(string Message);
    private sealed class Write;

    private sealed class DeterministicWorkflow : WorkflowDefinition<Request, Result>
    {
        public int BuildCount { get; private set; }

        protected override void Build(IWorkflowBuilder<Request, Result> workflow)
        {
            BuildCount++;
            workflow.Sequence.Add<Write, string>(
                "test/write@1",
                inputs => inputs.From("message", workflow.From(request => request.Message)),
                nodeId: "write");
        }
    }
}
