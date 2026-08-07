using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Models;

/// <summary>
/// One registered composite-activity structure kind, discovered from the active shell's
/// structure handlers so headless clients can author structure payloads per kind.
/// </summary>
/// <param name="Kind">The activity-owned structure kind identifier.</param>
/// <param name="SchemaVersion">The structure payload schema version owned by the activity module.</param>
/// <param name="SupportsScopedVariables">Whether nodes of this kind own container-scoped variable declarations.</param>
/// <param name="PayloadSchema">JSON Schema of the authored payload; <c>null</c> when the handler keeps the payload opaque.</param>
public sealed record ActivityStructureView(
    string Kind,
    string SchemaVersion,
    bool SupportsScopedVariables,
    JsonElement? PayloadSchema);

/// <summary>
/// Registry of composite-activity structure kinds available in the active shell.
/// </summary>
/// <param name="Items">The registered structure kinds, ordered ordinally by kind then schema version.</param>
/// <param name="Fingerprint">Canonical <c>sha256:</c> fingerprint of <paramref name="Items"/> for change detection.</param>
public sealed record ActivityStructuresResponse(
    IReadOnlyCollection<ActivityStructureView> Items,
    string Fingerprint);
