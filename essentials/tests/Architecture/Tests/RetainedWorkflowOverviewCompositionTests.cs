using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Attention.Api;
using Elsa.Attention.Core;
using Elsa.Cluster.Readability;
using Elsa.Events;
using Elsa.Foundation.Hosting;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Locking.Core;
using Elsa.Modularity.Api.Attention;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane.Admin;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class RetainedWorkflowOverviewCompositionTests
{
    private const string Scheme = "RetainedOverviewTest";
    private const string Tenant = "default";
    private const string PortfolioPath = "/_elsa/workflows/dashboard/definitions";
    private const string RunHealthPath = "/_elsa/workflows/dashboard/runs?from=2026-11-27T00%3A00%3A00Z&to=2026-11-28T00%3A00%3A00Z&bucket=day&timeZone=Etc%2FUTC";
    private const string AttentionPath = "/_elsa/attention/items?contributorId=workflows.runtime";

    [Fact]
    public async Task Retained_overview_serves_the_portfolio_and_reports_unavailable_actor_metrics_without_the_runner()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-retained-overview-");
        try
        {
            await using var app = await StartAsync(directory.FullName);
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(Tenant);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var features = provider.GetRequiredService<ShellSettings>().EnabledFeatures;
            Assert.DoesNotContain("WorkflowsRuntimeApi", features);
            Assert.DoesNotContain("WorkflowsRuntimeEntityFrameworkCore", features);
            Assert.Null(provider.GetService<IWorkflowSchedulerDrainer>());
            Assert.Null(provider.GetService<IWorkflowExecutionStateStore>());
            Assert.IsType<EfWorkflowPortfolioDataSource>(provider.GetRequiredService<IWorkflowPortfolioDataSource>());
            Assert.IsType<UnavailableWorkflowRunHealthDataSource>(provider.GetRequiredService<IWorkflowRunHealthDataSource>());
            Assert.Contains(provider.GetServices<IAttentionContributor>(), contributor => contributor.Descriptor.Id == "modularity");
            var capabilities = (await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync()).Capabilities;
            var dashboard = Assert.Single(capabilities, capability => capability.Id == "elsa.api.workflow-dashboard");
            Assert.Collection(dashboard.Links,
                link => Assert.Equal(("workflow-portfolio", PortfolioPath[1..]), (link.Rel, link.Href)),
                link => Assert.Equal(("workflow-run-health", "_elsa/workflows/dashboard/runs"), (link.Rel, link.Href)));

            const string definitionId = "retained-definition";
            var design = provider.GetRequiredService<WorkflowsDesignDbContext>();
            design.Definitions.Add(new() { Id = definitionId, TenantId = Tenant, Name = "Retained definition" });
            await design.SaveChangesAsync();
            var publishedAt = DateTimeOffset.UtcNow;
            await provider.GetRequiredService<IWorkflowExecutableSourceReferenceWriter>().SaveAsync(new(
                "retained-reference", "retained-artifact", "WorkflowDefinitionVersion", "retained-version", "1",
                definitionId, "retained-version", "1", publishedAt, publishedAt, WorkflowExecutableReferenceScope.Published, TenantId: Tenant));

            using var client = app.GetTestClient();
            using var anonymous = await client.GetAsync(PortfolioPath);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            client.DefaultRequestHeaders.Add("X-Overview-Permission", "restricted");
            using var restricted = await client.GetAsync(PortfolioPath);
            Assert.Equal(HttpStatusCode.Forbidden, restricted.StatusCode);
            client.DefaultRequestHeaders.Remove("X-Overview-Permission");
            client.DefaultRequestHeaders.Add("X-Overview-Permission", "operator");

            var portfolio = await client.GetFromJsonAsync<WorkflowPortfolioSnapshot>(PortfolioPath);
            Assert.Equal(WorkflowPortfolioStatus.Ready, portfolio!.Status);
            Assert.Equal(1, portfolio.ActiveDefinitionCount);
            Assert.Equal(1, portfolio.PublishedDefinitionCount);

            var health = await client.GetFromJsonAsync<WorkflowRunHealthSnapshot>(RunHealthPath);
            Assert.Equal(WorkflowRunHealthStatus.Unavailable, health!.Status);
            Assert.Equal("WORKFLOW_RUN_HEALTH_PROVIDER_UNAVAILABLE", health.ErrorCode);

            var attention = await client.GetFromJsonAsync<AttentionAggregationResult>(AttentionPath);
            var runtime = Assert.Single(attention!.Contributors);
            Assert.Equal("workflows.runtime", runtime.ContributorId);
            Assert.Equal(AttentionContributorStatus.Unavailable, runtime.Status);
            Assert.Equal("RUNTIME_ATTENTION_QUERY_UNAVAILABLE", runtime.ErrorCode);
            Assert.Equal("/workflows/instances", runtime.DiagnosticDestination?.Path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<WebApplication> StartAsync(string directory)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = directory });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        const string features = "CShells:Shells:default:Features";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CShells:Shells:default:Name"] = Tenant,
            ["CShells:Shells:default:Configuration:WebRouting:Path"] = "",
            [$"{features}:WorkflowsDashboardRetained"] = null,
            [$"{features}:WorkflowsDashboardPortfolioEntityFrameworkCore"] = null,
            [$"{features}:WorkflowsRuntimeRetainedAttention"] = null,
            [$"{features}:ModularityAttention"] = null,
            [$"{features}:FoundationIdentityAbstractions"] = null,
            [$"{features}:Serialization"] = null,
            [$"{features}:WorkflowsDesignEntityFrameworkCore:ConnectionString"] = $"Data Source={Path.Combine(directory, "design.db")};Pooling=False",
            [$"{features}:WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence:ConnectionString"] = $"Data Source={Path.Combine(directory, "runtime.db")};Pooling=False",
            [$"{features}:WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence:RecoveryContinuationSigningKey"] = "retained-overview-fixture-signing-key"
        });
        builder.AddNuplaneHostIntegration();
        builder.Services.AddNuplaneAdmin();
        builder.Services.ShareWithShells<INuplaneAdminOperations>();
        builder.Services.AddSingleton<IDistributedLockProvider, OverviewLockProvider>();
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(WorkflowsDashboardRetainedFeature).Assembly, typeof(WorkflowsDashboardPortfolioEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeRetainedAttentionFeature).Assembly, typeof(WorkflowsRuntimeApiFeature).Assembly,
                typeof(RuntimeArtifactsEntityFrameworkCoreFeature).Assembly, typeof(RuntimeDbContext).Assembly,
                typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsDesignApiFeature).Assembly, typeof(WorkflowDesignValidationsFeature).Assembly, typeof(TasksFeature).Assembly,
                typeof(EventsFeature).Assembly, typeof(SerializationFeature).Assembly, typeof(ApiCapabilitiesFeature).Assembly,
                typeof(FoundationIdentityAbstractionsFeature).Assembly,
                typeof(AttentionApiFeature).Assembly, typeof(ModularityAttentionFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration)
            .WithWebRouting(options => options.EnablePathRouting = true)
            .WithAuthenticationAndAuthorization());
        builder.Services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, OverviewAuthentication>(Scheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.Configure<FoundationIdentityOptions>(options => options.NormalizedAuthenticationTypes = new HashSet<string> { Scheme });

        var app = builder.Build();
        try
        {
            app.MapShells();
            app.UseAuthentication();
            app.UseAuthorization();
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class OverviewAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Overview-Permission", out var access))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "overview-test"),
                new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.TenantId, Tenant)
            };
            if (access == "operator")
                claims.Add(new(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new(principal, Scheme.Name)));
        }
    }

    private sealed class OverviewLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
