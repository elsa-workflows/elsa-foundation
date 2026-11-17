using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Options;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Elsa.Secrets.Services;
using Elsa.Secrets.Stores;
using Elsa.Secrets.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class DefaultWritableSecretStoreTests
{
    [Fact]
    public void Unset_option_resolves_to_encrypted()
    {
        Assert.Equal(SecretStoreNames.Encrypted, DefaultWritableSecretStore.ResolveName(null));
        Assert.Equal(SecretStoreNames.Encrypted, DefaultWritableSecretStore.ResolveName(" "));
        Assert.Equal(SecretStoreNames.Encrypted, new SecretsOptions().DefaultWritableStoreName);
    }

    [Fact]
    public async Task Startup_accepts_the_default_encrypted_store()
    {
        using var provider = new ServiceCollection().AddSecrets().BuildServiceProvider();
        var validator = provider.GetRequiredService<DefaultWritableSecretStoreValidator>();
        await validator.StartAsync(CancellationToken.None);
        await validator.InitializeAsync();
    }

    [Fact]
    public async Task Startup_accepts_a_registered_custom_writable_store()
    {
        var services = new ServiceCollection();
        services.AddSecrets();
        services.Configure<SecretsOptions>(options => options.DefaultWritableStoreName = RecordingSecretStore.StoreName);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretStore, RecordingSecretStore>());
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<DefaultWritableSecretStoreValidator>().StartAsync(CancellationToken.None);
        var text = provider.GetServices<ISecretTypeProvider>().OfType<TextSecretTypeProvider>().Single();
        Assert.Contains(RecordingSecretStore.StoreName, text.Descriptor.SupportedStoreNames);
    }

    [Fact]
    public async Task Startup_fails_when_the_configured_store_is_not_registered()
    {
        var services = new ServiceCollection();
        services.AddSecrets();
        services.Configure<SecretsOptions>(options => options.DefaultWritableStoreName = "missing-store");
        using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<DefaultWritableSecretStoreValidator>().StartAsync(CancellationToken.None));
        Assert.Contains("missing-store", error.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa:Secrets:DefaultWritableStoreName", error.Message, StringComparison.Ordinal);
        Assert.Contains(SecretStoreNames.Encrypted, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_fails_when_the_configured_store_is_read_only()
    {
        var services = new ServiceCollection();
        services.AddSecrets();
        services.Configure<SecretsOptions>(options => options.DefaultWritableStoreName = SecretStoreNames.Configuration);
        using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<DefaultWritableSecretStoreValidator>().StartAsync(CancellationToken.None));
        Assert.Contains(SecretStoreNames.Configuration, error.Message, StringComparison.Ordinal);
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_binds_the_default_writable_store_name()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:DefaultWritableStoreName"] = RecordingSecretStore.StoreName
            })
            .Build();

        using var provider = new ServiceCollection().AddSecrets(configuration).BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WritableSecretStoreOptions>>().Value;
        Assert.Equal(RecordingSecretStore.StoreName, options.DefaultWritableStoreName);
    }

    private sealed class RecordingSecretStore : SecretStoreBase
    {
        public const string StoreName = "custom-writable";

        protected override string TestFailureCode => "unavailable";
        protected override string TestFailureMessage => "Custom store has no value.";

        public override SecretStoreDescriptor Descriptor { get; } = new(
            StoreName,
            "Custom writable",
            "Test store",
            SecretStoreCapabilities.Read | SecretStoreCapabilities.Write | SecretStoreCapabilities.Delete | SecretStoreCapabilities.Test | SecretStoreCapabilities.Versioned,
            false);

        public override ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default) =>
            new(context.Payload);

        public override ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default) =>
            new(context.Version.Payload);
    }
}
