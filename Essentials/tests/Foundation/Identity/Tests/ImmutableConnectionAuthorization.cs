using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>Catalog-only unit fixtures model immutable IAM; native HTTP fixtures exercise real revision coordination.</summary>
internal sealed class ImmutableConnectionAuthorization(IConnectionWriteAuthorizer inner) : IConnectionWriteAuthorizer
{
    public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) => inner.AuthorizeAsync(actor, cancellationToken);
    public ValueTask<bool> AuthorizePolicyAsync(ClaimsPrincipal actor, ConnectionRecord proposed, CancellationToken cancellationToken = default) => inner.AuthorizePolicyAsync(actor, proposed, cancellationToken);
    public async ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ClaimsPrincipal actor, ConnectionRecord? proposed,
        CancellationToken cancellationToken = default) =>
        await inner.AuthorizeAsync(actor, cancellationToken) && (proposed is null || await inner.AuthorizePolicyAsync(actor, proposed, cancellationToken)) ? [] : null;

    public static void Register(IServiceCollection services)
    {
        var original = services.Single(descriptor => descriptor.ServiceType == typeof(IConnectionWriteAuthorizer));
        services.Remove(original);
        services.AddScoped<IConnectionWriteAuthorizer>(provider => new ImmutableConnectionAuthorization(
            (IConnectionWriteAuthorizer)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)));
        services.EnsureReplacementContract<IConnectionWriteAuthorizer, ImmutableConnectionAuthorization>();
    }
}
