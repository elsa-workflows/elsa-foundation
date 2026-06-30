using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Services;
using Elsa.Serialization.Core.Exceptions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

public sealed class VariableTypeDescriptorCatalogTests
{
    private sealed class StubProvider(params TypeDescriptor[] descriptors) : IVariableTypeDescriptorProvider
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() => descriptors;
    }

    private static TypeDescriptor Descriptor(string alias, string category = "Primitives") =>
        new(alias, typeof(object), alias, category, "text");

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

    // The descriptors/variables endpoint host (Elsa.Server) is internal and only reachable via full
    // hosting (out of scope per §2.23.6). This asserts the catalog → wire DTO mapping the handler
    // performs: only { alias, displayName, category, defaultEditor } reach the wire — never ClrType.
    [Fact]
    public void WireProjection_EmitsAliasDisplayNameCategoryDefaultEditorOnly()
    {
        var catalog = new VariableTypeDescriptorCatalog(
        [
            new StubProvider(new TypeDescriptor("String", typeof(string), "String", "Primitives", "text"))
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
