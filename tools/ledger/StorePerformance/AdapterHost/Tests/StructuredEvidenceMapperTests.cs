using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class StructuredEvidenceMapperTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Point_read_roundtrip_preserves_bounds_and_does_not_invent_uniqueness(bool observed)
    {
        var keyId = new ProviderOpaqueIdentity(Guid.NewGuid());
        var scopeId = new ProviderOpaqueIdentity(Guid.NewGuid());
        var evidence = new ProviderExecutionEvidence(
            new ProviderIdentity("SQLite", "test"),
            ProviderExecutionOperation.PointRead,
            ProviderCommandKind.Read,
            ProviderExecutionRole.Statement,
            new(keyId, scopeId, new(Guid.NewGuid()), new(Guid.NewGuid()), 0, 0),
            new(new StorageUnitId("logical-records"), new(Guid.NewGuid()), ProviderScopeBindingMode.Predicate),
            ProviderExecutionOutcome.Succeeded,
            failureCategory: null,
            shapeAvailability: ProviderEvidenceAvailability.Collected,
            pointRead: new(
                [new("id", QueryType.String, ProviderPointReadBindingRole.Key, keyId),
                 new(null, QueryType.String, ProviderPointReadBindingRole.Scope, scopeId)],
                observed
                    ? new(ProviderPointReadUniquenessStatus.Observed, ["id"], includesScopeBinding: true)
                    : new(ProviderPointReadUniquenessStatus.NotObserved),
                ProviderNativeBound.Absent,
                materializerReadsAtMostOne: true,
                lockMode: ProviderPointReadLockMode.None),
            plan: ProviderPlanEvidence.NotRequested);

        var serialized = JsonSerializer.Serialize(StructuredEvidenceMapper.Map(evidence), ArtifactStore.JsonOptions);
        var reloaded = JsonSerializer.Deserialize<StructuredExecutionEvidence>(serialized, ArtifactStore.JsonOptions);
        var point = Assert.IsType<StructuredPointReadEvidence>(reloaded!.PointRead);

        Assert.Null(reloaded.BoundedQuery);
        Assert.Equal("Absent", point.NativeLimit.Kind);
        Assert.Null(point.NativeLimit.Value);
        Assert.True(point.MaterializerReadsAtMostOne);
        Assert.Equal("None", point.LockMode);
        Assert.Collection(point.KeyBounds,
            bound => Assert.Equal(new StructuredPointReadKeyBound("id", "String", "Key", keyId.Value), bound),
            bound => Assert.Equal(new StructuredPointReadKeyBound(null, "String", "Scope", scopeId.Value), bound));
        Assert.Equal(observed ? "Observed" : "NotObserved", point.Uniqueness.Status);
        Assert.Equal(observed, point.Uniqueness.IncludesScopeBinding);
        Assert.Equal(observed ? new[] { "id" } : [], point.Uniqueness.EnforcedKeyColumns);
    }
}
