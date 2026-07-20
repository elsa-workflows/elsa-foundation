using Elsa.Persistence.Core.Design;
using Xunit;

namespace Elsa.Persistence.Core.Tests;

public class DesignOperationKeyTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Blank_values_are_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => new DesignOperationKey(value));
    }

    [Fact]
    public void The_caller_supplied_value_is_preserved_verbatim()
    {
        var key = new DesignOperationKey("  deployment/request:ABC-123  ");

        Assert.Equal("  deployment/request:ABC-123  ", key.Value);
    }

    [Fact]
    public void Equality_is_ordinal_and_case_sensitive()
    {
        Assert.Equal(new DesignOperationKey("request-1"), new DesignOperationKey("request-1"));
        Assert.NotEqual(new DesignOperationKey("request-1"), new DesignOperationKey("REQUEST-1"));
    }
}
