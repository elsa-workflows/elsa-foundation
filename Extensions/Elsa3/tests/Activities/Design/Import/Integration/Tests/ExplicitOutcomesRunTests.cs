using System.Net;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Mapping.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 1208 (S-3) FR-030: converted Elsa 3 routing is proven by conversion, publication and execution. Each test imports
/// a test-owned Elsa 3 workflow, publishes it and runs it on the real runtime: RunJavaScript <c>Done</c> (US2, #3295),
/// SendHttpRequest <c>Done</c> with expected status codes (US2 scenario 4), <c>setOutcome</c> / <c>setOutcomes</c>, a
/// computed outcome name, Switch <c>MatchAny</c> and a Complete with several outcomes (US5).
/// </summary>
public sealed class ExplicitOutcomesRunTests(ITestOutputHelper output)
{
    /// <summary>US2 scenarios 1 and 2: Done is followed without and with possible outcomes the script selects none of.</summary>
    [Fact]
    public async Task A_run_javascript_done_connection_runs_its_target()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var run = await ImportPublishRunAsync(host, "script-done");

        AssertCompleted(run);
        Assert.Equal(["plain", "after-plain", "listed", "after-listed"], run.ExecutedNodeIds.Where(id => id != "script-done-root"));
    }

    /// <summary>
    /// US2 scenario 4: with expected status codes Elsa 4 has no <c>Done</c>; the Elsa 3 <c>Done</c> connection becomes one
    /// per code and one for the unmatched status code, so its target runs for each expected code and an unexpected one.
    /// </summary>
    [Fact]
    public async Task A_send_http_request_done_connection_runs_its_target_for_every_response()
    {
        await using var stub = await StatusStub.StartAsync();
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var collection = Elsa3ImportHost.Collection("http-expected-codes-done").Replace("http://stub.invalid/status", stub.Url, StringComparison.Ordinal);
        var handle = await host.UploadCollectionJsonAsync(collection);
        var (planId, items) = await host.AnalyzeAsync(handle);
        var apply = await host.ApplyAsync(handle, planId, "import-http-expected-codes-done", Elsa3ImportHost.Required(Assert.Single(items), "sourceVersionId"));
        Assert.True(apply.StatusCode == HttpStatusCode.Created, apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(Assert.Single(items), "workflowVersionId"));

        foreach (var (status, outcome) in new[] { (200, "200"), (404, "404"), (500, "Unmatched status code") })
        {
            stub.Status = status;
            var run = await host.RunAsync(workflow);
            output.WriteLine($"{status}: {run.Describe()}");

            AssertCompleted(run);
            Assert.Equal([outcome], Node(run, "call").Completion?.OutcomeNames);
            Assert.Contains("after", run.ExecutedNodeIds);
        }
    }

    /// <summary>US5 scenario 1: <c>setOutcome('Approved')</c> runs only Approved's successor.</summary>
    [Fact]
    public async Task Set_outcome_runs_only_the_selected_branch()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var run = await ImportPublishRunAsync(host, "script-set-outcome");

        AssertCompleted(run);
        Assert.Equal(["Approved"], Node(run, "script").Completion?.OutcomeNames);
        Assert.Contains("on-approved", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-rejected", run.ExecutedNodeIds);
    }

    /// <summary>US5 scenario 2: <c>setOutcomes(['A','B'])</c> runs both branches, and the join after them runs once.</summary>
    [Fact]
    public async Task Set_outcomes_runs_every_selected_branch_and_the_join_once()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var run = await ImportPublishRunAsync(host, "script-set-outcomes");

        AssertCompleted(run);
        Assert.Equal(["A", "B"], Node(run, "script").Completion?.OutcomeNames);
        Assert.Contains("on-a", run.ExecutedNodeIds);
        Assert.Contains("on-b", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-c", run.ExecutedNodeIds);
        Assert.Single(run.ExecutedNodeIds, id => id == "joined");
    }

    /// <summary>
    /// US5 scenario 3: a computed name outside the possible outcomes selects the converter's catch-all, which has no
    /// connection, so the path ends without a fault, as an unknown name did in Elsa 3.
    /// </summary>
    [Fact]
    public async Task A_computed_outcome_outside_the_listed_set_ends_the_path_without_a_fault()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var run = await ImportPublishRunAsync(host, "script-computed-outcome");

        AssertCompleted(run);
        Assert.Equal(["Unlisted outcome"], Node(run, "script").Completion?.OutcomeNames);
        Assert.DoesNotContain("on-approved", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-rejected", run.ExecutedNodeIds);
    }

    /// <summary>US5: a computed name with no possible outcomes has no faithful route, so the import refuses it with guidance.</summary>
    [Fact]
    public async Task A_computed_outcome_without_possible_outcomes_is_refused()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var import = await host.ImportSelectionAsync("import-script-computed-outcome-unlisted", ["script-computed-outcome-unlisted"]);

        output.WriteLine(import.Apply.Body.ToJsonString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, import.Apply.StatusCode);
        Assert.Contains(Elsa3ImportDiagnosticCodes.ForbiddenCapability, import.Apply.DiagnosticCodes);
        var diagnostic = import.Apply.Body["diagnostics"]!.AsArray().First(item => (string?)item!["code"] == Elsa3ImportDiagnosticCodes.ForbiddenCapability)!;
        Assert.Contains("Possible Outcomes", diagnostic.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>US5 scenario 4: a FlowSwitch in MatchAny mode runs every matching case's connection.</summary>
    [Fact]
    public async Task A_match_any_switch_runs_every_matching_case()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var run = await ImportPublishRunAsync(host, "flow-switch-match-any");

        AssertCompleted(run);
        Assert.Equal(["a", "c"], Node(run, "route").Completion?.OutcomeNames);
        Assert.Contains("on-a", run.ExecutedNodeIds);
        Assert.Contains("on-c", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-b", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-default", run.ExecutedNodeIds);
    }

    /// <summary>The same switch in MatchFirst mode publishes too (its cases carry a case identity) and takes only the first.</summary>
    [Fact]
    public async Task A_match_first_switch_runs_only_the_first_matching_case()
    {
        await using var host = await Elsa3ImportHost.StartAsync();

        var run = await ImportPublishRunAsync(host, "flow-switch-match-first");

        AssertCompleted(run);
        Assert.Equal(["a"], Node(run, "route").Completion?.OutcomeNames);
        Assert.Contains("on-a", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-c", run.ExecutedNodeIds);
    }

    /// <summary>US5 scenario 5: a Complete with several literal outcomes inside a reusable; its caller sees all of them.</summary>
    [Fact]
    public async Task A_complete_with_several_outcomes_runs_every_caller_branch()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-completes-several", ["reusable-completes-several", "caller-routes-several"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var caller = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("fan-out-caller-v1"), "workflowVersionId"));

        var run = await host.RunAsync(caller);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.Equal(["Approved", "Audit"], Node(run, "call").Completion?.OutcomeNames.Order(StringComparer.Ordinal));
        Assert.Contains("on-approved", run.ExecutedNodeIds);
        Assert.Contains("on-audit", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-done", run.ExecutedNodeIds);
    }

    private async Task<RunState> ImportPublishRunAsync(Elsa3ImportHost host, string fixture)
    {
        var import = await host.ImportSelectionAsync($"import-{fixture}", [fixture]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{fixture}-v1"), "workflowVersionId"));
        var run = await host.RunAsync(workflow);
        output.WriteLine(run.Describe());
        return run;
    }

    private static void AssertCompleted(RunState run)
    {
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.All(run.Activities, state => Assert.True(state.Fault is null, run.Describe()));
    }

    private static ActivityExecutionState Node(RunState run, string nodeId) =>
        run.Activities.SingleOrDefault(state => state.Execution.ExecutableNodeId == nodeId)
        ?? throw new Xunit.Sdk.XunitException($"Node '{nodeId}' did not run once: {run.Describe()}.");

    /// <summary>A loopback HTTP server that answers every request with <see cref="Status"/>.</summary>
    private sealed class StatusStub : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private StatusStub(WebApplication app, string url)
        {
            _app = app;
            Url = url;
        }

        public string Url { get; }

        public int Status { get; set; } = 200;

        public static async Task<StatusStub> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            StatusStub? stub = null;
            app.Run(context =>
            {
                context.Response.StatusCode = stub!.Status;
                return Task.CompletedTask;
            });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            stub = new StatusStub(app, $"{address.TrimEnd('/')}/status");
            return stub;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
