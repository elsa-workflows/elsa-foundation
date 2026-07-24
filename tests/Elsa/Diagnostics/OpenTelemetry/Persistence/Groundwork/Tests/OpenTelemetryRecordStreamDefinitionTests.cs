using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Groundwork.DiagnosticRecords;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class OpenTelemetryRecordStreamDefinitionTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 14, 10, 11, 12, TimeSpan.Zero);

    [Fact]
    public void Every_immutable_signal_has_a_valid_bounded_distinct_stream_definition()
    {
        var definitions = Definitions();

        Assert.Equal(4, definitions.Select(x => x.LogicalStorageName).Distinct(StringComparer.Ordinal).Count());
        foreach (var definition in definitions)
        {
            DiagnosticRecordStreamDefinitionValidator.ValidateAndThrow(definition);
            Assert.Equal(CanonicalRecordSerializer.SchemaVersion, definition.SchemaVersion);
            Assert.Equal(definition.Fields.Count, definition.Limits.MaxFieldsPerRecord);
            Assert.InRange(definition.Limits.MaxBatchRecords, 1, 1_000);
            Assert.InRange(definition.Limits.MaxQueryLimit, 1, 5_000);
            Assert.All(definition.Fields, field => Assert.NotEmpty(field.SupportedPredicates));
            Assert.All(
                definition.Fields.Where(x => x.Type == DiagnosticFieldType.String && x.Name != RecordFields.OrderKey),
                field => Assert.Equal(DiagnosticStringCasePolicy.UnicodeOrdinalIgnoreCase, field.CasePolicy));
            var orderKey = definition.Fields.SingleOrDefault(x => x.Name == RecordFields.OrderKey);
            if (orderKey is not null)
            {
                Assert.True(orderKey.IsOrderable);
                Assert.Equal(DiagnosticStringCasePolicy.Ordinal, orderKey.CasePolicy);
            }
        }
    }

    [Fact]
    public void Definitions_declare_the_portable_unicode_range_order_membership_and_latest_query_contract()
    {
        var trace = OpenTelemetryRecordStreamDefinitions.CreateTraces("traces");
        var span = OpenTelemetryRecordStreamDefinitions.CreateSpans("spans");
        var point = OpenTelemetryRecordStreamDefinitions.CreateMetricPoints("points");
        var log = OpenTelemetryRecordStreamDefinitions.CreateLogs("logs");

        var traceId = Field(trace, RecordFields.TraceId);
        Assert.True(traceId.SupportsLatestPerKey);
        Assert.Equal(DiagnosticStringCasePolicy.UnicodeOrdinalIgnoreCase, traceId.CasePolicy);
        Assert.Contains(DiagnosticPredicateOperator.Contains, traceId.SupportedPredicates);

        var resourceId = Field(trace, RecordFields.ResourceId);
        Assert.Equal(DiagnosticFieldCardinality.Multiple, resourceId.Cardinality);
        Assert.True(resourceId.IsRequired);
        Assert.Equal(256, resourceId.MaxValues);

        AssertOrderableRange(Field(trace, RecordFields.StartTime));
        AssertOrderableRange(Field(span, RecordFields.StartTime));
        AssertOrderableRange(Field(point, RecordFields.Timestamp));
        AssertOrderableRange(Field(log, RecordFields.Timestamp));
        Assert.Contains(DiagnosticPredicateOperator.Contains, Field(log, RecordFields.Body).SupportedPredicates);
    }

    [Fact]
    public void Unicode_query_fields_remain_valid_raw_ordinal_values()
    {
        var definition = OpenTelemetryRecordStreamDefinitions.CreateTraces("traces");
        var record = new CanonicalRecordSerializer().ToRecord(
            "trace-record-unicode",
            new TelemetryTrace(
                "trace-één",
                "span-hoofd",
                "订单 verwerken",
                Timestamp,
                Timestamp.AddSeconds(1),
                TimeSpan.FromSeconds(1),
                SpanStatus.Ok,
                ["resource-tōkyō"],
                ["workflow-δοκιμή"],
                1));

        DiagnosticRecordRequestValidator.Validate(
            DiagnosticRecordBatch.Create(
                new("tenant", "scope"),
                definition.Stream,
                new(Timestamp, "append-unicode"),
                [record]),
            definition);
    }

    [Fact]
    public void Canonical_record_fields_are_fully_declared_by_the_corresponding_stream()
    {
        var serializer = new CanonicalRecordSerializer();
        var attributes = new Dictionary<string, string?> { ["key"] = "value" };

        var records = new[]
        {
            (serializer.ToRecord("trace-record", new TelemetryTrace(
                    "trace-1", "root", "orders", Timestamp, Timestamp.AddSeconds(1), TimeSpan.FromSeconds(1),
                    SpanStatus.Ok, ["resource-1"], ["workflow-1"], 1), ["orders"]),
                OpenTelemetryRecordStreamDefinitions.CreateTraces("traces")),
            (serializer.ToRecord("span-record", new TelemetrySpan(
                    "span-record", "trace-1", "span-1", "root", "resource-1", "orders", "internal",
                    Timestamp, Timestamp.AddSeconds(1), SpanStatus.Ok, null, attributes, [], [])),
                OpenTelemetryRecordStreamDefinitions.CreateSpans("spans")),
            (serializer.ToRecord("point-record", new MetricPoint(
                    "point-1", "instrument-1", "request.duration", "resource-1", Timestamp, 1, null, null,
                    attributes, "trace-1", "span-1"), "orders"),
                OpenTelemetryRecordStreamDefinitions.CreateMetricPoints("points")),
            (serializer.ToRecord("log-record", new OtlpLogRecord(
                    "log-1", "resource-1", Timestamp, "Information", 9, "accepted", "trace-1", "span-1",
                    attributes), "orders"),
                OpenTelemetryRecordStreamDefinitions.CreateLogs("logs"))
        };

        foreach (var (record, definition) in records)
        {
            Assert.Equal(
                definition.Fields.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal),
                record.Fields!.Keys.OrderBy(x => x, StringComparer.Ordinal));
            DiagnosticRecordRequestValidator.Validate(
                DiagnosticRecordBatch.Create(
                    new("tenant", "scope"),
                    definition.Stream,
                    new(Timestamp, $"append-{definition.Stream.Value}"),
                    [record]),
                definition);
        }
    }

    private static DiagnosticRecordStreamDefinition[] Definitions() =>
    [
        OpenTelemetryRecordStreamDefinitions.CreateTraces("traces"),
        OpenTelemetryRecordStreamDefinitions.CreateSpans("spans"),
        OpenTelemetryRecordStreamDefinitions.CreateMetricPoints("points"),
        OpenTelemetryRecordStreamDefinitions.CreateLogs("logs")
    ];

    private static DiagnosticFieldDefinition Field(DiagnosticRecordStreamDefinition definition, string name) =>
        Assert.Single(definition.Fields, x => StringComparer.Ordinal.Equals(x.Name, name));

    private static void AssertOrderableRange(DiagnosticFieldDefinition field)
    {
        Assert.True(field.IsOrderable);
        Assert.Contains(DiagnosticPredicateOperator.RangeInclusive, field.SupportedPredicates);
    }
}
