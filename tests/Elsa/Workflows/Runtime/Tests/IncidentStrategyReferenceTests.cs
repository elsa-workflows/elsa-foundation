using Elsa.Workflows.Primitives.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class IncidentStrategyReferenceTests
{
    [Fact]
    public void Equality_IgnoresAliasCase_ButRequiresExactVersion()
    {
        var canonical = new IncidentStrategyReference("Acme.Operations.Review", "Preview-1");

        Assert.Equal(canonical, new IncidentStrategyReference("acme.operations.review", "Preview-1"));
        Assert.NotEqual(canonical, new IncidentStrategyReference("Acme.Operations.Review", "preview-1"));
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData(" ", "1")]
    [InlineData("Fault", "")]
    [InlineData("Fault", " ")]
    public void Constructor_RejectsBlankIdentityParts(string alias, string version)
    {
        Assert.Throws<ArgumentException>(() => new IncidentStrategyReference(alias, version));
    }
}
