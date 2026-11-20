using CShells.Lifecycle;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The <c>Secrets</c> feature's key settings as CShells binds them from shell configuration, rather than as set on a
/// constructed feature: the binder has to reach all three, the dictionary-typed key ring included.
/// </summary>
public sealed class SecretsFeatureKeySettingsShellTests
{
    [Fact]
    public async Task Shell_configuration_supplies_the_key_ring()
    {
        await using var host = await SecretsHostCatalog.StartAsync(
            $$"""
            {
              "CShells": {
                "Shells": {
                  "{{SecretsHostCatalog.ShellName}}": {
                    "Name": "{{SecretsHostCatalog.ShellName}}",
                    "Features": {
                      "Secrets": {
                        "EncryptionKey": "{{SecretsHostCatalog.EncryptionKey}}",
                        "Keys": { "2026-10": "rotated-key" },
                        "ActiveKeyId": "2026-10"
                      }
                    }
                  }
                }
              }
            }
            """);

        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(SecretsHostCatalog.ShellName);
        var keyRing = shell.ServiceProvider.GetRequiredService<ISecretKeyRing>();

        Assert.Equal("2026-10", keyRing.ActiveKey.KeyId);
        Assert.True(keyRing.TryGetKey(SecretEncryptionKey.LegacyKeyId, out _));
        Assert.StartsWith("v2:2026-10:", shell.ServiceProvider.GetRequiredService<ISecretValueProtector>().Protect("value"));
    }
}
