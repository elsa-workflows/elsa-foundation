using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Host.ModuleManagement;
using Elsa.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// <c>POST /_module-management/reconcile</c> on <c>Elsa.Foundation.Host</c> answers with the outcome Nuplane reported: the outcome
/// code by its name, a status that says whether the cycle ran, and the reason in the body. The outcomes here are scripted; the
/// built host's own are exercised by <c>FoundationHostReconcileTests</c>.
/// </summary>
public sealed class FoundationHostReconcileEndpointTests : IAsyncLifetime
{
    private const string ApiKey = "expected-key";
    private readonly FakeNuplaneAdminOperations _operations = new();
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IShellRegistry>(_ => null!);
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());
        builder.Services.AddSingleton<INuplaneAdminOperations>(_operations);
        _app = builder.Build();
        _app.MapModuleManagementApi(new ModuleManagementOptions { Enabled = true, ApiKey = ApiKey });
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add(ModuleManagementOptions.ApiKeyHeader, ApiKey);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Theory]
    [InlineData(ManualReconcileOutcomeCode.Completed)]
    [InlineData(ManualReconcileOutcomeCode.Accepted)]
    public async Task Answers_200_with_the_outcome_by_name_when_the_cycle_ran(ManualReconcileOutcomeCode code)
    {
        _operations.Outcome = new ManualReconcileOutcome(code, "corr-1", null, null);

        using var response = await _client!.PostAsync("/_module-management/reconcile", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code.ToString(), body.GetProperty("outcomeCode").GetString());
        Assert.Equal("corr-1", body.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("single-flight-active", "already running")]
    [InlineData("store-lock-unavailable", "owns the package store")]
    public async Task Answers_a_409_problem_naming_the_outcome_and_the_reason_when_the_reconcile_was_rejected(string reason, string detail)
    {
        _operations.Outcome = new ManualReconcileOutcome(ManualReconcileOutcomeCode.Rejected, "corr-2", null, reason);

        using var response = await _client!.PostAsync("/_module-management/reconcile", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((409, "Rejected", reason, "corr-2"), (body.GetProperty("status").GetInt32(), body.GetProperty("outcomeCode").GetString(), body.GetProperty("reasonCode").GetString(), body.GetProperty("correlationId").GetString()));
        Assert.Contains(detail, body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_a_503_problem_carrying_the_failure_when_the_reconcile_service_is_unavailable()
    {
        _operations.Outcome = new ManualReconcileOutcome(ManualReconcileOutcomeCode.Unavailable, "corr-3", null, "The feed could not be read.");

        using var response = await _client!.PostAsync("/_module-management/reconcile", content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Unavailable", "The feed could not be read.", "corr-3"), (body.GetProperty("outcomeCode").GetString(), body.GetProperty("reasonCode").GetString(), body.GetProperty("correlationId").GetString()));
    }
}
