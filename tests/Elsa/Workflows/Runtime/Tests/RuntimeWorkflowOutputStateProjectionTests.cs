using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Unit coverage for the workflow-output read projection (#254 Seam R1): filtering to the reserved
/// <c>output:</c> value-id namespace, most-recent-capture-wins, and the policy-driven redaction contract
/// (policy-omitted and sensitive-marked payloads surface as named redacted markers, never silently absent).
/// </summary>
public sealed class RuntimeWorkflowOutputStateProjectionTests
{
    private const string ExecutionId = "exec-1";
    private readonly DateTimeOffset _now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly CapturePayloadsPolicy _permissivePolicy = new();

    [Fact]
    public void Project_ReturnsOnlyWorkflowOutputs_ExcludingVariablesInputsAndActivityCaptures()
    {
        // Variables and inputs live under their own reserved prefixes; an activity-output capture shares the
        // OutputName tag but derives its value id from the authored output reference. None may leak into the
        // workflow-output read surface.
        var durableValues = new List<DurableValueState>();
        durableValues.AddRange(States(RuntimeWorkflowStateSeed.BuildSeedChanges(
            ExecutionId,
            variables: new Dictionary<string, object?> { ["greeting"] = "Hello" },
            inputs: new Dictionary<string, object?> { ["name"] = "Ada" },
            capturedAt: _now)));
        durableValues.Add(NewDurableValue("authored-out-1", "ActivityResult", "activity-level", _now));
        durableValues.AddRange(States(RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges(
            ExecutionId,
            new Dictionary<string, object?> { ["Result"] = "done" },
            _now)));

        var projections = RuntimeWorkflowOutputStateProjection.Project(durableValues, _permissivePolicy);

        var projection = Assert.Single(projections);
        Assert.Equal("Result", projection.Name);
        Assert.Equal("done", projection.Value!.Value.GetString());
        Assert.False(projection.IsRedacted);
    }

    [Fact]
    public void Project_MostRecentCaptureWins_PerOutputName()
    {
        // SetOutput re-assignment upserts the same value id with a later capturedAt; the projection must
        // surface the latest value, mirroring the input-binding projection's last-capture-wins rule.
        var first = States(RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges(
            ExecutionId, new Dictionary<string, object?> { ["Result"] = "first" }, _now));
        var second = States(RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges(
            ExecutionId, new Dictionary<string, object?> { ["Result"] = "second" }, _now.AddMinutes(1)));

        // Older-last input order proves the rule sorts by capture time, not list position.
        var projections = RuntimeWorkflowOutputStateProjection.Project(second.Concat(first), _permissivePolicy);

        var projection = Assert.Single(projections);
        Assert.Equal("second", projection.Value!.Value.GetString());
        Assert.Equal(_now.AddMinutes(1), projection.CapturedAt);
    }

    [Fact]
    public void Project_DefaultPolicy_SurfacesNamedDiagnosticSnapshot_NotRawPayload()
    {
        var durableValues = States(RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges(
            ExecutionId, new Dictionary<string, object?> { ["Result"] = "done" }, _now));

        var projections = RuntimeWorkflowOutputStateProjection.Project(durableValues, new DefaultRuntimePayloadCapturePolicy());

        var projection = Assert.Single(projections);
        Assert.Equal("Result", projection.Name);
        Assert.False(projection.IsRedacted);
        Assert.Null(projection.RedactionReason);
        var snapshot = Assert.IsType<JsonElement>(projection.Value);
        Assert.Equal("string", snapshot.GetProperty("kind").GetString());
        Assert.Equal("done", snapshot.GetProperty("preview").GetString());
    }

    [Fact]
    public void Project_SensitiveMarkedOutput_IsRedacted_EvenWhenThePolicyCapturesPayloads()
    {
        // The IsSensitive metadata marker threads into the policy request: a capture-unless-sensitive host
        // policy exposes the plain output but redacts the sensitive one — as a marker, not a value, not absent.
        var durableValues = States(RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges(
                ExecutionId, new Dictionary<string, object?> { ["Result"] = "done" }, _now))
            .Append(NewDurableValue("output:Secret", "Secret", "hunter2", _now, isSensitive: true))
            .ToArray();

        var projections = RuntimeWorkflowOutputStateProjection.Project(durableValues, new CaptureUnlessSensitivePolicy());

        Assert.Collection(
            projections, // ordered by name for a deterministic read surface
            plain =>
            {
                Assert.Equal("Result", plain.Name);
                Assert.False(plain.IsRedacted);
                Assert.Equal("done", plain.Value!.Value.GetString());
            },
            sensitive =>
            {
                Assert.Equal("Secret", sensitive.Name);
                Assert.True(sensitive.IsRedacted);
                Assert.Null(sensitive.Value);
                Assert.False(string.IsNullOrWhiteSpace(sensitive.RedactionReason));
            });
    }

    [Fact]
    public void Project_ExternallyStoredOutput_SurfacesAsNamedMarker_NotAbsent()
    {
        // A workflow output whose payload is externalized carries no inline value: the projection must still
        // surface the name — as a marker with the not-stored-inline reason — even under a payload-capturing
        // policy, never silently dropping the entry. Latent today (nothing writes externalized workflow
        // outputs yet), but the read contract is honest from day one.
        var external = new DurableValueState(
            durableValueId: "durable-output:Report",
            workflowExecutionId: ExecutionId,
            valueId: "output:Report",
            type: new RuntimeValueTypeDescriptor("clr", "System.String", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.External,
            inlineValue: null,
            externalReference: new DurableValueExternalReference(
                StorageProfile: "blob",
                Locator: "runs/exec-1/report",
                Metadata: new Dictionary<string, string>()),
            sourceActivityExecutionId: null,
            capturedAt: _now,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.OutputName] = "Report"
            });

        var projections = RuntimeWorkflowOutputStateProjection.Project([external], _permissivePolicy);

        var projection = Assert.Single(projections);
        Assert.Equal("Report", projection.Name);
        Assert.True(projection.IsRedacted);
        Assert.Null(projection.Value);
        Assert.Equal(RuntimeWorkflowOutputStateProjection.NotStoredInlineReason, projection.RedactionReason);
    }

    private static IEnumerable<DurableValueState> States(IEnumerable<RuntimeStateChange<DurableValueState>> changes) =>
        changes.Select(change => change.State!);

    private static DurableValueState NewDurableValue(string valueId, string outputName, string value, DateTimeOffset capturedAt, bool isSensitive = false)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.OutputName] = outputName
        };
        if (isSensitive)
            metadata[RuntimeMetadataKeys.IsSensitive] = "true";

        return new DurableValueState(
            durableValueId: $"durable-{valueId}",
            workflowExecutionId: ExecutionId,
            valueId: valueId,
            type: new RuntimeValueTypeDescriptor("clr", "System.String", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: JsonSerializer.SerializeToElement(value),
            externalReference: null,
            sourceActivityExecutionId: null,
            capturedAt: capturedAt,
            metadata: metadata);
    }

    private sealed class CapturePayloadsPolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            new(RuntimePayloadCaptureMode.Payload, "Test policy captures payloads.");
    }

    private sealed class CaptureUnlessSensitivePolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            request.IsSensitive
                ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive runtime payloads are excluded.")
                : new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Test policy captures non-sensitive payloads.");
    }
}
