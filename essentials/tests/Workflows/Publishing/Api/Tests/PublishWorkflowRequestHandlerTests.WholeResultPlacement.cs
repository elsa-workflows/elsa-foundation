using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class PublishWorkflowRequestHandlerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Original_staged_whole_results_publish_and_place_with_independent_exact_producers(bool portable, bool nested)
    {
        var value = portable ? new ArgumentValue(new ExpressionDefinition("JavaScript", "args.result", new("Elsa.Any"),
            new Dictionary<string, ExpressionParameterBinding> { ["result"] = new ActivityResultExpressionParameterBinding("producer", "$result") },
            JsonSerializer.SerializeToElement(new { }), "binding-pure-v1"), "JavaScript") :
            new ArgumentValue(JsonSerializer.SerializeToElement(new { producerNodeId = "producer", projectionKey = "$result" }), "ActivityResult");
        var read = new ArgumentState("Text", value, null, null, null, null);
        var executable = await PublishPlacedBodyAsync(read, assignOutput: true, copies: 2, nested: nested, outputAlias: "Elsa.Any",
            beforePlacement: template =>
            {
                var setter = Assert.Single(template.Root.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.SetOutput);
                Assert.Equal("producer", Producer(setter.InputBindings["value"]));
                Assert.Contains(template.DirectDependencies, dependency => dependency.OccurrenceId == "producer");
                Assert.DoesNotContain(template.DirectDependencies, dependency => dependency.OccurrenceId == "assign");
            });
        var setters = executable.RootActivity.DescendantsAndSelf().Where(node => node.IntrinsicKind == WorkflowIntrinsicKind.SetOutput).ToArray();
        Assert.Equal(nested ? 4 : 2, setters.Length);
        var producers = setters.Select(setter => Producer(setter.InputBindings["value"])).ToArray();
        Assert.Equal(setters.Length, producers.Distinct(StringComparer.Ordinal).Count());
        foreach (var setter in setters)
        {
            var producerId = Producer(setter.InputBindings["value"]);
            Assert.NotEqual("producer", producerId);
            var parent = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.ChildSlots.SelectMany(slot => slot.Activities)
                .Any(child => child.ExecutableNodeId == setter.ExecutableNodeId));
            Assert.Contains(parent.ChildSlots.SelectMany(slot => slot.Activities), child => child.ExecutableNodeId == producerId);
        }

        static string Producer(RuntimeInputBinding binding) => binding.ActivityResult?.ProducerExecutableNodeId ??
            Assert.IsType<ActivityResultExpressionParameterBinding>(binding.Expression!.Parameters["result"]).ProducerNodeId;
    }
}
