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
/// code, so a migrator can see it. An unregistered type converts as Any with an info; a storage driver other than Elsa 3's
/// default is refused, since Elsa 4 cannot store a variable outside the instance yet. Registered types and the default
/// driver convert as before, without a diagnostic.
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
        Assert.Contains("until an Elsa 4 feature registers the type and the workflow is converted again", info.Message);
        Assert.Contains("register its CLR type", info.Guidance);
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
    [InlineData("JsonElement", false, null)]
    [InlineData("JsonObject", false, null)]
    [InlineData("List<JsonNode>", false, null)]
    [InlineData("ExpandoObject", false, null)]
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

    /// <summary>A dotted Elsa 3 name without an assembly keeps its type once a feature registers it under that alias.</summary>
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
        Assert.Contains("cannot store a variable outside the workflow instance yet", refusal.Message);
        Assert.Contains("default (the workflow instance)", refusal.Guidance);
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

    /// <summary>Registering the driver's type does not help: Elsa 4 refuses every storage driver for a variable at publication (VF-ACT-005).</summary>
    [Fact]
    public void A_storage_driver_registered_in_Elsa_4_is_refused_too()
    {
        var registry = ConversionServices.TypeRegistry();
        registry.RegisterType(typeof(BlobStorageDriver), "acme.blobStorageDriver");
        var source = new Elsa3Variable { Id = "v", Name = "v", TypeName = "String", StorageDriverTypeName = typeof(BlobStorageDriver).AssemblyQualifiedName };

        Assert.False(Elsa3VariableMapper.TryMap(registry, ObjectConverter, source, out _, out var refusal));
        Assert.Equal(Elsa3ImportDiagnosticCodes.VariableStorageDriverUnmapped, refusal!.Code);
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
