using System.Collections.Generic;
using System.Threading.Tasks;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Stages what another build did to a database's finalization record (spec 181), so a test can put a family at a version
/// its own build has not finalized. Each test project that stages one compiles this file in, the same way EF module test
/// projects compile in <c>UnorderedRowLimitGuard</c>.
/// </summary>
internal static class EfSchemaFinalizationTestSupport
{
    /// <summary>The host a newer build runs on, where a test does not name one.</summary>
    public static SchemaFinalizationMember NewerHost { get; } = new("newer-host", "newer");

    /// <summary>
    /// Finalizes <paramref name="version"/> for <paramref name="family"/> as a build whose chain is <paramref name="chain"/>
    /// would: the record is created at the chain's first version when it has none, then <paramref name="member"/>
    /// (<see cref="NewerHost"/> by default) records the intent and commits it. Nothing contends with a staged
    /// finalization, so each write must apply. A record already finalized at <paramref name="version"/> is left as it is.
    /// </summary>
    /// <returns>The record as finalized.</returns>
    public static async Task<SchemaFinalizationRecord> FinalizeAsync(
        EfSchemaFinalizationStore store,
        string family,
        IReadOnlyList<string> chain,
        string version,
        SchemaFinalizationMember? member = null)
    {
        member ??= NewerHost;
        var record = await store.GetOrCreateAsync(family, chain[0], chain, SchemaFinalizationActor.Of(member));
        if (record.FinalizedVersion == version)
            return record;

        var intended = await store.RecordIntentAsync(family, record.Revision, version, chain, member);
        Assert.True(intended.Applied, $"Staging '{family}' at '{version}': the intent lost its compare-and-set, yet nothing contends with it.");
        var finalized = await store.CommitIntentAsync(family, intended.Record.Revision, chain, member);
        Assert.True(finalized.Applied, $"Staging '{family}' at '{version}': the commit lost its compare-and-set, yet nothing contends with it.");
        return finalized.Record;
    }
}
