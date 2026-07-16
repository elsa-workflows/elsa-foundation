using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryFeatureTests
{
    [Fact]
    public void RegistersResolvableDiagnosticsServices()
    {
        var services = new ServiceCollection();

        new OpenTelemetryFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryStore>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryLiveFeed>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryIngestor>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryProvider>());
        Assert.NotNull(provider.GetRequiredService<ICollectorConfigurationProvider>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetrySourceRegistry>());
        Assert.NotNull(provider.GetRequiredService<IOpenTelemetryRedactor>());
        Assert.IsType<DefaultOtlpRequestAuthenticator>(provider.GetRequiredService<IOtlpRequestAuthenticator>());
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOtlpRequestAuthenticator)).Lifetime);
        Assert.NotNull(provider.GetRequiredService<OpenTelemetryStreamItemSerializer>());
        Assert.NotNull(provider.GetRequiredService<OpenTelemetrySseFormatter>());
        Assert.NotNull(provider.GetRequiredService<OpenTelemetrySseStreamWriter>());
        Assert.NotNull(provider.GetRequiredService<OpenTelemetryTraceFilterBinder>());
        Assert.NotNull(provider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>().Value);
    }

    [Fact]
    public void AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();

        new OpenTelemetryFeature
        {
            TraceCapacity = 123,
            ApiKey = "secret",
            AllowUnauthenticatedLoopback = false
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>().Value;

        Assert.Equal(123, options.TraceCapacity);
        Assert.Equal("secret", options.ApiKey);
        Assert.False(options.AllowUnauthenticatedLoopback);
    }

    [Fact]
    public void ContributorRegistrationsAreAdditiveAndDuplicateSafe()
    {
        var services = new ServiceCollection();

        services
            .AddOpenTelemetryIngestionContributor<FirstContributor>()
            .AddOpenTelemetryIngestionContributor<SecondContributor>()
            .AddOpenTelemetryIngestionContributor<FirstContributor>();
        new OpenTelemetryFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var contributors = provider.GetServices<IOpenTelemetryIngestionContributor>().ToArray();

        Assert.Collection(
            contributors,
            contributor => Assert.IsType<FirstContributor>(contributor),
            contributor => Assert.IsType<SecondContributor>(contributor));
    }

    [Fact]
    public void HostCanReplaceDefaultRequestAuthenticator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOtlpRequestAuthenticator, HostRequestAuthenticator>();

        new OpenTelemetryFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<HostRequestAuthenticator>(provider.GetRequiredService<IOtlpRequestAuthenticator>());
    }

    private sealed class FirstContributor : IOpenTelemetryIngestionContributor
    {
        public ValueTask ContributeAsync(
            Core.Models.OpenTelemetryBatch redactedBatch,
            Core.Models.OpenTelemetryIngestionContext ingestionContext,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class SecondContributor : IOpenTelemetryIngestionContributor
    {
        public ValueTask ContributeAsync(
            Core.Models.OpenTelemetryBatch redactedBatch,
            Core.Models.OpenTelemetryIngestionContext ingestionContext,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class HostRequestAuthenticator : IOtlpRequestAuthenticator
    {
        public ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(
            Microsoft.AspNetCore.Http.HttpContext httpContext,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OtlpRequestAuthenticationResult.Rejected);
    }
}
