using System.Text.Json;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Primitives.Tests;

/// <summary>
/// Spec 1228 User Story 4: one name for dynamic data. <c>Any</c> is the alias of the dynamic kind; <c>Object</c>,
/// <c>System.Object</c> and <c>Elsa.Any</c> read as <c>Any</c> when a definition loads; <c>DateOnly</c> and
/// <c>TimeOnly</c> are bare aliases.
/// </summary>
public class DynamicKindTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Object")]
    [InlineData("System.Object")]
    [InlineData("Elsa.Any")]
    [InlineData("object")]
    [InlineData("Any")]
    public void A_definition_naming_an_older_dynamic_name_loads_as_Any(string stored)
    {
        var reference = JsonSerializer.Deserialize<TypeReference>($$"""{"alias":"{{stored}}","collectionKind":"List"}""", Web)!;

        Assert.Equal("Any", reference.Alias);
        Assert.Equal(CollectionKind.List, reference.CollectionKind);
        Assert.Equal("Any", new ValueTypeDescriptor(stored).Alias);
        Assert.Equal("Any", (new TypeReference("String") with { Alias = stored }).Alias);
    }

    [Fact]
    public void A_nullable_older_name_keeps_its_nullable_suffix_and_other_aliases_read_as_themselves()
    {
        Assert.Equal("Any?", TypeAliasConvention.ReadAlias("Object?"));
        Assert.Equal("String", TypeAliasConvention.ReadAlias("String"));
        Assert.Equal("System.Text.Json.JsonElement", TypeAliasConvention.ReadAlias("System.Text.Json.JsonElement"));
        Assert.Equal("Acme.Order", new TypeReference("Acme.Order").Alias);
    }

    [Fact]
    public void A_dotnet_object_is_the_dynamic_kind_and_Object_is_no_longer_a_reserved_alias()
    {
        Assert.Equal("Any", TypeAliasConvention.CanonicalAlias(typeof(object)));
        Assert.Contains("Any", TypeAliasConvention.ReservedBareAliases);
        Assert.DoesNotContain("Object", TypeAliasConvention.ReservedBareAliases);
        Assert.True(TypeAliasConvention.IsAny("Elsa.Any"));
        Assert.False(TypeAliasConvention.IsAny("System.Text.Json.JsonElement"));
    }

    [Theory]
    [InlineData(typeof(DateOnly), "DateOnly")]
    [InlineData(typeof(TimeOnly), "TimeOnly")]
    [InlineData(typeof(DateOnly?), "DateOnly?")]
    public void DateOnly_and_TimeOnly_are_bare_aliases(Type type, string alias)
    {
        Assert.Equal(alias, TypeAliasConvention.CanonicalAlias(type));
        Assert.Contains(alias.TrimEnd('?'), TypeAliasConvention.ReservedBareAliases);
    }
}
