using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CShells;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Locking.Core;
using Elsa.Mediator;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Exercises the proposed Worker selection through its mapped, permission-protected HTTP routes and the EF runtime
/// store. The selected feature IDs are kept separate from CShells' dependency closure.
/// </summary>
public sealed class WorkerHttpFixtureHostEvidenceTests : IDisposable
{
    private const string ShellName = "worker-runtime";
    private const string ResourceName = "Worker";
    private const string RecoverySigningKey = "worker-runtime-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "worker-runtime-hierarchy-signing-key-32-bytes";
    private const string AuthenticationSchemeName = "WorkerRuntimeTest";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-worker-fixture-{Guid.NewGuid():N}.db");
    private readonly ITestOutputHelper output;

    public WorkerHttpFixtureHostEvidenceTests(ITestOutputHelper output) => this.output = output;

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    private static readonly string[] SelectedFeatureIds =
    [
        "Primitives",
        "Serialization",
        "Mediator",
        "Events",
        "Expressions",
        "ActivitiesRuntime",
        "ActivitiesPrimitives",
        "ActivitiesControlFlow",
        "ActivitiesSequence",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers",
        "ApiCapabilities",
        "WorkflowsRuntimeApi"
    ];

    private static readonly string[] DependencyFeatureIds = ["Tasks"];

    [Fact]
    public async Task Proposed_worker_selection_executes_and_resumes_over_authenticated_http_with_named_ef_resource()
    {
        await using var host = await StartHostAsync();
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var mountedRoutes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
        var runtimeRoutes = mountedRoutes
            .Where(route => route?.StartsWith("runtime/", StringComparison.Ordinal) == true)
            .Select(route => route!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();
        output.WriteLine($"Mounted Runtime route patterns ({runtimeRoutes.Length}):");
        foreach (var route in runtimeRoutes)
            output.WriteLine($"  /{route}");
        Assert.Contains("runtime/workflows/executables/{artifactId}/execute", mountedRoutes);
        Assert.Contains("runtime/workflows/stimuli", mountedRoutes);
        Assert.Contains("/capabilities", mountedRoutes);
        Assert.DoesNotContain(mountedRoutes, route => route is not null && route.TrimStart('/').StartsWith("design/", StringComparison.Ordinal));
        Assert.DoesNotContain(mountedRoutes, route => route is not null && route.TrimStart('/').StartsWith("publishing/", StringComparison.Ordinal));
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
        Assert.Equal(
            SelectedFeatureIds.Concat(DependencyFeatureIds).OrderBy(id => id, StringComparer.Ordinal),
            settings.EnabledFeatures.OrderBy(id => id, StringComparer.Ordinal));

        string workflowExecutionId;
        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<RuntimeWorkflowExecutionEntityFrameworkCoreOptions>();
            Assert.Equal("Sqlite", options.Provider);
            Assert.Equal(ResourceName, options.ConnectionName);
            Assert.Null(options.ConnectionString);

            var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            Assert.Equal(databasePath, context.Database.GetDbConnection().DataSource);
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            var executable = RuntimeEventExecutableTestFixture.Create("worker");
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
                new WorkflowExecutableSourceReference(
                    "worker-published-reference",
                    executable.Identity.ArtifactId,
                    "WorkflowDefinitionVersion",
                    executable.Identity.DefinitionId,
                    executable.Identity.ArtifactVersion,
                    executable.Identity.DefinitionId,
                    executable.Identity.DefinitionVersionId,
                    executable.Identity.ArtifactVersion,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    WorkflowExecutableReferenceScope.Published));
        }

        using var anonymousResponse = await host.Client.PostAsJsonAsync(
            "/runtime/workflows/executables/worker-event-artifact/execute",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        host.Client.DefaultRequestHeaders.Add("X-Worker-Permission", "workflow-runtime.read");
        using var forbiddenResponse = await host.Client.PostAsJsonAsync(
            "/runtime/workflows/executables/worker-event-artifact/execute",
            new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Worker-Permission");
        host.Client.DefaultRequestHeaders.Add("X-Worker-Permission", "workflow-runtime.execute");

        using var executeResponse = await host.Client.PostAsJsonAsync(
            "/runtime/workflows/executables/worker-event-artifact/execute",
            new { });
        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
        var started = await executeResponse.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>();
        Assert.NotNull(started);
        Assert.Equal("Accepted", started.CommandDispatchStatus);
        workflowExecutionId = started.WorkflowExecutionId;

        BookmarkState bookmark;
        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var bookmarks = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                .ListAllBookmarkStatesAsync(workflowExecutionId);
            bookmark = Assert.Single(bookmarks);
            Assert.Equal(ActivityExecutionStatus.Suspended, Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(workflowExecutionId)).Status);
            Assert.IsType<EfWorkflowExecutionStateStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>());
            Assert.IsType<EfBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
        }

        using var stimulusResponse = await host.Client.PostAsJsonAsync(
            "/runtime/workflows/stimuli",
            new DispatchStimulus(
                bookmark.StimulusType,
                bookmark.StimulusHash,
                JsonSerializer.SerializeToElement(new EventReceived("worker-ready")),
                Mode: "ResumeOnly"));
        Assert.Equal(HttpStatusCode.OK, stimulusResponse.StatusCode);
        var stimulus = await stimulusResponse.Content.ReadFromJsonAsync<DispatchStimulusResponse>();
        Assert.NotNull(stimulus);
        Assert.Equal(0, stimulus.StartedCount);
        Assert.Equal(1, stimulus.ResumedCount);
        Assert.Equal(workflowExecutionId, Assert.Single(stimulus.Resumes).WorkflowExecutionId);

        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var bookmarks = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
            Assert.Empty(await bookmarks.ListAllBookmarkStatesAsync(workflowExecutionId));
            Assert.Equal(ActivityExecutionStatus.Completed, Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(workflowExecutionId)).Status);
            Assert.Equal(WorkflowExecutionStatus.Completed, (await scope.ServiceProvider
                .GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(workflowExecutionId))?.Status);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private async Task<WorkerHttpHost> StartHostAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Persistence:DefaultResource"] = "primary",
                ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
                ["Elsa:Persistence:Resources:primary:ConnectionName"] = ResourceName,
                [$"ConnectionStrings:{ResourceName}"] = ConnectionString,
                [$"CShells:Shells:{ShellName}:Configuration:WebRouting:Path"] = "",
                [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] = RecoverySigningKey,
                [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey"] = HierarchySigningKey
            }.Concat(SelectedFeatureIds.Select(id => KeyValuePair.Create(
                $"CShells:Shells:{ShellName}:Features:{id}",
                (string?)null))))
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDistributedLockProvider, RuntimeEntityFrameworkCoreFeatureTests.ProcessLockProvider>();
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { AuthenticationSchemeName });
        builder.Services.AddAuthentication(AuthenticationSchemeName)
            .AddScheme<AuthenticationSchemeOptions, WorkerAuthenticationHandler>(AuthenticationSchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddEfPersistenceResources(configuration, typeof(WorkerHttpFixtureHostEvidenceTests).Assembly);
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(PrimitivesFeature).Assembly,
                typeof(SerializationFeature).Assembly,
                typeof(MediatorFeature).Assembly,
                typeof(EventsFeature).Assembly,
                typeof(ExpressionsFeature).Assembly,
                typeof(ActivitiesRuntimeFeature).Assembly,
                typeof(ActivitiesPrimitivesFeature).Assembly,
                typeof(ActivitiesControlFlowFeature).Assembly,
                typeof(ActivitiesSequenceFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly,
                typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(WorkflowsRuntimeTriggersFeature).Assembly,
                typeof(TasksFeature).Assembly)
            .WithConfigurationProvider(configuration));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapShells();
        await app.StartAsync();
        return new WorkerHttpHost(app, app.GetTestClient());
    }

    private sealed class WorkerHttpHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public IServiceProvider Services { get; } = app.Services;
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class WorkerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Worker-Permission", out var permission) || string.IsNullOrWhiteSpace(permission))
                return Task.FromResult(AuthenticateResult.NoResult());

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "worker-test-operator"),
                        new Claim(IdentityClaimTypes.Permission, permission.ToString()),
                        new Claim(IdentityClaimTypes.Normalized, "v1")
                    ],
                    Scheme.Name)),
                Scheme.Name)));
        }
    }
}
