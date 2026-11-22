using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// A workflow-level <c>Input</c> read, in the form Studio stores (<c>{ referenceKey }</c>), end to end: publication
/// lowers it to the request read of the declared input's name, and the run reads what the caller supplied, or the
/// input's declared default when the caller supplies nothing.
/// </summary>
public sealed class WorkflowInputReadPublicationRunTests
{
    // The reference key and the name differ, as they do for an input Studio has renamed.
    private static readonly InputDefinition Greeting = new("greeting-ref", "greeting", new TypeReference("String"), null, "Greeting", null,
        IsNullable: false, IsRequired: false, DefaultValue: JsonSerializer.SerializeToElement("hello from the default"), DefaultSyntax: "Literal");

    private static ActivityNode Root() => new("reader", "reader-version",
        [new(nameof(MemberReadPublicationRunTests.MemberReader.Value),
            new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "greeting-ref" }), "Input"), null, null, null, null)], []);

    [Fact]
    public async Task A_published_Input_read_is_a_request_read_of_the_inputs_name_and_reads_the_supplied_value()
    {
        using var publication = new MemberReadPublicationRunTests.Publication(memberReads: false, typed: false);

        var executable = await publication.Compile(Root(), inputs: [Greeting]);

        var binding = executable.RootActivity.InputBindings[nameof(MemberReadPublicationRunTests.MemberReader.Value)];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, binding.Source);
        Assert.Equal("greeting", binding.WorkflowRequest!.MemberKey);
        Assert.Null(binding.WorkflowRequest.Path);

        await using var harness = MemberReadPublicationRunTests.Harness();
        var run = await harness.RunAsync(executable, new Dictionary<string, JsonElement> { ["greeting"] = JsonSerializer.SerializeToElement("hello from the caller") });

        run.AssertWorkflowCompleted();
        Assert.Equal("hello from the caller", run.AssertCompleted("reader").InputSnapshot!.Values[nameof(MemberReadPublicationRunTests.MemberReader.Value)].InlineValue!.Value.GetString());
    }

    /// <summary>
    /// Started through the real start dispatcher with no inputs, the run reads the input's declared default: the
    /// dispatcher applies the executable's input contract, which is keyed by the same name.
    /// </summary>
    [Fact]
    public async Task A_published_Input_read_of_an_omitted_input_reads_its_declared_default()
    {
        using var publication = new MemberReadPublicationRunTests.Publication(memberReads: false, typed: false);
        var executable = await publication.Compile(Root(), inputs: [Greeting]);
        await using var harness = MemberReadPublicationRunTests.Harness();

        var reference = await harness.PublishAsync(executable, "source-input-read");
        await harness.StartPublishedAsync(reference, WorkflowExecutionHarness.WorkflowExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        run.AssertWorkflowCompleted();
        Assert.Equal("hello from the default", run.AssertCompleted("reader").InputSnapshot!.Values[nameof(MemberReadPublicationRunTests.MemberReader.Value)].InlineValue!.Value.GetString());
    }
}
