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

    [Theory]
    // Multi-word PascalCase splits into spaced Title Case.
    [InlineData("ExpectedStatusCodes", "Expected Status Codes")]
    [InlineData("ContentType", "Content Type")]
    [InlineData("RequestSizeLimit", "Request Size Limit")]
    [InlineData("ControlFlow", "Control Flow")]
    [InlineData("SendHttpRequest", "Send Http Request")]
    // Single word is just capitalized.
    [InlineData("message", "Message")]
    [InlineData("Message", "Message")]
    [InlineData("x", "X")]
    // Known acronyms are fully uppercased.
    [InlineData("Url", "URL")]
    [InlineData("Bpmn", "BPMN")]
    [InlineData("id", "ID")]
    [InlineData("ApiKey", "API Key")]
    // Acronym runs adjacent to a following word split correctly.
    [InlineData("HTTPRequest", "HTTP Request")]
    [InlineData("XMLParser", "XML Parser")]
    // camelCase, snake_case and kebab-case are all handled.
    [InlineData("expectedStatusCodes", "Expected Status Codes")]
    [InlineData("expected_status_codes", "Expected Status Codes")]
    [InlineData("expected-status-codes", "Expected Status Codes")]
    // Already-spaced input is preserved (idempotent).
    [InlineData("Expected Status Codes", "Expected Status Codes")]
    public void Humanize_ProducesReadableLabel(string input, string expected)
    {
        Assert.Equal(expected, input.Humanize());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Humanize_ReturnsInputUnchanged_WhenNullEmptyOrWhitespace(string input)
    {
        Assert.Equal(input, input.Humanize());
    }
}
