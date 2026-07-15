using Elsa.Persistence.Groundwork;
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
    public void ExecutableActivityTemplate_Declares_FlatEnvelope_TemplateHash_Index()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind);

        Assert.Contains(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByCollectionIndex);
        var byHash = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByTemplateHashIndex);
        Assert.Equal("templateHash", Assert.Single(byHash.Fields).Path);
        Assert.True(byHash.IsUnique);
        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByTemplateHashIndex);
    }

    [Fact]
    public void ActivityExecutionHierarchy_Declares_FlatEnvelope_Scope_And_Workflow_Indexes()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind);

        var byWorkflow = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex);
        Assert.Equal("workflowExecutionId", Assert.Single(byWorkflow.Fields).Path);

        var byScope = Assert.Single(unit.Indexes, i => i.Identity == ElsaRuntimeStorageManifest.ByExecutionScopeIndex);
        Assert.Equal("executionScopeId", Assert.Single(byScope.Fields).Path);

        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex);
        Assert.Contains(unit.Queries, q => q.IndexIdentity == ElsaRuntimeStorageManifest.ByExecutionScopeIndex);
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
}
