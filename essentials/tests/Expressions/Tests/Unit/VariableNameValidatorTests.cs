using Elsa.Expressions.Core.Extensions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// #408: <see cref="VariableNameValidator.IsValidVariableName"/> accepted any letters/digits-only name,
/// including digit-leading ones ("123", "1stName"), which are not valid JavaScript/TypeScript identifiers
/// and corrupted generated TS declaration documents. The predicate must require an identifier-start
/// character (letter, '_' or '$') followed by identifier-continue characters (letters, digits, '_', '$').
/// </summary>
public sealed class VariableNameValidatorTests
{
    [Theory]
    [InlineData("name", true)]
    [InlineData("a1", true)]
    [InlineData("_x", true)]
    [InlineData("$x", true)]
    [InlineData("camelCase9", true)]
    [InlineData("123", false)]
    [InlineData("1stName", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("with space", false)]
    [InlineData("with-dash", false)]
    [InlineData("dotted.name", false)]
    public void IsValidVariableName_RequiresValidJsIdentifier(string? name, bool expected)
        => Assert.Equal(expected, name.IsValidVariableName());

    [Fact]
    public void FilterInvalidVariableNames_DropsInvalidNames()
    {
        var names = new[] { "valid", "1invalid", "_alsoValid", "also invalid" };

        Assert.Equal(["valid", "_alsoValid"], names.FilterInvalidVariableNames());
    }
}
