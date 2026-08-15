using System.Reflection;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Secrets.Tests.Support;
using Xunit;

namespace Elsa.Secrets.Tests;

[CollectionDefinition(SecretsApiCollectibilityCollection.Name, DisableParallelization = true)]
public sealed class SecretsApiCollectibilityCollection
{
    public const string Name = "Secrets API collectibility";
}

[Collection(SecretsApiCollectibilityCollection.Name)]
public sealed class SecretsApiCollectibilityTests
{
    private const int ReleaseCollectionAttempts = 24;

    [Fact]
    public void Repeated_clean_cycles_collect_after_materialized_mapper_lifecycle()
    {
        for (var cycle = 0; cycle < 10; cycle++)
        {
            using var evidenceCycle = SecretsCollectibleFixture.Create();
            var evidence = evidenceCycle.VerifyCollection(ReleaseCollectionAttempts);
            Assert.True(evidence.Collected, evidence.Diagnostic);
            Assert.Equal(RetentionStage.Clean, evidence.Stage);
            Assert.False(evidence.LoadContext.IsAlive);
            Assert.False(evidence.Assembly.IsAlive);
            Assert.False(evidence.EndpointType.IsAlive);
        }
    }

    [Fact]
    public void Materialized_route_and_json_are_exercised_before_release()
    {
        using var cycle = SecretsCollectibleFixture.Create(RetentionStage.Route);

        Assert.Equal(10, cycle.RouteCount);
        Assert.True(cycle.JsonExercised, "A representative JSON request must execute before unload verification.");
        Assert.False(cycle.DocumentationGenerated);

        var retained = cycle.VerifyCollection();
        Assert.False(retained.Collected);
        Assert.Equal(RetentionStage.Route, retained.Stage);

        cycle.ReleaseRetention();
        var released = cycle.VerifyCollection(ReleaseCollectionAttempts);
        Assert.True(released.Collected, released.Diagnostic);
        Assert.Equal(RetentionStage.Clean, released.Stage);
    }

    [Fact]
    public void OpenApi_generation_is_exercised_and_framework_retention_is_reported_honestly()
    {
        using var cycle = SecretsCollectibleFixture.Create(generateDocumentation: true);

        Assert.True(cycle.DocumentationGenerated, "The real ASP.NET OpenAPI document provider must generate the consumed Secrets paths.");
        var evidence = cycle.VerifyCollection(ReleaseCollectionAttempts);

        Assert.False(evidence.Collected);
        Assert.Equal(RetentionStage.Clean, evidence.Stage);
        Assert.Contains("harness retention", evidence.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Service_provider_retention_releases_after_disposal()
    {
        using var cycle = SecretsCollectibleFixture.Create(RetentionStage.Services);
        var retained = cycle.VerifyCollection();
        Assert.False(retained.Collected);
        Assert.Equal(RetentionStage.Services, retained.Stage);
        Assert.Contains("DI/services", retained.Diagnostic, StringComparison.OrdinalIgnoreCase);

        cycle.ReleaseRetention();
        var released = cycle.VerifyCollection(ReleaseCollectionAttempts);
        Assert.True(released.Collected, released.Diagnostic);
    }

    [Fact]
    public void Serializer_and_documentation_retention_is_classified_without_a_false_release_claim()
    {
        using var cycle = SecretsCollectibleFixture.Create(RetentionStage.Serializer);
        var retained = cycle.VerifyCollection();
        Assert.False(retained.Collected);
        Assert.Equal(RetentionStage.Serializer, retained.Stage);
        Assert.Contains("serializer", retained.Diagnostic, StringComparison.OrdinalIgnoreCase);

        cycle.ReleaseRetention();
    }

    [Fact]
    public void Evidence_contains_only_weak_collectible_handles()
    {
        using var cycle = SecretsCollectibleFixture.Create();
        cycle.ReleaseRetention();
        var evidence = cycle.VerifyCollection(ReleaseCollectionAttempts);

        Assert.True(evidence.Collected, evidence.Diagnostic);
        Assert.DoesNotContain(typeof(Type), typeof(UnloadEvidence).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType));
        Assert.DoesNotContain(typeof(Assembly), typeof(UnloadEvidence).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType));
    }

}
