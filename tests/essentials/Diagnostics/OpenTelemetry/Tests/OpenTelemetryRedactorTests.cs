using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Services;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests.Services;

public class OpenTelemetryRedactorTests
{
    [Fact]
    public void Redact_WhenSensitiveAttributeNameExists_RedactsValue()
    {
        var redactor = new OpenTelemetryRedactor(OptionsFactory.Create(new OpenTelemetryDiagnosticsOptions()));
        var log = new OtlpLogRecord("1", "resource", DateTimeOffset.UtcNow, "Information", null, "hello", null, null, new Dictionary<string, string?> { ["password"] = "secret" });
        var batch = new OpenTelemetryBatch([], [], [], [], [], [log]);

        var result = redactor.Redact(batch);

        Assert.Equal("[Redacted]", result.Logs.Single().Attributes["password"]);
    }

    [Fact]
    public void Redact_WhenAttributeKeysDifferOnlyByCase_UsesLastValue()
    {
        var redactor = new OpenTelemetryRedactor(OptionsFactory.Create(new OpenTelemetryDiagnosticsOptions()));
        var log = new OtlpLogRecord("1", "resource", DateTimeOffset.UtcNow, "Information", null, "hello", null, null, new Dictionary<string, string?>
        {
            ["Password"] = "first",
            ["password"] = "second"
        });
        var batch = new OpenTelemetryBatch([], [], [], [], [], [log]);

        var result = redactor.Redact(batch);

        var attributes = result.Logs.Single().Attributes;
        Assert.Single(attributes);
        Assert.Equal("[Redacted]", attributes["Password"]);
    }

    [Fact]
    public void Redact_WhenSpanEventNameContainsSensitiveText_MasksName()
    {
        var redactor = new OpenTelemetryRedactor(OptionsFactory.Create(new OpenTelemetryDiagnosticsOptions()));
        var spanEvent = new TelemetrySpanEvent("Bearer abcDEF123.token-value", DateTimeOffset.UtcNow, new Dictionary<string, string?>());
        var span = new TelemetrySpan("1", "trace", "span", null, "resource", "op", "internal", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SpanStatus.Ok, null, new Dictionary<string, string?>(), [spanEvent], []);
        var batch = new OpenTelemetryBatch([], [], [span], [], [], []);

        var result = redactor.Redact(batch);

        var name = result.Spans.Single().Events.Single().Name;
        Assert.DoesNotContain("abcDEF123", name);
        Assert.Contains("[Redacted]", name);
    }

    [Fact]
    public void Redact_SensitiveNameIsRedactedAcrossAllThreeSignals()
    {
        // Issue #376: the same sensitive value must be redacted on Spans, Traces AND Logs.
        // Previously batch.Traces passed through unredacted because the `with` expression omitted Traces.
        const string sensitive = "password=hunter2";
        var redactor = new OpenTelemetryRedactor(OptionsFactory.Create(new OpenTelemetryDiagnosticsOptions()));

        var span = new TelemetrySpan("s1", "t1", sensitive, null, "resource", "op", "internal", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
        var trace = new TelemetryTrace("t1", "s1", sensitive, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.Zero, SpanStatus.Ok, [], [], 1);
        var log = new OtlpLogRecord("l1", "resource", DateTimeOffset.UtcNow, "Information", null, sensitive, null, null, new Dictionary<string, string?>());
        var batch = new OpenTelemetryBatch([], [trace], [span], [], [], [log]);

        var result = redactor.Redact(batch);

        Assert.DoesNotContain("hunter2", result.Spans.Single().Name);
        Assert.DoesNotContain("hunter2", result.Traces.Single().Name);
        Assert.DoesNotContain("hunter2", result.Logs.Single().Body);
        Assert.Equal("[Redacted]", result.Traces.Single().Name);
    }
}
