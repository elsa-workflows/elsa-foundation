using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

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
    string ReferenceKey,
    string Name,
    string Type,
    // The collection shape of the input's CLR type (Single/Array/List/HashSet/Dictionary). Together with
    // <see cref="Type"/> (the element-type alias) this lets the editor render a list/dictionary item editor
    // for a collection-typed input instead of a scalar text box (#924).
    CollectionKind CollectionKind,
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
    CollectionKind CollectionKind,
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
