using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 1241: the declared types of a closed scope's variables, read from their rows, which a step's write into one of them
/// is checked against. A row that names no type leaves its variable untyped (<c>Any</c>), as before.
/// </summary>
public sealed class BoundaryVariableTypeProjectionTests
{
    private const string Boundary = "actexec-boundary";

    [Fact]
    public void A_typed_row_projects_its_declared_type()
    {
        var types = RuntimeInputBindingStateProjection.ProjectBoundaryVariableTypes(
            [Row("total", JsonSerializer.SerializeToElement(new { alias = "Int32" }))], Boundary);

        Assert.Equal("Int32", types["total"].Alias);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"Int32\"")]
    [InlineData("{\"alias\":\"\"}")]
    [InlineData("{\"alias\":5}")]
    public void A_row_without_a_usable_type_stays_untyped(string? schema)
    {
        var types = RuntimeInputBindingStateProjection.ProjectBoundaryVariableTypes(
            [Row("total", schema is null ? null : JsonDocument.Parse(schema).RootElement.Clone())], Boundary);

        Assert.False(types.ContainsKey("total"));
    }

    [Fact]
    public void Only_the_boundarys_variable_rows_count()
    {
        var types = RuntimeInputBindingStateProjection.ProjectBoundaryVariableTypes(
            [
                Row("total", JsonSerializer.SerializeToElement(new { alias = "Int32" }), boundary: "actexec-other"),
                Row("amount", JsonSerializer.SerializeToElement(new { alias = "Int32" }), role: ActivityExecutionScopeValues.InputRole)
            ],
            Boundary);

        Assert.Empty(types);
    }

    private static DurableValueState Row(string key, JsonElement? schema, string boundary = Boundary, string role = ActivityExecutionScopeValues.VariableRole)
    {
        var id = $"activity-scope:{boundary}:{role}:{key}";
        return new DurableValueState(
            id,
            "wf-1",
            id,
            new RuntimeValueTypeDescriptor("Int32", "elsa.json", schema),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            JsonSerializer.SerializeToElement(1),
            null,
            boundary,
            DateTimeOffset.UnixEpoch,
            ActivityExecutionScopeValues.Metadata(boundary, role, key, "elsa.json"));
    }
}
