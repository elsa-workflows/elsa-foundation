using Elsa.Diagnostics.StructuredLogs.Tests.Support;
using System.Reflection;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

[CollectionDefinition(StructuredLogsCollectibilityCollection.Name, DisableParallelization = true)]
public sealed class StructuredLogsCollectibilityCollection
{
    public const string Name = "Structured Logs collectibility";
}

[Collection(StructuredLogsCollectibilityCollection.Name)]
public sealed class StructuredLogsApiCollectibilityTests
{
    private const int ReleaseCollectionAttempts = 24;

    [Fact]
    public void Clean_cycles_exercise_query_stream_serializer_and_openapi_then_collect_or_report_boundary()
    {
        for (var cycleNumber = 0; cycleNumber < 3; cycleNumber++)
        {
            var documentationFirst = cycleNumber % 2 == 1;
            using var cycle = StructuredLogsCollectibleFixture.Create(documentationFirst: documentationFirst);

            Assert.Equal(3, cycle.RouteCount);
            Assert.True(cycle.QueryExercised, "The materialized recent route must execute a representative query.");
            Assert.True(cycle.StreamStarted, "The materialized stream route must start an SSE response.");
            Assert.True(cycle.StreamCancelled, "The materialized stream route must observe cancellation.");
            Assert.True(cycle.SerializerExercised, "The production serializer must be exercised before unload.");
            Assert.True(cycle.AuthorizationExercised, "The production permission policy must authorize a normalized exact grant.");
            Assert.True(cycle.DocumentationGenerated, "The real ASP.NET Core OpenAPI provider must generate the document.");
            Assert.Equal(documentationFirst, cycle.DocumentationFirst);
            Assert.True(cycle.OpenApiDescription.DescriptionsInspected, "The API Explorer descriptions used by OpenAPI must be inspected.");
            Assert.Equal(3, cycle.OpenApiDescription.DescriptionCount);
            Assert.False(cycle.OpenApiDescription.HasModuleOwnedMetadata);

            var evidence = cycle.VerifyCollection(ReleaseCollectionAttempts);
            Assert.True(evidence.Collected, evidence.Diagnostic);
            Assert.False(evidence.SerializerContextType.IsAlive);
            Assert.Equal(StructuredLogsRetentionStage.Clean, evidence.Stage);
        }
    }

    [Fact]
    public void Combined_exercised_lifecycle_owner_retains_then_releases_the_module()
    {
        using var cycle = StructuredLogsCollectibleFixture.Create(
            StructuredLogsRetentionStage.ExercisedLifecycle,
            generateDocumentation: false);

        Assert.Equal(3, cycle.RouteCount);
        Assert.True(cycle.QueryExercised);
        Assert.True(cycle.StreamStarted);
        Assert.True(cycle.StreamCancelled);
        Assert.True(cycle.SerializerExercised);
        Assert.True(cycle.AuthorizationExercised);

        var retained = cycle.VerifyCollection();
        Assert.False(retained.Collected, retained.Diagnostic);
        Assert.Equal(StructuredLogsRetentionStage.ExercisedLifecycle, retained.Stage);
        Assert.Contains("combined", retained.Diagnostic, StringComparison.OrdinalIgnoreCase);

        cycle.ReleaseRetention();
        var released = cycle.VerifyCollection(ReleaseCollectionAttempts);
        Assert.True(released.Collected, released.Diagnostic);
        Assert.Equal(StructuredLogsRetentionStage.Clean, released.Stage);
        Assert.Empty(released.Diagnostic);
    }

    [Fact]
    public void Openapi_description_evidence_contains_only_values_and_weak_handles()
    {
        using var cycle = StructuredLogsCollectibleFixture.Create();
        var evidence = cycle.VerifyCollection(ReleaseCollectionAttempts);

        Assert.True(cycle.OpenApiDescription.DescriptionsInspected);
        Assert.Equal(3, cycle.OpenApiDescription.DescriptionCount);
        Assert.False(cycle.OpenApiDescription.HasModuleOwnedMetadata);
        Assert.DoesNotContain(
            typeof(Type),
            typeof(StructuredLogsUnloadEvidence).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType));
        Assert.DoesNotContain(
            typeof(Assembly),
            typeof(StructuredLogsUnloadEvidence).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType));
        Assert.DoesNotContain(
            typeof(MethodInfo),
            typeof(OpenApiDescriptionInspection).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType));
        Assert.DoesNotContain(
            typeof(Delegate),
            typeof(OpenApiDescriptionInspection).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType));

        Assert.True(evidence.Collected, evidence.Diagnostic);
        Assert.Equal(StructuredLogsRetentionStage.Clean, evidence.Stage);
    }
}
