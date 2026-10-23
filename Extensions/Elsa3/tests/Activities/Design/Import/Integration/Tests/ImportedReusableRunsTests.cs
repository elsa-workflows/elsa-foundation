using System.Net;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// One host that imported two flowchart-bodied reusables declaring Approved and Rejected, <c>reusable-two-outcomes</c>
/// (completes with Approved; declares an <c>approve</c> input it does not read) and <c>reusable-rejects</c> (completes
/// with Rejected), and a flowchart caller of each with one branch per outcome. Each body chooses its outcome from a
/// constant; bodies that read values are covered by <see cref="ImportedReusableValueReadTests"/>. Shared by the
/// scenarios that read or run that import.
/// </summary>
public sealed class ImportedApprovalFixture : IAsyncLifetime
{
    public const string IdempotencyKey = "import-approval";

    public Elsa3ImportHost Host { get; private set; } = null!;
    public ApplyResponse Apply { get; private set; } = null!;
    public JsonNode Reusable { get; private set; } = null!;
    public JsonNode Rejecting { get; private set; } = null!;

    /// <summary>The plan's id for the imported activity version, which callers and the wrapper reference.</summary>
    public string ActivityVersionId => Elsa3ImportHost.Required(Reusable, "activityDefinitionVersionId");

    public async Task InitializeAsync()
    {
        Host = await Elsa3ImportHost.StartAsync();
        var (apply, items) = await Host.ImportAsync(
            IdempotencyKey, "reusable-two-outcomes", "reusable-rejects", "caller-routes-outcome", "caller-routes-rejection");
        Apply = apply;
        Reusable = items.Single(item => Elsa3ImportHost.Required(item, "sourceVersionId") == "approval-v1");
        Rejecting = items.Single(item => Elsa3ImportHost.Required(item, "sourceVersionId") == "rejection-v1");
    }

    /// <summary>Publishes an imported workflow version once; the scenarios that run it share the publication.</summary>
    public async Task<PublishedWorkflow> PublishAsync(string workflowVersionId)
    {
        if (!_published.TryGetValue(workflowVersionId, out var published))
            _published[workflowVersionId] = published = await Host.PublishWorkflowAsync(workflowVersionId);
        return published;
    }

    private readonly Dictionary<string, PublishedWorkflow> _published = new(StringComparer.Ordinal);

    public async Task DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>
/// Spec 246 User Story 1: an imported Elsa 3 reusable activity is published by the apply and runs as a graph activity,
/// from a caller and through its direct-start wrapper; a reusable the graph provider cannot hold is refused.
/// </summary>
public sealed class ImportedReusableRunsTests(ImportedApprovalFixture fixture) : IClassFixture<ImportedApprovalFixture>
{
    private Elsa3ImportHost Host => fixture.Host;

    [Fact]
    public async Task Applied_reusable_has_an_active_publication_contract_outcomes_facet_and_template()
    {
        Assert.Equal(HttpStatusCode.Created, fixture.Apply.StatusCode);
        var states = fixture.Apply.Body["publications"]!.AsArray();
        Assert.Equal(["approval-v1", "rejection-v1"], states.Select(state => Elsa3ImportHost.Required(state, "sourceVersionId")).Order(StringComparer.Ordinal));
        Assert.All(states, state => Assert.True((int?)state!["status"] == (int)ReusableActivityPublicationStatus.Published, $"Not published: {state!.ToJsonString()}"));
        var approval = states.Single(state => Elsa3ImportHost.Required(state, "sourceVersionId") == "approval-v1");
        Assert.Equal(fixture.ActivityVersionId, Elsa3ImportHost.Required(approval, "activityDefinitionVersionId"));

        var publication = await PublicationAsync();
        Assert.Equal(ActivityDefinitionVersionLifecycle.Active, publication.Lifecycle);
        Assert.Equal("1.0.0", publication.Version);
        var template = await Host.ReadAsync(services => services.GetRequiredService<IExecutableActivityTemplateReader>().FindAsync(publication.TemplateId).AsTask());
        Assert.NotNull(template);
        Assert.Equal(publication.TemplateHash, template.TemplateHash);
    }

    [Fact]
    public async Task Contract_and_facet_carry_done_and_each_converted_outcome()
    {
        var publication = await PublicationAsync();
        Assert.Equal(["Approved", "Done", "Rejected"], publication.Contract.Outcomes.Select(outcome => outcome.Name).Order(StringComparer.Ordinal));
        Assert.Equal("approve", Assert.Single(publication.Contract.Inputs).Name);

        var version = await Host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionLookup>().FindVersion(fixture.ActivityVersionId));
        var facet = Assert.Single(version!.DesignFacets, facet => facet.Kind == "elsa.outcomes");
        var ports = facet.Payload.GetProperty("ports").EnumerateArray().Select(port => port.GetProperty("name").GetString()).Order(StringComparer.Ordinal);
        Assert.Equal(["Approved", "Done", "Rejected"], ports);
    }

    [Fact]
    public async Task Caller_runs_the_published_activity_with_its_input()
    {
        // The caller binds a literal to the activity's declared approve input; this body does not read it.
        var caller = await fixture.PublishAsync("caller-v1");

        var run = await Host.RunAsync(caller);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Equal(ActivityExecutionStatus.Completed, Node(run, "call").Status);
        Assert.Contains("on-approved", run.ExecutedNodeIds);
    }

    [Theory]
    [InlineData("caller-v1", "on-approved", "on-rejected")]
    [InlineData("rejection-caller-v1", "on-rejected", "on-approved")]
    public async Task Flowchart_caller_follows_the_non_done_outcome(string callerVersionId, string taken, string notTaken)
    {
        var caller = await fixture.PublishAsync(callerVersionId);

        var run = await Host.RunAsync(caller);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Equal(ActivityExecutionStatus.Completed, Node(run, taken).Status);
        Assert.DoesNotContain(notTaken, run.ExecutedNodeIds);
    }

    [Theory]
    [InlineData("approval-v1", "Approved")]
    [InlineData("rejection-v1", "Rejected")]
    public async Task Direct_start_wrapper_runs_the_published_activity(string sourceVersionId, string outcome)
    {
        var reusable = sourceVersionId == "approval-v1" ? fixture.Reusable : fixture.Rejecting;
        var wrapper = await fixture.PublishAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        // The approval wrapper passes its request's approve member to the declared input.
        var run = await Host.RunAsync(wrapper, new JsonObject { ["approve"] = true });

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal(outcome, activity.Completion?.OutcomeKey);
    }

    [Fact]
    public async Task Flowchart_bodied_reusable_publishes_and_runs()
    {
        var publication = await PublicationAsync();
        Assert.Equal(ReusableActivityImportDrafts.GraphProviderKey, publication.Provider.ProviderKey);
        var wrapper = await fixture.PublishAsync(Elsa3ImportHost.Required(fixture.Rejecting, "workflowVersionId"));

        var run = await Host.RunAsync(wrapper);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        // The body ran: its flowchart, the decision and the one Complete it reached.
        Assert.Contains(run.Activities, state => state.Execution.ActivityType == "Elsa.Activities.Flowchart.Activities.Flowchart");
        Assert.Contains(run.Activities, state => state.Execution.ActivityType == "Elsa.Activities.If.Activities.If" && state.Status == ActivityExecutionStatus.Completed);
        Assert.Single(run.Activities, state => state.Execution.ActivityType == "Elsa.Activities.Graph.Runtime.Activities.Complete" && state.Status == ActivityExecutionStatus.Completed);
    }

    [Fact]
    public Task Reusable_using_an_intrinsic_is_refused_and_nothing_is_written() =>
        AssertRefusedAsync("reusable-sets-variable", Elsa3ActivityDiagnosticCodes.ReusableIntrinsicUnsupported);

    /// <summary>Applies the one reusable of <paramref name="fixtureName"/> in a fresh host; the refusal writes nothing.</summary>
    private static async Task AssertRefusedAsync(string fixtureName, string code)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var handle = await host.UploadCollectionAsync(fixtureName);
        var (planId, items) = await host.AnalyzeAsync(handle);
        var before = await host.CountDesignRowsAsync();

        var apply = await host.ApplyAsync(handle, planId, fixtureName, Elsa3ImportHost.Required(Assert.Single(items), "sourceVersionId"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, apply.StatusCode);
        Assert.Contains(code, apply.DiagnosticCodes);
        Assert.Equal(before, await host.CountDesignRowsAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetImportStatusAsync(fixtureName)).StatusCode);
    }

    private async Task<ActivityDefinitionVersionPublication> PublicationAsync() =>
        await Host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionVersionPublicationStore>().FindAsync(fixture.ActivityVersionId))
        ?? throw new InvalidOperationException($"Activity version '{fixture.ActivityVersionId}' has no publication: {fixture.Apply.Body.ToJsonString()}");

    private static ActivityExecutionState Node(RunState run, string nodeId) =>
        run.Activities.SingleOrDefault(state => state.Execution.ExecutableNodeId == nodeId)
        ?? throw new Xunit.Sdk.XunitException($"Node '{nodeId}' did not run: {run.Describe()}.");
}

/// <summary>
/// An imported Elsa 3 reusable whose body reads its own input converts, publishes and runs,
/// reading the value its caller passed. Result reads and captures are converted (mapping tests)
/// but not run here: any converted RunJavaScript, reusable or not, faults on its default arguments, and its Object
/// result cannot feed a Boolean input without a conversion.
/// </summary>
public sealed class ImportedReusableValueReadTests
{
    [Theory]
    [InlineData(true, "Approved")]
    [InlineData(false, "Rejected")]
    public async Task A_body_reads_the_input_its_wrapper_passed(bool approve, string outcome)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var (apply, items) = await host.ImportAsync("reads-input", "reusable-reads-input");
        var reusable = Assert.Single(items);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper, new JsonObject { ["approve"] = approve });

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal(outcome, activity.Completion?.OutcomeKey);
    }

    [Fact]
    public async Task A_body_reads_the_value_its_caller_passed_not_the_callers_own_input()
    {
        // The caller's own approve input is true, but it passes false to the activity.
        await using var host = await Elsa3ImportHost.StartAsync();
        await host.ImportAsync("caller-passes-input", "reusable-reads-input", "caller-passes-input");
        var caller = await host.PublishWorkflowAsync("input-caller-v1");

        var run = await host.RunAsync(caller, new JsonObject { ["approve"] = true });

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Contains("on-rejected", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-approved", run.ExecutedNodeIds);
    }
}
