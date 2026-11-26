using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class PublishWorkflowRequestHandlerTests
{
    // A reusable activity call in a workflow can bind its input to one of the workflow's own inputs, as an ordinary
    // activity can. The workflow's request is keyed by the input's name, so the call reads that request member;
    // an unlowered Input expression has no runtime handler and poisons the call when it starts.
    [Fact]
    public async Task A_reusable_call_input_reading_a_workflow_input_publishes_the_request_read_of_its_name()
    {
        var read = new WorkflowArgumentState("value", new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "greeting" }), "Input"), null, null, null, null);
        var fixture = await ReusableDefaultAsync(new("Literal", JsonSerializer.SerializeToElement("default")), read);
        fixture.Version.State = fixture.Version.State with
        {
            Inputs = [new InputDefinition("greeting", "Greeting", new("String"), null, "Greeting", null, IsNullable: false, IsRequired: true)]
        };

        var publication = await fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var call = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.InputBindings.ContainsKey("value"));
        var binding = call.InputBindings["value"];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, binding.Source);
        Assert.Equal("Greeting", binding.WorkflowRequest!.MemberKey);
        Assert.Null(binding.Expression);
    }
}
