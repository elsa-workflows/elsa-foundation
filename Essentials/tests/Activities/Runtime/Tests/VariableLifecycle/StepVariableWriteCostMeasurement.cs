using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1231 T042 (FR-035, SC-007): what a step write costs. A session read hands out a JSON copy of the committed value
/// (no serialization); a session write serializes the written value once, and its commit takes one canonical digest of
/// it (S-10's <c>VariableValueDigest</c>) and records a write-history entry. Measured for a 1 MiB value and a hot loop of
/// small writes. A measurement, not a gate: it prints only when <c>ELSA_MEASURE_VARIABLE_LIFECYCLE=1</c>; the always-on
/// assertions only check that the measured operations behave.
/// </summary>
public sealed class StepVariableWriteCostMeasurement(ITestOutputHelper output)
{
    private const string Key = "var-order";
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("ELSA_MEASURE_VARIABLE_LIFECYCLE") == "1";
    private static readonly ValueTypeDescriptor AnyType = new("Elsa.Any");

    [Fact]
    public void Measure_read_write_and_commit_of_a_large_value_and_a_hot_loop()
    {
        foreach (var size in new[] { 1_024, 64 * 1_024, 1_024 * 1_024 })
        {
            var stored = Payload(size);
            var frame = Frame(stored);
            var runs = Enabled ? Math.Max(5, 20_000_000 / size) : 2;

            // Read: the copy a session read hands out (JsonNode from the stored element). Nothing is serialized.
            var read = Stopwatch.StartNew();
            JsonNode? node = null;
            for (var i = 0; i < runs; i++)
                node = JsonNode.Parse(stored.InlineValue!.Value.GetRawText());
            read.Stop();

            // Write: serialize the changed value once into the intent's envelope.
            node!["total"] = 1;
            var write = Stopwatch.StartNew();
            ValueEnvelope? intent = null;
            for (var i = 0; i < runs; i++)
                intent = ValueEnvelope.Inline(AnyType, JsonSerializer.SerializeToElement(node), ValueProtectionPolicy.InstanceInline);
            write.Stop();

            // Commit: the commit rule, with its one canonical digest of the new value and the history record.
            var writer = new VariableWriter(VariableWriteSource.SessionChange, "node", "actexec", "attempt-1");
            var commit = Stopwatch.StartNew();
            VariableFrameWrite? committed = null;
            for (var i = 0; i < runs; i++)
            {
                committed = frame.Commit(Key, intent!, frame.Revision, new VariableWriteBase(0, null), LostUpdateMode.Default, writer);
                _ = VariableWriteRecord.From(frame, committed, Key, writer, DateTimeOffset.UnixEpoch);
            }
            commit.Stop();
            Assert.Equal(1, committed!.Frame.VersionOf(Key).Version);
            Report($"{size / 1024,5} KiB: read copy {Ms(read, runs)} ms, write serialize {Ms(write, runs)} ms, commit (digest + record) {Ms(commit, runs)} ms per step");
        }

        // Hot loop: a counter written N times through the session write path (serialize, commit, record, advance base).
        var iterations = Enabled ? 5_000 : 20;
        var counter = Frame(ValueEnvelope.Inline(new ValueTypeDescriptor("Int32"), JsonSerializer.SerializeToElement(0), ValueProtectionPolicy.InstanceInline));
        var loopWriter = new VariableWriter(VariableWriteSource.SessionChange, "node", "actexec", "attempt-1");
        var loop = Stopwatch.StartNew();
        for (var i = 1; i <= iterations; i++)
        {
            var value = ValueEnvelope.Inline(new ValueTypeDescriptor("Int32"), JsonSerializer.SerializeToElement(i), ValueProtectionPolicy.InstanceInline);
            var result = counter.Commit(Key, value, counter.Revision, new VariableWriteBase(counter.VersionOf(Key).Version, null), LostUpdateMode.Default, loopWriter);
            _ = VariableWriteRecord.From(counter, result, Key, loopWriter, DateTimeOffset.UnixEpoch);
            counter = result.Frame;
        }
        loop.Stop();
        Assert.Equal(iterations, counter.VersionOf(Key).Version);
        Report($"hot loop ({iterations} small session writes): {loop.Elapsed.TotalMicroseconds / iterations:F2} µs per write");
    }

    private static string Ms(Stopwatch watch, int runs) => (watch.Elapsed.TotalMilliseconds / runs).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

    private void Report(string line)
    {
        if (Enabled)
            output.WriteLine(line);
    }

    private static VariableFrameState Frame(ValueEnvelope value) =>
        new("frame-root", "workflow", "run-1", null, VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { [Key] = value });

    private static ValueEnvelope Payload(int size)
    {
        var builder = new StringBuilder("{\"total\":10,\"status\":\"open\",\"lines\":[");
        var line = 0;
        while (builder.Length < size)
            builder.Append(line == 0 ? "" : ",").Append($"{{\"sku\":\"item-{line}\",\"quantity\":{line % 7},\"price\":12.50}}").Append(line++ == int.MaxValue ? "" : "");
        builder.Append("]}");
        return ValueEnvelope.Inline(AnyType, JsonDocument.Parse(builder.ToString()).RootElement.Clone(), ValueProtectionPolicy.InstanceInline);
    }
}
