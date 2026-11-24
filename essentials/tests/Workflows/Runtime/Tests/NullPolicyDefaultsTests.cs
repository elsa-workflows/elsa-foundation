using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class NullPolicyDefaultsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Contributed_type_nullability_is_authoritative_for_reference_types(bool supportsNull)
    {
        var catalog = new TestCatalog(supportsNull);
        Assert.Equal(supportsNull, NullPolicyDefaults.AcceptsNull(new TypeReference("Acme.Reference"), null, catalog));
        Assert.True(NullPolicyDefaults.AcceptsNull(new TypeReference("Acme.Reference", CollectionKind.Array), null, catalog));
    }

    private sealed class TestCatalog(bool supportsNull) : IVariableTypeDescriptorCatalog
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() =>
            [new("Acme.Reference", typeof(Uri), "Reference", "Test", "text", new HashSet<CollectionKind>(Enum.GetValues<CollectionKind>()), supportsNull, true, new HashSet<string> { "elsa.json" })];
        public IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory() => GetDescriptors().GroupBy(item => item.Category);
    }

    private enum TestChoice { First = 1 }

    [Theory]
    [InlineData(typeof(bool))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(TestChoice))]
    public void Supported_scalar_defaults_preserve_the_declared_CLR_type(Type type)
    {
        Assert.True(NullPolicyDefaults.TryGetTypeDefault(type, out var value));
        Assert.NotNull(value);
        Assert.Equal(type, value.GetType());
        Assert.Equal(Activator.CreateInstance(type), value);
    }

    [Theory]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(DateOnly))]
    [InlineData(typeof(TimeOnly))]
    [InlineData(typeof(TimeSpan))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(string))]
    [InlineData(typeof(object))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(int?))]
    public void Other_types_have_no_fabricated_default(Type type)
    {
        Assert.False(NullPolicyDefaults.TryGetTypeDefault(type, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("Boolean", false)]
    [InlineData("Int32", 0)]
    [InlineData("Int64", 0L)]
    [InlineData("Double", 0d)]
    public void Authored_aliases_have_the_same_defaults(string alias, object expected)
    {
        Assert.True(NullPolicyDefaults.TryGetTypeDefault(alias, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Any")]
    [InlineData("DateTime")]
    [InlineData("TimeSpan")]
    [InlineData("Guid")]
    public void Other_authored_aliases_have_no_fabricated_default(string alias) =>
        Assert.False(NullPolicyDefaults.TryGetTypeDefault(alias, out _));
}
