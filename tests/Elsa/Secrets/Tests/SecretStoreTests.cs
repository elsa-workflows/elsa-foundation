using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Stores;
using Microsoft.Extensions.Configuration;
using Elsa.Secrets.Options;
using Elsa.Secrets.Services;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretStoreTests
{
    [Fact]
    public async Task Encrypted_Store_Protects_Stored_Value()
    {
        var store = new EncryptedSecretStore(new DefaultSecretValueProtector(new DefaultSecretKeyRing(Microsoft.Extensions.Options.Options.Create(new SecretsOptions { EncryptionKey = "test-key" }))));
        var secret = Secret(SecretStoreNames.Encrypted);
        var version = secret.LatestActiveVersion!;

        version.Payload = await store.WriteAsync(new(secret, version, SecretPayload.FromValue("clear-text")));
        var read = await store.ReadAsync(new(secret, version));

        Assert.Equal("clear-text", read!.Value);
        Assert.NotEqual("clear-text", version.Payload.Metadata["protectedValue"]);
    }

    [Fact]
    public async Task Configuration_Store_Reads_From_Configuration_Key()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:ApiKey"] = "configured" }).Build();
        var store = new ConfigurationSecretStore(configuration);
        var secret = Secret(SecretStoreNames.Configuration);
        var version = secret.LatestActiveVersion!;
        version.Payload.Metadata[ConfigurationSecretStore.ConfigurationKeyMetadataName] = "Secrets:ApiKey";

        var read = await store.ReadAsync(new(secret, version));

        Assert.Equal("configured", read!.Value);
    }

    private static Secret Secret(string storeName) => new()
    {
        Name = "payments.api",
        StoreName = storeName,
        Versions = [new SecretVersion { Version = 1 }]
    };
}
