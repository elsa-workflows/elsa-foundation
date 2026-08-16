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
    [Theory]
    [InlineData("exact")]
    [InlineData("implied")]
    [InlineData("wildcard")]
    [InlineData("normalized-external")]
    public async Task Foundation_evaluator_paths_flow_through_the_design_context(string grantKind)
    {
        var services = CreateServices();
        if (grantKind == "implied")
            services.ReplacePermissionCatalog<DesignPermissionCatalog>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var grant = grantKind switch
        {
            "implied" => "activities.design.author.bundle",
            "wildcard" => "*",
            _ => HttpContextActivityDesignAuthorizationContext.AuthorPermission
        };
        var principal = grantKind == "normalized-external"
            ? await NormalizeExternalPrincipalAsync(provider, HttpContextActivityDesignAuthorizationContext.AuthorPermission)
            : Principal("tenant-a", grant);
        accessor.HttpContext = new DefaultHttpContext { User = principal };
        var context = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.True(await context.CanAuthorProviderAsync("provider"));
    }

    private static async Task<ClaimsPrincipal> NormalizeExternalPrincipalAsync(
        ServiceProvider provider,
        string permission)
    {
        var rawPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("groups", "admins")], "entra"));
        var result = await provider.GetRequiredService<IClaimsNormalizer>().NormalizeAsync(new(
            rawPrincipal,
            "tenant-a",
            "entra",
            [new ClaimMappingRule(
                "design-admins",
                "tenant-a",
                "entra",
                "groups",
                "admins",
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>([permission], StringComparer.OrdinalIgnoreCase),
                1,
                false)],
            "test"));

        return result.Principal;
    }

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
        await counter.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
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

    [Fact]
    public async Task Untrusted_or_absent_http_context_fails_closed_before_a_replacement_service()
    {
        var services = CreateServices();
        services.ReplacePermissionAuthorizationService<GrantingAuthorizationService>();
        using var provider = services.BuildServiceProvider();
        var service = (GrantingAuthorizationService)provider.GetRequiredService<IPermissionAuthorizationService>();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var context = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            service,
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.False(await context.CanAuthorProviderAsync("provider"));
        Assert.False(await context.CanManageActivityDefinitionsAsync());
        Assert.False(await context.CanReadProviderPayloadAsync("provider"));
        Assert.False(await context.CanReadAsync(new("ActivityVersion", "definition")));
        Assert.Equal("untrusted", await context.GetAuthorizationProfileAsync());
        Assert.Equal(0, service.Calls);

        accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "raw-user"),
                new Claim("provider", "entra"),
                new Claim("groups", "admins"),
                new Claim(IdentityClaimTypes.Permission, HttpContextActivityDesignAuthorizationContext.AuthorPermission)
            ], "test"))
        };
        var rawContext = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            service,
            provider.GetRequiredService<NormalizedPrincipalValidator>());
        Assert.False(await rawContext.CanAuthorProviderAsync("provider"));
        Assert.False(await rawContext.CanReadProviderPayloadAsync("provider"));
        Assert.False(await rawContext.CanManageActivityDefinitionsAsync());
        Assert.False(await rawContext.CanReadAsync(new("ActivityVersion", "definition")));
        Assert.Equal("untrusted", await rawContext.GetAuthorizationProfileAsync());
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Trusted_context_uses_a_host_replacement_authorization_service()
    {
        var services = CreateServices();
        services.ReplacePermissionAuthorizationService<GrantingAuthorizationService>();
        using var provider = services.BuildServiceProvider();
        var service = (GrantingAuthorizationService)provider.GetRequiredService<IPermissionAuthorizationService>();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var context = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            service,
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.False(await context.CanAuthorProviderAsync(string.Empty));
        Assert.False(await context.CanReadProviderPayloadAsync(string.Empty));
        Assert.Equal(0, service.Calls);
        Assert.True(await context.CanAuthorProviderAsync("provider"));
        Assert.True(service.Calls > 0);

        Assert.Throws<InvalidOperationException>(() => context.AuthorizationProfile);
        Assert.Throws<InvalidOperationException>(() => context.CanAuthorProvider("provider"));
        Assert.Throws<InvalidOperationException>(() => context.CanReadProviderPayload("provider"));
        Assert.Throws<InvalidOperationException>(() => context.CanRead(new("ActivityVersion", "definition")));
        Assert.Throws<InvalidOperationException>(() => context.CanManageActivityDefinitions);
    }

    [Fact]
    public async Task Origin_constructor_fails_closed_without_permission_matching()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal("tenant-a", "*") } };
        var context = new HttpContextActivityDesignAuthorizationContext(accessor);

        Assert.False(await context.CanAuthorProviderAsync("provider"));
        Assert.False(await context.CanReadProviderPayloadAsync("provider"));
        Assert.False(await context.CanManageActivityDefinitionsAsync());
        Assert.False(await context.CanReadAsync(new("ActivityVersion", "definition")));
        Assert.Equal("untrusted", await context.GetAuthorizationProfileAsync());
    }

    [Fact]
    public void Constructors_guard_null_dependencies_and_public_adapters()
    {
        Assert.Throws<ArgumentNullException>(() => new HttpContextActivityDesignAuthorizationContext(null!));
        Assert.Throws<ArgumentNullException>(() => new LegacyActivityAuthoringContextAdapter(null!));
        Assert.Throws<ArgumentNullException>(() => new LegacyActivityDependencyContextAdapter(null!));

        using var provider = CreateServices().BuildServiceProvider();
        var accessor = new HttpContextAccessor();
        var authorization = provider.GetRequiredService<IPermissionAuthorizationService>();
        var validator = provider.GetRequiredService<NormalizedPrincipalValidator>();
        Assert.Throws<ArgumentNullException>(() => new HttpContextActivityDesignAuthorizationContext(null!, authorization, validator));
        Assert.Throws<ArgumentNullException>(() => new HttpContextActivityDesignAuthorizationContext(accessor, null!, validator));
        Assert.Throws<ArgumentNullException>(() => new HttpContextActivityDesignAuthorizationContext(accessor, authorization, null!));
    }

    [Fact]
    public async Task Canceled_snapshot_task_is_evicted_and_the_same_context_can_retry()
    {
        var probe = new SnapshotProbe();
        var services = CreateServices();
        services.AddSingleton(probe);
        services.ReplacePermissionEvaluator<CancelOncePermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var first = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());
        var pending = first.GetAuthorizationProfileAsync().AsTask();

        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.False(string.IsNullOrWhiteSpace(await first.GetAuthorizationProfileAsync()));
    }

    [Fact]
    public async Task Faulted_snapshot_task_is_evicted_and_the_same_context_can_retry()
    {
        var services = CreateServices();
        services.ReplacePermissionEvaluator<FaultOncePermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var first = new HttpContextActivityDesignAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.GetAuthorizationProfileAsync().AsTask());

        Assert.False(string.IsNullOrWhiteSpace(await first.GetAuthorizationProfileAsync()));
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
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DelayedPermissionEvaluator(EvaluationCounter counter) : IPermissionEvaluator
    {
        public async ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref counter.Calls);
            counter.Started.TrySetResult();
            await Task.Delay(25, cancellationToken);
            return PermissionEvaluationResult.Denied();
        }
    }

    private sealed class FaultOncePermissionEvaluator : IPermissionEvaluator
    {
        private int _calls;

        public ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("synthetic snapshot failure");

            return ValueTask.FromResult(PermissionEvaluationResult.Denied());
        }
    }

    private sealed class SnapshotProbe
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CancelOncePermissionEvaluator(SnapshotProbe probe) : IPermissionEvaluator
    {
        private int _calls;

        public ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                probe.Started.TrySetResult();
                return ValueTask.FromCanceled<PermissionEvaluationResult>(new CancellationToken(true));
            }

            return ValueTask.FromResult(PermissionEvaluationResult.Denied());
        }
    }

    private sealed class GrantingAuthorizationService : IPermissionAuthorizationService
    {
        public int Calls;

        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return ValueTask.FromResult(PermissionEvaluationResult.Success);
        }
    }

    private sealed class DesignPermissionCatalog : IPermissionCatalog
    {
        private static readonly Permission Bundle = new(
            "activities.design.author.bundle",
            "Design author bundle",
            "test",
            "Test implication",
            new HashSet<string>([HttpContextActivityDesignAuthorizationContext.AuthorPermission], StringComparer.Ordinal));

        public IReadOnlyCollection<Permission> List() => [Bundle];
        public Permission? Find(string key) => string.Equals(key, Bundle.Key, StringComparison.Ordinal) ? Bundle : null;
    }
}
