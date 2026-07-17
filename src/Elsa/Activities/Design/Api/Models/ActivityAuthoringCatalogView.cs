using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityAuthoringCatalogView(IReadOnlyCollection<ActivityAuthoringDescriptorView> Activities);

public sealed record ActivityAuthoringDescriptorView(
    string ActivityVersionId,
    string ActivityTypeKey,
    string Version,
    string DisplayName,
    string? Category,
    string? Description,
    string ExecutionType,
    bool Available,
    string? AvailabilityReason,
    IReadOnlyCollection<ActivityInputDescriptorView> Inputs,
    IReadOnlyCollection<ActivityOutputDescriptorView> Outputs,
    IReadOnlyCollection<ActivityPortDescriptorView> Ports,
    JsonElement? ContainerStructure,
    ActivityAuthoringTemplateView AuthoringTemplate);

public sealed record ActivityInputDescriptorView(
    string Name,
    string Type,
    string? DisplayName,
    string? Description,
    float Order,
    string? Category,
    bool IsBrowsable,
    bool IsRequired,
    string? UiHint,
    JsonElement? DefaultValue,
    string? DefaultSyntax,
    JsonElement? UiSpecifications)
{
    public bool? IsNullable { get; init; }
}

public sealed record ActivityOutputDescriptorView(
    string Name,
    string Type,
    string? DisplayName,
    string? Description,
    string? Category,
    bool IsBrowsable);

public sealed record ActivityPortDescriptorView(string Name, string? DisplayName, string? Type, bool IsBrowsable);

public sealed record ActivityAuthoringTemplateView(
    string NodeId,
    string ActivityVersionId,
    IReadOnlyDictionary<string, ActivityArgumentValue> Inputs,
    IReadOnlyDictionary<string, ActivityArgumentValue> Outputs,
    ActivityAuthoringStructureView? Structure);

public sealed record ActivityAuthoringStructureView(string Kind, string SchemaVersion, JsonElement Payload);
