using System.Net;
using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class RuntimeHistoryApiTests
{
    private static readonly RuntimeHistoryCapturePolicy OccurrencePolicy = new("occurrence-api-tests", 1, true, ["ActivityStarted"]);

    [Fact]
    public async Task Occurrences_forward_one_bounded_request_and_preserve_64_bit_wire_identities_and_unknown_time()
    {
        const long large = 9_007_199_254_740_993;
        var source = new OccurrenceProjection { Occurrences = OccurrencePage(large) };
        await using var host = await OccurrenceHost(source); host.Authenticate();
        using var response = await host.Client.GetAsync(Path + "/occurrences?limit=1&sequenceToken=window&position=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<RuntimeHistoryOccurrenceQuery>(source.OccurrenceQuery);
        Assert.Equal(1, query.Limit); Assert.Equal("window", query.SequenceToken); Assert.Equal(0, query.Position);
        Assert.Equal(RuntimeHistoryIdentity.CreateBinding(["tenant-a", "operator", "structure-only"]), query.AccessBinding);
        Assert.Equal(1, source.Reads); Assert.Equal(0, source.IndependentExactReads); Assert.Equal(0, host.Authorization.ValueReads);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = json.RootElement;
        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal("9007199254740993", root.GetProperty("upperSequence").GetString());
        Assert.Equal("9007199254740993", root.GetProperty("coverage").GetProperty("lastSequence").GetString());
        Assert.Equal("1", root.GetProperty("totalCount").GetString());
        var entry = root.GetProperty("items")[0];
        Assert.Equal("9007199254740993", entry.GetProperty("sequence").GetString());
        Assert.Equal("0", entry.GetProperty("position").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("occurredAt").ValueKind);
        Assert.False(entry.GetProperty("occurredAtKnown").GetBoolean());
        Assert.False(entry.TryGetProperty("payload", out _)); Assert.False(entry.TryGetProperty("metadata", out _));
    }

    [Theory]
    [InlineData("?limit=101")]
    [InlineData("?limit=0")]
    [InlineData("?position=-1")]
    [InlineData("?position=0&anchorActivityExecutionId=activity")]
    [InlineData("?includeDescendants=true")]
    public async Task Invalid_occurrence_requests_fail_before_projection_access(string query)
    {
        var source = new OccurrenceProjection();
        await using var host = await OccurrenceHost(source); host.Authenticate();
        using var response = await host.Client.GetAsync(Path + "/occurrences" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occurrence_access_or_profile_retired_during_a_pending_read_never_discloses_rows(bool profile)
    {
        var source = new OccurrenceProjection();
        await using var host = await OccurrenceHost(source); host.Authenticate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Pending = async token => { entered.SetResult(); await release.Task.WaitAsync(token); return source.Occurrences; };
        var request = host.Client.GetAsync(Path + "/occurrences");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (profile) host.Authorization.Subject = "replacement"; else host.Authorization.Allowed = false;
        release.SetResult();
        using var response = await request;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, host.Authorization.ValueReads);
    }

    [Theory]
    [InlineData("wrong-run")]
    [InlineData("wrong-position")]
    [InlineData("changed-window")]
    [InlineData("missing-head")]
    [InlineData("future-schema")]
    [InlineData("observation-only")]
    public async Task Contradictory_occurrence_projections_return_invalid_coverage_without_rows(string scenario)
    {
        var good = OccurrencePage(); var item = good.Items[0].Observation;
        var badEvent = new RuntimeHistoryEvent(item.HistoryEventId, scenario == "wrong-run" ? "other" : "run", 1,
            RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", Now, "activity")
        { Authority = scenario == "observation-only" ? RuntimeHistoryAuthority.DeliveryObservation : RuntimeHistoryAuthority.CommittedTransition,
            SchemaVersion = scenario == "future-schema" ? 2 : 1, RecordedAt = Now, PhysicalCommitId = "physical" };
        var source = new OccurrenceProjection { Occurrences = new(scenario == "missing-head" ? null : good.Head,
            RuntimeHistoryOccurrenceAvailability.Available, null, 1, 0, 1, 0,
            [new(scenario == "wrong-position" ? 1 : 0, badEvent)], scenario == "changed-window" ? "replacement" : "window", false) };
        await using var host = await OccurrenceHost(source); host.Authenticate();
        using var response = await host.Client.GetAsync(Path + "/occurrences?sequenceToken=window");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", json.RootElement.GetProperty("state").GetString());
        Assert.Equal("history-invalid", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal("invalid", json.RootElement.GetProperty("coverage").GetProperty("state").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Optional_occurrence_absence_is_unavailable_and_capability_requires_actual_paired_facet()
    {
        await using var host = await Host.StartAsync(); host.Authenticate();
        using var response = await host.Client.GetAsync(Path + "/occurrences");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("occurrence-facet-unsupported", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("totalCount").ValueKind);
        Assert.Equal(0, host.History.Reads);
        foreach (var paired in new[] { false, true })
        {
            var declarations = await new RuntimeHistoryCapabilitySource(new Writer(paired), new OccurrenceProjection()).GetCapabilitiesAsync();
            Assert.Equal(paired, declarations.SelectMany(item => item.Links).Any(item => item.Rel == "workflow-history-occurrences"));
        }
        var legacy = await new RuntimeHistoryCapabilitySource(new Writer(true), new BaseProjection()).GetCapabilitiesAsync();
        Assert.DoesNotContain(legacy.SelectMany(item => item.Links), item => item.Rel == "workflow-history-occurrences");
    }

    [Fact]
    public async Task Oversized_occurrence_token_is_rejected_before_the_provider()
    {
        var source = new OccurrenceProjection();
        await using var host = await OccurrenceHost(source); host.Authenticate();
        using var response = await host.Client.GetAsync(Path + "/occurrences?sequenceToken=" +
            new string('a', RuntimeStorePageRequest.MaximumContinuationTokenLength + 1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task Unknown_provider_limitation_does_not_expose_arbitrary_diagnostic_text()
    {
        var p = OccurrencePage();
        var source = new OccurrenceProjection { Occurrences = new(p.Head, RuntimeHistoryOccurrenceAvailability.Unavailable,
            "private-provider-details", p.UpperSequence, 0, null, null, [], null, false) };
        await using var host = await OccurrenceHost(source); host.Authenticate();
        using var response = await host.Client.GetAsync(Path + "/occurrences");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("occurrence-limitation-unknown", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task An_unknown_occurrence_count_remains_unknown_without_discarding_the_exact_selected_position()
    {
        var p = OccurrencePage();
        var source = new OccurrenceProjection { Occurrences = new(p.Head, p.Availability, p.Reason, p.UpperSequence,
            p.RetainedAfterSequence, null, 0, p.Items, p.SequenceToken, false) };
        await using var host = await OccurrenceHost(source); host.Authenticate();
        using var response = await host.Client.GetAsync(Path + "/occurrences");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("totalCount").ValueKind);
        Assert.Equal("0", json.RootElement.GetProperty("selectedPosition").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("items").GetArrayLength());
    }

    private static Task<Host> OccurrenceHost(OccurrenceProjection source) => Host.StartAsync(
        services => services.AddSingleton<IRuntimeHistoryStore>(source), workflow: Workflow() with { HistoryCapturePolicy = OccurrencePolicy });

    private static RuntimeHistoryOccurrencePage OccurrencePage(long sequence = 1)
    {
        var head = Head() with { Policy = OccurrencePolicy, LastSequence = sequence };
        var observation = new RuntimeHistoryEvent("first", "run", sequence, RuntimeHistoryEventCategory.ActivityLifecycle,
            "ActivityStarted", Now, "activity", metadata: new Dictionary<string, string> { ["private"] = "withheld" })
        { Authority = RuntimeHistoryAuthority.CommittedTransition, RecordedAt = Now, PhysicalCommitId = "physical", OccurredAtKnown = false };
        return new(head, RuntimeHistoryOccurrenceAvailability.Available, null, sequence, 0, 1, 0, [new(0, observation)], "window", false);
    }

    private sealed class OccurrenceProjection : BaseProjection, IRuntimeHistoryOccurrenceStore
    {
        public RuntimeHistoryOccurrencePage Occurrences { get; set; } = OccurrencePage();
        public RuntimeHistoryOccurrenceQuery? OccurrenceQuery { get; private set; }
        public Func<CancellationToken, Task<RuntimeHistoryOccurrencePage>>? Pending { get; set; }
        public async ValueTask<RuntimeHistoryOccurrencePage> ReadOccurrencesAsync(RuntimeHistoryOccurrenceQuery query, CancellationToken token = default)
        {
            Read(token); OccurrenceQuery = query;
            return Pending is { } pending ? await pending(token) : Occurrences;
        }
    }
}
