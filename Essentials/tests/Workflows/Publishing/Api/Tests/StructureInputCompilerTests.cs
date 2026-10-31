using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class StructureInputCompilerTests
{
    [Fact]
    public async Task Projected_inputs_use_normal_typed_bindings_and_replace_consumed_ordinary_inputs()
    {
        var executable = await Compile(true);
        var node = executable.RootActivity;
        var binding = Assert.Single(node.InputBindings).Value;
        Assert.Equal("Case:a:Condition", binding.InputKey);
        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal("Boolean", binding.TargetType.Alias);
        Assert.True(Assert.Single(node.ActivityContract!.Inputs).Value.IsStructureInput);
        Assert.True(binding.Literal!.InlineValue!.Value.GetBoolean());
    }

    [Fact]
    public async Task Changing_a_projected_condition_changes_the_normal_artifact_hash()
    {
        var first = await Compile(true);
        var second = await Compile(false);
        Assert.NotEqual(first.Identity.ArtifactHash, second.Identity.ArtifactHash);
    }

    private static Task<WorkflowExecutable> Compile(bool condition)
    {
        var node = SecretBindingCompilerFixture.Node(typeof(WriteLine), new ArgumentState("text",
            new("a stale, deliberately invalid source", "UnknownSyntax"), null, null, null, null));
        return SecretBindingCompilerFixture.CompileAsync(node, [typeof(WriteLine)], structures: new ProjectedStructure(condition));
    }

    private sealed class ProjectedStructure(bool condition) : IActivityStructureService
    {
        public ActivityStructureInputProjection ProjectInputs(ActivityNode activity) => new([
            new("Case:a:Condition", new InputDefinition("Case:a:Condition", "Condition", new("Boolean"), null, "Condition", null, false),
                new("untrusted-authored-key", new(condition, "Literal"), null, null, null, null))], ["text"]);
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
    }
}
