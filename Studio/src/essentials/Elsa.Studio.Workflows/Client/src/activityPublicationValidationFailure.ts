import { StudioHttpError, type StudioActivityDiagnostic } from "@elsa-workflows/studio-sdk";

/** Only the two graph expression refusals have a public authoring correction here.
 * Error payloads remain untrusted: never forward their free-form text, metadata or identities. */
export function graphPublicationValidationFailure(error: unknown, draftId: string, revision: number): StudioActivityDiagnostic[] | null {
  if (!(error instanceof StudioHttpError) || error.status !== 400 || !record(error.payload) || error.payload.errorCode !== "activity-data.invalid") return null;
  const diagnostics = error.payload.diagnostics;
  if (!Array.isArray(diagnostics) || !diagnostics.length || diagnostics.length > 128) return null;
  const result: StudioActivityDiagnostic[] = [];
  for (const diagnostic of diagnostics) {
    if (!record(diagnostic) || !record(diagnostic.location) || diagnostic.location.providerKey !== "elsa.activity-graph" ||
        !record(diagnostic.subject) || diagnostic.subject.kind !== "ActivityDraft" || diagnostic.subject.id !== draftId ||
        diagnostic.subject.revision != null && diagnostic.subject.revision !== revision ||
        typeof diagnostic.severity !== "string" || diagnostic.severity.toLowerCase() !== "error") return null;
    const variable = diagnostic.code === "ActivityDataSelection/activity.graph.variable.initial-value-invalid";
    const output = diagnostic.code === "ActivityDataSelection/activity.graph.output.source-invalid";
    if (!variable && !output) return null;
    const metadata = record(diagnostic.metadata) ? diagnostic.metadata : null;
    const pointer = diagnostic.authoredPath ?? metadata?.authoredPath;
    const pattern = variable ? /^\/variables\/(0|[1-9][0-9]{0,3})\/initialValue$/ : /^\/outputMappings\/(0|[1-9][0-9]{0,3})\/source$/;
    if (typeof pointer !== "string" || !pattern.test(pointer)) return null;
    if (metadata?.authoredPath != null && metadata.authoredPath !== pointer) return null;
    result.push({
      code: variable ? "activity.graph.variable.initial-value-invalid" : "activity.graph.output.source-invalid",
      severity: "Error",
      message: variable ? "The variable initial value is not a valid expression." : "The output value source is missing or is not a valid expression.",
      remediation: variable ? "Enter a valid initial value in Variables." : "Choose an expression type and value for this output in Outputs.",
      subject: { kind: "ActivityDraft", id: draftId, revision },
      location: { providerKey: "elsa.activity-graph", jsonPointer: pointer },
      metadata: { actionLabel: variable ? "Edit variable initial value" : "Edit output value source" }
    });
  }
  return result;
}

function record(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object" && !Array.isArray(value));
}
