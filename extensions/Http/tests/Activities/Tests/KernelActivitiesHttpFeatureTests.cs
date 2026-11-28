using CShells.Features;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Middleware;
using Elsa.Api.AspNetCore;
using Elsa.Http.Core.Options;
using Elsa.Activities.Http.Options;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="KernelActivitiesHttpFeature"/>. The HTTP
/// activities are transiently activated from their compiled CLR contracts, so the feature registers no per-type
/// activity instances; it does own the outbound transport (the named client), contributes the
/// <see cref="HttpEndpoint"/> start trigger's stimulus provider, and registers the supported ingress middleware.
/// </summary>
public sealed class KernelActivitiesHttpFeatureTests
{
    [Fact]
    public void ConfigureServices_AppliesTheFeatureRequestSettingsToEndpointMiddlewareOptions()
    {
        var services = new ServiceCollection();

        new KernelActivitiesHttpFeature { BasePath = "/workflows", SyncRequestTimeout = TimeSpan.FromSeconds(5) }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.Equal("/workflows", provider.GetRequiredService<IOptions<HttpEndpointOptions>>().Value.BasePath);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<IOptions<HttpEndpointOptions>>().Value.SyncRequestTimeout);
        Assert.Equal("/workflows", provider.GetRequiredService<IOptions<HttpRoutePublicationOptions>>().Value.BasePath);
    }

    [Fact]
    public void ConfiguredClient_UsesTheWellKnownClientName()
    {
        var services = new ServiceCollection();
        new KernelActivitiesHttpFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpActivityConstants.HttpClientName);

        // The activity disables the ambient client timeout and enforces its own via a linked token source.
        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void Kernel_feature_retains_shared_options_and_excludes_legacy_middleware()
    {
        var services = new ServiceCollection();
        var feature = new KernelActivitiesHttpFeature { BasePath = "/custom", SyncRequestTimeout = TimeSpan.FromSeconds(12) };

        feature.ConfigureServices(services);
        feature.PostConfigureServices(services);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(HttpTestRunIngressMiddleware));
        services.AddSingleton<ITestRunTargetStore, UnusedTestRunTargetStore>();
        services.AddSingleton<IWorkflowTestScopeStore, UnusedTestScopeStore>();
        services.AddSingleton<ITestRunTargetDispatcher, UnusedTestRunTargetDispatcher>();
        services.AddSingleton<IWorkflowExecutionPartitionAccessor, TestPartitionAccessor>();
        feature.PostConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<WorkflowHttpEndpointSettings>();
        Assert.Equal("/custom", settings.BasePath);
        Assert.Equal(TimeSpan.FromSeconds(12), settings.SyncRequestTimeout);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(HttpTestRunIngressMiddleware));

    }

    [Fact]
    public void DeclaresExpectedShellFeatureMetadata()
    {
        var attribute = Assert.Single(
            typeof(KernelActivitiesHttpFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("KernelActivitiesHttp", attribute.Name);
    }

    [Fact]
    public void DependsOn_IncludesStandaloneHttpRouteProjection()
    {
        var attribute = Assert.Single(
            typeof(KernelActivitiesHttpFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Contains("WorkflowsRuntimeStandaloneHttp", attribute.DependsOn.Cast<object>().Select(d => d.ToString()));
    }

    private sealed class UnusedTestRunTargetStore : ITestRunTargetStore
    {
        public ValueTask<TestRunTarget> SaveAsync(TestRunTarget target, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTarget?> FindAsync(string testRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTargetAdmission> BeginDispatchAsync(string testRunId, string stimulusDigest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTarget?> CompleteDispatchAsync(string testRunId, TestRunTargetDispatchResult result, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTarget?> MarkStoppedAsync(string testRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTarget?> RemoveAsync(string testRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedTestScopeStore : IWorkflowTestScopeStore
    {
        public ValueTask<WorkflowTestScopeRecord> CreateAsync(WorkflowTestScope scope, DateTimeOffset createdAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeRecord?> FindAsync(string scopeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeCloseResult> CloseAsync(WorkflowTestScopeCloseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeRecord> CompleteAsync(string scopeId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopePage> QueryAsync(WorkflowTestScopePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedTestRunTargetDispatcher : ITestRunTargetDispatcher
    {
        public ValueTask<TestRunTargetDispatchResult> DispatchAsync(string testRunId, System.Text.Json.JsonElement? stimulusInput, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestPartitionAccessor : IWorkflowExecutionPartitionAccessor
    {
        public WorkflowExecutionPartition Current { get; } = new("test-partition");
    }
}
