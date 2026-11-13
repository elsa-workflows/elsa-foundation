using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Tests.VariableLifecycle.Benchmarks;

/// <summary>
/// T002 / T038 (spec 1221, SC-005, Open question B): the measurement harness for what the variable lifecycle adds to a
/// write. It measures the digest per applied write on large payloads, the cost of a versioned frame write in a hot loop
/// against an unversioned baseline, and the stored size of a write-history record. It is a measurement, not a gate:
/// it runs only when <c>ELSA_MEASURE_VARIABLE_LIFECYCLE=1</c> and prints its numbers; the always-on assertions only check
/// that the measured operations behave.
/// </summary>
public sealed class VariableLifecycleOverheadMeasurement(ITestOutputHelper output)
{
    private const string Key = "var-payload";
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("ELSA_MEASURE_VARIABLE_LIFECYCLE") == "1";

    [Fact]
    public void Measure_digest_hot_loop_and_history_record_overhead()
    {
        var iterations = Enabled ? 2_000 : 20;
        foreach (var size in new[] { 1_024, 64 * 1_024, 1_024 * 1_024 })
        {
            var envelope = Payload(size, 0);
            VariableValueDigest.Compute(envelope);
            var digestRuns = Enabled ? Math.Max(5, 50_000_000 / size) : 2;
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < digestRuns; i++)
                VariableValueDigest.Compute(envelope);
            watch.Stop();
            // Baseline: hashing the stored JSON as it is, with no canonical form, to show what canonicalization costs.
            var raw = Stopwatch.StartNew();
            for (var i = 0; i < digestRuns; i++)
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(envelope.InlineValue!.Value.GetRawText()));
            raw.Stop();
            Report($"digest {size / 1024,5} KiB: canonical {watch.Elapsed.TotalMicroseconds / digestRuns:F1} µs, raw-hash baseline {raw.Elapsed.TotalMicroseconds / digestRuns:F1} µs per applied write");
        }

        // Hot loop: a counter written N times, versioned (Commit, the shipped path) versus the bare frame copy it replaces.
        var frame = Frame(Int(0));
        var versioned = Stopwatch.StartNew();
        for (var i = 1; i <= iterations; i++)
            frame = frame.Commit(Key, Int(i), frame.Revision, new VariableWriteBase(frame.VersionOf(Key).Version, null), LostUpdateMode.Default).Frame;
        versioned.Stop();
        Assert.Equal(iterations, frame.VersionOf(Key).Version);

        var bare = Frame(Int(0));
        var baseline = Stopwatch.StartNew();
        for (var i = 1; i <= iterations; i++)
        {
            var values = bare.Values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            values[Key] = Int(i);
            bare = new VariableFrameState(bare.FrameId, bare.ScopeId, bare.ActivationId, null, VariableFrameKind.Root, values, bare.Revision + 1);
        }
        baseline.Stop();
        Report($"hot loop ({iterations} writes): versioned {versioned.Elapsed.TotalMicroseconds / iterations:F2} µs/write, unversioned {baseline.Elapsed.TotalMicroseconds / iterations:F2} µs/write");

        var before = Frame(Int(0)).Set(Key, Int(1), 0);
        var write = before.Commit(Key, Int(7), before.Revision, new VariableWriteBase(0, null), LostUpdateMode.Default,
            new VariableWriter(VariableWriteSource.Capture, "node-right", "actexec-right", "actexec-right:attempt:1"));
        var record = VariableWriteRecord.From(before, write, Key, write.Frame.VersionOf(Key).Writer!, DateTimeOffset.UnixEpoch);
        var metadataOnly = record with { };
        var applied = new VariableWriteRecord(record.ScopeId, record.VariableKey, record.Writer, VariableCommitOutcome.Current, 1, 1, 2, 2, DateTimeOffset.UnixEpoch);
        Report($"history record: applied write {Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(applied))} B; lost-update warning with overwritten value {Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(metadataOnly))} B");
        Assert.True(record.IsLostUpdate);
        Assert.Equal(1, record.OverwrittenValue!.InlineValue!.Value.GetInt32());
    }

    private void Report(string line)
    {
        if (Enabled)
            output.WriteLine(line);
    }

    private static VariableFrameState Frame(ValueEnvelope value) =>
        new("frame:root", "workflow", "wfexec-1", null, VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { [Key] = value });

    private static ValueEnvelope Int(int value) =>
        ValueEnvelope.Inline(new ValueTypeDescriptor("Int32"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    private static ValueEnvelope Payload(int bytes, int seed)
    {
        var items = Enumerable.Range(0, Math.Max(1, bytes / 48))
            .Select(index => new { id = index + seed, name = $"item-{index:D8}", price = index * 1.25m })
            .ToArray();
        return ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonSerializer.SerializeToElement(items), ValueProtectionPolicy.InstanceInline);
    }
}
