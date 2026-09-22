using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Feeds.Credentials;
// Both modules call their reference type SecretReference, and this is the one file that names both
// namespaces. The alias picks Elsa's, which is the only one this file constructs.
using SecretReference = Elsa.Secrets.Core.Models.SecretReference;

namespace Elsa.Secrets.Nuplane;

/// <summary>
/// Resolves a package feed's <c>secrets://elsa/&lt;name&gt;</c> credential reference out of the Secrets
/// module, so a private feed's token can live beside every other secret the host owns instead of in an
/// environment variable beside the process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the value has to come from.</b> Nuplane resolves <c>IEnumerable&lt;ISecretReferenceProvider&gt;</c>
/// out of the container that composed <c>AddNuplane</c>, once, and shares the instance across every feed and
/// every cycle. So this provider only ever answers for feeds composed in the same container, and the
/// <see cref="ISecretValueResolver"/> it reads through has to be resolvable there too — which is why the
/// scheme is claimed by a registration call the composing container makes
/// (<c>AddSecretsFeedCredentials</c>) rather than by something the Secrets module registers on its own
/// behalf. A container that never makes that call has no <c>elsa</c> provider at all, and Nuplane refuses a
/// feed referencing one by name, exactly as it refuses a reference to any other unregistered provider.
/// </para>
/// <para>
/// <b>A scope per lookup.</b> <see cref="ISecretValueResolver"/> is scoped — the durable repositories behind
/// it are — while this provider is a shared singleton Nuplane may call concurrently. Each lookup therefore
/// opens and disposes its own scope, so no two feeds share a resolver and nothing captures a scope that
/// outlives the call.
/// </para>
/// <para>
/// <b>Never the value.</b> The raw string is returned and nothing else is done with it: it is not logged,
/// not put into a message, and not returned as part of anything that carries a name. Nuplane treats it as a
/// secret from the moment it comes back, and this holds the same line on the way out — a failed resolution
/// contributes its failure code at most, never <see cref="ResolvedSecret.Value"/>.
/// </para>
/// </remarks>
public sealed class SecretsFeedCredentialProvider(IServiceScopeFactory scopeFactory, string tenantId) : ISecretReferenceProvider
{
    /// <summary>The provider segment this claims: the <c>elsa</c> in <c>secrets://elsa/feed-token</c>.</summary>
    public const string SchemeName = "elsa";

    /// <summary>
    /// The tenant a feed credential is read under when nothing configures one.
    /// </summary>
    /// <remarks>
    /// A reconcile is host-level work with no request and no principal in flight, so there is no tenant to
    /// carry in from a caller and one has to be chosen. <c>"default"</c> is the value the tree already uses
    /// for exactly that: it is <c>AspNetCoreIdentityDefaults.DefaultTenantId</c>, the tenant the identity
    /// seeder writes under, and the default of the <c>TenantId</c> setting on the EF OpenTelemetry feature.
    /// A host that keeps its feed tokens under another tenant sets the feature's own <c>TenantId</c>.
    /// </remarks>
    public const string DefaultTenantId = "default";

    private readonly string _tenantId = string.IsNullOrWhiteSpace(tenantId) ? DefaultTenantId : tenantId;

    /// <inheritdoc />
    public string Scheme => SchemeName;

    /// <inheritdoc />
    /// <remarks>
    /// Having no such secret is an ordinary answer rather than a failure, and so is having one the lifecycle
    /// policy refuses — expired, revoked, retired, wrong tenant. All of them return <see langword="null"/>,
    /// which refuses the feed by name and contacts it for nothing at all. Only a lookup that could not be
    /// performed throws, and the one way that happens here is a container with no
    /// <see cref="ISecretValueResolver"/> in it: a composition error, reported as one rather than mistaken
    /// for a feed whose token merely has not been created yet.
    /// <para>
    /// The reference names no secret type on purpose. This is a bridge, not a policy: whether the stored
    /// value is a usable credential — <c>user:password</c>, or a bare token — is Nuplane's own rule, and it
    /// refuses a feed whose secret is not one rather than sending it half-formed. Constraining the type here
    /// would add a second, quieter rule that turns a wrongly-typed secret into "no such secret".
    /// </para>
    /// </remarks>
    public async ValueTask<string?> ResolveAsync(string name, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISecretValueResolver>();
        var resolved = await resolver.ResolveAsync(_tenantId, new SecretReference(name), cancellationToken);
        return resolved.Succeeded ? resolved.Value : null;
    }
}
