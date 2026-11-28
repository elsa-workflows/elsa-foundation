using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class MappedClrCatalogAliasTests
{
    private const string CatalogKey = "Elsa.Examples.Migrated.WriteLine";

    [Fact]
    public void A_mounted_catalog_alias_can_select_the_pinned_builtin()
    {
        var workflow = NativeExecutableMapper.Map(Executable(ClrNode(CatalogKey, Types.WriteLine, Types.WriteLine)));

        var node = workflow.Nodes["mounted-activity"];
        Assert.Equal(typeof(Native.WriteLine), node.Type);
        Assert.Equal("mounted value", Assert.IsType<Native.LiteralBinding>(node.Inputs["Text"]).Value!.GetValue<string>());
    }

    [Theory]
    [InlineData(CatalogKey, Types.WriteLine, Types.WriteLine, "2")]
    [InlineData(CatalogKey, Types.WriteLine, Types.Event, "1")]
    [InlineData(CatalogKey, Types.WriteLine, Types.WriteLine, "1", Types.Event)]
    [InlineData(CatalogKey, "Custom.Activity", "Custom.Activity", "1")]
    [InlineData(CatalogKey, "Elsa.Finish", "Elsa.Finish", "1")]
    [InlineData(Types.CompensationScope, Types.WriteLine, Types.WriteLine, "1")]
    public void Stale_hidden_or_unsupported_catalog_aliases_are_refused(string catalogKey, string alias, string activation,
        string schema, string? contractAlias = null)
    {
        var fault = Assert.Throws<NativeMappingRefusedException>(() =>
            NativeExecutableMapper.Map(Executable(ClrNode(catalogKey, alias, activation, schema, contractAlias))));

        Assert.Contains("mounted-activity", fault.Message, StringComparison.Ordinal);
    }

    private static ExecutableNode ClrNode(string catalogKey, string alias, string activation, string schema = "1",
        string? contractAlias = null)
    {
        var kind = typeof(ClrActivityDescriptor).FullName!;
        var descriptor = JsonSerializer.SerializeToElement(new { typeAlias = alias });
        var contractPayload = JsonSerializer.SerializeToElement(new { typeAlias = contractAlias ?? alias });
        var contract = new ActivityContract(catalogKey, "1.0.0", kind, contractPayload, [],
            new ActivityResultContract(new ValueTypeDescriptor("Object"), false, ActivityValuePolicy.Default, []),
            [], new ActivityActivationRequirement(kind, activation));
        return new("mounted-activity", "mounted-activity", catalogKey, "1.0.0",
            new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, schema, descriptor),
            new Dictionary<string, RuntimeInputBinding> { ["text"] = Literal("text", "mounted value", "String") },
            new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            ActivityExecutionPlacement.Inline, activityContract: contract);
    }
}
