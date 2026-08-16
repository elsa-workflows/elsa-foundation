using System.Security.Claims;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Services;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class HttpContextActivityDesignAuthorizationContextTests
{
    [Fact]
    public async Task Provider_resource_handlers_can_grant_payload_access_and_veto_claim_grants()
    {
        var services = CreateServices();
        services.AddScoped<IPermissionResourceHandler, ProviderResourceHandler>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var authorization = provider.GetRequiredService<IPermissionAuthorizationService>();
        var validator = provider.GetRequiredService<NormalizedPrincipalValidator>();

        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var resourceContext = new HttpContextActivityDesignAuthorizationContext(accessor, authorization, validator);
        Assert.True(await resourceContext.CanAuthorProviderAsync("resource-granted"));
        Assert.False(await resourceContext.CanReadProviderPayloadAsync("resource-granted"));
        Assert.True(await resourceContext.CanReadProviderPayloadAsync("payload-only"));
        Assert.False(await resourceContext.CanAuthorProviderAsync("payload-only"));

        accessor.HttpContext = new DefaultHttpContext
        {
            User = Principal("tenant-a", HttpContextActivityDesignAuthorizationContext.AuthorPermission)
        };
        var vetoedContext = new HttpContextActivityDesignAuthorizationContext(accessor, authorization, validator);
        Assert.False(await vetoedContext.CanAuthorProviderAsync("blocked"));
    }

    [Fact]
    public async Task Authorization_profile_snapshot_is_started_once_under_concurrent_callers()
    {
        var counter = new EvaluationCounter();
        var services = CreateServices();
        services.AddSingleton(counter);
        services.ReplacePermissionEvaluator<DelayedPermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var context = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        var profiles = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => context.GetAuthorizationProfileAsync().AsTask()));

        Assert.All(profiles, profile => Assert.Equal(profiles[0], profile));
        Assert.Equal(3, counter.Calls);
    }

    [Fact]
    public async Task Canceled_waiter_does_not_cancel_the_shared_snapshot_for_another_caller()
    {
        var counter = new EvaluationCounter();
        var services = CreateServices();
        services.AddSingleton(counter);
        services.ReplacePermissionEvaluator<DelayedPermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var context = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());
        using var cancellation = new CancellationTokenSource();

        var canceled = context.GetAuthorizationProfileAsync(cancellation.Token).AsTask();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref counter.Calls) > 0, TimeSpan.FromSeconds(1)));
        cancellation.Cancel();
        var successful = context.GetAuthorizationProfileAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(string.IsNullOrWhiteSpace(await successful));
        Assert.Equal(3, counter.Calls);
    }

    [Fact]
    public async Task Mixed_principal_uses_only_the_single_trusted_identity()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = MixedPrincipal() };
        var context = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.Equal("tenant-trusted", context.TenantId);
        Assert.Equal("actor-trusted", context.ActorId);
        Assert.False(await context.CanAuthorProviderAsync("provider"));
        Assert.False(await context.CanReadAsync(new("ActivityVersion", "definition", TenantId: "tenant-untrusted")));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        return services;
    }

    private static ClaimsPrincipal Principal(string tenantId, params string[] permissions) =>
        new(new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, tenantId),
            ..permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
        ], "test"));

    private static ClaimsPrincipal MixedPrincipal() =>
        new ClaimsPrincipal([
            new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.TenantId, "tenant-untrusted"),
            new Claim(ClaimTypes.NameIdentifier, "actor-untrusted"),
            new Claim(IdentityClaimTypes.Permission, HttpContextActivityDesignAuthorizationContext.AuthorPermission)
        ], "untrusted"), new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-trusted"),
            new Claim(ClaimTypes.NameIdentifier, "actor-trusted")
        ], "test")]);

    private sealed class ProviderResourceHandler : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Resource is not ActivityProviderAuthorizationResource resource || resource.TenantId != "tenant-a")
                return ValueTask.FromResult<PermissionEvaluationResult?>(null);

            if (resource.ProviderKey == "blocked" && context.Permission == HttpContextActivityDesignAuthorizationContext.AuthorPermission)
                return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied("blocked"));

            if (resource.ProviderKey == "resource-granted" && context.Permission == HttpContextActivityDesignAuthorizationContext.AuthorPermission)
                return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);

            if (resource.ProviderKey == "payload-only" && context.Permission == HttpContextActivityDesignAuthorizationContext.ProviderPayloadReadPermission)
                return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);

            return ValueTask.FromResult<PermissionEvaluationResult?>(null);
        }
    }

    private sealed class EvaluationCounter
    {
        public int Calls;
    }

    private sealed class DelayedPermissionEvaluator(EvaluationCounter counter) : IPermissionEvaluator
    {
        public async ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref counter.Calls);
            await Task.Delay(25, cancellationToken);
            return PermissionEvaluationResult.Denied();
        }
    }
}
