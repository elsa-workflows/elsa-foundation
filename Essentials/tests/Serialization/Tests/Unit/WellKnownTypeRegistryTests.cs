using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Exceptions;
using Elsa.Serialization.SystemText.Services;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

/// <summary>
/// Registration is fail-fast. Duplicate alias or duplicate type throws;
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
            (typeof(System.Text.Json.JsonElement), "Any")
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
        // An unregistered non-primitive type yields its dotted FullName (the convention alias) —
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
        // Even a syntactically valid assembly-qualified name must NOT resolve via Type.GetType.
        Assert.Equal(typeof(object), _registry.GetTypeOrDefault("System.Uri, System.Private.Uri"));
        Assert.False(_registry.TryGetTypeOrDefault("System.Uri, System.Private.Uri", out _));
    }

    // Spec 1228 FR-017, FR-018: JSON is seeded as Any; its older names and the full .NET name activities declare all
    // resolve to the same CLR type, and Any is the alias the registry gives that type.
    [Theory]
    [InlineData("Any", typeof(System.Text.Json.JsonElement))]
    [InlineData("Object", typeof(System.Text.Json.JsonElement))]
    [InlineData("Elsa.Any", typeof(System.Text.Json.JsonElement))]
    [InlineData("System.Text.Json.JsonElement", typeof(System.Text.Json.JsonElement))]
    [InlineData("System.Text.Json.JsonElement?", typeof(System.Text.Json.JsonElement?))]
    public void The_dynamic_kind_resolves_by_each_of_its_names(string alias, Type expected)
    {
        _registry.RegisterType(typeof(System.Text.Json.JsonElement), "Any");

        Assert.True(_registry.TryGetType(alias, out var type));
        Assert.Equal(expected, type);
        Assert.Equal("Any", _registry.GetAliasOrDefault(typeof(System.Text.Json.JsonElement)));
    }

    // The reflection pass over activity types may register JsonElement under its full name before the primitive seed
    // registers it as Any: the seed still succeeds, and both names resolve.
    [Fact]
    public void The_seeded_Any_alias_takes_over_a_JsonElement_registered_first_under_its_full_name()
    {
        _registry.RegisterType(typeof(System.Text.Json.JsonElement), "System.Text.Json.JsonElement");

        _registry.RegisterType(typeof(System.Text.Json.JsonElement), "Any");

        Assert.Equal("Any", _registry.GetAliasOrDefault(typeof(System.Text.Json.JsonElement)));
        Assert.True(_registry.TryGetType("System.Text.Json.JsonElement", out var full));
        Assert.Equal(typeof(System.Text.Json.JsonElement), full);
        Assert.Throws<DuplicateTypeAliasException>(() => _registry.RegisterType(typeof(System.Text.Json.JsonElement), "Acme.Json"));
    }

    [Fact]
    public void A_registered_alias_wins_over_a_convention_alias()
    {
        _registry.RegisterType(typeof(System.Text.Json.JsonElement), "Any");
        _registry.RegisterType(typeof(Uri), "System.Uri");

        Assert.True(_registry.TryGetType("System.Uri", out var uri));
        Assert.Equal(typeof(Uri), uri);
        Assert.False(_registry.TryGetType("JsonElement", out _));
    }

    // Spec 1228 FR-020, User Story 4 acceptance 4: DateOnly and TimeOnly are stored as ISO 8601 text.
    [Fact]
    public void DateOnly_and_TimeOnly_are_stored_as_ISO_8601_text()
    {
        var serializer = Elsa.Serialization.Testing.SerializationServices.PayloadSerializer();

        Assert.Equal("2026-10-28", serializer.SerializeToElement(new DateOnly(2026, 10, 28)).GetString());
        Assert.Equal("09:15:00", serializer.SerializeToElement(new TimeOnly(9, 15)).GetString());
        _registry.RegisterType(typeof(DateOnly), "DateOnly");
        _registry.RegisterType(typeof(TimeOnly), "TimeOnly");
        Assert.True(_registry.TryGetType("DateOnly?", out var nullableDate));
        Assert.Equal(typeof(DateOnly?), nullableDate);
    }
}
