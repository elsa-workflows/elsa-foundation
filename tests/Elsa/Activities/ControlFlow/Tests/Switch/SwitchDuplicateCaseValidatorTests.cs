using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Switch.Internal;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Tests;

public sealed class SwitchDuplicateCaseValidatorTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structureService;
    private readonly SwitchDuplicateCaseValidator _validator;

    public SwitchDuplicateCaseValidatorTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.Design.ActivitiesControlFlowDesignFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _structureService = new DefaultActivityStructureService(_provider.GetServices<IActivityStructureHandler>());
        _validator = new SwitchDuplicateCaseValidator(_structureService);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_RegistersSwitchDuplicateCaseValidatorAsDraftValidator()
    {
        // Inspect the descriptor rather than activating: the validator's IActivityStructureService
        // dependency is supplied by the design-validations feature at composition time, not by the
        // activity feature in isolation.
        var services = new ServiceCollection();
        new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.ControlFlow.Design.ActivitiesControlFlowDesignFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDraftValidator) &&
            descriptor.ImplementationType == typeof(SwitchDuplicateCaseValidator));
    }

    [Fact]
    public async Task DuplicateCaseMatchValues_ProduceValidationError()
    {
        var errors = await Validate(SwitchNode("switch-1", "a", "a"));

        var error = Assert.Single(errors);
        Assert.Equal("switch-1", error.Path);
        Assert.Equal(SwitchDuplicateCaseValidator.ErrorType, error.Type);
        Assert.Contains("'a'", error.Message);
    }

    [Fact]
    public async Task UniqueCaseMatchValues_ProduceNoError()
    {
        var errors = await Validate(SwitchNode("switch-1", "a", "b", "c"));

        Assert.Empty(errors);
    }

    [Fact]
    public async Task MultipleDuplicateValues_ProduceOneErrorPerOffendingValue()
    {
        var errors = await Validate(SwitchNode("switch-1", "a", "a", "b", "b", "c"));

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal("switch-1", error.Path));
        Assert.Contains(errors, error => error.Message.Contains("'a'"));
        Assert.Contains(errors, error => error.Message.Contains("'b'"));
    }

    [Fact]
    public async Task NestedSwitch_IsValidated()
    {
        // An inner Switch with duplicate cases sits inside an outer Switch's "outer" case branch; the
        // validator must walk the tree and surface the inner node's duplicate.
        var inner = SwitchNode("switch-inner", "x", "x");
        var outer = _structureService.ReplaceChildren(
            SwitchNode("switch-outer", "outer"),
            [new ActivityChildProjection(SwitchActivity.CaseSlotName("outer"), [inner])]);

        var errors = await Validate(outer);

        var error = Assert.Single(errors);
        Assert.Equal("switch-inner", error.Path);
        Assert.Contains("'x'", error.Message);
    }

    private async Task<IReadOnlyList<ValidationError>> Validate(ActivityNode rootActivity)
    {
        var state = new WorkflowDefinitionState([], rootActivity, [], [], null);
        return [.. await _validator.Validate(new StubDraft(state), CancellationToken.None)];
    }

    // Builds a Switch ActivityNode whose authored structure declares the given case match values (no
    // branch activities) by serializing the cases array directly.
    private static ActivityNode SwitchNode(string nodeId, params string[] matches)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            cases = matches.Select(match => new { match }).ToArray()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new ActivityNode(nodeId, $"activity-version-{nodeId}", [], [],
            new ActivityNodeStructure(SwitchActivity.StructureKind, SwitchActivity.StructureSchemaVersion, payload));
    }

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UtcNow;
    }
}
