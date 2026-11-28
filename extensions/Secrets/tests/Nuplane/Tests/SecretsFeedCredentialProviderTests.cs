using Elsa.Secrets.Nuplane.Extensions;
using Elsa.Secrets.Nuplane.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Feeds.Credentials;
using Xunit;

namespace Elsa.Secrets.Nuplane.Tests;

/// <summary>
/// The <c>elsa</c> secret-reference provider on its own: what a name resolves to, and what each way of
/// having no value resolves to.
/// </summary>
/// <remarks>
/// Nuplane's rule is that having no value is an ordinary answer — return null and the feed is refused by
/// name — while a lookup that could not be performed throws. The two are kept apart here deliberately: a
/// refused feed is reported, whereas a provider that swallowed a broken composition and answered "no value"
/// would leave an operator reading a refusal about a feed whose token is sitting in the store.
/// </remarks>
public sealed class SecretsFeedCredentialProviderTests : IAsyncDisposable
{
    private const string Name = "feed-token";
    private const string Value = "feed-token-value-7c4a1e";

    private readonly ServiceProvider services = new ServiceCollection()
        .AddSecretsReadPath()
        .AddSecretsFeedCredentials()
        .BuildServiceProvider();

    private ISecretReferenceProvider Provider => Assert.Single(services.GetServices<ISecretReferenceProvider>());

    public ValueTask DisposeAsync() => services.DisposeAsync();

    [Fact]
    public void The_provider_claims_the_elsa_segment()
    {
        Assert.Equal("elsa", Provider.Scheme);
        Assert.Equal(SecretsFeedCredentialProvider.SchemeName, Provider.Scheme);
    }

    [Fact]
    public async Task A_stored_secret_resolves_to_its_value()
    {
        await services.StoreAsync(Name, Value);

        Assert.Equal(Value, await Provider.ResolveAsync(Name, CancellationToken.None));
    }

    [Fact]
    public async Task A_name_nothing_is_stored_under_resolves_to_null()
    {
        await services.StoreAsync(Name, Value);

        Assert.Null(await Provider.ResolveAsync("some-other-token", CancellationToken.None));
    }

    /// <summary>
    /// The <c>TenantId</c> setting has to mean something: a reconcile carries no principal, so the tenant is
    /// whatever the composition named, and a secret belonging to another tenant is not this host's to send.
    /// </summary>
    [Fact]
    public async Task A_secret_stored_under_another_tenant_resolves_to_null()
    {
        await services.StoreAsync(Name, Value, tenantId: "some-other-tenant");

        Assert.Null(await Provider.ResolveAsync(Name, CancellationToken.None));
    }

    /// <summary>
    /// The direction that could be mistaken for success: the secret exists and its value is still in the
    /// store, so a provider that read the payload without asking the lifecycle policy would authenticate
    /// with a token the operator has revoked.
    /// </summary>
    [Fact]
    public async Task A_revoked_secret_resolves_to_null_rather_than_to_the_value_still_in_the_store()
    {
        await services.StoreAsync(Name, Value);
        Assert.Equal(Value, await Provider.ResolveAsync(Name, CancellationToken.None));

        await services.RevokeAsync(Name);

        Assert.Null(await Provider.ResolveAsync(Name, CancellationToken.None));
    }

    /// <summary>
    /// A container that claims the <c>elsa</c> segment without the Secrets read path behind it is a
    /// composition error, and it is reported as one. Returning null there would be the silent failure: every
    /// feed refused by name, with nothing anywhere saying the lookup never had a chance to run.
    /// </summary>
    [Fact]
    public async Task A_composition_without_the_secrets_read_path_fails_loudly_rather_than_reporting_no_value()
    {
        await using var orphaned = new ServiceCollection().AddSecretsFeedCredentials().BuildServiceProvider();
        var provider = Assert.Single(orphaned.GetServices<ISecretReferenceProvider>());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.ResolveAsync(Name, CancellationToken.None));

        Assert.Contains(nameof(Elsa.Secrets.Core.Contracts.ISecretValueResolver), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nuplane refuses the whole composition when two providers claim one segment, on purpose: a replacement
    /// that did not take effect must not look like one that did. A host that registers the provider on its
    /// own container and a shell feature that registers it again are the ordinary way that would happen, so
    /// the registration has to be idempotent — and the proof is Nuplane's own resolver accepting the result.
    /// </summary>
    [Fact]
    public async Task Registering_the_provider_twice_still_composes_one_provider_nuplane_accepts()
    {
        await using var twice = new ServiceCollection()
            .AddSecretsReadPath()
            .AddSecretsFeedCredentials()
            .AddSecretsFeedCredentials("some-other-tenant")
            .BuildServiceProvider();

        var providers = twice.GetServices<ISecretReferenceProvider>().ToArray();

        Assert.Equal("elsa", Assert.Single(providers).Scheme);
        _ = new SecretReferenceResolver(providers);
    }
}
