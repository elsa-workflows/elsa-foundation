using System.Text.Json;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Primitives.Tests;

public class TypeReferenceFactoryTests
{
    [Fact]
    public void A_dictionary_of_string_arrays_names_the_dictionary_and_its_element_arrays()
    {
        var reference = TypeReferenceFactory.FromClrType(typeof(IDictionary<string, string[]>), TypeAliasConvention.CanonicalAlias);

        Assert.Equal(CollectionKind.Dictionary, reference.CollectionKind);
        Assert.Equal("String", reference.Alias);
        Assert.Equal(new TypeReference("String", CollectionKind.Array), reference.Element);
    }

    [Fact]
    public void A_nested_type_closes_back_to_the_same_shape()
    {
        var reference = TypeReferenceFactory.FromClrType(typeof(IDictionary<string, string[]>), TypeAliasConvention.CanonicalAlias);

        var type = TypeReferenceFactory.Resolve(reference, alias => alias == "String" ? typeof(string) : typeof(object));

        Assert.Equal(typeof(Dictionary<string, string[]>), type);
        Assert.Equal(typeof(Dictionary<string, string[]>), TypeReferenceFactory.Close(typeof(string), reference));
    }

    [Fact]
    public void A_list_of_lists_nests_twice()
    {
        var reference = TypeReferenceFactory.FromClrType(typeof(List<List<int[]>>), TypeAliasConvention.CanonicalAlias);

        Assert.Equal("Int32", reference.Alias);
        Assert.Equal(CollectionKind.List, reference.CollectionKind);
        Assert.Equal(new TypeReference("Int32", CollectionKind.List, new TypeReference("Int32", CollectionKind.Array)), reference.Element);
        Assert.Equal("List<List<Int32[]>>", reference.ToDisplayString());
        Assert.Equal(typeof(List<List<int[]>>), TypeReferenceFactory.Resolve(reference, _ => typeof(int)));
    }

    [Fact]
    public void A_flat_collection_has_no_element()
    {
        var reference = TypeReferenceFactory.FromClrType(typeof(List<string>), TypeAliasConvention.CanonicalAlias);

        Assert.Equal(new TypeReference("String", CollectionKind.List), reference);
        Assert.Null(reference.Element);
    }

    [Theory]
    [InlineData(typeof(string), "String")]
    [InlineData(typeof(string[]), "String[]")]
    [InlineData(typeof(List<int>), "List<Int32>")]
    [InlineData(typeof(HashSet<Guid>), "HashSet<Guid>")]
    [InlineData(typeof(Dictionary<string, bool>), "Dictionary<String, Boolean>")]
    [InlineData(typeof(IDictionary<string, string[]>), "Dictionary<String, String[]>")]
    public void Diagnostics_show_the_whole_type(Type type, string expected)
    {
        Assert.Equal(expected, TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias).ToDisplayString());
    }

    [Fact]
    public void A_nested_type_round_trips_through_json_and_a_flat_one_omits_the_element()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var nested = TypeReferenceFactory.FromClrType(typeof(IDictionary<string, string[]>), TypeAliasConvention.CanonicalAlias);

        var json = JsonSerializer.Serialize(nested, options);
        var read = JsonSerializer.Deserialize<TypeReference>(json, options);

        Assert.Equal(nested, read);
        Assert.Equal("""{"alias":"String","collectionKind":"Single"}""", JsonSerializer.Serialize(new TypeReference("String"), options));
    }

    [Fact]
    public void A_value_type_descriptor_carries_the_nested_element()
    {
        var reference = TypeReferenceFactory.FromClrType(typeof(IDictionary<string, string[]>), TypeAliasConvention.CanonicalAlias);

        var descriptor = ValueTypeDescriptor.FromTypeReference(reference);

        Assert.Equal(reference, descriptor.ToTypeReference());
        Assert.Equal("Dictionary<String, String[]>", descriptor.ToDisplayString());
    }
}
