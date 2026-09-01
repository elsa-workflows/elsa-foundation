using Elsa.Workflows.Design.Persistence.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

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

    [Fact]
    public void CreateOrGenerate_preserves_a_supplied_value()
    {
        Assert.Equal(new DesignOperationKey("request-1"), DesignOperationKey.CreateOrGenerate("request-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateOrGenerate_produces_a_non_empty_key_when_none_is_supplied(string? value)
    {
        var key = DesignOperationKey.CreateOrGenerate(value);

        Assert.False(string.IsNullOrWhiteSpace(key.Value));
    }

    [Fact]
    public void CreateOrGenerate_produces_distinct_keys_across_calls_when_none_is_supplied()
    {
        Assert.NotEqual(DesignOperationKey.CreateOrGenerate(null), DesignOperationKey.CreateOrGenerate(null));
    }
}
