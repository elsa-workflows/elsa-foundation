using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Endpoints;
using Elsa.Workflows.Runtime.Core.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.ExecutionEvidence.Tests;

/// <summary>
/// Boots the module's three endpoints in an in-process TestServer, following the host pattern in
/// tests/Elsa/Architecture/DomainManagementApiCompositionTests.cs. Anonymous access is configured on the *test host*;
/// the module's own endpoints keep their permission requirements.
/// </summary>
public sealed class ExecutionEvidenceEndpointTests(ExecutionEvidenceEndpointTests.EvidenceHost host)
    : IClassFixture<ExecutionEvidenceEndpointTests.EvidenceHost>, IDisposable
{
    private const string WorkflowId = "wf-1";
    private const string Base = "/_elsa/execution-evidence";
    private const string WorkflowRoute = $"{Base}/workflows/{WorkflowId}";

    /// <summary>
    /// A budget far larger than the seed delay, so returning at the deadline instead of at the write is unmistakable
    /// rather than a wall-clock near-miss.
    /// </summary>
    private const int LongBudgetMs = 30_000;

    private HttpClient Client => host.Client;

    private IExecutionEvidenceStore Store => host.Store;

    /// <summary>The host is shared for speed, so each test hands the next one an empty buffer.</summary>
    public void Dispose() => Store.Clear(null);

    [Fact]
    public async Task Workflow_evidence_is_returned_in_the_documented_camel_case_shape()
    {
        Seed("cp-1");

        using var response = await Client.GetAsync(WorkflowRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadJsonAsync(response);
        var record = Assert.Single(Records(page));
        Assert.Equal(WorkflowId, record.GetProperty("workflowExecutionId").GetString());
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, record.GetProperty("kind").GetString());
        Assert.Equal("cp-1", record.GetProperty("checkpointId").GetString());
        Assert.Equal(1, record.GetProperty("sequence").GetInt64());
        Assert.Equal(1, page.GetProperty("firstSequence").GetInt64());
        Assert.Equal(1, page.GetProperty("lastSequence").GetInt64());
        Assert.Equal(1, page.GetProperty("matchedWorkflows").GetInt32());
        Assert.False(page.GetProperty("terminal").GetBoolean());
    }

    [Fact]
    public async Task Workflow_evidence_honours_the_after_cursor()
    {
        Seed("cp-1");
        Seed("cp-2");

        var page = await GetPageAsync($"{WorkflowRoute}?after=1");

        var record = Assert.Single(Records(page));
        Assert.Equal(2, record.GetProperty("sequence").GetInt64());
    }

    [Fact]
    public async Task Workflow_evidence_reports_a_terminal_execution()
    {
        Seed("cp-1", terminal: true);

        var page = await GetPageAsync(WorkflowRoute);

        Assert.True(page.GetProperty("terminal").GetBoolean());
    }

    [Fact]
    public async Task Unknown_workflow_returns_an_empty_page_rather_than_a_404()
    {
        var page = await GetPageAsync($"{Base}/workflows/missing");

        Assert.Empty(page.GetProperty("records").EnumerateArray());
        Assert.Equal(0, page.GetProperty("lastSequence").GetInt64());
        Assert.Equal(0, page.GetProperty("matchedWorkflows").GetInt32());
    }

    [Theory]
    [InlineData("%20")]
    [InlineData("%09")]
    public async Task A_blank_workflow_execution_id_is_rejected(string segment)
    {
        // A blank segment means the caller built the route from an id it never actually had; answering with an empty
        // page would let that pass as "this workflow recorded nothing".
        using var response = await Client.GetAsync($"{Base}/workflows/{segment}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Correlated_evidence_spans_workflow_executions()
    {
        Seed("cp-1", correlationId: "corr-1");
        Seed("cp-2", workflowExecutionId: "wf-2", correlationId: "corr-1");
        Seed("cp-3", workflowExecutionId: "wf-3", correlationId: "corr-2");

        var page = await GetPageAsync($"{Base}?correlationId=corr-1");

        Assert.Equal(
            [WorkflowId, "wf-2"],
            Records(page).Select(record => record.GetProperty("workflowExecutionId").GetString()));
        Assert.Equal(2, page.GetProperty("matchedWorkflows").GetInt32());
    }

    [Fact]
    public async Task Correlated_evidence_honours_the_after_cursor()
    {
        Seed("cp-1", correlationId: "corr-1");
        Seed("cp-2", correlationId: "corr-1");

        var page = await GetPageAsync($"{Base}?correlationId=corr-1&after=1");

        var record = Assert.Single(Records(page));
        Assert.Equal(2, record.GetProperty("sequence").GetInt64());
    }

    [Theory]
    [InlineData("/_elsa/execution-evidence")]
    [InlineData("/_elsa/execution-evidence?correlationId=")]
    [InlineData("/_elsa/execution-evidence?correlationId=%20")]
    public async Task Correlated_evidence_requires_a_non_blank_correlation_id(string route)
    {
        using var response = await Client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/_elsa/execution-evidence")]
    [InlineData("/_elsa/execution-evidence?all=false")]
    [InlineData("/_elsa/execution-evidence?workflowExecutionId=%20")]
    public async Task Delete_without_a_target_or_an_explicit_opt_in_is_rejected(string route)
    {
        // The buffer is shared across whatever runs on this host, so an unqualified wipe must not be the easiest
        // call to make.
        Seed("cp-1");

        using var response = await Client.DeleteAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(Store.List(WorkflowId, 0).Records);
    }

    [Fact]
    public async Task Delete_with_the_explicit_opt_in_empties_the_store_and_returns_no_content()
    {
        Seed("cp-1");
        Seed("cp-2", workflowExecutionId: "wf-2");

        using var response = await Client.DeleteAsync($"{Base}?all=true");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(Store.List(WorkflowId, 0).Records);
        Assert.Empty(Store.List("wf-2", 0).Records);
    }

    [Fact]
    public async Task Delete_with_a_workflow_id_drops_only_that_workflow()
    {
        Seed("cp-1");
        Seed("cp-2", workflowExecutionId: "wf-2");

        using var response = await Client.DeleteAsync($"{Base}?workflowExecutionId={WorkflowId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(Store.List(WorkflowId, 0).Records);
        Assert.NotEmpty(Store.List("wf-2", 0).Records);
    }

    [Fact]
    public async Task Long_poll_returns_when_evidence_appears_rather_than_when_the_budget_expires()
    {
        var page = await SeedWhilePollingAsync($"{WorkflowRoute}?waitMs={LongBudgetMs}", () => Seed("cp-1"));

        Assert.Single(Records(page));
    }

    [Fact]
    public async Task Correlated_long_poll_waits_for_a_correlated_workflow_to_appear()
    {
        // Without waitMs support on this route the read would return an empty page immediately, before the seed.
        var page = await SeedWhilePollingAsync(
            $"{Base}?correlationId=corr-1&waitMs={LongBudgetMs}",
            () => Seed("cp-1", workflowExecutionId: "wf-child", correlationId: "corr-1"));

        Assert.Single(Records(page));
    }

    [Fact]
    public async Task Long_poll_gives_up_with_an_empty_page_rather_than_hanging()
    {
        var page = await GetPageAsync($"{WorkflowRoute}?waitMs=150");

        Assert.Empty(page.GetProperty("records").EnumerateArray());
        Assert.Equal(0, page.GetProperty("lastSequence").GetInt64());
        Assert.False(page.GetProperty("terminal").GetBoolean());
    }

    [Fact]
    public async Task A_negative_wait_budget_is_clamped_to_no_wait_rather_than_rejected()
    {
        var page = await GetPageAsync($"{WorkflowRoute}?waitMs=-1");

        Assert.Empty(page.GetProperty("records").EnumerateArray());
    }

    [Fact]
    public async Task A_wait_budget_shorter_than_the_poll_interval_is_not_rounded_up_to_it()
    {
        const int reads = 8;
        await GetPageAsync(WorkflowRoute); // Warm the host so the measurement is not paying for first-request cost.

        var startedAt = Stopwatch.GetTimestamp();

        for (var index = 0; index < reads; index++)
            await GetPageAsync($"{WorkflowRoute}?waitMs=1");

        // Sleeping a whole 50ms poll interval per read would cost at least 400ms here; honouring the budget costs
        // roughly the budget. The bound is deliberately far from both numbers so this measures behaviour, not load.
        Assert.True(
            Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromMilliseconds(300),
            $"{reads} reads with a 1ms budget must not each pay a full poll interval.");
    }

    private async Task<JsonElement> SeedWhilePollingAsync(string route, Action seed)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var pending = GetPageAsync(route);

        await Task.Delay(100);
        seed();
        var page = await pending;

        Assert.True(
            Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(5),
            "The long poll should return when evidence appears, not when its wait budget expires.");

        return page;
    }

    private void Seed(
        string checkpointId,
        string workflowExecutionId = WorkflowId,
        string? correlationId = null,
        bool terminal = false) =>
        Store.Append(EvidenceFactory.Batch(checkpointId, workflowExecutionId, correlationId, terminal));

    private async Task<JsonElement> GetPageAsync(string route)
    {
        using var response = await Client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static IReadOnlyList<JsonElement> Records(JsonElement page) =>
        page.GetProperty("records").EnumerateArray().ToArray();

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    public sealed class EvidenceHost : IAsyncLifetime
    {
        private WebApplication _app = null!;

        public HttpClient Client { get; private set; } = null!;

        public IExecutionEvidenceStore Store => _app.Services.GetRequiredService<IExecutionEvidenceStore>();

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            new WorkflowsExecutionEvidenceFeature().ConfigureServices(builder.Services);
            builder.Services.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "none" });
            builder.Services.AddAuthentication(options => options.DefaultAuthenticateScheme = "none")
                .AddScheme<AuthenticationSchemeOptions, WildcardAuthenticationHandler>("none", _ => { });
            builder.Services.AddAuthorization();

            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            ExecutionEvidenceApi.MapExecutionEvidenceApi(_app);
            await _app.StartAsync();
            Client = _app.GetTestClient();
        }

        public async Task DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }

        private sealed class WildcardAuthenticationHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "execution-evidence-test"),
                    new Claim(IdentityClaimTypes.Normalized, "v1"),
                    new Claim(IdentityClaimTypes.Provider, "execution-evidence-test"),
                    new Claim(IdentityClaimTypes.TenantId, "execution-evidence-test"),
                    new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard)
                };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
            }
        }
    }
}
