using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class ActivitiesDesignStorageManifestTests
{
    [Fact]
    public void ReusableActivityDocuments_Declare_EnvelopeAligned_EqualityIndexes()
    {
        var manifest = ActivitiesDesignStorageManifest.Create();

        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionIndex, ActivitiesDesignStorageManifest.DefinitionIdField),
            (ActivitiesDesignStorageManifest.ByHeadVersionIndex, ActivitiesDesignStorageManifest.HeadVersionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionIndex, ActivitiesDesignStorageManifest.DefinitionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
            (ActivitiesDesignStorageManifest.ByDraftIndex, ActivitiesDesignStorageManifest.DraftIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind,
            (ActivitiesDesignStorageManifest.ByDraftIndex, ActivitiesDesignStorageManifest.DraftIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionIndex, ActivitiesDesignStorageManifest.DefinitionIdField),
            (ActivitiesDesignStorageManifest.ByDefinitionVersionIndex, ActivitiesDesignStorageManifest.DefinitionVersionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionVersionIndex, ActivitiesDesignStorageManifest.DefinitionVersionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
            (ActivitiesDesignStorageManifest.ByOwnerVersionIndex, ActivitiesDesignStorageManifest.OwnerVersionIdField),
            (ActivitiesDesignStorageManifest.ByDependencyVersionIndex, ActivitiesDesignStorageManifest.DependencyVersionIdField));
    }

    [Fact]
    public void SchemaVersion_Remains_The_Frozen_Legacy_Stamp()
    {
        Assert.Equal("1.0.0", ActivitiesDesignStorageManifest.SchemaVersion);
    }

    private static void AssertUnit(
        global::Groundwork.Core.Manifests.StorageManifest manifest,
        string documentKind,
        params (string Identity, string Path)[] expectedIndexes)
    {
        var unit = manifest.StorageUnits.Single(x => x.Identity.Value == documentKind);

        AssertIndex(unit, ActivitiesDesignStorageManifest.ByCollectionIndex, ActivitiesDesignStorageManifest.CollectionField);
        foreach (var (identity, path) in expectedIndexes)
            AssertIndex(unit, identity, path);
    }

    private static void AssertIndex(global::Groundwork.Core.Manifests.StorageUnit unit, string identity, string path)
    {
        var index = Assert.Single(unit.Indexes, x => x.Identity == identity);
        Assert.Equal(path, Assert.Single(index.Fields).Path);
        Assert.Contains(unit.Queries, x => x.IndexIdentity == identity);
    }
}
