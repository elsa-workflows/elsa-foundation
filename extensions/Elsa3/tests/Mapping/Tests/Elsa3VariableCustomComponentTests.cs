using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// #5666: a variable whose Elsa 3 type or storage driver is a custom component Elsa 4 does not register gets a diagnostic
/// code, so a migrator can see it. An unregistered type converts as Any with an info; an unregistered storage driver is
/// refused. Registered types and drivers, and Elsa 3's default driver, convert as before, without a diagnostic.
/// </summary>
public sealed class Elsa3VariableCustomComponentTests
{
    private const string DefaultDriver = "Elsa.Workflows.WorkflowInstanceStorageDriver, Elsa.Workflows.Core";
    private const string CustomDriver = "Acme.BlobStorageDriver, Acme";
    private static readonly IObjectConverter ObjectConverter = SerializationServices.ObjectConverter();

    [Fact]
    public async Task A_workflow_variable_of_an_unregistered_type_converts_as_Any_with_an_info()
    {
        var result = await ConvertAsync(new Elsa3Variable { Id = "order", Name = "order", TypeName = "Acme.Orders.OrderDto, Acme.Orders" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(new TypeReference("Any"), Assert.Single(result.Value!.State.Variables).Type);
        var info = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.VariableTypeUnregistered);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Info, info.Severity);
        Assert.Equal("$.variables[0]", info.Path);
        Assert.Equal("order", info.Metadata["VariableName"]);
        Assert.Contains("Variable 'order'", info.Message);
        Assert.Contains("'Acme.Orders.OrderDto, Acme.Orders'", info.Message);
        Assert.Contains("its Elsa 4 type is Any", info.Message);
        Assert.Contains("until an Elsa 4 feature registers the type under a dotted alias and the workflow is converted again", info.Message);
    }

    [Fact]
    public async Task A_container_variable_of_an_unregistered_type_converts_with_an_info()
    {
        var root = Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        root.AdditionalProperties["variables"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "Acme.Orders.OrderDto, Acme.Orders" } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var info = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.VariableTypeUnregistered);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Info, info.Severity);
        Assert.Contains("Variable 'local'", info.Message);
    }

    /// <summary>A collection names its unregistered element; a nesting Elsa 4 cannot express is Any whatever is registered, so it gets no info.</summary>
    [Theory]
    [InlineData("Acme.Orders.OrderDto, Acme.Orders", false, "Acme.Orders.OrderDto, Acme.Orders")]
    [InlineData("Acme.Orders.OrderDto", true, "Acme.Orders.OrderDto")]
    [InlineData("List<Acme.Orders.OrderDto>", false, "Acme.Orders.OrderDto")]
    [InlineData("List<Acme.Orders.OrderDto[]>", false, "Acme.Orders.OrderDto")]
    [InlineData("System.DayOfWeek", false, "System.DayOfWeek")]
    [InlineData("OrderDto", false, "OrderDto")]
    [InlineData("List<List<String>>", false, null)]
    [InlineData("ObjectDictionary[]", false, null)]
    [InlineData("Object", false, null)]
    [InlineData("ObjectDictionary", false, null)]
    [InlineData("String", false, null)]
    [InlineData("List<Int32>", false, null)]
    public void The_info_names_the_type_Elsa_4_does_not_register(string typeName, bool isArray, string? unregistered)
    {
        var notice = Elsa3VariableMapper.DescribeUnregisteredType(ConversionServices.TypeRegistry(), new Elsa3Variable { Id = "v", Name = "v", TypeName = typeName, IsArray = isArray });

        if (unregistered is null)
            Assert.Null(notice);
        else
        {
            Assert.NotNull(notice);
            Assert.Contains($"'{typeName}'", notice.Message);
            if (typeName != unregistered)
                Assert.Contains($"whose element type '{unregistered}'", notice.Message);
        }
    }

    /// <summary>
    /// The guidance matches how the converter finds a type: an assembly-qualified name by loading it, a dotted name by that
    /// exact alias, and a bare name not at all, since Elsa 4 refuses bare aliases for module types.
    /// </summary>
    [Theory]
    [InlineData("Acme.Orders.OrderDto, Acme.Orders", "with its assembly loaded in the host that converts")]
    [InlineData("Acme.Orders.OrderDto", "under the alias 'Acme.Orders.OrderDto'")]
    [InlineData("OrderDto", "cannot register the bare name 'OrderDto'")]
    public void The_guidance_says_how_this_name_can_keep_its_type(string typeName, string guidance)
    {
        var notice = Elsa3VariableMapper.DescribeUnregisteredType(ConversionServices.TypeRegistry(), new Elsa3Variable { Id = "v", Name = "v", TypeName = typeName });

        Assert.Contains(guidance, notice!.Guidance);
    }

    /// <summary>A dotted Elsa 3 name without an assembly keeps its type once a feature registers it under that exact alias, as the guidance says.</summary>
    [Fact]
    public void A_dotted_name_registered_under_that_alias_keeps_its_type()
    {
        var registry = ConversionServices.TypeRegistry();
        registry.RegisterType(typeof(OrderDto), "Acme.Orders.OrderDto");
        var source = new Elsa3Variable { Id = "order", Name = "order", TypeName = "Acme.Orders.OrderDto" };

        Assert.Null(Elsa3VariableMapper.DescribeUnregisteredType(registry, source));
        Assert.True(Elsa3VariableMapper.TryMap(registry, ObjectConverter, source, out var variable, out var refusal), refusal?.Message);
        Assert.Equal(new TypeReference("Acme.Orders.OrderDto"), variable.Type);
    }

    [Fact]
    public void A_type_registered_under_a_dotted_alias_keeps_its_type_without_a_diagnostic()
    {
        var registry = ConversionServices.TypeRegistry();
        registry.RegisterType(typeof(OrderDto), "acme.orders.OrderDto");
        var source = new Elsa3Variable { Id = "order", Name = "order", TypeName = typeof(OrderDto).AssemblyQualifiedName };

        Assert.Null(Elsa3VariableMapper.DescribeUnregisteredType(registry, source));
        Assert.True(Elsa3VariableMapper.TryMap(registry, ObjectConverter, source, out var variable, out var refusal), refusal?.Message);
        Assert.Equal(new TypeReference("acme.orders.OrderDto"), variable.Type);
    }

    [Fact]
    public async Task A_variable_on_the_default_storage_driver_converts_as_before()
    {
        var result = await ConvertAsync(new Elsa3Variable { Id = "v", Name = "v", TypeName = "String", StorageDriverTypeName = DefaultDriver });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Null(Assert.Single(result.Value!.State.Variables).StorageDriverType);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is Elsa3ImportDiagnosticCodes.VariableStorageDriverUnmapped or Elsa3ImportDiagnosticCodes.VariableTypeUnregistered);
    }

    [Fact]
    public async Task A_workflow_variable_on_an_unregistered_storage_driver_is_refused_with_guidance()
    {
        var result = await ConvertAsync(new Elsa3Variable { Id = "blob", Name = "blob", TypeName = "String", StorageDriverTypeName = CustomDriver });

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ImportDiagnosticCodes.VariableStorageDriverUnmapped, refusal.Code);
        Assert.Equal("$.variables[0]", refusal.Path);
        Assert.Equal("blob", refusal.Metadata["VariableName"]);
        Assert.Contains("Variable 'blob'", refusal.Message);
        Assert.Contains($"'{CustomDriver}'", refusal.Message);
        Assert.Contains("default (the workflow instance)", refusal.Guidance);
        Assert.Contains("register the driver type", refusal.Guidance);
    }

    [Fact]
    public async Task A_container_variable_on_an_unregistered_storage_driver_is_refused()
    {
        var root = Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))));
        root.AdditionalProperties["variables"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "String", storageDriverTypeName = CustomDriver } });

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ImportDiagnosticCodes.VariableStorageDriverUnmapped, refusal.Code);
        Assert.Contains("Variable 'local'", refusal.Message);
    }

    [Fact]
    public void A_storage_driver_registered_in_Elsa_4_maps_to_its_alias_as_before()
    {
        var registry = ConversionServices.TypeRegistry();
        registry.RegisterType(typeof(BlobStorageDriver), "acme.blobStorageDriver");
        var source = new Elsa3Variable { Id = "v", Name = "v", TypeName = "String", StorageDriverTypeName = typeof(BlobStorageDriver).AssemblyQualifiedName };

        Assert.True(Elsa3VariableMapper.TryMap(registry, ObjectConverter, source, out var variable, out var refusal), refusal?.Message);
        Assert.Equal("acme.blobStorageDriver", variable.StorageDriverType);
    }

    private static Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(
            RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi")))), variables: variables));

    private sealed class OrderDto
    {
        public int Id { get; set; }
    }

    private sealed class BlobStorageDriver;
}
