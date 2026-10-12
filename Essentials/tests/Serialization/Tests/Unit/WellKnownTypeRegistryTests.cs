using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Exceptions;
using Elsa.Serialization.SystemText.Services;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// FR-010..FR-013 / SC-004 (research D7): registration is fail-fast. Duplicate alias or duplicate type throws;
/// bare non-primitive aliases are rejected; reserved primitive bare aliases and dotted module aliases register;
/// and the nullable auto-registration never self-collides.
/// </summary>
public sealed class WellKnownTypeRegistryTests
{
    private readonly WellKnownTypeRegistry _registry = new();

    [Fact]
    public void RegisterType_DuplicateAlias_ThrowsDuplicateTypeAliasException()
    {
        _registry.RegisterType(typeof(string), "String");

        var exception = Assert.Throws<DuplicateTypeAliasException>(() => _registry.RegisterType(typeof(Uri), "String"));
        Assert.Equal("String", exception.Alias);
    }

    [Fact]
    public void RegisterType_DuplicateType_UnderDifferentAlias_Throws()
    {
        _registry.RegisterType(typeof(string), "String");

        Assert.Throws<DuplicateTypeAliasException>(() => _registry.RegisterType(typeof(string), "Acme.MyString"));
    }

    [Fact]
    public void RegisterType_SameTypeAndAliasTwice_IsIdempotent()
    {
        _registry.RegisterType(typeof(string), "String");
        _registry.RegisterType(typeof(string), "String"); // No throw.

        Assert.True(_registry.TryGetType("String", out var type));
        Assert.Equal(typeof(string), type);
    }

    [Fact]
    public void RegisterType_BareNonPrimitiveAlias_ThrowsReservedAliasNamespaceException()
    {
        var exception = Assert.Throws<ReservedAliasNamespaceException>(() => _registry.RegisterType(typeof(Uri), "Customer"));
        Assert.Equal("Customer", exception.Alias);
    }

    [Fact]
    public void RegisterType_ReservedPrimitiveBareAlias_RegistersSuccessfully()
    {
        _registry.RegisterType(typeof(int), "Int32");

        Assert.True(_registry.TryGetType("Int32", out var type));
        Assert.Equal(typeof(int), type);
    }

    [Fact]
    public void RegisterType_DottedModuleAlias_RegistersSuccessfully()
    {
        _registry.RegisterType(typeof(Uri), "Acme.Customer");

        Assert.True(_registry.TryGetType("Acme.Customer", out var type));
        Assert.Equal(typeof(Uri), type);
        Assert.True(_registry.TryGetAlias(typeof(Uri), out var alias));
        Assert.Equal("Acme.Customer", alias);
    }

    [Fact]
    public void RegisterType_HappyPath_RoundTripsAliasAndType()
    {
        _registry.RegisterType(typeof(string), "String");

        Assert.True(_registry.TryGetAlias(typeof(string), out var alias));
        Assert.Equal("String", alias);
        Assert.True(_registry.TryGetType("String", out var type));
        Assert.Equal(typeof(string), type);
    }

    [Fact]
    public void RegisterType_ValueType_AutoRegistersNullable_WithoutSelfCollision()
    {
        _registry.RegisterType(typeof(int), "Int32"); // Must not throw on its own derived "Int32?".

        Assert.True(_registry.TryGetType("Int32", out var intType));
        Assert.Equal(typeof(int), intType);
        Assert.True(_registry.TryGetType("Int32?", out var nullableType));
        Assert.Equal(typeof(int?), nullableType);
    }

    [Fact]
    public void RegisterType_AllReservedPrimitivesViaProviderAliases_RegisterWithoutThrowing()
    {
        // Mirror the framework primitive aliases the seed installs; each is a reserved bare alias and must seed cleanly.
        (Type Type, string Alias)[] primitives =
        [
            (typeof(string), "String"),
            (typeof(bool), "Boolean"),
            (typeof(int), "Int32"),
            (typeof(long), "Int64"),
            (typeof(double), "Double"),
            (typeof(decimal), "Decimal"),
            (typeof(DateTime), "DateTime"),
            (typeof(DateTimeOffset), "DateTimeOffset"),
            (typeof(TimeSpan), "TimeSpan"),
            (typeof(Guid), "Guid"),
            (typeof(object), "Object")
        ];

        foreach (var (type, alias) in primitives)
            _registry.RegisterType(type, alias);

        foreach (var (type, alias) in primitives)
        {
            Assert.True(_registry.TryGetType(alias, out var resolved));
            Assert.Equal(type, resolved);
        }
    }

    [Fact]
    public void GetAliasOrDefault_UnregisteredNonPrimitive_ReturnsFullName_NotAssemblyQualified()
    {
        // FR-004 / FR-004a: an unregistered non-primitive type yields its dotted FullName (the convention alias) —
        // NEVER an assembly-qualified name with a version. The call is pure: it does not register the type.
        var alias = _registry.GetAliasOrDefault(typeof(Uri));

        Assert.Equal(typeof(Uri).FullName, alias);
        Assert.DoesNotContain(", Version=", alias, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Private.CoreLib", alias, StringComparison.Ordinal);
        Assert.False(_registry.TryGetAlias(typeof(Uri), out _)); // Pure: no mutation.
    }

    [Fact]
    public void GetAliasOrDefault_RegisteredType_ReturnsRegisteredAlias()
    {
        _registry.RegisterType(typeof(Uri), "Acme.Customer");

        Assert.Equal("Acme.Customer", _registry.GetAliasOrDefault(typeof(Uri)));
    }

    [Fact]
    public void GetAliasOrDefault_Primitive_ReturnsBareAlias()
    {
        // Even without registration, a BCL primitive resolves to its reserved bare alias via the convention.
        Assert.Equal("String", _registry.GetAliasOrDefault(typeof(string)));
        Assert.Equal("Int32", _registry.GetAliasOrDefault(typeof(int)));
    }

    [Fact]
    public void GetTypeOrDefault_UnknownAlias_ReturnsObject_NoTypeGetTypeFallback()
    {
        // FR-004a: even a syntactically valid assembly-qualified name must NOT resolve via Type.GetType.
        Assert.Equal(typeof(object), _registry.GetTypeOrDefault("System.Uri, System.Private.Uri"));
        Assert.False(_registry.TryGetTypeOrDefault("System.Uri, System.Private.Uri", out _));
    }
}
