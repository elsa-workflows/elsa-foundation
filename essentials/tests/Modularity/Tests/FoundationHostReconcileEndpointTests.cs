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
using Microsoft.Extensions.Logging;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Models;
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
    private readonly CapturingLogger _log = new();
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IShellRegistry>(_ => null!);
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());
        builder.Services.AddSingleton<INuplaneAdminOperations>(_operations);
        builder.Services.AddSingleton<ILoggerFactory>(_log);
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
    public async Task Answers_a_503_problem_with_a_fixed_reason_and_logs_the_failure_when_the_reconcile_service_is_unavailable()
    {
        const string failure = @"The feed at https://user:secret@feeds.internal/nuget could not be read from C:\ops\packages.";
        _operations.Outcome = new ManualReconcileOutcome(ManualReconcileOutcomeCode.Unavailable, "corr-3", null, failure);

        using var response = await _client!.PostAsync("/_module-management/reconcile", content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("feeds.internal", text, StringComparison.Ordinal);
        var body = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal(("Unavailable", "reconcile-service-failed", "corr-3"), (body.GetProperty("outcomeCode").GetString(), body.GetProperty("reasonCode").GetString(), body.GetProperty("correlationId").GetString()));
        var logged = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(failure, logged.Message, StringComparison.Ordinal);
        Assert.Contains("corr-3", logged.Message, StringComparison.Ordinal);
    }

    /// <summary>A Nuplane newer than the host's can add an outcome code; the host must answer it, not throw.</summary>
    [Fact]
    public async Task Answers_a_500_problem_naming_the_outcome_and_logs_it_when_nuplane_reports_a_code_this_host_does_not_map()
    {
        var unknown = (ManualReconcileOutcomeCode)99;
        _operations.Outcome = new ManualReconcileOutcome(unknown, "corr-4", null, "a newer reason");

        using var response = await _client!.PostAsync("/_module-management/reconcile", content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("99", "reconcile-outcome-unrecognized", "corr-4"), (body.GetProperty("outcomeCode").GetString(), body.GetProperty("reasonCode").GetString(), body.GetProperty("correlationId").GetString()));
        Assert.Contains("99", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var logged = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("corr-4", logged.Message, StringComparison.Ordinal);
    }

    /// <summary>What the cycle installed is answered by identity, without where the host put it.</summary>
    [Fact]
    public async Task Answers_the_packages_a_cycle_changed_without_their_install_paths()
    {
        var added = new ResolvedPackage("Acme.Widgets", "2.0.0", "local-packages", "/srv/elsa/packages/acme.widgets/2.0.0", DateTimeOffset.UnixEpoch, "local-source");
        var run = new ReconciliationRunResult(false, new PackageChangeSet([added], [], ["Acme.Old"], "corr-5", DateTimeOffset.UnixEpoch), ["Acme.Broken"], IsDegraded: true);
        _operations.Outcome = new ManualReconcileOutcome(ManualReconcileOutcomeCode.Completed, "corr-5", run, null);

        using var response = await _client!.PostAsync("/_module-management/reconcile", content: null);

        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/srv/elsa", text, StringComparison.Ordinal);
        Assert.DoesNotContain("installPath", text, StringComparison.OrdinalIgnoreCase);
        var result = JsonSerializer.Deserialize<JsonElement>(text).GetProperty("runResult");
        var package = Assert.Single(result.GetProperty("changeSet").GetProperty("added").EnumerateArray());
        Assert.Equal(("Acme.Widgets", "2.0.0", "local-packages", "local-source"), (package.GetProperty("id").GetString(), package.GetProperty("version").GetString(), package.GetProperty("feedName").GetString(), package.GetProperty("sourceName").GetString()));
        Assert.Equal(["Acme.Old"], result.GetProperty("changeSet").GetProperty("removed").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal((false, 0, true, "Acme.Broken"), (result.GetProperty("skipped").GetBoolean(), result.GetProperty("skipReason").GetInt32(), result.GetProperty("isDegraded").GetBoolean(), result.GetProperty("failedPackages")[0].GetString()));
    }
}
