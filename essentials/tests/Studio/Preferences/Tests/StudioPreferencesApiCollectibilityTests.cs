using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Studio.Preferences.Api;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

[CollectionDefinition(StudioPreferencesCollectibilityCollection.Name, DisableParallelization = true)]
public sealed class StudioPreferencesCollectibilityCollection
{
    public const string Name = "Studio Preferences collectibility";
}

[Collection(StudioPreferencesCollectibilityCollection.Name)]
public sealed class StudioPreferencesApiCollectibilityTests
{
    private const int ReleaseCollectionAttempts = 24;

    private static readonly CollectibleModule Module = new()
    {
        FeatureType = typeof(StudioPreferencesApiFeature),
        AssemblyNamePrefix = "Elsa.Studio.Preferences.Collectible"
    };

    [Theory]
    [InlineData(RetentionStage.Route, "route")]
    [InlineData(RetentionStage.Services, "DI/services")]
    public void Repeated_production_references_keep_the_context_alive_until_released(RetentionStage stage, string classification)
    {
        for (var cycle = 0; cycle < 10; cycle++)
            VerifyRetainReleaseCycle(stage, classification);
    }

    [Fact]
    public void Collection_evidence_contains_no_strong_collectible_type_handles()
    {
        using var cycle = Create(RetentionStage.Route);
        cycle.ReleaseRetention();

        var evidence = cycle.VerifyCollection(ReleaseCollectionAttempts);

        Assert.True(evidence.Collected, evidence.Diagnostic);
        Assert.False(evidence.LoadContext.IsAlive);
        Assert.False(evidence.Assembly.IsAlive);
        Assert.False(evidence.EndpointType.IsAlive);
        var evidenceFieldTypes = typeof(UnloadEvidence)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.FieldType);
        Assert.DoesNotContain(typeof(Type), evidenceFieldTypes);
        Assert.DoesNotContain(typeof(Assembly), evidenceFieldTypes);
    }

    private static CollectibleModuleCycle<int> Create(RetentionStage stage) =>
        CollectibleModuleFixture.Create(Module, stage, host => host.Endpoints.Count);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyRetainReleaseCycle(RetentionStage stage, string classification)
    {
        using var cycle = Create(stage);
        if (stage == RetentionStage.Route)
            Assert.True(cycle.Observation >= 2, "The production Studio Preferences mapper must publish its endpoints.");

        var retained = cycle.VerifyCollection();
        Assert.False(retained.Collected);
        Assert.Equal(stage, retained.Stage);
        Assert.Contains(classification, retained.Diagnostic, StringComparison.OrdinalIgnoreCase);

        cycle.ReleaseRetention();
        var released = cycle.VerifyCollection(ReleaseCollectionAttempts);
        Assert.True(released.Collected, released.Diagnostic);
        Assert.Equal(RetentionStage.Clean, released.Stage);
        Assert.Null(released.Diagnostic);
    }
}
