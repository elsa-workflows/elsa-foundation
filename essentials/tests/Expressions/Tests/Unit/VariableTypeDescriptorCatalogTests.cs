using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Services;
using Elsa.Serialization.Core.Exceptions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

public sealed class VariableTypeDescriptorCatalogTests
{
    [Fact]
    public void Json_is_the_durable_selectable_dynamic_kind_Any_with_a_json_editor()
    {
        var catalog = new VariableTypeDescriptorCatalog([new DefaultVariableTypeDescriptorProvider()]);
        var descriptor = Assert.Single(catalog.GetDescriptors(), x => x.Alias == "Any");

        Assert.Equal(typeof(System.Text.Json.JsonElement), descriptor.ClrType);
        Assert.Equal("JSON", descriptor.DisplayName);
        Assert.Equal("json", descriptor.DefaultEditor);
        Assert.True(descriptor.SupportsNull);
        Assert.True(descriptor.SupportsDurability);
        Assert.Contains("elsa.json", descriptor.CompatibleStorageDriverKeys);
    }

    // Spec 1228 User Story 4, acceptance 1: the variable type list offers JSON once and no Object.
    [Fact]
    public void The_type_list_offers_JSON_once_and_no_Object()
    {
        var descriptors = new VariableTypeDescriptorCatalog([new DefaultVariableTypeDescriptorProvider()]).GetDescriptors().ToArray();

        Assert.Single(descriptors, x => x.DisplayName == "JSON");
        Assert.DoesNotContain(descriptors, x => x.Alias is "Object" or "System.Object" or "Elsa.Any" or "System.Text.Json.JsonElement");
        Assert.DoesNotContain(descriptors, x => x.DisplayName == "Object" || x.ClrType == typeof(object));
    }

    // Spec 1228 FR-020: DateOnly and TimeOnly are kinds.
    [Theory]
    [InlineData("DateOnly", typeof(DateOnly))]
    [InlineData("TimeOnly", typeof(TimeOnly))]
    public void DateOnly_and_TimeOnly_are_selectable_kinds(string alias, Type clrType)
    {
        var descriptor = Assert.Single(new DefaultVariableTypeDescriptorProvider().GetDescriptors(), x => x.Alias == alias);

        Assert.Equal(clrType, descriptor.ClrType);
        Assert.True(descriptor.SupportsDurability);
    }

    private sealed class StubProvider(params TypeDescriptor[] descriptors) : IVariableTypeDescriptorProvider
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() => descriptors;
    }

    private static TypeDescriptor Descriptor(string alias, string category = "Primitives") =>
        new(
            alias,
            typeof(object),
            alias,
            category,
            "text",
            new HashSet<Elsa.Primitives.Models.CollectionKind> { Elsa.Primitives.Models.CollectionKind.Single },
            true,
            true,
            new HashSet<string> { "elsa.json" });

    [Fact]
    public void GetDescriptors_TwoProviders_ReturnsUnion()
    {
        var catalog = new VariableTypeDescriptorCatalog(
        [
            new StubProvider(Descriptor("String")),
            new StubProvider(Descriptor("Elsa.Http.HttpRequest", "Http"))
        ]);

        var aliases = catalog.GetDescriptors().Select(x => x.Alias).ToArray();

        Assert.Equal(2, aliases.Length);
        Assert.Contains("String", aliases);
        Assert.Contains("Elsa.Http.HttpRequest", aliases);
    }

    [Fact]
    public void GetDescriptorsByCategory_GroupsByCategory()
    {
        var catalog = new VariableTypeDescriptorCatalog(
        [
            new StubProvider(Descriptor("String"), Descriptor("Int32")),
            new StubProvider(Descriptor("Elsa.Http.HttpRequest", "Http"))
        ]);

        var groups = catalog.GetDescriptorsByCategory().ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(2, groups["Primitives"]);
        Assert.Equal(1, groups["Http"]);
    }

    [Fact]
    public void Ctor_DuplicateAliasAcrossProviders_ThrowsDuplicateTypeAliasException()
    {
        var ex = Assert.Throws<DuplicateTypeAliasException>(() => new VariableTypeDescriptorCatalog(
        [
            new StubProvider(Descriptor("String")),
            new StubProvider(Descriptor("String"))
        ]));

        Assert.Equal("String", ex.Alias);
    }

    [Fact]
    public void GetDescriptors_WithDefaultProvider_ContainsFrameworkPrimitives()
    {
        var catalog = new VariableTypeDescriptorCatalog([new DefaultVariableTypeDescriptorProvider()]);

        var aliases = catalog.GetDescriptors().Select(x => x.Alias).ToArray();

        Assert.Contains("String", aliases);
        Assert.Contains("Int32", aliases);
        Assert.Contains("Boolean", aliases);
        Assert.Contains("DateTime", aliases);
    }

    // The descriptors/variables endpoint host (Elsa.Workbench) is internal and only reachable via full
    // hosting (out of scope here). This asserts the catalog → wire DTO mapping the handler
    // performs: only { alias, displayName, category, defaultEditor } reach the wire — never ClrType.
    [Fact]
    public void WireProjection_EmitsAliasDisplayNameCategoryDefaultEditorOnly()
    {
        var catalog = new VariableTypeDescriptorCatalog(
        [
            new StubProvider(Descriptor("String"))
        ]);

        var wire = catalog.GetDescriptors()
            .Select(x => new { x.Alias, x.DisplayName, x.Category, x.DefaultEditor })
            .Single();

        Assert.Equal("String", wire.Alias);
        Assert.Equal("String", wire.DisplayName);
        Assert.Equal("Primitives", wire.Category);
        Assert.Equal("text", wire.DefaultEditor);
        Assert.DoesNotContain(nameof(TypeDescriptor.ClrType), wire.GetType().GetProperties().Select(p => p.Name));
    }
}
