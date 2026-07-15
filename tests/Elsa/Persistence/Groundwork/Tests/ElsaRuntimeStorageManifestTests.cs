using Elsa.Persistence.Groundwork;
using Groundwork.Core.Indexing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Manifest-shape guards for the runtime storage manifest. These pin the declared index surface the store
/// bridges depend on so a rename or accidental removal fails loudly rather than silently degrading a query.
/// </summary>
public sealed class ElsaRuntimeStorageManifestTests
{
    [Fact]
    public void ActivityExecutionState_Declares_ByParent_Index_And_Query()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind);

        // The additive parent-scoped index (#514/#413 item 3) must be declared over the persisted nested field, alongside
        // the pre-existing by-workflow-execution index (this is additive, not a replacement).
        Assert.Contains(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex);

        var byParent = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex);
        Assert.Equal(ElsaRuntimeStorageManifest.ParentActivityExecutionIdField, Assert.Single(byParent.Fields).Path);
        Assert.Equal("state.parentActivityExecutionId", byParent.Fields[0].Path);

        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex);
    }

    [Fact]
    public void SchemaVersion_Stays_The_Frozen_Legacy_Stamp_Despite_The_Additive_Index()
    {
        // Adding an index must NOT change this constant. It is the frozen legacy stamp that
        // ElsaRuntimeDocumentVersions.Parse recognizes (only a positive integer or "1.0.0" is accepted); documents/tests
        // stamp with it, so any other value makes Parse reject every kind. Added-index backfill (Condition 7) triggers on
        // the physicalized index-set change, not on this string — the pre-existing bookmarkState by-stimulus index added
        // an index without bumping it too.
        Assert.Equal("1.0.0", ElsaRuntimeStorageManifest.SchemaVersion);
    }

    [Fact]
    public void Every_Query_Backed_Index_Is_An_Optimized_Physical_Projection()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();

        foreach (var unit in manifest.StorageUnits)
        {
            foreach (var query in unit.Queries)
            {
                var index = Assert.Single(unit.Indexes, candidate => candidate.Identity == query.IndexIdentity);
                Assert.Equal(IndexPhysicalizationPolicy.Optimized, index.Physicalization);
            }
        }
    }
}
