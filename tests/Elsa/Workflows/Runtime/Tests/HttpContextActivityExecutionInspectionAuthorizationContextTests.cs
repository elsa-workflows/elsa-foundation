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

    private static ClaimsPrincipal Principal(string tenantId) =>
        new(new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, tenantId)
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
}
