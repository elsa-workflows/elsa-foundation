using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
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
    private const string HoldPath = "/feed-module-fixture/startup-control/hold/{operationId}";
    public const string StatusPath = "/feed-module-fixture/startup-control/status";

    public void ConfigureServices(IServiceCollection services) => services.AddSingleton<FeedModuleStartupControl>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
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

        endpoints.MapGet(StatusPath, (IShell shell) => Results.Ok(new { generation = shell.Descriptor.Generation, fixtureVersion = FeedModule.CurrentVersion }));

        endpoints.MapPost(HoldPath, async (HttpContext context, string operationId, IShell shell, IShellRegistry registry, FeedModuleStartupControl control) =>
        {
            if (!control.IsGenerationHoldEnabled)
                return Results.NotFound();

            var generation = shell.Descriptor.Generation;
            context.Response.OnCompleted(() =>
            {
                control.Start(registry, shell, operationId, generation);
                return Task.CompletedTask;
            });

            control.MarkEntered(operationId, generation);
            await control.WaitForReleaseAsync(operationId, context.RequestAborted).ConfigureAwait(false);
            return Results.Ok(new { generation });
        });
    }
}

/// <summary>Retains and reports the one bounded drain operation scheduled by the fixture control route.</summary>
public sealed class FeedModuleStartupControl
{
    private const string Marker = "FEED_MODULE_STARTUP_CONTROL";
    private const string GenerationMarker = "FEED_MODULE_GENERATION_HOLD";
    private static readonly TimeSpan GenerationHoldTimeout = TimeSpan.FromSeconds(90);
    private readonly object _gate = new();
    private readonly string? _generationHoldDirectory;
    private Task? _operation;

    public FeedModuleStartupControl(IConfiguration configuration)
    {
        _generationHoldDirectory = configuration["Elsa:Cluster:ReadabilityControl:Directory"];
        if (_generationHoldDirectory is not null)
            Directory.CreateDirectory(_generationHoldDirectory);
    }

    public bool IsGenerationHoldEnabled => _generationHoldDirectory is not null;

    public void MarkEntered(string operationId, int generation)
    {
        var path = MarkerPath(operationId, "entered");
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.Move(temporaryPath, path);
        Console.WriteLine($"{GenerationMarker}|{operationId}|entered|{generation}");
    }

    public Task WaitForReleaseAsync(string operationId, CancellationToken cancellationToken) =>
        WaitForFileAsync(MarkerPath(operationId, "release"), GenerationHoldTimeout, cancellationToken);

    public void Start(IShellRegistry registry, IShell shell, string operationId)
        => Start(registry, shell, operationId, shell.Descriptor.Generation);

    public void Start(IShellRegistry registry, IShell shell, string operationId, int generation)
    {
        lock (_gate)
        {
            if (_operation is null)
            {
                _operation = RunAsync(registry, shell, operationId, generation);
                return;
            }

            Console.WriteLine($"{Marker}|{operationId}|failed|operation-already-started");
        }
    }

    private async Task RunAsync(IShellRegistry registry, IShell shell, string operationId, int generation)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var drain = await registry.DrainAsync(shell, timeout.Token)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            await drain.WaitAsync(timeout.Token).ConfigureAwait(false);
            Console.WriteLine($"{Marker}|{operationId}|completed|{shell.State}");
            if (_generationHoldDirectory is not null)
                Console.WriteLine($"{GenerationMarker}|{operationId}|disposed|{generation}|{shell.State}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{Marker}|{operationId}|failed|{exception.GetType().Name}");
            if (_generationHoldDirectory is not null)
                Console.WriteLine($"{GenerationMarker}|{operationId}|failed|{generation}|{exception.GetType().Name}");
        }
    }

    private string MarkerPath(string operationId, string marker) =>
        Path.Join(_generationHoldDirectory ?? throw new InvalidOperationException("The generation hold is not enabled."), $"{operationId}.{marker}");

    private static async Task WaitForFileAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
            return;

        var directory = Path.GetDirectoryName(path)!;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
        };
        FileSystemEventHandler signal = (_, _) => completion.TrySetResult();
        RenamedEventHandler signalRename = (_, _) => completion.TrySetResult();
        watcher.Created += signal;
        watcher.Changed += signal;
        watcher.Renamed += signalRename;
        watcher.EnableRaisingEvents = true;

        if (File.Exists(path))
            return;

        await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }
}
