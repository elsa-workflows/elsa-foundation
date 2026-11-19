using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed partial class ArchitectureGuardTests
{
    [Fact]
    public void Owned_secret_and_oauth_grant_creates_do_not_hard_code_encrypted()
    {
        var connectionManager = File.ReadAllText(Path.Combine(
            RepoRoot, "Essentials", "src", "Connections", "Elsa.Connections", "Services", "DefaultConnectionManager.cs"));
        var tokenService = File.ReadAllText(Path.Combine(
            RepoRoot, "Extensions", "Integrations", "src", "Connectors", "OAuth2", "OAuth2TokenService.cs"));
        var scheme = File.ReadAllText(Path.Combine(
            RepoRoot, "Extensions", "Integrations", "src", "Connectors", "OAuth2", "OAuth2AuthenticationScheme.cs"));

        Assert.DoesNotContain("SecretStoreNames.Encrypted", connectionManager, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretStoreNames.Encrypted", tokenService, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretStoreNames.Encrypted", scheme, StringComparison.Ordinal);
    }
}
