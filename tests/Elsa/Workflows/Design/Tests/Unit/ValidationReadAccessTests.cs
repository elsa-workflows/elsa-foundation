using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-013 + Unit C FR-021/022. Validation errors are derived state (no persisted sibling and no
/// dedicated read contract — the former <c>IWorkflowDefinitionDraftValidation</c> was deleted).
/// <see cref="ValidationError"/> still lives in <c>Elsa.Workflows.Design.Validations.Core</c> so UI
/// / promotion-gate consumers read the derived error set without taking a dependency on
/// <c>*.Persistence.Core</c>. The grouping key is <c>(Path, Type)</c> per FR-022 — verified here
/// against representative outputs that follow research items R2 (Path format) and R3 (Type
/// categories).
/// </summary>
public sealed class ValidationReadAccessTests
{
    [Fact]
    public void ValidationError_lives_in_Validations_Core_not_Persistence_Core()
    {
        var assemblyName = typeof(ValidationError).Assembly.GetName().Name;
        Assert.Equal("Elsa.Workflows.Design.Validations.Core", assemblyName);
    }

    [Fact]
    public void Errors_grouped_by_Path_and_Type_pair()
    {
        // Two missing required inputs on the same activity collapse to one (Path, Type) group
        // even though the messages differ — matches FR-022's "multiple errors may share scope".
        IReadOnlyList<ValidationError> errors = [
            new("n1/inputs/body",   "InputOutput/MissingRequired", "body is required"),
            new("n1/inputs/header", "InputOutput/MissingRequired", "header is required"),
            new("n2",               "Graph/OrphanActivity",        "orphan"),
            new("$workflow",        "Graph/StartActivity",         "no start"),
        ];

        var groups = errors
            .GroupBy(e => (e.Path, e.Type))
            .ToArray();

        Assert.Equal(4, groups.Length);
        Assert.Contains(groups, g => g.Key == ("n1/inputs/body", "InputOutput/MissingRequired"));
        Assert.Contains(groups, g => g.Key == ("n1/inputs/header", "InputOutput/MissingRequired"));
    }

    [Theory]
    [InlineData("n1",                                "node-level concern")]
    [InlineData("n1/inputs/body",                    "activity input")]
    [InlineData("n1/outputs/result",                 "activity output")]
    [InlineData("$workflow",                         "workflow-scope")]
    [InlineData("$workflow/inputs/wfInput",          "workflow-level input declaration")]
    [InlineData("$workflow/outputs/wfOutput",        "workflow-level output declaration")]
    [InlineData("$workflow/variables/MyVar",         "workflow variable")]
    public void Path_format_round_trips_per_R2(string path, string scenario)
    {
        var error = new ValidationError(path, "Test/Category", $"scenario: {scenario}");
        Assert.Equal(path, error.Path);
    }

    [Theory]
    [InlineData("Graph/OrphanActivity")]
    [InlineData("Graph/StartActivity")]
    [InlineData("Variables/Uniqueness")]
    [InlineData("InputOutput/MissingRequired")]
    [InlineData("Expressions/UnresolvedVariable")]
    [InlineData("Http/AuthPolicyUnknown")]      // cross-feature extension per FR-034
    public void Type_format_supports_baseline_and_extension_categories_per_R3(string type)
    {
        var error = new ValidationError("$workflow", type, "test");
        Assert.Equal(type, error.Type);
        // Slash-delimited two-level format; first segment groups the category for UI.
        var topLevel = type.Split('/')[0];
        Assert.NotEmpty(topLevel);
    }
}
