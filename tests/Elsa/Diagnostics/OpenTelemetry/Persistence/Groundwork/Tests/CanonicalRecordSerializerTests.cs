using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Groundwork.DiagnosticRecords;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class CanonicalRecordSerializerTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 14, 10, 11, 12, 345, TimeSpan.FromHours(2));

    private readonly CanonicalRecordSerializer _serializer = new();

    [Fact]
    public void Every_immutable_signal_round_trips_without_losing_domain_fields()
    {
        var trace = Trace();
        var span = Span(Attributes(("z", "last"), ("a", "first")));
        var point = Point();
        var log = Log();

        var actualTrace = _serializer.ToTrace(Committed(_serializer.ToRecord("trace-record-1", trace)));
        var actualSpan = _serializer.ToSpan(Committed(_serializer.ToRecord("span-record-1", span)));
        var actualPoint = _serializer.ToMetricPoint(Committed(_serializer.ToRecord("point-record-1", point)));
        var actualLog = _serializer.ToLog(Committed(_serializer.ToRecord("log-record-1", log)));

        Assert.Equal(trace with
        {
            ResourceIds = actualTrace.ResourceIds,
            WorkflowInstanceIds = actualTrace.WorkflowInstanceIds
        }, actualTrace);
        Assert.Equal(span with
        {
            Attributes = actualSpan.Attributes,
            Events = actualSpan.Events,
            Links = actualSpan.Links
        }, actualSpan);
        AssertAttributes(span.Attributes, actualSpan.Attributes);
        AssertAttributes(Assert.Single(span.Events).Attributes, Assert.Single(actualSpan.Events).Attributes);
        AssertAttributes(Assert.Single(span.Links).Attributes, Assert.Single(actualSpan.Links).Attributes);
        Assert.Equal(BitConverter.DoubleToInt64Bits(point.Value!.Value), BitConverter.DoubleToInt64Bits(actualPoint.Value!.Value));
        Assert.Equal(point with { Attributes = actualPoint.Attributes }, actualPoint);
        AssertAttributes(point.Attributes, actualPoint.Attributes);
        Assert.Equal(log with { Attributes = actualLog.Attributes }, actualLog);
        AssertAttributes(log.Attributes, actualLog.Attributes);
    }

    [Fact]
    public void Equivalent_attribute_insertion_orders_produce_one_payload_and_append_fingerprint()
    {
        var first = _serializer.ToRecord("span-record-1", Span(Attributes(("b", "2"), ("a", "1"))));
        var second = _serializer.ToRecord("span-record-1", Span(Attributes(("a", "1"), ("b", "2"))));
        var scope = new DiagnosticStorageScope("tenant", "scope");
        var stream = new DiagnosticStreamId("spans");

        Assert.Equal(first.Payload, second.Payload);
        Assert.Equal(first.Fields, second.Fields);
        Assert.Equal(
            DiagnosticRequestFingerprint.ForAppend(scope, stream, [first]),
            DiagnosticRequestFingerprint.ForAppend(scope, stream, [second]));
    }

    [Fact]
    public void Trace_set_fields_are_canonical_when_input_enumeration_order_changes()
    {
        var first = Trace() with
        {
            ResourceIds = ["resource-2", "resource-1"],
            WorkflowInstanceIds = ["workflow-2", "workflow-1"]
        };
        var second = Trace() with
        {
            ResourceIds = ["resource-1", "resource-2"],
            WorkflowInstanceIds = ["workflow-1", "workflow-2"]
        };

        var firstRecord = _serializer.ToRecord("trace-record-1", first);
        var secondRecord = _serializer.ToRecord("trace-record-1", second);

        Assert.Equal(firstRecord.Payload, secondRecord.Payload);
        Assert.Equal(firstRecord.Fields, secondRecord.Fields);
    }

    [Fact]
    public void Span_event_and_link_order_is_preserved_as_meaningful_payload_order()
    {
        var span = Span(Attributes()) with
        {
            Events =
            [
                new("second-by-name", Timestamp.AddMilliseconds(2), Attributes()),
                new("first-by-name", Timestamp.AddMilliseconds(1), Attributes())
            ],
            Links =
            [
                new("trace-z", "span-z", Attributes()),
                new("trace-a", "span-a", Attributes())
            ]
        };

        var restored = _serializer.ToSpan(Committed(_serializer.ToRecord("span-record", span)));

        Assert.Equal(["second-by-name", "first-by-name"], restored.Events.Select(x => x.Name));
        Assert.Equal(["trace-z", "trace-a"], restored.Links.Select(x => x.TraceId));
    }

    [Fact]
    public void Record_fields_cover_bounded_query_identity_order_and_filter_values()
    {
        var trace = _serializer.ToRecord("trace-record-1", Trace());
        var span = _serializer.ToRecord("span-record-1", Span(Attributes()));
        var point = _serializer.ToRecord("point-record-1", Point());
        var log = _serializer.ToRecord("log-record-1", Log());

        Assert.Equal(7, trace.Fields!.Count);
        Assert.Equal(8, span.Fields!.Count);
        Assert.Equal(6, point.Fields!.Count);
        Assert.Equal(7, log.Fields!.Count);
        Assert.Equal("trace-1", Assert.Single(trace.Fields[RecordFields.TraceId]).CanonicalValue);
        Assert.Equal(Timestamp.ToUniversalTime().ToString("O"), Assert.Single(point.Fields[RecordFields.Timestamp]).CanonicalValue);
        Assert.Equal("Information", Assert.Single(log.Fields[RecordFields.SeverityText]).CanonicalValue);
    }

    [Fact]
    public void Optional_query_fields_are_absent_instead_of_becoming_empty_filter_values()
    {
        var point = Point() with { TraceId = null, SpanId = null };
        var log = Log() with { SeverityNumber = null, TraceId = null, SpanId = null };

        var pointFields = _serializer.ToRecord("point-record-1", point).Fields!;
        var logFields = _serializer.ToRecord("log-record-1", log).Fields!;

        Assert.DoesNotContain(RecordFields.TraceId, pointFields.Keys);
        Assert.DoesNotContain(RecordFields.SpanId, pointFields.Keys);
        Assert.DoesNotContain(RecordFields.SeverityNumber, logFields.Keys);
        Assert.DoesNotContain(RecordFields.TraceId, logFields.Keys);
        Assert.DoesNotContain(RecordFields.SpanId, logFields.Keys);
    }

    [Fact]
    public void Wrong_kind_and_malformed_payloads_are_visible_as_record_payload_failures()
    {
        var logRecord = Committed(_serializer.ToRecord("log-record-1", Log()));
        var malformed = logRecord with { Payload = "{" };

        Assert.Throws<RecordPayloadException>(() => _serializer.ToTrace(logRecord));
        var exception = Assert.Throws<RecordPayloadException>(() => _serializer.ToLog(malformed));
        Assert.NotNull(exception.InnerException);

        var missingRequiredFields = logRecord with
        {
            Payload = "{\"schemaVersion\":1,\"kind\":\"log\",\"value\":{\"id\":\"log-1\"}}"
        };
        Assert.Throws<RecordPayloadException>(() => _serializer.ToLog(missingRequiredFields));
    }

    [Fact]
    public void Missing_required_domain_values_and_null_attributes_fail_before_append()
    {
        Assert.Throws<RecordPayloadException>(() =>
            _serializer.ToRecord("trace-record", Trace() with { TraceId = " " }));
        Assert.Throws<RecordPayloadException>(() =>
            _serializer.ToRecord("trace-record", Trace() with { ResourceIds = [] }));
        Assert.Throws<RecordPayloadException>(() =>
            _serializer.ToRecord("point-record", Point() with { Attributes = null! }));
    }

    [Fact]
    public void Null_and_duplicate_persisted_attribute_keys_are_visible_payload_failures()
    {
        var input = _serializer.ToRecord("point-record", Point());
        var duplicate = WithMutatedAttributes(input, attributes => attributes.Add(attributes[0]!.DeepClone()));
        var nullKey = WithMutatedAttributes(input, attributes => attributes[0]!["key"] = null);

        Assert.Throws<RecordPayloadException>(() => _serializer.ToMetricPoint(duplicate));
        Assert.Throws<RecordPayloadException>(() => _serializer.ToMetricPoint(nullKey));
    }

    private static DiagnosticRecord Committed(DiagnosticRecordInput input) => new(
        input.RecordId,
        input.OccurredAt,
        input.Payload,
        new DiagnosticCursor("1"),
        input.Fields);

    private static DiagnosticRecord WithMutatedAttributes(
        DiagnosticRecordInput input,
        Action<JsonArray> mutate)
    {
        var payload = JsonNode.Parse(input.Payload)!;
        var attributes = payload["value"]!["attributes"]!.AsArray();
        mutate(attributes);
        return Committed(input) with { Payload = payload.ToJsonString() };
    }

    private static TelemetryTrace Trace() => new(
        "trace-1",
        "root-span",
        "orders",
        Timestamp,
        Timestamp.AddMilliseconds(15),
        TimeSpan.FromMilliseconds(15),
        SpanStatus.Ok,
        ["resource-1", "resource-2"],
        ["workflow-1"],
        2);

    private static TelemetrySpan Span(IDictionary<string, string?> attributes) => new(
        "span-domain-id",
        "trace-1",
        "span-1",
        "root-span",
        "resource-1",
        "process-order",
        "internal",
        Timestamp,
        Timestamp.AddMilliseconds(10),
        SpanStatus.Ok,
        "complete",
        attributes,
        [new("accepted", Timestamp.AddMilliseconds(1), Attributes(("event-z", null), ("event-a", "1")))],
        [new("trace-linked", "span-linked", Attributes(("link-z", "2"), ("link-a", "1")))]);

    private static MetricPoint Point() => new(
        "point-1",
        "instrument-1",
        "request.duration",
        "resource-1",
        Timestamp,
        -0d,
        42.5,
        3,
        Attributes(("point-z", null), ("point-a", "1")),
        "trace-1",
        "span-1");

    private static OtlpLogRecord Log() => new(
        "log-1",
        "resource-1",
        Timestamp,
        "Information",
        9,
        "order accepted",
        "trace-1",
        "span-1",
        Attributes(("log-z", null), ("log-a", "1")));

    private static Dictionary<string, string?> Attributes(params (string Key, string? Value)[] values)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
            result.Add(key, value);
        return result;
    }

    private static void AssertAttributes(
        IDictionary<string, string?> expected,
        IDictionary<string, string?> actual) =>
        Assert.Equal(
            expected.OrderBy(x => x.Key, StringComparer.Ordinal),
            actual.OrderBy(x => x.Key, StringComparer.Ordinal));
}
