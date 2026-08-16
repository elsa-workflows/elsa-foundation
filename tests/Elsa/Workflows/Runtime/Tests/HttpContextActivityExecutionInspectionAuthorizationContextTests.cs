using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class HttpContextActivityExecutionInspectionAuthorizationContextTests
{
    [Theory]
    [InlineData("exact")]
    [InlineData("implied")]
    [InlineData("wildcard")]
    [InlineData("normalized-external")]
    public async Task Foundation_evaluator_paths_flow_through_the_runtime_context(string grantKind)
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        if (grantKind == "implied")
            services.ReplacePermissionCatalog<RuntimePermissionCatalog>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var grant = grantKind switch
        {
            "implied" => "workflows.activity-executions.inspect.bundle",
            "wildcard" => "*",
            _ => HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission
        };
        var principal = grantKind == "normalized-external"
            ? await NormalizeExternalPrincipalAsync(provider, HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission)
            : Principal("tenant-a", grant);
        accessor.HttpContext = new DefaultHttpContext { User = principal };
        var context = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.True(await context.CanInspectStructureAsync(Execution("tenant-a")));
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
                "runtime-admins",
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
    public async Task Authorization_profile_snapshot_is_started_once_under_concurrent_callers()
    {
        var counter = new EvaluationCounter();
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        services.AddSingleton(counter);
        services.ReplacePermissionEvaluator<DelayedPermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var context = new HttpContextActivityExecutionInspectionAuthorizationContext(
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
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        services.AddSingleton(counter);
        services.ReplacePermissionEvaluator<DelayedPermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var context = new HttpContextActivityExecutionInspectionAuthorizationContext(
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
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = MixedPrincipal() };
        var context = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.Equal("tenant:tenant-trusted", context.TenantScope);
        Assert.Equal("actor-trusted", context.AuditSubject);
        Assert.False(await context.CanInspectStructureAsync(Execution("tenant-untrusted")));
        Assert.False(await context.CanInspectStructureAsync(Execution("tenant-trusted")));
    }

    [Fact]
    public async Task Untrusted_or_absent_http_context_fails_closed_before_a_replacement_service()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        services.ReplacePermissionAuthorizationService<GrantingAuthorizationService>();
        using var provider = services.BuildServiceProvider();
        var service = (GrantingAuthorizationService)provider.GetRequiredService<IPermissionAuthorizationService>();
        var context = new HttpContextActivityExecutionInspectionAuthorizationContext(
            provider.GetRequiredService<IHttpContextAccessor>(),
            service,
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.False(await context.CanInspectStructureAsync(Execution("tenant")));
        Assert.Equal("untrusted", await context.GetAuthorizationProfileAsync());
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Trusted_context_uses_a_host_replacement_authorization_service()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        services.ReplacePermissionAuthorizationService<GrantingAuthorizationService>();
        using var provider = services.BuildServiceProvider();
        var service = (GrantingAuthorizationService)provider.GetRequiredService<IPermissionAuthorizationService>();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var context = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            service,
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.True(await context.CanInspectStructureAsync(Execution("tenant-a")));
        Assert.True(service.Calls > 0);
    }

    [Fact]
    public async Task Origin_constructor_fails_closed_without_permission_matching()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal("tenant-a", "*") } };
        var context = new HttpContextActivityExecutionInspectionAuthorizationContext(accessor);

        Assert.False(await context.CanInspectStructureAsync(Execution("tenant-a")));
        Assert.Equal("untrusted", await context.GetAuthorizationProfileAsync());
    }

    [Fact]
    public async Task Canceled_snapshot_task_is_evicted_and_a_fresh_request_can_retry()
    {
        var counter = new EvaluationCounter();
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        services.AddSingleton(counter);
        services.ReplacePermissionEvaluator<DelayedPermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        using var aborted = new CancellationTokenSource();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a"), RequestAborted = aborted.Token };
        var first = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());
        var pending = first.GetAuthorizationProfileAsync().AsTask();

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref counter.Calls) > 0, TimeSpan.FromSeconds(1)));
        aborted.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var retry = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());
        Assert.False(string.IsNullOrWhiteSpace(await retry.GetAuthorizationProfileAsync()));
    }

    [Fact]
    public async Task Faulted_snapshot_task_is_evicted_and_a_fresh_request_can_retry()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        services.ReplacePermissionEvaluator<FaultOncePermissionEvaluator>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var first = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.GetAuthorizationProfileAsync().AsTask());

        accessor.HttpContext = new DefaultHttpContext { User = Principal("tenant-a") };
        var retry = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());
        Assert.False(string.IsNullOrWhiteSpace(await retry.GetAuthorizationProfileAsync()));
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
            new Claim(IdentityClaimTypes.Permission, HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission)
        ], "untrusted"), new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-trusted"),
            new Claim(ClaimTypes.NameIdentifier, "actor-trusted")
        ], "test")]);

    private static WorkflowExecutionState Execution(string tenantId) =>
        new(
            "workflow",
            new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            null,
            null,
            tenantId,
            new Dictionary<string, string>());

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

    private sealed class RuntimePermissionCatalog : IPermissionCatalog
    {
        private static readonly Permission Bundle = new(
            "workflows.activity-executions.inspect.bundle",
            "Inspection bundle",
            "test",
            "Test implication",
            new HashSet<string>([HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission], StringComparer.Ordinal));

        public IReadOnlyCollection<Permission> List() => [Bundle];
        public Permission? Find(string key) => string.Equals(key, Bundle.Key, StringComparison.Ordinal) ? Bundle : null;
    }
}
