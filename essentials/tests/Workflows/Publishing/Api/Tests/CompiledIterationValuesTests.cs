using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// #5849: a compiled structure carries exactly the iteration values its node projects, so read admission can trust it
/// where no authored document remains. Publishing owns the field: a value the owner's structure handler wrote is replaced.
/// </summary>
public sealed class CompiledIterationValuesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_compiled_structure_carries_exactly_its_projected_iteration_values(bool declaresIndex)
    {
        var node = SecretBindingCompilerFixture.Node(typeof(WriteLine), new ArgumentState("text",
            ArgumentValue.FromClr("hello", "Literal"), null, null, null, null));

        var executable = await SecretBindingCompilerFixture.CompileAsync(node, [typeof(WriteLine)], structures: new ForgingStructure(declaresIndex));

        var payload = executable.RootActivity.Structure!.Payload;
        Assert.Equal("owned", payload.GetProperty("ownerField").GetString());
        if (declaresIndex)
            Assert.Equal(new[] { "index" }, payload.GetProperty(CompiledStructureFields.IterationValues).Deserialize<string[]>());
        else
            Assert.False(payload.TryGetProperty(CompiledStructureFields.IterationValues, out _));
    }

    /// <summary>An owner whose compiled structure forges the Publishing-owned field.</summary>
    private sealed class ForgingStructure(bool declaresIndex) : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public IReadOnlyCollection<VariableDefinition> ProjectIterationVariables(ActivityNode activity) =>
            declaresIndex ? [new("index", "index", new TypeReference("Any"), null, null)] : [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) =>
            new("forging", "1", JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["ownerField"] = "owned",
                [CompiledStructureFields.IterationValues] = new[] { "forged" }
            }));
    }
}
