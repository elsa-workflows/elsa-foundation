using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>A test-only control route for observing a settled Foundation shell's later drain in a child process.</summary>
[ShellFeature(
    name: FeedModuleStartupControlFeature.FeatureName,
    DisplayName = "Feed module fixture startup control",
    DependsOn = new object[] { FeedModule.EntityFrameworkCoreFeature })]
public sealed class FeedModuleStartupControlFeature : IWebShellFeature
{
    public const string FeatureName = "FeedModuleFixtureStartupControl";
    private const string DrainPath = "/feed-module-fixture/startup-control/drain/{operationId}";

    public void ConfigureServices(IServiceCollection services) => services.AddSingleton<FeedModuleStartupControl>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        endpoints.MapPost(DrainPath, (HttpContext context, string operationId, IShellRegistry registry, FeedModuleStartupControl control) =>
        {
            var shell = registry.GetActive("default");
            if (shell is null)
                return Results.Conflict();

            // ShellMiddleware releases this request's tracked shell scope from its own OnCompleted callback. Kestrel invokes
            // callbacks in reverse registration order, so this callback must only schedule the tracked drain and return; awaiting
            // it here would make the drain wait on this request's scope while the response completion waits on the drain.
            context.Response.OnCompleted(() =>
            {
                control.Start(registry, shell, operationId);
                return Task.CompletedTask;
            });

            return Results.Accepted();
        });
}

/// <summary>Retains and reports the one bounded drain operation scheduled by the fixture control route.</summary>
public sealed class FeedModuleStartupControl
{
    private const string Marker = "FEED_MODULE_STARTUP_CONTROL";
    private readonly object _gate = new();
    private Task? _operation;

    public void Start(IShellRegistry registry, IShell shell, string operationId)
    {
        lock (_gate)
        {
            if (_operation is null)
            {
                _operation = RunAsync(registry, shell, operationId);
                return;
            }

            Console.WriteLine($"{Marker}|{operationId}|failed|operation-already-started");
        }
    }

    private static async Task RunAsync(IShellRegistry registry, IShell shell, string operationId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var drain = await registry.DrainAsync(shell, timeout.Token)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            await drain.WaitAsync(timeout.Token).ConfigureAwait(false);
            Console.WriteLine($"{Marker}|{operationId}|completed|{shell.State}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{Marker}|{operationId}|failed|{exception.GetType().Name}");
        }
    }
}
