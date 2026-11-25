using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Every Elsa 3 storage driver converts to Elsa 4's default storage (elsa.json), on variables, inputs and outputs, reusable
/// workflow or not. The Workflow Instance driver and the obsolete Workflow driver kept the value with the instance, so they
/// convert silently. The Memory driver and custom drivers convert with a behaviour warning whose metadata names the member
/// and the Elsa 3 driver type, so the migration report never parses the message.
/// </summary>
public sealed class Elsa3StorageDriverCorrectionTests
{
    private const string InstanceDriver = "Elsa.Workflows.WorkflowInstanceStorageDriver, Elsa.Workflows.Core";
    private const string ObsoleteWorkflowDriver = "Elsa.Workflows.Services.WorkflowStorageDriver, Elsa.Workflows.Core";
    private const string MemoryDriver = "Elsa.Workflows.MemoryStorageDriver, Elsa.Workflows.Core";
    private const string CustomDriver = "Acme.BlobStorageDriver, Acme";

    public enum Member { Variable, Input, Output }

    [Theory]
    [InlineData(null, "WorkflowInstance")]
    [InlineData("", "WorkflowInstance")]
    [InlineData(InstanceDriver, "WorkflowInstance")]
    [InlineData("WorkflowInstanceStorageDriver", "WorkflowInstance")]
    [InlineData(ObsoleteWorkflowDriver, "WorkflowInstance")]
    [InlineData("WorkflowStorageDriver", "WorkflowInstance")]
    [InlineData(MemoryDriver, "Memory")]
    [InlineData("Elsa.Workflows.MemoryStorageDriver, Elsa.Workflows.Core, Version=3.6.3.0, Culture=neutral, PublicKeyToken=null", "Memory")]
    [InlineData("MemoryStorageDriver", "Memory")]
    [InlineData(CustomDriver, "Custom")]
    [InlineData("Acme.MemoryStorageDriver, Acme", "Custom")]
    public void Drivers_are_classified_by_their_type_name(string? driver, string kind) =>
        Assert.Equal(kind, Elsa3StorageDriverCorrections.Classify(driver).ToString());

    [Theory]
    [InlineData(Member.Variable, false, InstanceDriver)]
    [InlineData(Member.Input, false, InstanceDriver)]
    [InlineData(Member.Output, false, InstanceDriver)]
    [InlineData(Member.Variable, true, ObsoleteWorkflowDriver)]
    [InlineData(Member.Input, true, ObsoleteWorkflowDriver)]
    [InlineData(Member.Output, true, InstanceDriver)]
    [InlineData(Member.Output, true, ObsoleteWorkflowDriver)]
    [InlineData(Member.Input, false, ObsoleteWorkflowDriver)]
    public async Task A_driver_that_kept_the_value_with_the_instance_converts_silently(Member member, bool reusable, string driver)
    {
        var result = await ConvertAsync(Definition(member, driver, reusable), reusable);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Null(Driver(result.Value!, member));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.Contains("STORAGE", StringComparison.Ordinal));
        if (reusable && member != Member.Variable)
            Assert.Equal("elsa.json", ContractDriver(result.Value!, member));
    }

    [Theory]
    [InlineData(Member.Variable, false)]
    [InlineData(Member.Input, false)]
    [InlineData(Member.Output, false)]
    [InlineData(Member.Variable, true)]
    [InlineData(Member.Input, true)]
    [InlineData(Member.Output, true)]
    public async Task A_member_on_the_Memory_driver_converts_to_the_default_storage_with_a_warning(Member member, bool reusable)
    {
        var result = await ConvertAsync(Definition(member, MemoryDriver, reusable), reusable);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Null(Driver(result.Value!, member));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.MemoryStoragePersisted);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(Path(member), warning.Path);
        Assert.Equal("report", warning.Metadata[$"{member}Name"]);
        Assert.Equal(MemoryDriver, warning.Metadata["StorageDriverType"]);
        Assert.Equal("storage-driver", warning.Metadata["ComponentKind"]);
        Assert.Equal(MemoryDriver, warning.Metadata["ComponentName"]);
        Assert.Contains($"{member} 'report' used the Elsa 3 Memory storage driver", warning.Message);
        Assert.Contains("now persisted with the workflow instance", warning.Message);
        Assert.Contains("must never be persisted", warning.Guidance);
        Assert.Contains(Elsa3StorageDriverCorrections.Guide, warning.Guidance);
        if (reusable && member != Member.Variable)
            Assert.Equal("elsa.json", ContractDriver(result.Value!, member));
    }

    /// <summary>A reusable workflow too: the converter never carries a driver over, even one an Elsa 4 host could register.</summary>
    [Theory]
    [InlineData(Member.Variable, false)]
    [InlineData(Member.Input, false)]
    [InlineData(Member.Output, false)]
    [InlineData(Member.Variable, true)]
    [InlineData(Member.Input, true)]
    [InlineData(Member.Output, true)]
    public async Task A_member_on_a_custom_driver_converts_to_the_default_storage_with_a_warning(Member member, bool reusable)
    {
        var result = await ConvertAsync(Definition(member, $" {CustomDriver} ", reusable), reusable);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Null(Driver(result.Value!, member));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(Path(member), warning.Path);
        Assert.Equal("report", warning.Metadata[$"{member}Name"]);
        Assert.Equal(CustomDriver, warning.Metadata["StorageDriverType"]);
        Assert.Equal("storage-driver", warning.Metadata["ComponentKind"]);
        Assert.Equal(CustomDriver, warning.Metadata["ComponentName"]);
        Assert.Contains($"{member} 'report' used the custom Elsa 3 storage driver '{CustomDriver}'", warning.Message);
        Assert.Contains("now stored with the workflow instance", warning.Message);
        Assert.Contains("plan an Elsa 4 solution", warning.Guidance);
        Assert.Contains(Elsa3StorageDriverCorrections.Guide, warning.Guidance);
        if (reusable && member != Member.Variable)
            Assert.Equal("elsa.json", ContractDriver(result.Value!, member));
    }

    [Theory]
    [InlineData(MemoryDriver, Elsa3ImportDiagnosticCodes.MemoryStoragePersisted)]
    [InlineData(CustomDriver, Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced)]
    public async Task A_container_variable_converts_to_the_default_storage_with_a_warning(string driver, string code)
    {
        var root = Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        root.AdditionalProperties["variables"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "String", storageDriverTypeName = driver } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var variable = Assert.Single(Node(result.Value!.State, "root").Structure!.Payload.GetProperty("variables").EnumerateArray());
        Assert.True(!variable.TryGetProperty("storageDriverType", out var stored) || stored.ValueKind == System.Text.Json.JsonValueKind.Null, variable.GetRawText());
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code.Contains("STORAGE", StringComparison.Ordinal));
        Assert.Equal(code, warning.Code);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("local", warning.Metadata["VariableName"]);
        Assert.Equal(driver, warning.Metadata["StorageDriverType"]);
        Assert.Equal("storage-driver", warning.Metadata["ComponentKind"]);
        Assert.Equal(driver, warning.Metadata["ComponentName"]);
        Assert.Equal("root", warning.Metadata["ActivityNodeId"]);
    }

    [Theory]
    [InlineData(InstanceDriver)]
    [InlineData(ObsoleteWorkflowDriver)]
    public async Task A_container_variable_on_a_driver_that_kept_the_value_with_the_instance_converts_silently(string driver)
    {
        var root = Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        root.AdditionalProperties["variables"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "String", storageDriverTypeName = driver } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.Contains("STORAGE", StringComparison.Ordinal));
    }

    /// <summary>A reusable workflow's variable on a Memory or custom driver is an elsa.json graph variable.</summary>
    [Theory]
    [InlineData(MemoryDriver)]
    [InlineData(CustomDriver)]
    public async Task A_reusable_workflow_variable_becomes_an_elsa_json_graph_variable(string driver)
    {
        var result = await ConvertAsync(Definition(Member.Variable, driver, reusable: true), reusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var manifest = Elsa3ReusableActivityImportMaterializer.BuildGraphManifest(result.Value!);
        var variable = Assert.Single(manifest.GetProperty("variables").EnumerateArray(), item => item.GetProperty("name").GetString() == "report");
        Assert.Equal("elsa.json", variable.GetProperty("storageDriverKey").GetString());
    }

    /// <summary>
    /// A reusable SetOutput naming a declared output on a custom driver reports the driver once, as a warning; the driver
    /// adds no error of its own (the SetOutput itself is refused in a reusable workflow for another reason).
    /// </summary>
    [Fact]
    public async Task A_reusable_output_written_by_SetOutput_reports_its_driver_once_as_a_warning()
    {
        var definition = Definition(Member.Output, CustomDriver, reusable: true,
            Activity("set", "Elsa.SetOutput", ("outputName", Literal("report")), ("outputValue", Literal("42"))));

        var result = await ConvertAsync(definition, reusable: true);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.IsError && diagnostic.Code.Contains("STORAGE", StringComparison.Ordinal));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced);
        Assert.Equal("$.outputs[0]", warning.Path);
    }

    /// <summary>Each member on a Memory or custom driver is reported once, at its own index.</summary>
    [Fact]
    public async Task Every_member_is_reported_at_its_index()
    {
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi")))),
            variables:
            [
                new Elsa3Variable { Id = "a", Name = "a", TypeName = "String" },
                new Elsa3Variable { Id = "token", Name = "token", TypeName = "String", StorageDriverTypeName = MemoryDriver }
            ]);
        definition.Inputs =
        [
            new Elsa3WorkflowArgumentDefinition { Name = "kept", Type = "String" },
            new Elsa3WorkflowArgumentDefinition { Name = "doc", Type = "String", StorageDriverType = CustomDriver }
        ];
        definition.Outputs =
        [
            new Elsa3WorkflowArgumentDefinition { Name = "report", Type = "String", StorageDriverType = CustomDriver },
            new Elsa3WorkflowArgumentDefinition { Name = "summary", Type = "String", StorageDriverType = InstanceDriver }
        ];

        var result = await ConvertAsync(definition, reusable: false);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(
            [
                (Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced, "$.inputs[1]"),
                (Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced, "$.outputs[0]"),
                (Elsa3ImportDiagnosticCodes.MemoryStoragePersisted, "$.variables[1]")
            ],
            result.Diagnostics.Where(diagnostic => diagnostic.Code.Contains("STORAGE", StringComparison.Ordinal))
                .Select(diagnostic => (diagnostic.Code, diagnostic.Path)).OrderBy(item => item.Path, StringComparer.Ordinal));
    }

    private static string Path(Member member) => member switch
    {
        Member.Variable => "$.variables[0]",
        Member.Input => "$.inputs[0]",
        _ => "$.outputs[0]"
    };

    private static string? Driver(Elsa3ConvertedDefinition converted, Member member) => member switch
    {
        Member.Variable => converted.State.Variables.Single(variable => variable.Name == "report").StorageDriverType,
        Member.Input => converted.State.Inputs.Single().StorageDriverType,
        _ => converted.State.Outputs.Single().StorageDriverType
    };

    private static string ContractDriver(Elsa3ConvertedDefinition converted, Member member)
    {
        var contract = Elsa3ImportedActivityContract.Build(converted);
        return member == Member.Input ? contract.Inputs.Single().StorageDriverKey : contract.Outputs.Single().StorageDriverKey;
    }

    private static Elsa3WorkflowDefinition Definition(Member member, string driver, bool reusable, Elsa3Activity? body = null)
    {
        var root = Sequence("root", body ?? Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        var definition = member == Member.Variable
            ? ConversionServices.Definition(root, variables: [new Elsa3Variable { Id = "report", Name = "report", TypeName = "String", StorageDriverTypeName = driver }])
            : ConversionServices.Definition(root);
        var declared = new Elsa3WorkflowArgumentDefinition { Name = "report", Type = "String", StorageDriverType = driver };
        if (member == Member.Input)
            definition.Inputs = [declared];
        else if (member == Member.Output)
            definition.Outputs = [declared];
        if (reusable)
            definition.Options = new Elsa3WorkflowOptions { UsableAsActivity = true };
        return definition;
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(Elsa3WorkflowDefinition definition, bool reusable) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, reusable);
}
