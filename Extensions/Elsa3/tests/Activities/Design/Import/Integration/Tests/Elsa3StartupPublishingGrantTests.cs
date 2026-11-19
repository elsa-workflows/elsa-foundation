using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>Spec 235 research R-3: the startup grant answers for one tenant inside its own scope only.</summary>
public sealed class Elsa3StartupPublishingGrantTests
{
    [Fact]
    public void Without_a_grant_every_call_reaches_the_inner_context()
    {
        var inner = new FixedContext("tenant-a");
        var context = new Elsa3StartupPublishingAuthorization(inner, new Elsa3StartupPublishingGrant());

        Assert.Equal("tenant-a", context.TenantId);
        Assert.True(context.CanAccessTenant("tenant-a"));
        Assert.False(context.CanAccessTenant("default"));
    }

    [Fact]
    public void With_a_grant_only_the_granted_tenant_and_no_tenant_are_accessible()
    {
        var grant = new Elsa3StartupPublishingGrant();
        grant.Grant("default");
        var context = new Elsa3StartupPublishingAuthorization(new FixedContext(null), grant);

        Assert.Equal("default", context.TenantId);
        Assert.True(context.CanAccessTenant("default"));
        Assert.True(context.CanAccessTenant(null));
        Assert.False(context.CanAccessTenant("other"));
    }

    [Fact]
    public void A_grant_is_set_once()
    {
        var grant = new Elsa3StartupPublishingGrant();
        grant.Grant("default");

        Assert.Throws<InvalidOperationException>(() => grant.Grant("other"));
    }

    [Fact]
    public async Task Decoration_keeps_the_inner_registration_and_scopes_the_grant()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityPublishingAuthorizationContext>(_ => new FixedContext(null));
        services.AddScoped<Elsa3StartupPublishingGrant>();
        Elsa3StartupPublishingAuthorization.Decorate(services);
        await using var provider = services.BuildServiceProvider();

        await using var granted = provider.CreateAsyncScope();
        granted.ServiceProvider.GetRequiredService<Elsa3StartupPublishingGrant>().Grant("default");
        Assert.True(granted.ServiceProvider.GetRequiredService<IActivityPublishingAuthorizationContext>().CanAccessTenant("default"));

        await using var other = provider.CreateAsyncScope();
        var context = other.ServiceProvider.GetRequiredService<IActivityPublishingAuthorizationContext>();
        Assert.IsType<Elsa3StartupPublishingAuthorization>(context);
        Assert.False(context.CanAccessTenant("default"));
    }

    private sealed class FixedContext(string? tenantId) : IActivityPublishingAuthorizationContext
    {
        public string? TenantId => tenantId;

        public bool CanAccessTenant(string? candidate) => candidate is null || StringComparer.Ordinal.Equals(candidate, tenantId);
    }
}
