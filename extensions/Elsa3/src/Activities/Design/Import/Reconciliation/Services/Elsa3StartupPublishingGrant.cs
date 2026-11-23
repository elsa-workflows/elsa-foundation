using Elsa.Workflows.Publishing.Api.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>
/// The tenant the import at engine start may publish for, held by the DI scope the startup task runs its import in.
/// Only that task grants it, inside the operation scope the persistence runner created for it, so no HTTP request
/// shares the scope (spec 235 research R-3).
/// </summary>
internal sealed class Elsa3StartupPublishingGrant
{
    public bool IsGranted { get; private set; }

    public string? TenantId { get; private set; }

    public void Grant(string tenantId)
    {
        if (IsGranted)
            throw new InvalidOperationException("The startup publishing grant is already set for this scope.");
        TenantId = tenantId;
        IsGranted = true;
    }
}

/// <summary>
/// Decorates <see cref="IActivityPublishingAuthorizationContext"/>: while the scope's
/// <see cref="Elsa3StartupPublishingGrant"/> is set, the activity publisher may read rows of the granted tenant and of
/// no tenant; otherwise every call goes to the decorated context unchanged. The activity publisher authorizes by the
/// HTTP user's tenant claim, and at engine start there is none.
/// </summary>
internal sealed class Elsa3StartupPublishingAuthorization(
    IActivityPublishingAuthorizationContext inner,
    Elsa3StartupPublishingGrant grant) : IActivityPublishingAuthorizationContext
{
    public string? TenantId => grant.IsGranted ? grant.TenantId : inner.TenantId;

    public bool CanAccessTenant(string? tenantId) =>
        grant.IsGranted
            ? tenantId is null || StringComparer.Ordinal.Equals(tenantId, grant.TenantId)
            : inner.CanAccessTenant(tenantId);

    /// <summary>
    /// Replaces the registered context with this decorator around it. The context is a scoped registration of
    /// Workflows Publishing's API, which this feature depends on, so it is registered by the time this runs.
    /// </summary>
    public static void Decorate(IServiceCollection services)
    {
        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IActivityPublishingAuthorizationContext))
            ?? throw new InvalidOperationException(
                $"{nameof(IActivityPublishingAuthorizationContext)} is not registered; the Elsa3JsonReconciliation feature needs WorkflowsPublishingApi.");
        services.Remove(existing);
        services.Add(ServiceDescriptor.Describe(
            typeof(IActivityPublishingAuthorizationContext),
            provider => new Elsa3StartupPublishingAuthorization(Inner(provider, existing), provider.GetRequiredService<Elsa3StartupPublishingGrant>()),
            existing.Lifetime));
    }

    private static IActivityPublishingAuthorizationContext Inner(IServiceProvider provider, ServiceDescriptor existing) =>
        (IActivityPublishingAuthorizationContext)(existing.ImplementationInstance
            ?? existing.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, existing.ImplementationType!));
}
