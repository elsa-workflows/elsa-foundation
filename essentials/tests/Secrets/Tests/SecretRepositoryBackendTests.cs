using Elsa.Secrets.Core.Contracts;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretRepositoryBackendTests
{
    [Fact]
    public void Compatibility_check_is_provider_agnostic()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecretRepositoryBackend.EnsureCompatible("selected", "incoming"));

        Assert.Contains("selected", exception.Message, StringComparison.Ordinal);
        Assert.Contains("incoming", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compatibility_check_allows_repeated_registration_of_the_same_name()
    {
        SecretRepositoryBackend.EnsureCompatible("selected", "selected");
    }
}
