using System.Reflection;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Diagnostics.StructuredLogs.Tests.Support;
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
            using var cycle = StructuredLogsCollectibleModule.Create(documentationFirst: documentationFirst);

            AssertLifecycleExercised(cycle.Observation!);
            Assert.True(cycle.Observation.DocumentationGenerated, "The real ASP.NET Core OpenAPI provider must generate the document.");
            Assert.Equal(documentationFirst, cycle.Observation.DocumentationFirst);
            AssertDescriptionsInspected(cycle.Observation.OpenApiDescription);

            var evidence = cycle.VerifyCollection(ReleaseCollectionAttempts);
            Assert.True(evidence.Collected, evidence.Diagnostic);
            Assert.All(evidence.ObservedTypes, serializerContextType => Assert.False(serializerContextType.IsAlive));
            Assert.Equal(RetentionStage.Clean, evidence.Stage);
        }
    }

    [Fact]
    public void Combined_exercised_lifecycle_owner_retains_then_releases_the_module()
    {
        using var cycle = StructuredLogsCollectibleModule.Create(RetentionStage.Route, generateDocumentation: false);

        AssertLifecycleExercised(cycle.Observation!);

        var retained = cycle.VerifyCollection();
        Assert.False(retained.Collected, retained.Diagnostic);
        Assert.Equal(RetentionStage.Route, retained.Stage);
        Assert.Contains("route", retained.Diagnostic, StringComparison.OrdinalIgnoreCase);

        cycle.ReleaseRetention();
        var released = cycle.VerifyCollection(ReleaseCollectionAttempts);
        Assert.True(released.Collected, released.Diagnostic);
        Assert.Equal(RetentionStage.Clean, released.Stage);
        Assert.Null(released.Diagnostic);
    }

    [Fact]
    public void Openapi_description_evidence_contains_only_values_and_weak_handles()
    {
        using var cycle = StructuredLogsCollectibleModule.Create();
        var evidence = cycle.VerifyCollection(ReleaseCollectionAttempts);

        AssertDescriptionsInspected(cycle.Observation!.OpenApiDescription);
        Assert.DoesNotContain(typeof(Type), FieldTypes<UnloadEvidence>());
        Assert.DoesNotContain(typeof(Assembly), FieldTypes<UnloadEvidence>());
        Assert.DoesNotContain(typeof(MethodInfo), FieldTypes<OpenApiDescriptionInspection>());
        Assert.DoesNotContain(typeof(Delegate), FieldTypes<OpenApiDescriptionInspection>());

        Assert.True(evidence.Collected, evidence.Diagnostic);
        Assert.Equal(RetentionStage.Clean, evidence.Stage);
    }

    private static void AssertLifecycleExercised(StructuredLogsObservation observation)
    {
        Assert.Equal(3, observation.RouteCount);
        Assert.True(observation.QueryExercised, "The materialized recent route must execute a representative query.");
        Assert.True(observation.StreamStarted, "The materialized stream route must start an SSE response.");
        Assert.True(observation.StreamCancelled, "The materialized stream route must observe cancellation.");
        Assert.True(observation.SerializerExercised, "The production serializer must be exercised before unload.");
        Assert.True(observation.AuthorizationExercised, "The production permission policy must authorize a normalized exact grant.");
    }

    private static void AssertDescriptionsInspected(OpenApiDescriptionInspection description)
    {
        Assert.True(description.DescriptionsInspected, "The API Explorer descriptions used by OpenAPI must be inspected.");
        Assert.Equal(3, description.DescriptionCount);
        Assert.False(description.HasModuleOwnedMetadata);
    }

    private static IEnumerable<Type> FieldTypes<T>() =>
        typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType);
}
