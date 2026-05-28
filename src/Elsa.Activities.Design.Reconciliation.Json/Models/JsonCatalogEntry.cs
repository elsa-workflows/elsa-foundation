using Elsa.Activities.Design.Core.Models;
using System.Text.Json;

namespace Elsa.Activities.Design.Reconciliation.Json.Models;

/// <summary>
/// Mirrors a single entry in <c>elsa-core-activities.json</c>. The descriptor payload is
/// kept as a raw <see cref="JsonElement"/> because its concrete shape is polymorphic on
/// <see cref="ImplementationKind"/>; the validator resolves the target type via the
/// descriptor registry and deserialises in a second pass.
/// </summary>
public sealed record JsonCatalogEntry(
    int Version,
    ActivityExecutionType ExecutionType,
    string ImplementationKind,
    JsonElement ImplementationDescriptor,
    JsonCatalogDefinition Definition,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityPortDefinition>? Ports
);

/// <summary>
/// The nested <c>definition</c> object inside each catalog entry.
/// </summary>
public sealed record JsonCatalogDefinition(
    string ActivityTypeKey,
    string Category,
    string? DisplayName,
    string? Description
);
