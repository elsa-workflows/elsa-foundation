using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.VariableLifecycle;

/// <summary>T008 (spec 1221 FR-009, contracts/commit-rule.md): the one commit rule every write site evaluates.</summary>
public sealed class CommitRuleTests
{
    private const string Key = "var-count";
    private static readonly ValueTypeDescriptor Int32Type = new("Int32");

    [Fact]
    public void A_blind_write_applies_and_is_never_a_lost_update()
    {
        var frame = Frame(0).Commit(Key, Int(5), 0, writeBase: null, LostUpdateMode.Fault);

        Assert.Equal(VariableCommitOutcome.Blind, frame.Decision.Outcome);
        Assert.False(frame.Decision.IsLostUpdate);
        Assert.Equal(5, Value(frame.Frame));
        Assert.Equal(1, frame.Frame.VersionOf(Key).Version);
    }

    [Fact]
    public void A_write_on_the_current_version_applies()
    {
        var frame = Frame(0);
        var write = frame.Commit(Key, Int(1), frame.Revision, new VariableWriteBase(0, null), LostUpdateMode.Default);

        Assert.Equal(VariableCommitOutcome.Current, write.Decision.Outcome);
        Assert.Equal(1, Value(write.Frame));
    }

    [Fact]
    public void Default_mode_applies_a_stale_write_with_a_lost_update_and_keeps_the_overwritten_value()
    {
        var advanced = Frame(0).Set(Key, Int(1), 0);
        var write = advanced.Commit(Key, Int(7), advanced.Revision, new VariableWriteBase(0, null), LostUpdateMode.Default);

        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, write.Decision.Outcome);
        Assert.True(write.Decision.IsLostUpdate);
        Assert.Equal(7, Value(write.Frame));
        Assert.Equal(1, write.OverwrittenValue!.InlineValue!.Value.GetInt32());
        Assert.Equal(2, write.Frame.VersionOf(Key).Version);
    }

    [Fact]
    public void Default_mode_warns_when_the_value_moved_and_came_back()
    {
        var frame = Frame(0).Set(Key, Int(1), 0);
        frame = frame.Set(Key, Int(0), frame.Revision);
        var write = frame.Commit(Key, Int(1), frame.Revision, new VariableWriteBase(0, null), LostUpdateMode.Default);

        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, write.Decision.Outcome);
        Assert.Equal(1, Value(write.Frame));
    }

    [Fact]
    public void Fault_mode_applies_a_returned_value_and_records_the_mismatch()
    {
        var initial = Frame(0);
        var baseDigest = initial.CurrentDigestOf(Key);
        var frame = initial.Set(Key, Int(1), 0);
        frame = frame.Set(Key, Int(0), frame.Revision);

        var write = frame.Commit(Key, Int(1), frame.Revision, new VariableWriteBase(0, baseDigest), LostUpdateMode.Fault);

        Assert.Equal(VariableCommitOutcome.ReturnedValue, write.Decision.Outcome);
        Assert.False(write.Decision.IsLostUpdate);
        Assert.Equal(1, Value(write.Frame));
    }

    [Fact]
    public void Fault_mode_refuses_a_real_lost_update_and_leaves_the_frame_unchanged()
    {
        var initial = Frame(0);
        var baseDigest = initial.CurrentDigestOf(Key);
        var frame = initial.Set(Key, Int(1), 0);

        var write = frame.Commit(Key, Int(1), frame.Revision, new VariableWriteBase(0, baseDigest), LostUpdateMode.Fault);

        Assert.Equal(VariableCommitOutcome.LostUpdateFault, write.Decision.Outcome);
        Assert.False(write.Decision.Applies);
        Assert.Same(frame, write.Frame);
    }

    [Fact]
    public void An_unchanged_value_applies_nothing_but_two_writers_of_the_same_value_are_not_hidden()
    {
        var first = Frame(0).Commit(Key, Int(1), 0, new VariableWriteBase(0, null), LostUpdateMode.Default);
        var second = first.Frame.Commit(Key, Int(1), first.Frame.Revision, new VariableWriteBase(0, null), LostUpdateMode.Default);

        Assert.Equal(VariableCommitOutcome.Current, first.Decision.Outcome);
        // The second writer also read 0 and wrote 1: a lost update, not a silent no-op.
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, second.Decision.Outcome);
        // It applies nothing: the value and version stay.
        Assert.False(second.Decision.ChangesValue);
        Assert.Same(first.Frame, second.Frame);
        Assert.Equal(1, second.Frame.VersionOf(Key).Version);
    }

    [Fact]
    public void Equal_values_in_different_spellings_have_one_digest()
    {
        var a = ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonDocument.Parse("""{"b":1.0,"a":"x"}""").RootElement, ValueProtectionPolicy.InstanceInline);
        var b = ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonDocument.Parse("""{"a":"x","b":1}""").RootElement, ValueProtectionPolicy.InstanceInline);

        Assert.Equal(Digest(a), Digest(b));
        Assert.NotEqual(Digest(a), Digest(Int(1)));

        // Escaped and plain strings, and numbers with and without an exponent, digest by meaning.
        var escaped = Parse("{\"a\":\"\\u0078\",\"n\":[1.50,100,-0.0,0.001]}");
        var plain = Parse("{\"a\":\"x\",\"n\":[15e-1,1E2,0,1e-3]}");
        Assert.Equal(Digest(escaped), Digest(plain));
        Assert.NotEqual(Digest(plain), Digest(Parse("{\"a\":\"x\",\"n\":[15e-1,1E2,0,1e-2]}")));
    }

    [Fact]
    public void A_frame_stored_before_versions_reads_back_at_version_zero_and_round_trips_its_versions()
    {
        var legacy = JsonSerializer.Serialize(Frame(3));
        Assert.DoesNotContain("Versions", legacy, StringComparison.Ordinal);

        var readBack = JsonSerializer.Deserialize<VariableFrameState>(legacy)!;
        Assert.Equal(VariableVersion.Initial, readBack.VersionOf(Key));

        var written = readBack.Set(Key, Int(4), readBack.Revision);
        var roundTripped = JsonSerializer.Deserialize<VariableFrameState>(JsonSerializer.Serialize(written))!;
        Assert.Equal(written.VersionOf(Key), roundTripped.VersionOf(Key));
        Assert.Equal(1, roundTripped.VersionOf(Key).Version);
    }

    private static VariableFrameState Frame(int count) =>
        new("frame:root", "workflow", "wfexec-1", null, VariableFrameKind.Root,
            new Dictionary<string, ValueEnvelope> { [Key] = Int(count) });

    // The digest a version-0 variable gets: what a frame computes once for the stored value.
    private static string Digest(ValueEnvelope value) =>
        new VariableFrameState("frame:digest", "workflow", "wfexec-digest", null, VariableFrameKind.Root,
            new Dictionary<string, ValueEnvelope> { ["value"] = value }).CurrentDigestOf("value");

    private static ValueEnvelope Int(int value) =>
        ValueEnvelope.Inline(Int32Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    private static ValueEnvelope Parse(string json) =>
        ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonDocument.Parse(json).RootElement, ValueProtectionPolicy.InstanceInline);

    private static int Value(VariableFrameState frame) => frame.Values[Key].InlineValue!.Value.GetInt32();
}
