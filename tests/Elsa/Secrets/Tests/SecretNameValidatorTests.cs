using Elsa.Secrets.Services;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretNameValidatorTests
{
    private readonly DefaultSecretNameValidator _validator = new();

    [Theory]
    [InlineData(" Payments.Api ", "payments.api")]
    [InlineData("tenant:payments-api_key", "tenant:payments-api_key")]
    public void Normalize_Trims_And_Lowercases(string input, string expected)
    {
        Assert.Equal(expected, _validator.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("invalid/value")]
    public void IsValid_Rejects_Invalid_Names(string name)
    {
        Assert.False(_validator.IsValid(name, out var error));
        Assert.NotNull(error);
    }
}
