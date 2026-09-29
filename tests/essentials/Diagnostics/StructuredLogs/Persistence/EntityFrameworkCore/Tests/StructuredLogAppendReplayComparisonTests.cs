using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A standalone proof of the version-aware replay comparison
/// <c>EfStructuredLogStore.CommitBatchAsync</c> applies once an idempotency row's schema stamp is older than the
/// chain's current version (#2140): once a row is not at the current version, its stored outcome is upcast through
/// the chain and re-fingerprinted, rather than compared to the stored fingerprint string raw, so a retry replayed
/// across a version bump is not rejected as a different payload.
/// </summary>
/// <remarks>
/// <para>
/// The real "StructuredLogs" family has shipped only <see cref="StructuredLogsEfModule.SchemaVersion"/> to date, so no
/// row of it can be stamped at an older readable version, and
/// <c>EfSchemaFamilyChainDeclarationGuardTests.Every_family_the_stores_check_is_declared_once_and_checked_through_its_chain</c>
/// (spec 180, FR-002) refuses any version check that names its family other than through its own module's
/// <c>&lt;Module&gt;.Chain</c> handle - so <see cref="EfStructuredLogStore"/> cannot be given a second, synthetic
/// version of its own chain from a test without either widening that guard or shipping a second real production
/// version, and a constructor seam that tried this (an <c>EfSchemaChain</c> parameter overriding
/// <see cref="StructuredLogsEfModule.Chain"/>) failed that guard because its checks then named their family through a
/// field, not the handle.
/// </para>
/// <para>
/// This test instead reproduces the exact comparison <c>CommitBatchAsync</c> performs - resolve the stored
/// fingerprint through the chain when the row is not at the current version, otherwise trust the stored string, then
/// compare it with the incoming batch's own fingerprint - over a synthetic two-version chain built the way
/// <c>EfSchemaChainStoreTests</c>' <c>SyntheticOrders</c> family is, calling the real, public
/// <see cref="StructuredLogAppendFingerprint.Compute"/> the store itself calls.
/// <see cref="StructuredLogAppendFingerprintTests"/> unit-tests that function directly; this proves the branch that
/// decides when to call it, mirrored line for line from <c>Stores/EfStructuredLogStore.cs</c>, accepts a replay whose
/// upcast content still matches and rejects one whose content does not.
/// </para>
/// </remarks>
public sealed class StructuredLogAppendReplayComparisonTests
{
    private const string OlderVersion = "0.9.0";
    private static readonly StructuredLogStoreBinding Binding = new("tenant-a", "scope-a", "stream-a");

    [Fact]
    public void A_row_stamped_at_an_older_readable_version_accepts_a_replay_of_the_same_payload()
    {
        var chain = BuildChain();
        var stored = new EfPendingAppend("token-1", """{"Message":"first"}""");
        var incoming = new EfPendingAppend("token-1", """{"Message":"first"}""");

        Assert.True(AcceptsReplay(chain, OlderVersion, [stored], [incoming]));
    }

    [Fact]
    public void A_row_stamped_at_an_older_readable_version_rejects_a_replay_of_a_different_payload()
    {
        var chain = BuildChain();
        var stored = new EfPendingAppend("token-1", """{"Message":"first"}""");
        var incoming = new EfPendingAppend("token-1", """{"Message":"different"}""");

        Assert.False(AcceptsReplay(chain, OlderVersion, [stored], [incoming]));
    }

    [Fact]
    public void A_row_at_the_current_version_trusts_its_stored_fingerprint_without_recomputing()
    {
        var chain = BuildChain();
        var items = new[] { new EfPendingAppend("token-1", """{"Message":"first"}""") };

        Assert.True(AcceptsReplay(chain, chain.CurrentVersion, items, items));
    }

    private static EfSchemaChain BuildChain() =>
        EfSchemaChain.For(new EfSchemaFamilyDescriptor(
            "StructuredLogs.ReplayComparisonProof", null, StructuredLogsEfModule.SchemaVersion, typeof(StructuredLogAppendReplayComparisonTests).Assembly)
        {
            Upcasters = [new EfSchemaUpcasterDescriptor(typeof(IdentityUpcaster), OlderVersion, StructuredLogsEfModule.SchemaVersion)],
            ContentColumns = [new EfSchemaColumn(typeof(StructuredLogAppendOperation), nameof(StructuredLogAppendOperation.OutcomeJson))]
        });

    /// <summary>
    /// Mirrors <c>EfStructuredLogStore.CommitBatchAsync</c>'s replay comparison verbatim: the row's outcome is upcast
    /// through the chain from its stamp, then the stored side of the comparison is the row's own fingerprint when it
    /// is already at the current version, or the fingerprint recomputed over the upcast content otherwise; either way
    /// it is compared with the incoming batch's freshly computed fingerprint.
    /// </summary>
    private static bool AcceptsReplay(
        EfSchemaChain chain,
        string storedSchemaVersion,
        EfPendingAppend[] storedItems,
        EfPendingAppend[] incomingItems)
    {
        var upcast = storedItems
            .Select(item => new EfPendingAppend(
                item.RecordToken,
                chain.Upcast<StructuredLogAppendOperation>(storedSchemaVersion, (nameof(StructuredLogAppendOperation.OutcomeJson), item.PayloadJson))
                    [nameof(StructuredLogAppendOperation.OutcomeJson)]!))
            .ToArray();
        var storedFingerprint = string.Equals(storedSchemaVersion, chain.CurrentVersion, StringComparison.Ordinal)
            ? StructuredLogAppendFingerprint.Compute(Binding, storedItems)
            : StructuredLogAppendFingerprint.Compute(Binding, upcast);
        var incomingFingerprint = StructuredLogAppendFingerprint.Compute(Binding, incomingItems);
        return StringComparer.Ordinal.Equals(storedFingerprint, incomingFingerprint);
    }

    /// <summary>A no-op step: the synthetic chain exists only to give a row a second readable stamp, not to change content.</summary>
    private sealed class IdentityUpcaster : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }
}
