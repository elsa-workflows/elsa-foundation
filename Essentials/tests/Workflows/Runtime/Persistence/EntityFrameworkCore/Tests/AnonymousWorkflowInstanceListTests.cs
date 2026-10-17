using System.Net;
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
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Retest of elsa-workflows/elsa-foundation#1409 on the current EF-only persistence family.
/// The original report saw <c>GET /runtime/workflows/instances</c> return an empty list on an
/// identity-free / AllowAnonymous engine even though runs were persisted; Groundwork's
/// authority-partition physical route was blamed. Groundwork is gone; this host is the smallest
/// remaining equivalent: Runtime API + EF Core SQLite, no identity provider, Studio's list calls.
/// </summary>
public sealed class AnonymousWorkflowInstanceListTests : IDisposable
{
    private const string ShellName = "anonymous-instance-list";
    private const string ResourceName = "AnonymousList";
    private const string RecoverySigningKey = "anonymous-list-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "anonymous-list-hierarchy-signing-key-32-bytes";
    private const string AuthenticationSchemeName = "AnonymousListTest";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-anonymous-list-{Guid.NewGuid():N}.db");
    private readonly ITestOutputHelper output;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AnonymousWorkflowInstanceListTests(ITestOutputHelper output) => this.output = output;

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

    [Fact]
    public async Task Identity_free_host_persists_runs_and_does_not_hide_them_behind_an_authority_partition_filter()
    {
        await using var host = await StartHostAsync(composeIdentityAbstractions: false);
        var evidence = await SeedRunsAsync(host, listServiceAvailable: false);

        Assert.Equal(2, evidence.PersistedRows.Count);
        Assert.Equal(evidence.ExecutionIds.OrderBy(id => id, StringComparer.Ordinal),
            evidence.StorePageIds.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Contains(evidence.PersistedRows, row =>
            row.Status is (int)WorkflowExecutionStatus.Running or (int)WorkflowExecutionStatus.Suspended);
        Assert.Contains(evidence.PersistedRows, row => row.Status == (int)WorkflowExecutionStatus.Completed);

        WriteDatabaseEvidence(evidence);
        await AssertIdentityFreeListIsNotSilentEmptyAsync(host, "/runtime/workflows/instances", evidence.ExecutionIds);
        await AssertIdentityFreeListIsNotSilentEmptyAsync(host, "/runtime/workflows/instances/page?take=25", evidence.ExecutionIds);
    }

    [Fact]
    public async Task Anonymous_identity_abstractions_host_persists_runs_and_does_not_return_a_silent_empty_list()
    {
        await using var host = await StartHostAsync(composeIdentityAbstractions: true);
        var evidence = await SeedRunsAsync(host, listServiceAvailable: true);

        Assert.Equal(2, evidence.PersistedRows.Count);
        Assert.Equal(evidence.ExecutionIds.OrderBy(id => id, StringComparer.Ordinal),
            evidence.StorePageIds.OrderBy(id => id, StringComparer.Ordinal));

        using var legacy = await host.Client.GetAsync("/runtime/workflows/instances");
        using var page = await host.Client.GetAsync("/runtime/workflows/instances/page?take=25");
        var legacyBody = await legacy.Content.ReadAsStringAsync();
        var pageBody = await page.Content.ReadAsStringAsync();
        WriteHttpEvidence("anonymous GET /runtime/workflows/instances", legacy, legacyBody);
        WriteHttpEvidence("anonymous GET /runtime/workflows/instances/page", page, pageBody);
        WriteDatabaseEvidence(evidence);

        Assert.Equal(HttpStatusCode.Unauthorized, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        Assert.Empty(evidence.AnonymousServiceIds);
    }

    [Fact]
    public async Task Authenticated_caller_with_read_and_inspect_sees_the_persisted_runs()
    {
        await using var host = await StartHostAsync(composeIdentityAbstractions: true);
        var evidence = await SeedRunsAsync(host, listServiceAvailable: true);
        host.Authorize("workflow-runtime.read", HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission);

        using var legacy = await host.Client.GetAsync("/runtime/workflows/instances");
        using var page = await host.Client.GetAsync("/runtime/workflows/instances/page?take=25");
        var legacyBody = await legacy.Content.ReadAsStringAsync();
        var pageBody = await page.Content.ReadAsStringAsync();
        WriteHttpEvidence("authenticated GET /runtime/workflows/instances", legacy, legacyBody);
        WriteHttpEvidence("authenticated GET /runtime/workflows/instances/page", page, pageBody);

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var legacyItems = TryReadLegacy(legacy.StatusCode, legacyBody);
        var pageView = TryReadPage(page.StatusCode, pageBody);
        Assert.NotNull(legacyItems);
        Assert.NotNull(pageView);
        AssertContainsAll(evidence.ExecutionIds, legacyItems!.Select(item => item.WorkflowExecutionId));
        AssertContainsAll(evidence.ExecutionIds, pageView!.Items.Select(item => item.WorkflowExecutionId));
    }

    public void Dispose()
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private async Task<SeededRuns> SeedRunsAsync(AnonymousListHost host, bool listServiceAvailable)
    {
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        string suspendedId;
        string completedId;
        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var executable = RuntimeEventExecutableTestFixture.Create("anonymous-list");
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
                new WorkflowExecutableSourceReference(
                    "anonymous-list-published-reference",
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

            var start = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStartService>();
            var first = await start.ExecuteAsync(new ExecuteWorkflow(executable.Identity.ArtifactId), CancellationToken.None);
            var second = await start.ExecuteAsync(new ExecuteWorkflow(executable.Identity.ArtifactId), CancellationToken.None);
            Assert.Equal("Accepted", first.CommandDispatchStatus);
            Assert.Equal("Accepted", second.CommandDispatchStatus);
            suspendedId = first.WorkflowExecutionId;
            completedId = second.WorkflowExecutionId;

            var bookmarks = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                .ListAllBookmarkStatesAsync(completedId);
            var bookmark = Assert.Single(bookmarks);
            var resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(
                    completedId,
                    bookmark.StimulusType,
                    bookmark.StimulusHash,
                    JsonSerializer.SerializeToElement(new EventReceived("anonymous-list-ready"))));
            Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed.Status);
        }

        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            var rows = await context.WorkflowExecutionStates.AsNoTracking()
                .Select(row => new PersistedRow(row.WorkflowExecutionId, row.Status, row.TenantId, row.AuthorityPartitionKey, row.ScopeKey))
                .ToArrayAsync();
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
            var page = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(10), CancellationToken.None);
            IReadOnlyCollection<string> listedIds = [];
            if (listServiceAvailable)
            {
                var listed = await scope.ServiceProvider.GetRequiredService<IWorkflowInstanceListService>()
                    .ListAsync(new ListWorkflowInstances(null, null, null, 25), CancellationToken.None);
                listedIds = listed.Items.Select(item => item.WorkflowExecutionId).ToArray();
            }
            return new SeededRuns(
                [suspendedId, completedId],
                rows,
                page.Items.Select(item => item.WorkflowExecutionId).ToArray(),
                listedIds);
        }
    }

    private async Task<AnonymousListHost> StartHostAsync(bool composeIdentityAbstractions)
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
        if (composeIdentityAbstractions)
        {
            builder.Services.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { AuthenticationSchemeName });
            builder.Services.AddAuthentication(AuthenticationSchemeName)
                .AddScheme<AuthenticationSchemeOptions, ListAuthenticationHandler>(AuthenticationSchemeName, _ => { });
            builder.Services.AddAuthorization();
        }

        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddEfPersistenceResources(configuration, typeof(AnonymousWorkflowInstanceListTests).Assembly);
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
        if (composeIdentityAbstractions)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.MapShells();
        await app.StartAsync();
        return new AnonymousListHost(app, app.GetTestClient());
    }

    private void WriteHttpEvidence(string label, HttpResponseMessage response, string body)
    {
        output.WriteLine($"{label} -> {(int)response.StatusCode} {response.StatusCode}");
        output.WriteLine(string.IsNullOrWhiteSpace(body) ? "<empty body>" : body);
    }

    private void WriteDatabaseEvidence(SeededRuns evidence)
    {
        output.WriteLine($"Persisted execution ids: {string.Join(", ", evidence.ExecutionIds)}");
        foreach (var row in evidence.PersistedRows)
            output.WriteLine($"DB row encodedId={row.WorkflowExecutionId} status={row.Status} tenant={row.TenantId ?? "<null>"} authority={row.AuthorityPartitionKey ?? "<null>"} scope={row.ScopeKey}");
        output.WriteLine($"Store QueryPageAsync ids: {string.Join(", ", evidence.StorePageIds)}");
        output.WriteLine($"Anonymous IWorkflowInstanceListService ids: {string.Join(", ", evidence.AnonymousServiceIds)}");
    }

    private async Task AssertIdentityFreeListIsNotSilentEmptyAsync(
        AnonymousListHost host,
        string path,
        IReadOnlyCollection<string> executionIds)
    {
        // #1409's original symptom was HTTP 200 with an empty list while rows existed.
        // An identity-free host cannot serve RequirePermission routes without authorization
        // middleware; that refusal is not a silent empty success. A 200 must include the runs.
        try
        {
            using var response = await host.Client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            WriteHttpEvidence($"identity-free GET {path}", response, body);
            if (response.StatusCode != HttpStatusCode.OK)
                return;
            var items = path.Contains("/page", StringComparison.Ordinal)
                ? TryReadPage(response.StatusCode, body)?.Items
                : TryReadLegacy(response.StatusCode, body);
            Assert.NotNull(items);
            AssertContainsAll(executionIds, items!.Select(item => item.WorkflowExecutionId));
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("authorization metadata", StringComparison.Ordinal))
        {
            output.WriteLine($"identity-free GET {path} is unservable without authorization middleware: {exception.Message}");
        }
    }

    private static void AssertContainsAll(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var set = actual.ToHashSet(StringComparer.Ordinal);
        foreach (var id in expected)
            Assert.Contains(id, set);
    }

    private static IReadOnlyCollection<WorkflowInstanceSummaryView>? TryReadLegacy(HttpStatusCode status, string body) =>
        status == HttpStatusCode.OK ? JsonSerializer.Deserialize<IReadOnlyCollection<WorkflowInstanceSummaryView>>(body, Json) : null;

    private static WorkflowInstanceListView? TryReadPage(HttpStatusCode status, string body) =>
        status == HttpStatusCode.OK ? JsonSerializer.Deserialize<WorkflowInstanceListView>(body, Json) : null;

    private sealed class AnonymousListHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public IServiceProvider Services { get; } = app.Services;
        public HttpClient Client { get; } = client;

        public void Authorize(params string[] permissions)
        {
            Client.DefaultRequestHeaders.Remove("X-List-Permission");
            Client.DefaultRequestHeaders.Add("X-List-Permission", string.Join(' ', permissions));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed record PersistedRow(string WorkflowExecutionId, int Status, string? TenantId, string? AuthorityPartitionKey, string ScopeKey);

    private sealed record SeededRuns(
        IReadOnlyCollection<string> ExecutionIds,
        IReadOnlyCollection<PersistedRow> PersistedRows,
        IReadOnlyCollection<string> StorePageIds,
        IReadOnlyCollection<string> AnonymousServiceIds);

    private sealed class ListAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-List-Permission", out var permission) || string.IsNullOrWhiteSpace(permission))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "anonymous-list-operator"),
                new(IdentityClaimTypes.Normalized, "v1")
            };
            claims.AddRange(permission.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => new Claim(IdentityClaimTypes.Permission, value)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)),
                Scheme.Name)));
        }
    }
}
