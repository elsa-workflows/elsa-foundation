using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

/// <summary>
/// Regression coverage for elsa-foundation#902: a Sequence-nested activity input whose authored wire JSON carries
/// <c>"conversion": { "mode": "json" }</c> — the camelCase <see cref="AuthoredValueConversionMode"/> string the
/// global FastEndpoints API options emit for <c>ArgumentState.Conversion</c> — must project through the container
/// structure handler. Before the fix the handler deserialized nested payloads with bare
/// <c>JsonSerializerDefaults.Web</c> options that could not read the enum string, so projecting any visually
/// authored container with a conversion request threw <see cref="JsonException"/> at
/// <c>SequenceStructureHandler.ReadAuthoredStructure</c> during preflight/publish. The authored <c>Json</c> mode is
/// what publication pins to the built-in <c>elsa.json@1</c> conversion plan.
/// </summary>
public sealed class SequenceStructureConversionTests
{
    private static IActivityStructureService StructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    // Authored wire JSON exactly as the FastEndpoints options emit it: camelCase property names and a camelCase
    // AuthoredValueConversionMode string ("json"). Parsed as a raw payload rather than round-tripped from an
    // in-memory model, because serializing the model with Web options would write the enum as a number and would
    // not reproduce the string-read failure.
    private const string WireJson =
        """
        {
          "activities": [
            {
              "nodeId": "write-one",
              "activityVersionId": "activity-write-line",
              "inputs": [
                {
                  "referenceKey": "Text",
                  "value": { "value": "{\"name\":\"Grace\"}", "expressionType": "Literal" },
                  "conversion": { "mode": "json" }
                }
              ],
              "outputs": []
            }
          ],
          "variables": []
        }
        """;

    private static ActivityNode SequenceNodeFromWire() =>
        new(
            "sequence",
            "activity-sequence",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.Deserialize<JsonElement>(WireJson)));

    [Fact]
    public void ProjectChildren_ReadsAuthoredCamelCaseConversionEnum_OnContainerNestedInput()
    {
        var service = StructureService();
        var sequence = SequenceNodeFromWire();

        var projection = Assert.Single(service.ProjectChildren(sequence));
        var child = Assert.Single(projection.Activities);
        var input = Assert.Single(child.Inputs);

        Assert.Equal("Text", input.ReferenceKey);
        Assert.NotNull(input.Conversion);
        Assert.Equal(AuthoredValueConversionMode.Json, input.Conversion!.Mode);
    }
}
