using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Minimal API test host with injectable compiler and sender seams for HTTP behavior tests that need
/// deterministic failures from services not exposed by <see cref="PublishingMinimalApiHost"/>.
/// </summary>
internal sealed class PublishingMinimalApiScenarioHost(WebApplication app) : IAsyncDisposable
{
    public HttpClient Client { get; } = app.GetTestClient();

    public static async Task<PublishingMinimalApiScenarioHost> StartAsync(
        Func<IServiceProvider, IRequestSender>? requestSenderFactory = null,
        IWorkflowExecutableCompiler? compiler = null,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? configurePipeline = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "elsa-workflows-publishing-after-migration-scenario"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddWorkflowRuntime();
        builder.Services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        builder.Services.AddAuthentication(CaptureAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CaptureAuthenticationHandler>(CaptureAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>([CaptureAuthenticationHandler.SchemeName], StringComparer.Ordinal));
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<IWorkflowTriggerBindingExtractor>(new WorkflowTriggerBindingExtractor([]));
        builder.Services.AddSingleton<IWorkflowExecutableCompiler>(compiler ?? new CaptureWorkflowExecutableCompiler());
        new WorkflowsPublishingFeature().ConfigureServices(builder.Services);
        new WorkflowsPublishingApiFeature().ConfigureServices(builder.Services);
        PublishingDomainSeams.Register(builder.Services);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider, CaptureTimeProvider>();
        if (requestSenderFactory is null)
            builder.Services.AddSingleton<IRequestSender, CaptureRequestSender>();
        else
            builder.Services.AddSingleton<IRequestSender>(services => requestSenderFactory(services));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        _ = app.Services.GetRequiredService<IRequestSender>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        configurePipeline?.Invoke(app);
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(app);
        app.MapOpenApi();
        await app.StartAsync();

        var slotStore = app.Services.GetRequiredService<IPublicationSlotStore>();
        await slotStore.TryActivateAsync(
            "definition-route",
            "default",
            "publication-capture",
            expectedRevision: 0,
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        return new PublishingMinimalApiScenarioHost(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

internal sealed class PublishingTestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
{
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public ICollection<EndpointDataSource> DataSources { get; } = [];
    public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
}
