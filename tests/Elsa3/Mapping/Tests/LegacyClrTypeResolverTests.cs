using System.Text;
using Elsa.Serialization.SystemText.Services;
using Elsa3.Mapping;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// The Elsa-3 import boundary interprets legacy alias-or-CLR-name tokens, then the caller normalises the
/// resolved type to an Elsa-4 alias for storage. Order: registered alias → legacy <c>Type.GetType</c> → object.
/// </summary>
public sealed class LegacyClrTypeResolverTests
{
    [Fact]
    public void Resolve_RegisteredAlias_ReturnsRegisteredType()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");

        Assert.Equal(typeof(string), LegacyClrTypeResolver.Resolve(registry, "String"));
    }

    [Fact]
    public void Resolve_UnregisteredButLoadableClrName_ResolvesViaTypeGetType()
    {
        var registry = new WellKnownTypeRegistry();

        // Not registered as an alias; resolvable by full name because it lives in the core library.
        Assert.Equal(typeof(StringBuilder), LegacyClrTypeResolver.Resolve(registry, typeof(StringBuilder).FullName!));
    }

    [Fact]
    public void Resolve_UnresolvableName_FallsBackToObject()
    {
        var registry = new WellKnownTypeRegistry();

        Assert.Equal(typeof(object), LegacyClrTypeResolver.Resolve(registry, "No.Such.Type, No.Such.Assembly"));
    }
}
