using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Models;

/// <summary>
/// Versioned JSON Schema for the request body of <c>POST design/workflows/definitions/submit</c>,
/// letting headless clients generate and validate submit payloads without a designer.
/// </summary>
/// <param name="SchemaVersion">The version of the published schema document contract.</param>
/// <param name="Fingerprint">Canonical <c>sha256:</c> fingerprint of <paramref name="Schema"/> for change detection.</param>
/// <param name="Schema">The exported JSON Schema of the submit command body.</param>
public sealed record WorkflowDefinitionSubmitSchemaView(
    string SchemaVersion,
    string Fingerprint,
    JsonElement Schema);
