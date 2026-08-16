using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class PermissionAuthorizationSemanticsTests
{
    [Fact]
    public async Task AnyAndAllEvaluateCanonicalMembersInOrderAndShortCircuit()
    {
        var state = new EvaluationState();
        state.Results["READ"] = false;
        state.Results["WRITE"] = true;
        using var provider = BuildProvider(state);
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var codec = provider.GetRequiredService<IPermissionPolicyCodec>();

        var any = await authorization.AuthorizeAsync(TrustedPrincipal(), null,
            codec.Format(PermissionPolicyDescriptor.Any("write", "read", "READ")));
        Assert.True(any.Succeeded);
        Assert.Equal(["READ", "WRITE"], state.EvaluatorCalls);

        state.Reset();
        state.Results["READ"] = true;
        var anyShortCircuit = await authorization.AuthorizeAsync(TrustedPrincipal(), null,
            codec.Format(PermissionPolicyDescriptor.Any("write", "read")));
        Assert.True(anyShortCircuit.Succeeded);
        Assert.Equal(["READ"], state.EvaluatorCalls);

        state.Reset();
        state.Results["READ"] = true;
        state.Results["WRITE"] = false;
        var all = await authorization.AuthorizeAsync(TrustedPrincipal(), null,
            codec.Format(PermissionPolicyDescriptor.All("write", "read")));
        Assert.False(all.Succeeded);
        Assert.Equal(["READ", "WRITE"], state.EvaluatorCalls);

        state.Reset();
        state.Results["READ"] = false;
        var allShortCircuit = await authorization.AuthorizeAsync(TrustedPrincipal(), null,
            codec.Format(PermissionPolicyDescriptor.All("write", "read")));
        Assert.False(allShortCircuit.Succeeded);
        Assert.Equal(["READ"], state.EvaluatorCalls);
    }

    [Fact]
    public async Task ResourceDenialVetoesGrantAndEvaluatorWhileAnyMembersRemainIsolated()
    {
        var state = new EvaluationState();
        using var provider = BuildProvider(state, services =>
        {
            services.AddScoped<IPermissionResourceHandler, ConditionalGrantResourceHandler>();
            services.AddScoped<IPermissionResourceHandler, ConditionalDenyResourceHandler>();
        });
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var codec = provider.GetRequiredService<IPermissionPolicyCodec>();

        var deniedSingle = await authorization.AuthorizeAsync(TrustedPrincipal(), new DomainResource("tenant-a"),
            codec.Format(PermissionPolicyDescriptor.Single("read")));
        Assert.False(deniedSingle.Succeeded);
        Assert.Empty(state.EvaluatorCalls);

        var any = await authorization.AuthorizeAsync(TrustedPrincipal(), new DomainResource("tenant-a"),
            codec.Format(PermissionPolicyDescriptor.Any("read", "write")));
        Assert.True(any.Succeeded);
        Assert.Empty(state.EvaluatorCalls);

        var all = await authorization.AuthorizeAsync(TrustedPrincipal(), new DomainResource("tenant-a"),
            codec.Format(PermissionPolicyDescriptor.All("read", "write")));
        Assert.False(all.Succeeded);
        Assert.Empty(state.EvaluatorCalls);
    }

    [Fact]
    public async Task OperationalFailuresPropagateAndStopLaterGrantSources()
    {
        var state = new EvaluationState();
        using (var provider = BuildProvider(state, services =>
               {
                   services.AddScoped<IPermissionResourceHandler, ThrowingResourceHandler>();
                   services.AddScoped<IPermissionResourceHandler, LaterGrantResourceHandler>();
               }))
        {
            var authorization = provider.GetRequiredService<IAuthorizationService>();
            var policy = provider.GetRequiredService<IPermissionPolicyCodec>()
                .Format(PermissionPolicyDescriptor.Single("read"));

            await Assert.ThrowsAsync<TimeoutException>(() => authorization.AuthorizeAsync(TrustedPrincipal(), null, policy));
            Assert.Equal(0, state.LaterResourceCalls);
            Assert.Empty(state.EvaluatorCalls);
        }

        state.Reset();
        state.EvaluatorException = new InvalidOperationException("evaluator failed");
        using (var provider = BuildProvider(state))
        {
            var authorization = provider.GetRequiredService<IAuthorizationService>();
            var policy = provider.GetRequiredService<IPermissionPolicyCodec>()
                .Format(PermissionPolicyDescriptor.Single("read"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => authorization.AuthorizeAsync(TrustedPrincipal(), null, policy));
            Assert.Equal("evaluator failed", exception.Message);
            Assert.Equal(["READ"], state.EvaluatorCalls);
        }
    }

    [Fact]
    public async Task RequestCancellationUsesResourceOrAccessorWithoutReplacingDomainResource()
    {
        var state = new EvaluationState { ThrowOnCancellation = true };
        using var provider = BuildProvider(state, services =>
            services.AddScoped<IPermissionResourceHandler, CancellationRecordingResourceHandler>());
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var policy = provider.GetRequiredService<IPermissionPolicyCodec>()
            .Format(PermissionPolicyDescriptor.Single("read"));

        using var source = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext { RequestServices = provider, RequestAborted = source.Token };
        var result = await authorization.AuthorizeAsync(TrustedPrincipal(), httpContext, policy);
        Assert.True(result.Succeeded);
        Assert.Same(httpContext, state.Resources.Single());
        Assert.Equal(source.Token, state.MethodTokens.Single());
        Assert.Equal(source.Token, state.ContextTokens.Single());

        state.Reset();
        state.ThrowOnCancellation = false;
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestAborted = source.Token };
        var resource = new DomainResource("tenant-resource");
        await authorization.AuthorizeAsync(TrustedPrincipal(), resource, policy);
        Assert.Same(resource, state.Resources.Single());
        Assert.Equal(source.Token, state.MethodTokens.Single());
        Assert.Equal(source.Token, state.ContextTokens.Single());
        Assert.Equal("tenant-resource", state.TenantIds.Single());

        state.Reset();
        accessor.HttpContext = null;
        await authorization.AuthorizeAsync(TrustedPrincipal(), resource, policy);
        Assert.Equal(CancellationToken.None, state.MethodTokens.Single());
        Assert.Equal(CancellationToken.None, state.ContextTokens.Single());
    }

    [Fact]
    public async Task CancellationIsEnforcedBeforeAndAfterReplacementSourcesThatIgnoreTheToken()
    {
        var state = new EvaluationState();
        using var provider = BuildProvider(state, services =>
        {
            services.AddScoped<IPermissionResourceHandler, CancellationRecordingResourceHandler>();
            services.AddScoped<IPermissionResourceHandler, LaterGrantResourceHandler>();
        });
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var policy = provider.GetRequiredService<IPermissionPolicyCodec>()
            .Format(PermissionPolicyDescriptor.Single("read"));

        using (var alreadyCancelled = new CancellationTokenSource())
        {
            alreadyCancelled.Cancel();
            var context = new DefaultHttpContext { RequestAborted = alreadyCancelled.Token };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => authorization.AuthorizeAsync(TrustedPrincipal(), context, policy));

            Assert.Empty(state.Resources);
            Assert.Equal(0, state.LaterResourceCalls);
            Assert.Empty(state.EvaluatorCalls);
        }

        state.Reset();
        using (var cancelledByResource = new CancellationTokenSource())
        {
            state.ResourceCallback = cancelledByResource.Cancel;
            var context = new DefaultHttpContext { RequestAborted = cancelledByResource.Token };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => authorization.AuthorizeAsync(TrustedPrincipal(), context, policy));

            Assert.Single(state.Resources);
            Assert.Equal(0, state.LaterResourceCalls);
            Assert.Empty(state.EvaluatorCalls);
        }

        state.Reset();
        state.Results["READ"] = true;
        using var evaluatorProvider = BuildProvider(state);
        var evaluatorAuthorization = evaluatorProvider.GetRequiredService<IAuthorizationService>();
        using (var cancelledByEvaluator = new CancellationTokenSource())
        {
            state.EvaluatorCallback = cancelledByEvaluator.Cancel;
            var context = new DefaultHttpContext { RequestAborted = cancelledByEvaluator.Token };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => evaluatorAuthorization.AuthorizeAsync(TrustedPrincipal(), context, policy));

            Assert.Equal(["READ"], state.EvaluatorCalls);
        }
    }

    [Fact]
    public async Task DirectAuthorizationServicePropagatesExplicitCallerCancellationToResourceSources()
    {
        var probe = new CancellationProbe();
        using var provider = BuildProvider(new(), services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<IPermissionResourceHandler, DelayedResourceHandler>();
        });
        var authorization = provider.GetRequiredService<IPermissionAuthorizationService>();
        using var cancellation = new CancellationTokenSource();
        var operation = authorization.AuthorizeAsync(
            new PermissionEvaluationContext(TrustedPrincipal(), "read"),
            cancellation.Token).AsTask();

        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    private static ServiceProvider BuildProvider(EvaluationState state, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "trusted" });
        services.ReplacePermissionEvaluator<RecordingPermissionEvaluator>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal TrustedPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(IdentityClaimTypes.Normalized, "v1"),
        new Claim(IdentityClaimTypes.TenantId, "tenant-claim")
    ], "trusted"));

    private sealed record DomainResource(string TenantId);

    private sealed class EvaluationState
    {
        public Dictionary<string, bool> Results { get; } = new(StringComparer.Ordinal);
        public List<string> EvaluatorCalls { get; } = [];
        public List<object?> Resources { get; } = [];
        public List<CancellationToken> MethodTokens { get; } = [];
        public List<CancellationToken> ContextTokens { get; } = [];
        public List<string?> TenantIds { get; } = [];
        public Exception? EvaluatorException { get; set; }
        public int LaterResourceCalls { get; set; }
        public bool ThrowOnCancellation { get; set; }
        public Action? ResourceCallback { get; set; }
        public Action? EvaluatorCallback { get; set; }

        public void Reset()
        {
            Results.Clear();
            EvaluatorCalls.Clear();
            Resources.Clear();
            MethodTokens.Clear();
            ContextTokens.Clear();
            TenantIds.Clear();
            EvaluatorException = null;
            LaterResourceCalls = 0;
            ResourceCallback = null;
            EvaluatorCallback = null;
        }
    }

    private sealed class RecordingPermissionEvaluator(EvaluationState state) : IPermissionEvaluator
    {
        public ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            state.EvaluatorCalls.Add(context.Permission);
            state.EvaluatorCallback?.Invoke();
            if (state.EvaluatorException is not null)
                throw state.EvaluatorException;

            return ValueTask.FromResult(state.Results.GetValueOrDefault(context.Permission)
                ? PermissionEvaluationResult.Success
                : PermissionEvaluationResult.Denied());
        }
    }

    private sealed class ConditionalGrantResourceHandler : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);
    }

    private sealed class ConditionalDenyResourceHandler : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PermissionEvaluationResult?>(context.Permission == "READ"
                ? PermissionEvaluationResult.Denied()
                : null);
    }

    private sealed class ThrowingResourceHandler : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default) =>
            throw new TimeoutException("resource timed out");
    }

    private sealed class LaterGrantResourceHandler(EvaluationState state) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            state.LaterResourceCalls++;
            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);
        }
    }

    private sealed class CancellationRecordingResourceHandler(EvaluationState state) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            state.Resources.Add(context.Resource);
            state.MethodTokens.Add(cancellationToken);
            state.ContextTokens.Add(context.CancellationToken);
            state.TenantIds.Add(context.TenantId);
            state.ResourceCallback?.Invoke();
            if (state.ThrowOnCancellation)
                cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);
        }
    }

    private sealed class CancellationProbe
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DelayedResourceHandler(CancellationProbe probe) : IPermissionResourceHandler
    {
        public async ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            probe.Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
