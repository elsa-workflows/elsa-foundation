using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for the bounded successor-id derivation (#923). The previous scheme concatenated the full parent id
/// at every hop, so ids grew quadratically with workflow progress and overflowed the 128-char <c>by-incident-id</c>
/// projection column (#922). These tests pin the three properties the runtime relies on: the derived id stays
/// bounded regardless of chain depth, the derivation is deterministic (so idempotency / dedupe by id equality keeps
/// working across replay), and distinct steps / ancestries yield distinct ids.
/// </summary>
public sealed class RuntimeChainIdTests
{
    [Fact]
    public void Fingerprint_IsFixedLengthLowerHex()
    {
        var fingerprint = RuntimeChainId.Fingerprint("workitem-1:invoke:activity-7");

        Assert.Equal(RuntimeChainId.FingerprintLength, fingerprint.Length);
        Assert.Matches("^[0-9a-f]+$", fingerprint);
    }

    [Fact]
    public void Fingerprint_IsDeterministic()
    {
        Assert.Equal(RuntimeChainId.Fingerprint("abc"), RuntimeChainId.Fingerprint("abc"));
        Assert.NotEqual(RuntimeChainId.Fingerprint("abc"), RuntimeChainId.Fingerprint("abd"));
    }

    [Fact]
    public void Derive_StaysBoundedRegardlessOfChainDepth()
    {
        // Walk a deep chain the way the scheduler handlers do: each hop derives its successor from the previous id.
        // Under the old concatenating scheme this id would be multi-KB after a handful of hops; here every hop's
        // length is FingerprintLength + 1 + step.Length, independent of depth.
        var id = "envelope-1";
        var previousLengths = new List<int>();
        for (var hop = 0; hop < 500; hop++)
        {
            id = RuntimeChainId.Derive(id, $"invoke:activity-{hop}");
            previousLengths.Add(id.Length);
        }

        var maxStepLength = "invoke:activity-499".Length;
        Assert.All(previousLengths, length => Assert.True(length <= RuntimeChainId.FingerprintLength + 1 + maxStepLength));
        // Comfortable margin under the 128-char projection column contract, even at depth 500.
        Assert.True(id.Length <= 128);
    }

    [Fact]
    public void Derive_IsDeterministicForSameInputs()
    {
        var parent = "envelope-1:schedule:root";

        Assert.Equal(
            RuntimeChainId.Derive(parent, "invoke:activity-1"),
            RuntimeChainId.Derive(parent, "invoke:activity-1"));
    }

    [Fact]
    public void Derive_ProducesDistinctIdsForDistinctSteps()
    {
        var parent = "envelope-1:schedule:root";

        Assert.NotEqual(
            RuntimeChainId.Derive(parent, "invoke:activity-1"),
            RuntimeChainId.Derive(parent, "invoke:activity-2"));
    }

    [Fact]
    public void Derive_ProducesDistinctIdsForDistinctParents()
    {
        Assert.NotEqual(
            RuntimeChainId.Derive("parent-a", "invoke:activity-1"),
            RuntimeChainId.Derive("parent-b", "invoke:activity-1"));
    }

    [Fact]
    public void Derive_KeepsStepReadable()
    {
        var id = RuntimeChainId.Derive("envelope-1", "schedule-child:node-7:activity-9");

        Assert.EndsWith(":schedule-child:node-7:activity-9", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Derive_RejectsBlankInputs(string? bad)
    {
        Assert.ThrowsAny<ArgumentException>(() => RuntimeChainId.Derive(bad!, "step"));
        Assert.ThrowsAny<ArgumentException>(() => RuntimeChainId.Derive("parent", bad!));
    }
}
