using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// BulkDispatchWorkflows runs its slots concurrently across item passes, so a slot that writes a workflow variable gets
/// spec 266's publish warning, never a refusal (spec 1242 FR-010a).
/// </summary>
public sealed partial class ValueFlowValidatorTests
{
    [Theory]
    [InlineData("childCompleted")]
    [InlineData("childFaulted")]
    public async Task A_bulk_dispatch_slot_writing_a_workflow_variable_is_warned_about(string slot)
    {
        var bulk = Structured("bulk", BulkDispatchWorkflowsConstants.StructureKind,
            new Dictionary<string, ActivityNode> { ["dispatch"] = Activity("dispatch"), [slot] = Set("set", "counter", "$workflow") });

        var errors = await ValidateWithBulkAsync(Sequence("root", bulk));

        Assert.Equal("$workflow/variables/counter", SingleConcurrentWrite(errors).Path);
        Assert.DoesNotContain(errors, error => error.Severity != "Warning");
    }

    [Fact]
    public async Task A_bulk_dispatch_Dispatch_capturing_its_output_into_a_workflow_variable_is_warned_about()
    {
        var bulk = Structured("bulk", BulkDispatchWorkflowsConstants.StructureKind,
            new Dictionary<string, ActivityNode> { ["dispatch"] = Capture("dispatch", "lastChild") });

        var errors = await ValidateWithBulkAsync(Sequence("root", bulk));

        Assert.Equal("$workflow/variables/lastChild", SingleConcurrentWrite(errors).Path);
        Assert.DoesNotContain(errors, error => error.Severity != "Warning");
    }

    private static async Task<IReadOnlyCollection<Elsa.Workflows.Design.Validations.Core.Models.ValidationError>> ValidateWithBulkAsync(ActivityNode root)
    {
        var catalog = new StubActivityCatalog().Add("test.activity@1").Add("elsa.sequence.structure@1").Add($"{BulkDispatchWorkflowsConstants.StructureKind}@1");
        using var services = ActivityDataSelectionTestSupport.Provider(catalog, configure: collection =>
        {
            var feature = new ServiceCollection();
            new DispatchWorkflowDesignFeature().ConfigureServices(feature);
            foreach (var handler in feature.Where(descriptor => descriptor.ServiceType == typeof(IActivityStructureHandler)))
                collection.Add(handler);
        });
        var validator = new ValueFlowValidator(services.GetRequiredService<IActivityStructureService>(), services.GetRequiredService<IActivityDataSelectionAnalyzer>());
        return [.. await validator.Validate(new StubDraft(new WorkflowDefinitionState([], root, [], [], null)), CancellationToken.None)];
    }
}
