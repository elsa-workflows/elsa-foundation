using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnVariableInterchangeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Object")]
    [InlineData("Elsa.Any")]
    [InlineData("Any")]
    public void Any_aliases_round_trip_through_the_native_variable_profile(string? typeHint)
    {
        var issues = new List<BpmnImportIssue>();
        var imported = BpmnVariableInterchange.Import(new("items", typeHint), "process", issues);

        Assert.Empty(issues);
        Assert.Equal(TypeAliasConvention.AnyAlias, imported.Type.Alias);
        Assert.True(BpmnVariableInterchange.TryExport(imported, out var declaration, out var reason), reason);
        Assert.Equal(TypeAliasConvention.AnyAlias, declaration.TypeHint);

        var reimported = BpmnVariableInterchange.Import(declaration, "process", issues);
        Assert.Empty(issues);
        Assert.Equal(imported, reimported);
    }
}
