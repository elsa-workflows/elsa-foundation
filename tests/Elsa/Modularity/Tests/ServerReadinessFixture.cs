using System.Net;
using System.Net.Http.Json;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Server.Readiness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Modularity.Tests;

public sealed class ServerReadinessFixture : IAsyncDisposable
{
    public const string DefaultShellName = "default";
    public const string OtherShellName = "other";
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    private readonly WebApplication _app;

    private ServerReadinessFixture(
        WebApplication app,
        HttpClient client,
        RouteInitializationControl routeInitialization,
        ShellReadinessState readinessState,
        RecordingLogger<DefaultShellWarmup> warmupLogger)
    {
        _app = app;
        Client = client;
        RouteInitialization = routeInitialization;
        ReadinessState = readinessState;
        WarmupLogger = warmupLogger;
        Registry = app.Services.GetRequiredService<IShellRegistry>();
    }

    public HttpClient Client { get; }
    public RouteInitializationControl RouteInitialization { get; }
    public ShellReadinessState ReadinessState { get; }
    public RecordingLogger<DefaultShellWarmup> WarmupLogger { get; }
    public IShellRegistry Registry { get; }

    public static async Task<ServerReadinessFixture> StartAsync(
        bool warmDefaultShell = true,
        string defaultShellName = DefaultShellName)
    {
        var routeInitialization = new RouteInitializationControl();
        var readinessState = new ShellReadinessState(TimeProvider.System);
        var readinessOptions = new ShellReadinessOptions
        {
            WarmDefaultShell = warmDefaultShell,
            DefaultShellName = defaultShellName
        };
        var warmupLogger = new RecordingLogger<DefaultShellWarmup>();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(readinessState);
        builder.Services.AddSingleton<IOptions<ShellReadinessOptions>>(Options.Create(readinessOptions));
        builder.Services.AddSingleton<ILogger<DefaultShellWarmup>>(warmupLogger);
        builder.Services.AddSingleton<DefaultShellWarmup>();
        builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<DefaultShellWarmup>());
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(ReadinessProbeFeature).Assembly)
            .WithWebRouting(options =>
            {
                options.EnablePathRouting = true;
                options.EnableHostRouting = false;
                options.ExcludePaths = [LivePath, ReadyPath];
            })
            .AddShell(DefaultShellName, shell => shell.WithFeature<ReadinessProbeFeature>(feature =>
            {
                feature.ControlId = routeInitialization.Id;
                feature.ShellName = DefaultShellName;
            }))
            .AddShell(OtherShellName, shell => shell
                .WithPath(OtherShellName)
                .WithFeature<ReadinessProbeFeature>(feature =>
                {
                    feature.ControlId = routeInitialization.Id;
                    feature.ShellName = OtherShellName;
                })));

        var app = builder.Build();
        MapHealthEndpoints(app);
        app.MapShells();
        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        return new ServerReadinessFixture(app, client, routeInitialization, readinessState, warmupLogger);
    }

    public async Task<HealthResponse> ReadLiveAsync() =>
        (await Client.GetFromJsonAsync<HealthResponse>(LivePath))!;

    public async Task<(HttpStatusCode StatusCode, HealthResponse Body)> ReadReadyAsync()
    {
        using var response = await Client.GetAsync(ReadyPath);
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<HealthResponse>())!);
    }

    public async Task WaitUntilReadyAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var response = await ReadReadyAsync();
            if (response.StatusCode == HttpStatusCode.OK)
                return;

            await Task.Delay(10, timeout.Token);
        }
    }

    public async Task WaitForStatusAsync(ShellReadinessStatus status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (ReadinessState.Snapshot.Status != status)
            await Task.Delay(10, timeout.Token);
    }

    public async Task WaitForDefaultRouteInitializationAsync()
    {
        var gate = RouteInitialization.For(DefaultShellName);
        var entered = gate.WaitUntilEnteredAsync();
        var failed = WaitForStatusAsync(ShellReadinessStatus.Failed);
        var completed = await Task.WhenAny(entered, failed);
        if (completed == failed)
            throw new InvalidOperationException($"Default shell activation failed before route initialization: {WarmupLogger.Exception}");
        await entered;
    }

    public async ValueTask DisposeAsync()
    {
        RouteInitialization.ReleaseAll();
        RouteInitialization.Dispose();
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        app.MapGet(LivePath, () => Results.Ok(new HealthResponse("live")));
        app.MapGet(ReadyPath, (IShellRegistry registry, ShellReadinessState state, IOptions<ShellReadinessOptions> options) =>
        {
            var active = registry.GetActive(options.Value.DefaultShellName);
            if (active?.State == ShellLifecycleState.Active)
            {
                var snapshot = state.Snapshot;
                return Results.Json(
                    new HealthResponse(
                        "ready",
                        options.Value.DefaultShellName,
                        active.Descriptor.Generation,
                        snapshot.Duration?.TotalMilliseconds),
                    statusCode: StatusCodes.Status200OK);
            }

            var unavailable = state.Snapshot;
            return Results.Json(
                new HealthResponse(
                    unavailable.Status.ToString().ToLowerInvariant(),
                    options.Value.DefaultShellName,
                    Code: unavailable.Code),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }

    public sealed record HealthResponse(
        string Status,
        string? Shell = null,
        int? Generation = null,
        double? DurationMs = null,
        string? Code = null);

    public sealed class RecordingLogger<T> : ILogger<T>
    {
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
                Exception = exception;
        }
    }

    public sealed class RouteInitializationControl : IDisposable
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RouteInitializationControl> Instances = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Gate> _gates = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _syncRoot = new();

        public RouteInitializationControl()
        {
            Id = Guid.NewGuid().ToString("n");
            Instances[Id] = this;
        }

        public string Id { get; }

        public static RouteInitializationControl Resolve(string id) =>
            Instances.TryGetValue(id, out var control)
                ? control
                : throw new InvalidOperationException($"No readiness test control is registered for '{id}'.");

        public Gate For(string shellName)
        {
            lock (_syncRoot)
                return _gates.TryGetValue(shellName, out var gate)
                    ? gate
                    : _gates[shellName] = new Gate();
        }

        public void ReleaseAll()
        {
            lock (_syncRoot)
                foreach (var gate in _gates.Values)
                    gate.Release();
        }

        public void Dispose() => Instances.TryRemove(Id, out _);

        public sealed class Gate
        {
            private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _attempts;

            public int Attempts => Volatile.Read(ref _attempts);
            public bool Initialized { get; private set; }
            public Exception? Failure { get; set; }

            public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            public void Release() => _release.TrySetResult();

            public async Task InitializeAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attempts);
                _entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
                if (Failure is not null)
                    throw Failure;
                Initialized = true;
            }
        }
    }

    [ShellFeature(
        name: "ReadinessProbe",
        DisplayName = "Readiness Probe",
        Description = "Test-only controlled route-initialization boundary.")]
    public sealed class ReadinessProbeFeature : IShellFeature
    {
        public string ControlId { get; set; } = null!;
        public string ShellName { get; set; } = null!;

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IShellInitializer>(new ControlledRouteInitialization(ShellName, ControlId));
        }
    }

    public sealed class ControlledRouteInitialization(
        string shellName,
        string controlId) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken) =>
            RouteInitializationControl.Resolve(controlId).For(shellName).InitializeAsync(cancellationToken);
    }
}
