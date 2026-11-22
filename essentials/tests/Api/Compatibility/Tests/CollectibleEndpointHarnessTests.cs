using Elsa.Api.Compatibility.Testing.Collectibility;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class CollectibleEndpointHarnessTests
{
    [Fact]
    public void Clean_cycles_collect_repeatedly()
    {
        var evidence = Enumerable.Range(0, 10)
            .Select(_ => CollectibleEndpointFixture.Create().VerifyCollection())
            .ToArray();

        Assert.All(evidence, item =>
        {
            Assert.True(item.Collected, item.Diagnostic);
            Assert.Equal(RetentionStage.Clean, item.Stage);
            Assert.Null(item.Diagnostic);
            Assert.InRange(item.CollectionAttempts, 1, UnloadEvidence.DefaultMaxCollectionAttempts);
        });
    }

    [Theory]
    [InlineData(RetentionStage.Route, "route")]
    [InlineData(RetentionStage.Services, "DI/services")]
    public void Deliberate_route_and_service_retention_is_classified_and_releases(RetentionStage stage, string classification)
    {
        using var cycle = CollectibleEndpointFixture.Create(stage);

        var retained = cycle.VerifyCollection();
        Assert.False(retained.Collected);
        Assert.Equal(stage, retained.Stage);
        Assert.Contains(classification, retained.Diagnostic, StringComparison.OrdinalIgnoreCase);

        cycle.ReleaseRetention();
        var released = cycle.VerifyCollection();
        Assert.True(released.Collected, released.Diagnostic);
        Assert.Equal(RetentionStage.Clean, released.Stage);
        Assert.Null(released.Diagnostic);
    }

    [Fact]
    public void Deliberate_serializer_retention_is_classified_without_assuming_cache_release()
    {
        using var cycle = CollectibleEndpointFixture.Create(RetentionStage.Serializer);

        var retained = cycle.VerifyCollection();

        Assert.False(retained.Collected);
        Assert.Equal(RetentionStage.Serializer, retained.Stage);
        Assert.Contains("serializer", retained.Diagnostic, StringComparison.OrdinalIgnoreCase);

        // Release the fixture-owned options. The runtime serializer may retain metadata in a
        // cache beyond that release, so this test intentionally makes no post-release collection claim.
        cycle.ReleaseRetention();
    }

    [Fact]
    public void Harness_retention_is_distinguished_from_framework_stages()
    {
        using var cycle = CollectibleEndpointFixture.Create(RetentionStage.Harness);

        var evidence = cycle.VerifyCollection();

        Assert.False(evidence.Collected);
        Assert.Equal(RetentionStage.Harness, evidence.Stage);
        Assert.Contains("harness", evidence.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<WeakReference>(evidence.LoadContext);
        Assert.IsType<WeakReference>(evidence.Assembly);
        Assert.IsType<WeakReference>(evidence.EndpointType);
    }

    [Fact]
    public void Evidence_contains_only_weak_handles_and_does_not_prevent_collection()
    {
        using var cycle = CollectibleEndpointFixture.Create();
        var evidence = cycle.VerifyCollection();

        Assert.True(evidence.Collected, evidence.Diagnostic);
        Assert.False(evidence.LoadContext.IsAlive);
        Assert.False(evidence.Assembly.IsAlive);
        Assert.False(evidence.EndpointType.IsAlive);
        Assert.DoesNotContain(typeof(Type), typeof(UnloadEvidence).GetFields().Select(field => field.FieldType));
    }
}
