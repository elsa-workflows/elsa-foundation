using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Elsa.Secrets.Tests.Support;
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

    [Fact]
    public void IsValid_Rejects_NamesBeyondThePortablePhysicalIndexBound()
    {
        Assert.False(
            _validator.IsValid(new string('a', SecretNameConstraints.MaximumLength + 1), out var error));
        Assert.Contains(SecretNameConstraints.MaximumLength.ToString(), error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The key-limit tests size their names by <see cref="SecretKeyLimit.SecretIdLength"/>, so a change to the id a new
    /// secret gets fails here rather than as a name that silently misses the limit.
    /// </summary>
    [Fact]
    public void A_new_secret_id_has_the_length_the_key_limit_tests_assume()
    {
        Assert.Equal(SecretKeyLimit.SecretIdLength, new Secret().Id.Length);
    }
}
