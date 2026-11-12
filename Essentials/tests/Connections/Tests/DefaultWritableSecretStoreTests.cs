using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;
using Elsa.Connections.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Options;
using Elsa.Secrets.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class DefaultWritableSecretStoreTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task Owned_field_secret_is_created_in_encrypted_when_the_store_name_is_unset()
    {
        await using var provider = ConnectionsTestComposition.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();

        await manager.CreateAsync(Tenant, Bearer("default-store", "tok"));

        var secret = await secrets.FindAsync(Tenant, ConnectionName.OwnedSecretName("default-store", HttpConnectionSecretFields.Token));
        Assert.Equal(SecretStoreNames.Encrypted, secret!.StoreName);
    }

    [Fact]
    public async Task Owned_field_secret_is_created_in_the_configured_writable_store()
    {
        var store = new RecordingSecretStore();
        await using var provider = ConnectionsTestComposition.CreateProvider(configure: services =>
        {
            services.Configure<SecretsOptions>(options => options.DefaultWritableStoreName = RecordingSecretStore.StoreName);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretStore>(store));
        });
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();

        await manager.CreateAsync(Tenant, Bearer("custom-store", "tok"));

        var name = ConnectionName.OwnedSecretName("custom-store", HttpConnectionSecretFields.Token);
        var secret = await secrets.FindAsync(Tenant, name);
        Assert.Equal(RecordingSecretStore.StoreName, secret!.StoreName);
        Assert.Contains(name, store.Written);
        var resolved = await scope.ServiceProvider.GetRequiredService<ISecretValueResolver>().ResolveAsync(Tenant, new SecretReference(name));
        Assert.True(resolved.Succeeded);
        Assert.Equal("tok", resolved.Value);
    }

    private static CreateConnectionRequest Bearer(string name, string token) => new()
    {
        Name = name,
        AuthScheme = HttpAuthSchemes.Bearer,
        Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
        SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { Value = token }
        }
    };

    private sealed class RecordingSecretStore : SecretStoreBase
    {
        public const string StoreName = "custom-writable";
        public List<string> Written { get; } = [];

        protected override string TestFailureCode => "unavailable";
        protected override string TestFailureMessage => "Custom store has no value.";

        public override SecretStoreDescriptor Descriptor { get; } = new(
            StoreName,
            "Custom writable",
            "Test store",
            SecretStoreCapabilities.Read | SecretStoreCapabilities.Write | SecretStoreCapabilities.Delete | SecretStoreCapabilities.Test | SecretStoreCapabilities.Versioned,
            false);

        public override ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default)
        {
            Written.Add(context.Secret.Name);
            return new(new SecretPayload
            {
                Value = context.Payload.Value,
                Metadata = new Dictionary<string, string>(context.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
            });
        }

        public override ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default) =>
            new(context.Version.Payload);
    }
}
