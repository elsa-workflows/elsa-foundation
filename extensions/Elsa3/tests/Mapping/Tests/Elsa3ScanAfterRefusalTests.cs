using System.Text.Json;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// An activity refusal refuses the definition but does not end the scan (#6231): the converter still analyses every
/// script, variable type and other node, so one migration run reports every finding, and the spec 1237 inventory lists
/// every component an engineer must port.
/// </summary>
public sealed class Elsa3ScanAfterRefusalTests
{
    [Fact]
    public async Task An_unmapped_activity_does_not_hide_the_host_functions_another_nodes_script_calls()
    {
        var root = Sequence("root",
            Activity("unmapped", "Acme.Unknown"),
            Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "someFunction()"))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        var unmapped = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.UnmappedType);
        Assert.Equal("Acme.Unknown", unmapped.Metadata[Elsa3MigrationDiagnosticMetadataKeys.ComponentName]);
        var missing = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.HostFunctionMissing);
        Assert.Equal("someFunction", missing.Metadata[Elsa3MigrationDiagnosticMetadataKeys.ComponentName]);
        Assert.Equal("$.root.activities[1].text.expression.value", missing.Path);
    }

    [Fact]
    public async Task The_unmapped_activitys_own_scripts_and_the_workflows_variable_types_are_still_reported()
    {
        var root = Sequence("root",
            Activity("unmapped", "Acme.Unknown", ("text", Expression("JavaScript", "otherFunction()"))));
        var variables = new[] { new Elsa3Variable { Id = "order", Name = "Order", TypeName = "Acme.Order" } };

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));

        Assert.False(result.Succeeded);
        Assert.Equal(
            [
                (Elsa3ActivityDiagnosticCodes.UnmappedType, "Acme.Unknown"),
                (Elsa3ImportDiagnosticCodes.HostFunctionMissing, "otherFunction"),
                (Elsa3ImportDiagnosticCodes.VariableTypeUnregistered, "Acme.Order")
            ],
            result.Diagnostics
                .Where(diagnostic => diagnostic.Metadata.ContainsKey(Elsa3MigrationDiagnosticMetadataKeys.ComponentName))
                .Select(diagnostic => (diagnostic.Code, diagnostic.Metadata[Elsa3MigrationDiagnosticMetadataKeys.ComponentName]))
                .OrderBy(item => item.Code, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_switch_case_holding_an_unmapped_activity_is_refused_once_for_that_activity()
    {
        var @switch = Activity("switch", "Elsa.Switch", ("mode", Literal("MatchAny")));
        @switch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label = "Case", condition = new { expression = new { type = "Literal", value = "true" } }, activity = Port(Activity("body", "Acme.Unknown")) }
        });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", @switch)));

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnmappedType, error.Code);
    }
}
