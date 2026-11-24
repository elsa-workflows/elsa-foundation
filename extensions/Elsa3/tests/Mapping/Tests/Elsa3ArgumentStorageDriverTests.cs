using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// #5683: a workflow input or output on an Elsa 3 storage driver other than the default is refused with a code, like a
/// variable (#5666). No Elsa 4 storage driver stands for an Elsa 3 one: the type registry's alias for the driver is not a
/// storage driver key, so a reusable workflow's publication refused it and a plain workflow dropped it silently. Registering
/// the driver's type does not change that. The default driver converts to no storage driver.
/// </summary>
public sealed class Elsa3ArgumentStorageDriverTests
{
    private const string DefaultDriver = "Elsa.Workflows.WorkflowInstanceStorageDriver, Elsa.Workflows.Core";
    private const string CustomDriver = "Acme.BlobStorageDriver, Acme";
    private static readonly string RegisteredDriver = typeof(BlobStorageDriver).AssemblyQualifiedName!;

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
        var state = result.Value!.State;
        Assert.Null(argument == Argument.Input ? state.Inputs.Single().StorageDriverType : state.Outputs.Single().StorageDriverType);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is Elsa3ImportDiagnosticCodes.InputStorageDriverUnmapped or Elsa3ImportDiagnosticCodes.OutputStorageDriverUnmapped);
        if (reusable)
        {
            // The published contract keeps the value inline, on the built-in driver.
            var contract = Elsa3ImportedActivityContract.Build(result.Value);
            Assert.Equal("elsa.json", argument == Argument.Input ? contract.Inputs.Single().StorageDriverKey : contract.Outputs.Single().StorageDriverKey);
        }
    }

    [Theory]
    [InlineData(Argument.Input, false)]
    [InlineData(Argument.Output, false)]
    [InlineData(Argument.Input, true)]
    [InlineData(Argument.Output, true)]
    public async Task An_unregistered_storage_driver_is_refused_with_guidance(Argument argument, bool reusable)
    {
        var result = await ConvertAsync(Definition(argument, CustomDriver, reusable), reusable);

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        var (code, kind, path) = Expected(argument);
        Assert.Equal(code, refusal.Code);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Error, refusal.Severity);
        Assert.Equal(path, refusal.Path);
        Assert.Equal("report", refusal.Metadata[$"{kind}Name"]);
        Assert.Contains($"{kind} 'report'", refusal.Message);
        Assert.Contains($"'{CustomDriver}'", refusal.Message);
        Assert.Contains("no storage driver that stands for an Elsa 3 one", refusal.Message);
        Assert.Contains($"set the {kind.ToLowerInvariant()}'s storage back to the default (the workflow instance)", refusal.Guidance);
    }

    [Theory]
    [InlineData(Argument.Input, false)]
    [InlineData(Argument.Output, false)]
    [InlineData(Argument.Input, true)]
    [InlineData(Argument.Output, true)]
    public async Task A_storage_driver_registered_in_Elsa_4_is_refused_too(Argument argument, bool reusable)
    {
        var registry = ConversionServices.TypeRegistry();
        registry.RegisterType(typeof(BlobStorageDriver), "acme.blobStorageDriver");

        var result = await ConvertAsync(Definition(argument, RegisteredDriver, reusable), reusable, services => services.AddSingleton(registry));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Expected(argument).Code, refusal.Code);
        Assert.Contains($"'{RegisteredDriver}'", refusal.Message);
    }

    /// <summary>A reusable SetOutput naming a declared output on a custom driver reports that driver, not only the intrinsic.</summary>
    [Fact]
    public async Task A_reusable_output_written_by_SetOutput_reports_its_storage_driver()
    {
        var definition = Definition(Argument.Output, CustomDriver, reusable: true,
            Activity("set", "Elsa.SetOutput", ("outputName", Literal("report")), ("outputValue", Literal("42"))));

        var result = await ConvertAsync(definition, reusable: true);

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.OutputStorageDriverUnmapped);
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
            [(Elsa3ImportDiagnosticCodes.InputStorageDriverUnmapped, "$.inputs[1]"), (Elsa3ImportDiagnosticCodes.OutputStorageDriverUnmapped, "$.outputs[0]")],
            result.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => (diagnostic.Code, diagnostic.Path)).OrderBy(item => item.Path, StringComparer.Ordinal));
    }

    private static (string Code, string Kind, string Path) Expected(Argument argument) => argument == Argument.Input
        ? (Elsa3ImportDiagnosticCodes.InputStorageDriverUnmapped, "Input", "$.inputs[0]")
        : (Elsa3ImportDiagnosticCodes.OutputStorageDriverUnmapped, "Output", "$.outputs[0]");

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

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3WorkflowDefinition definition,
        bool reusable,
        Action<IServiceCollection>? configure = null) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, reusable, configure: configure);

    private sealed class BlobStorageDriver;
}
