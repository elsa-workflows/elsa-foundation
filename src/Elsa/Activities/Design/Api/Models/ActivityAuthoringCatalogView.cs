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
    ActivityAuthoringTemplateView AuthoringTemplate,
    ActivityAuthoringIntrinsicView? Intrinsic = null);

/// <summary>
/// Present only on built-in engine-intrinsic catalog entries (e.g. Set Variable, Set Output). It tells
/// the authoring client that placing this descriptor must materialize an engine-owned intrinsic node —
/// an <c>ActivityNode</c> carrying an <c>AuthoredWorkflowIntrinsic</c> — rather than a catalog activity
/// reference. The intrinsic never activates a CLR activity at runtime (ADR 0045): the engine writes the
/// variable or workflow output directly. <see cref="Kind"/> is the authored intrinsic kind
/// (e.g. <c>Set</c>, <c>SetOutput</c>); the input-key fields name which descriptor inputs the client maps
/// onto the intrinsic's variable target, value, and (for Set Output) literal output name.
/// </summary>
public sealed record ActivityAuthoringIntrinsicView(
    string Kind,
    string ValueInputKey,
    string? VariableInputKey,
    string? OutputNameInputKey);

public sealed record ActivityInputDescriptorView(
    string ReferenceKey,
    string Name,
    string Type,
    string? DisplayName,
    string? Description,
    float Order,
    string? Category,
    bool IsBrowsable,
    bool IsRequired,
    bool IsNullable,
    string? UiHint,
    JsonElement? DefaultValue,
    string? DefaultSyntax,
    JsonElement? UiSpecifications);

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
