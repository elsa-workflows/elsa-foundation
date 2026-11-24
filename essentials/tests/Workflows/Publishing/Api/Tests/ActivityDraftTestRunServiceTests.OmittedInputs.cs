using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using DesignInput = Elsa.Activities.Design.Core.Models.ActivityInputContract;
using RuntimeContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class ActivityDraftTestRunServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activity_test_preparation_rejects_nonnullable_null_before_dispatch(bool published)
    {
        var inputs = new DesignInput[]
        {
            new("note", "Note", new("String"), true, true, null, "elsa.json"),
            new("count", "Count", new("Int32"), false, false, null, "elsa.json")
        };
        var contract = new DesignContract("1", inputs, [], []);
        var runtimeInputs = inputs.Select(input => new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(
            input.ReferenceKey, input.Name, new(input.Type.Alias), input.IsRequired, input.IsNullable,
            false, null, ActivityValuePolicy.Default)).ToArray();
        var runtimeContract = new RuntimeContract("graph-type", "1", WellKnownRuntimeActivityConsumers.GraphActivity, Json("{}"), runtimeInputs,
            new(new ValueTypeDescriptor("Any"), false, ActivityValuePolicy.Default, []), ["Done"], new("test", "graph-type"));
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), includePublishedVersion: true, acceptDispatch: true,
            publicContract: contract, rootRuntimeContract: runtimeContract);
        var supplied = new Dictionary<string, ActivityDraftTestRunInput>
        {
            ["note"] = new("Present", Json("null")),
            ["count"] = new("Present", Json("null"))
        };
        var request = fixture.Request with { Inputs = supplied };
        var versionRequest = new StartActivityVersionTestRun(fixture.PublishedVersion!.DefinitionVersionId, "null-refusal", supplied);
        var response = published ? await fixture.Service.StartVersionAsync(versionRequest) : await fixture.Service.StartAsync(request);
        Assert.Equal("ValidationRejected", response.Status);
        Assert.Equal("activity.test-run.preparation-failed", response.Failure?.Code);
        Assert.Null(response.ArtifactId);
        Assert.Null(response.SourceReferenceId);
        Assert.Null(response.CommandDispatchStatus);
        Assert.False(response.Expiration.EvidenceRetained);
        Assert.False(fixture.Dispatcher.WasCalled);
        var replay = published ? await fixture.Service.StartVersionAsync(versionRequest) : await fixture.Service.StartAsync(request);
        Assert.Equal(response.TestRunId, replay.TestRunId);
        Assert.Equal(response.Failure, replay.Failure);
        Assert.False(fixture.Dispatcher.WasCalled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Activity_test_preparation_completes_omitted_public_inputs_before_runtime(bool published, bool explicitAbsent)
    {
        var inputs = new DesignInput[]
        {
            new("note", "Note", new("String"), true, true, null, "elsa.json"),
            new("count", "Count", new("Int32"), false, false, null, "elsa.json"),
            new("empty", "Empty", new("String"), false, true, null, "elsa.json"),
            new("declared", "Declared", new("Int32"), false, false, new("Literal", Json("7")), "elsa.json")
        };
        var contract = new DesignContract("1", inputs, [], []);
        var runtimeInputs = inputs.Select(input => new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(
            input.ReferenceKey, input.Name, new(input.Type.Alias), input.IsRequired, input.IsNullable,
            input.Default is not null, input.Default?.Value, ActivityValuePolicy.Default)).ToArray();
        var runtimeContract = new RuntimeContract("graph-type", "1", WellKnownRuntimeActivityConsumers.GraphActivity, Json("{}"), runtimeInputs,
            new(new ValueTypeDescriptor("Any"), false, ActivityValuePolicy.Default, []), ["Done"], new("test", "graph-type"));
        var fixture = await CreateFixture(childTemplate: ValidChildTemplate(), includePublishedVersion: true, acceptDispatch: true,
            publicContract: contract, rootRuntimeContract: runtimeContract);
        var supplied = new Dictionary<string, ActivityDraftTestRunInput> { ["note"] = new("Present", Json("null")) };
        if (explicitAbsent)
            foreach (var key in new[] { "count", "empty", "declared" }) supplied[key] = new("Absent");
        var response = published
            ? await fixture.Service.StartVersionAsync(new(fixture.PublishedVersion!.DefinitionVersionId, "omission-version", supplied))
            : await fixture.Service.StartAsync(fixture.Request with { Inputs = supplied });
        Assert.True(fixture.Dispatcher.WasCalled);
        var wrapper = Assert.IsType<WorkflowExecutable>(await fixture.Artifacts.FindAsync(response.ArtifactId!));
        Assert.Equal(new[] { "count", "declared", "empty", "note" }, wrapper.RootActivity.InputBindings.Keys.Order(StringComparer.Ordinal));
        var snapshot = await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(wrapper.RootActivity, "invocation",
            new(response.WorkflowExecutionId, "invocation", executable: wrapper), DateTimeOffset.UnixEpoch);
        Assert.Equal(0, snapshot.Values["count"].InlineValue!.Value.GetInt32());
        Assert.Equal(7, snapshot.Values["declared"].InlineValue!.Value.GetInt32());
        Assert.Equal(ValuePresence.Absent, snapshot.Values["empty"].Presence);
        Assert.Equal(ValuePresence.ExplicitNull, snapshot.Values["note"].Presence);
    }
}
