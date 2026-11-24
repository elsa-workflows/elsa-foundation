using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// #5683: a workflow input or output on an Elsa 3 storage driver other than the default keeps the driver type's full name
/// as its Elsa 4 key, so a driver ported to Elsa 4 under that key matches without a mapping. Elsa 4 honours it only on a
/// reusable workflow whose host can use the driver for the member's type; anything else is refused with a code. The
/// default driver converts to no storage driver.
/// </summary>
public sealed class Elsa3ArgumentStorageDriverTests
{
    private const string DefaultDriver = "Elsa.Workflows.WorkflowInstanceStorageDriver, Elsa.Workflows.Core";
    private const string CustomDriver = "Acme.BlobStorageDriver, Acme";
    private const string CustomDriverKey = "Acme.BlobStorageDriver";

    public enum Argument { Input, Output }

    [Theory]
    [InlineData(Argument.Input, false)]
    [InlineData(Argument.Output, false)]
    [InlineData(Argument.Input, true)]
    [InlineData(Argument.Output, true)]
    public async Task The_default_storage_driver_converts_to_no_storage_driver(Argument argument, bool reusable)
    {
        var result = await ConvertAsync(Definition(argument, DefaultDriver, reusable), reusable);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Null(Driver(result.Value!, argument));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.Contains("STORAGE-DRIVER", StringComparison.Ordinal));
        if (reusable)
            Assert.Equal("elsa.json", ContractDriver(result.Value!, argument));
    }

    [Theory]
    [InlineData("Acme.BlobStorageDriver, Acme", "Acme.BlobStorageDriver")]
    [InlineData(" Acme.BlobStorageDriver ", "Acme.BlobStorageDriver")]
    [InlineData("Elsa.Workflows.WorkflowInstanceStorageDriver, Elsa.Workflows.Core", null)]
    [InlineData("WorkflowInstanceStorageDriver", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_key_is_the_Elsa_3_driver_type_full_name(string? elsa3Driver, string? key) =>
        Assert.Equal(key, Elsa3VariableMapper.StorageDriverKey(elsa3Driver));

    /// <summary>A reusable workflow keeps the key when this host can use the driver for the member's type, as publication requires.</summary>
    [Theory]
    [InlineData(Argument.Input)]
    [InlineData(Argument.Output)]
    public async Task A_reusable_member_keeps_a_driver_this_host_can_use(Argument argument)
    {
        var result = await ConvertAsync(Definition(argument, CustomDriver, reusable: true), reusable: true, Capabilities("elsa.json", CustomDriverKey));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(CustomDriverKey, Driver(result.Value!, argument));
        Assert.Equal(CustomDriverKey, ContractDriver(result.Value!, argument));
    }

    /// <summary>No driver under the key, or one the member's type does not list, would fail publication; it is refused instead.</summary>
    [Theory]
    [InlineData(Argument.Input, false)]
    [InlineData(Argument.Output, false)]
    [InlineData(Argument.Input, true)]
    [InlineData(Argument.Output, true)]
    public async Task A_reusable_member_on_a_driver_this_host_cannot_use_is_refused_with_guidance(Argument argument, bool hostHasCapabilities)
    {
        var configure = hostHasCapabilities ? Capabilities("elsa.json") : null;

        var result = await ConvertAsync(Definition(argument, CustomDriver, reusable: true), reusable: true, configure);

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        var (unregistered, _, kind, path) = Expected(argument);
        Assert.Equal(unregistered, refusal.Code);
        Assert.Equal(path, refusal.Path);
        Assert.Equal("report", refusal.Metadata[$"{kind}Name"]);
        Assert.Equal(CustomDriverKey, refusal.Metadata["StorageDriverKey"]);
        Assert.Contains($"{kind} 'report' uses the Elsa 3 storage driver '{CustomDriver}'", refusal.Message);
        Assert.Contains($"no storage driver with the key '{CustomDriverKey}'", refusal.Message);
        Assert.Contains($"whose key is '{CustomDriverKey}' (the Elsa 3 driver type's full name)", refusal.Guidance);
        Assert.Contains("(#5698)", refusal.Guidance);
        Assert.Contains($"set the {kind.ToLowerInvariant()}'s storage back to the default (the workflow instance)", refusal.Guidance);
    }

    /// <summary>Elsa 4 keeps a workflow's own inputs and outputs in the instance (#5698), so a driver is refused there even when the host has it.</summary>
    [Theory]
    [InlineData(Argument.Input)]
    [InlineData(Argument.Output)]
    public async Task A_member_of_a_workflow_that_is_not_reusable_is_refused_even_when_the_host_has_the_driver(Argument argument)
    {
        var result = await ConvertAsync(Definition(argument, CustomDriver, reusable: false), reusable: false, Capabilities("elsa.json", CustomDriverKey));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        var (_, unsupported, kind, path) = Expected(argument);
        Assert.Equal(unsupported, refusal.Code);
        Assert.Equal(path, refusal.Path);
        Assert.Contains($"{kind} 'report' uses the Elsa 3 storage driver '{CustomDriver}'", refusal.Message);
        Assert.Contains("no storage driver for them yet (#5698)", refusal.Message);
        Assert.Contains($"registered under the key '{CustomDriverKey}' will convert", refusal.Guidance);
    }

    /// <summary>A reusable SetOutput naming a declared output on a driver the host cannot use reports that driver.</summary>
    [Fact]
    public async Task A_reusable_output_written_by_SetOutput_reports_its_storage_driver()
    {
        var definition = Definition(Argument.Output, CustomDriver, reusable: true,
            Activity("set", "Elsa.SetOutput", ("outputName", Literal("report")), ("outputValue", Literal("42"))));

        var result = await ConvertAsync(definition, reusable: true);

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.OutputStorageDriverUnregistered);
        Assert.Equal("$.outputs[0]", refusal.Path);
    }

    /// <summary>Each input and output on a custom driver is reported, at its own index.</summary>
    [Fact]
    public async Task Every_argument_on_a_custom_driver_is_reported_at_its_index()
    {
        var definition = ConversionServices.Definition(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi")))));
        definition.Inputs =
        [
            new Elsa3WorkflowArgumentDefinition { Name = "kept", Type = "String" },
            new Elsa3WorkflowArgumentDefinition { Name = "doc", Type = "String", StorageDriverType = CustomDriver }
        ];
        definition.Outputs =
        [
            new Elsa3WorkflowArgumentDefinition { Name = "report", Type = "String", StorageDriverType = CustomDriver },
            new Elsa3WorkflowArgumentDefinition { Name = "summary", Type = "String", StorageDriverType = DefaultDriver }
        ];

        var result = await ConvertAsync(definition, reusable: false);

        Assert.False(result.Succeeded);
        Assert.Equal(
            [(Elsa3ImportDiagnosticCodes.InputStorageDriverUnsupported, "$.inputs[1]"), (Elsa3ImportDiagnosticCodes.OutputStorageDriverUnsupported, "$.outputs[0]")],
            result.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => (diagnostic.Code, diagnostic.Path)).OrderBy(item => item.Path, StringComparer.Ordinal));
    }

    private static (string Unregistered, string Unsupported, string Kind, string Path) Expected(Argument argument) => argument == Argument.Input
        ? (Elsa3ImportDiagnosticCodes.InputStorageDriverUnregistered, Elsa3ImportDiagnosticCodes.InputStorageDriverUnsupported, "Input", "$.inputs[0]")
        : (Elsa3ImportDiagnosticCodes.OutputStorageDriverUnregistered, Elsa3ImportDiagnosticCodes.OutputStorageDriverUnsupported, "Output", "$.outputs[0]");

    private static string? Driver(Elsa3ConvertedDefinition converted, Argument argument) =>
        argument == Argument.Input ? converted.State.Inputs.Single().StorageDriverType : converted.State.Outputs.Single().StorageDriverType;

    private static string ContractDriver(Elsa3ConvertedDefinition converted, Argument argument)
    {
        var contract = Elsa3ImportedActivityContract.Build(converted);
        return argument == Argument.Input ? contract.Inputs.Single().StorageDriverKey : contract.Outputs.Single().StorageDriverKey;
    }

    private static Elsa3WorkflowDefinition Definition(Argument argument, string driver, bool reusable, Elsa3Activity? body = null)
    {
        var definition = ConversionServices.Definition(Sequence("root", body ?? Activity("write", "Elsa.WriteLine", ("text", Literal("hi")))));
        var declared = new Elsa3WorkflowArgumentDefinition { Name = "report", Type = "String", StorageDriverType = driver };
        if (argument == Argument.Input)
            definition.Inputs = [declared];
        else
            definition.Outputs = [declared];
        if (reusable)
            definition.Options = new Elsa3WorkflowOptions { UsableAsActivity = true };
        return definition;
    }

    /// <summary>A host whose String type lists <paramref name="driverKeys"/> as its compatible storage drivers.</summary>
    private static Action<IServiceCollection> Capabilities(params string[] driverKeys) =>
        services => services.AddSingleton<IActivityContractCapabilityCatalog>(new Catalog(
        [
            new ActivityContractTypeCapability("String", "String", "Primitives", "text",
                new HashSet<CollectionKind> { CollectionKind.Single }, SupportsNull: true, SupportsDurability: true,
                driverKeys.ToHashSet(StringComparer.Ordinal))
        ]));

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3WorkflowDefinition definition,
        bool reusable,
        Action<IServiceCollection>? configure = null) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, reusable, configure: configure);

    private sealed record Catalog(IReadOnlyCollection<ActivityContractTypeCapability> Types) : IActivityContractCapabilityCatalog;
}
