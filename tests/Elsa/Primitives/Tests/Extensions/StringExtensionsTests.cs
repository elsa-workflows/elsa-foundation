using Elsa.Primitives.Extensions;
using Xunit;

namespace Elsa.Primitives.Tests.Extensions;

/// <summary>
/// Covers <see cref="StringExtensions.GetLoadedType"/>. The regression (C1): the method resolved type names solely
/// through <see cref="System.Type.GetType(string)"/>, which only searches the calling assembly, Elsa.Primitives,
/// and corelib — so an unqualified name whose type lives in any other loaded assembly threw. The fix falls back to
/// scanning every loaded assembly in the app domain.
/// </summary>
public sealed class StringExtensionsTests
{
    [Fact]
    public void GetLoadedType_ResolvesTypeFromANonPrimitivesLoadedAssembly()
    {
        // Xunit.FactAttribute lives in the xunit assembly — not corelib, not Elsa.Primitives — so Type.GetType with
        // this unqualified name returns null and only the AppDomain fallback can resolve it.
        var expected = typeof(FactAttribute);
        Assert.Null(System.Type.GetType(expected.FullName!));

        var resolved = expected.FullName!.GetLoadedType();

        Assert.Same(expected, resolved);
    }

    [Fact]
    public void GetLoadedType_ResolvesAssemblyQualifiedName()
    {
        var expected = typeof(FactAttribute);

        var resolved = expected.GetSimpleAssemblyQualifiedName().GetLoadedType();

        Assert.Same(expected, resolved);
    }

    [Fact]
    public void GetLoadedType_Throws_WhenTypeCannotBeResolved()
    {
        Assert.Throws<ArgumentException>(() => "No.Such.Type.Anywhere".GetLoadedType());
    }
}
