using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Internal;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests;

public sealed class ProjectedInputValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Variable_validation_ignores_consumed_unused_values_and_checks_projected_predicates(bool invalidPredicate)
    {
        var consumer = new ActivityNode("consumer", "test", [Argument("Value", new VariableReference("stale"), "Variable")], []);
        var predicate = invalidPredicate ? Argument("foreign", new VariableReference("missing"), "Variable") : Argument("foreign", false, "Literal");
        var structures = new ProjectedStructure([consumer], predicate);
        var validator = new VariableExpressionResolverValidator(Options(), new ActivityTreeWalker(structures), new ScopedVariableResolver(structures));
        var errors = (await Validate(validator, StateOf(consumer))).ToArray();
        if (invalidPredicate) Assert.Contains(errors, error => error.Path.EndsWith("Case:a:Condition", StringComparison.Ordinal));
        else Assert.Empty(errors);
        Assert.DoesNotContain(errors, error => error.Path.EndsWith("Value", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_flow_validation_checks_projected_producers_and_ignores_unused_value(bool laterProducer)
    {
        var consumer = new ActivityNode("consumer", "test", [Result("Value", "missing")], []);
        var producer = new ActivityNode("producer", "test", [], []);
        var predicate = laterProducer ? Result("foreign", "producer") : Argument("foreign", true, "Literal");
        var structures = new ProjectedStructure([consumer, producer], predicate);
        var errors = (await Validate(new ValueFlowValidator(structures), StateOf(consumer, producer))).ToArray();
        if (laterProducer) Assert.Contains(errors, error => error.Type == "ValueFlow/UnavailableProducer" && error.Path.EndsWith("Case:a:Condition", StringComparison.Ordinal));
        else Assert.Empty(errors);
        Assert.DoesNotContain(errors, error => error.Path.EndsWith("Value", StringComparison.Ordinal));
    }

    private static WorkflowDefinitionState StateOf(params ActivityNode[] children) => new([], new("root", "sequence", [], [],
        new("elsa.sequence.structure", "1.0.0", JsonSerializer.SerializeToElement(new { activities = children }))), [], [], null);
    private static ArgumentState Argument(string key, object value, string syntax) => new(key, new(value, syntax), null, null, null, null);
    private static ArgumentState Result(string key, string producer) => Argument(key,
        JsonSerializer.SerializeToElement(new { producerNodeId = producer, projectionKey = "$result", producerScopeId = "workflow", isOptional = false }), "ActivityResult");

    private sealed class ProjectedStructure(ActivityNode[] children, ArgumentState predicate) : IActivityStructureService
    {
        public ActivityStructureInputProjection ProjectInputs(ActivityNode activity) => activity.NodeId == "consumer"
            ? new([new("Case:a:Condition", null, predicate)], ["Value"]) : ActivityStructureInputProjection.Empty;
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => activity.NodeId == "root" ? [new("Activities", children)] : [];
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
    }
}
