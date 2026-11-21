using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Options;
using Elsa.Secrets.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class DefaultWritableSecretStoreTests
{
    [Fact]
    public async Task Grant_is_created_in_encrypted_when_the_store_name_is_unset()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.ClientCredentialsScheme(preview));

        await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);

        var grant = await harness.Secrets.FindAsync(
            OAuth2TestSupport.TenantId,
            OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName));
        Assert.Equal(SecretStoreNames.Encrypted, grant!.StoreName);
    }

    [Fact]
    public async Task Configured_store_receives_owned_field_secrets_and_oauth_grants()
    {
        var store = new RecordingSecretStore();
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServices: services =>
            {
                services.Configure<SecretsOptions>(options => options.DefaultWritableStoreName = RecordingSecretStore.StoreName);
                services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretStore>(store));
            },
            inlineClientSecret: true);

        var ownedName = $"connection:{OAuth2TestSupport.ConnectionName}:{OAuth2SecretFields.ClientSecret.ToLowerInvariant()}";
        var owned = await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, ownedName);
        Assert.Equal(RecordingSecretStore.StoreName, owned!.StoreName);
        Assert.Contains(ownedName, store.Written);

        var tokens = harness.Services.GetRequiredService<OAuth2TokenService>();
        var resolver = harness.Services.GetRequiredService<IConnectionResolver>();
        await using var connection = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = OAuth2TestSupport.TenantId,
            Name = OAuth2TestSupport.ConnectionName,
            RequiredType = ConnectionTypeNames.Http,
            Owner = ConnectionOwners.Tenant,
            AllowNeedsAuthorization = true
        });

        var started = await tokens.BeginAuthorizationCodeAsync(connection);
        var pkce = store.Written.Single(name => name.Contains(":oauth2-pkce:", StringComparison.Ordinal));
        Assert.Equal(RecordingSecretStore.StoreName, (await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, pkce))!.StoreName);

        var code = harness.Server.ApproveAuthorization(new Uri(started.AuthorizationUrl));
        await tokens.CompleteAuthorizationCodeAsync(connection, code, started.State);

        var grantName = OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName);
        var grant = await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, grantName);
        Assert.Equal(RecordingSecretStore.StoreName, grant!.StoreName);
        Assert.Contains(grantName, store.Written);
    }

    [Fact]
    public async Task OAuth2_writes_grants_when_the_configured_store_is_present_and_encrypted_is_not()
    {
        var store = new RecordingSecretStore();
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.ClientCredentialsScheme(preview),
            configureServices: services =>
            {
                services.Configure<SecretsOptions>(options => options.DefaultWritableStoreName = RecordingSecretStore.StoreName);
                foreach (var descriptor in services.Where(registration => registration.ImplementationType == typeof(EncryptedSecretStore)).ToList())
                    services.Remove(descriptor);
                services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretStore>(store));
            },
            inlineClientSecret: true);

        await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);

        var grantName = OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName);
        var grant = await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, grantName);
        Assert.Equal(RecordingSecretStore.StoreName, grant!.StoreName);
        Assert.Contains(grantName, store.Written);
        Assert.DoesNotContain(harness.Services.GetServices<ISecretStore>(), item => item is EncryptedSecretStore);
    }

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
