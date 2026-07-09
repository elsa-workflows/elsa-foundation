using CShells.AspNetCore.Features;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Complements the host-level end-to-end tests (which mount the middleware directly the way a shell would): this
/// asserts the other half of that equivalence — that <see cref="ActivitiesHttpFeature"/> actually implements the
/// CShells middleware seam <see cref="IMiddlewareShellFeature"/>, and that its <c>UseMiddleware</c> mounts the
/// inbound <see cref="HttpEndpointMiddleware"/> into the shell pipeline. Together they prove the end-to-end wiring
/// matches what a real shell composition performs (spec 089 FR-003), without the weight of a full CShells host.
/// </summary>
public sealed class ActivitiesHttpFeatureMiddlewareSeamTests
{
    [Fact]
    public void Feature_ImplementsTheMiddlewareShellSeam()
    {
        Assert.IsAssignableFrom<IMiddlewareShellFeature>(new ActivitiesHttpFeature());
    }

    [Fact]
    public async Task UseMiddleware_MountsTheHttpEndpointMiddleware_IntoThePipeline()
    {
        // A DI container with just enough to resolve the IMiddleware the feature mounts. UseMiddleware<T>() for an
        // IMiddleware resolves T from RequestServices per request, so the middleware itself must be registered.
        var router = new RecordingStimulusRouter();
        var services = new ServiceCollection();
        services.AddSingleton<IMiddlewareFactory, MiddlewareFactory>();
        services.AddScoped<HttpEndpointMiddleware>();
        services.AddSingleton<IStimulusRouter>(router);
        services.AddSingleton<IOptions<HttpEndpointOptions>>(Microsoft.Extensions.Options.Options.Create(new HttpEndpointOptions()));
        // The middleware's route-table/matcher/binding-store dependencies are contributed in production by the
        // features ActivitiesHttpFeature.DependsOn (Http provides IRouteTable/IRouteMatcher; WorkflowsRuntime-
        // Triggers provides IWorkflowTriggerBindingStore). Register production-equivalent doubles and an empty
        // binding store, then seed the probe's route so the request resolves and reaches dispatch (proving the
        // mounted middleware, not the sentinel, answered).
        services.AddSingleton<Elsa.Http.Core.Contracts.IRouteMatcher, TestRouteMatcher>();
        services.AddSingleton<Elsa.Http.Core.Contracts.IRouteTable, FakeRouteTable>();
        services.AddSingleton<Elsa.Workflows.Runtime.Core.Contracts.IWorkflowTriggerBindingStore, Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowTriggerBindingStore>();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<Elsa.Http.Core.Contracts.IRouteTable>().Add("seam-probe");

        var app = new StubApplicationBuilder(provider);
        ((IMiddlewareShellFeature)new ActivitiesHttpFeature()).UseMiddleware(app, environment: null);

        // Exactly one component was mounted; build the pipeline and drive a request under the endpoint base path.
        var pipeline = app.Build();
        var context = NewContext("/workflows/http/seam-probe", "GET");
        await pipeline(context);

        // The mounted component is HttpEndpointMiddleware: it consumed the request (routed a stimulus) rather than
        // falling through to the terminal, which is the only way the router could have been invoked.
        Assert.True(router.WasInvoked);
    }

    private static DefaultHttpContext NewContext(string path, string method, IServiceProvider? requestServices = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        if (requestServices is not null)
            context.RequestServices = requestServices;
        return context;
    }

    /// <summary>Records middleware components and resolves the terminal to a no-op; supplies RequestServices so an IMiddleware resolves.</summary>
    private sealed class StubApplicationBuilder(IServiceProvider services) : IApplicationBuilder
    {
        private readonly List<Func<RequestDelegate, RequestDelegate>> _components = [];

        public IServiceProvider ApplicationServices { get; set; } = services;
        public IFeatureCollection ServerFeatures { get; } = new FeatureCollection();
        public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
        {
            _components.Add(middleware);
            return this;
        }

        public IApplicationBuilder New() => new StubApplicationBuilder(ApplicationServices);

        public RequestDelegate Build()
        {
            Assert.Single(_components);
            RequestDelegate app = context =>
            {
                // Ensure an IMiddleware can resolve itself from the request scope during the run.
                context.RequestServices = ApplicationServices;
                return Task.CompletedTask;
            };
            for (var index = _components.Count - 1; index >= 0; index--)
                app = _components[index](app);
            return context =>
            {
                context.RequestServices = ApplicationServices;
                return app(context);
            };
        }
    }
}
