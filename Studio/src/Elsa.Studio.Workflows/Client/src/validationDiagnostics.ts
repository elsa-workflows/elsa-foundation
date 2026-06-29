import type { ValidationError } from "./workflowTypes";

/**
 * Draft validation diagnostics, focused on the scoped-variable repair surface (ADR-0027).
 *
 * Invalid scoped references surface as backend `ValidationError`s of type
 * `Expressions/UnresolvedVariable` ("...is not visible from this activity's scope") through the
 * normal draft-validation results the Studio already consumes (`WorkflowDraft.validationErrors`).
 * Copy/import remapping is handled backend-side; the Studio lists the resulting invalid references
 * so the author can navigate to the offending input and re-pick a variable deliberately — it never
 * auto-retargets by name.
 */

export const UNRESOLVED_VARIABLE_TYPE = "Expressions/UnresolvedVariable";

// The backend serializes ValidationError.Type as `type`; `code` is tolerated for older payloads.
export function errorType(error: ValidationError): string {
  return String(error.type ?? error.code ?? "");
}

export function isUnresolvedVariableError(error: ValidationError): boolean {
  return errorType(error) === UNRESOLVED_VARIABLE_TYPE;
}

export interface ParsedValidationPath {
  // Authored node id the concern is bound to, or null for a workflow-scope ($workflow) concern.
  nodeId: string | null;
  isWorkflowScope: boolean;
  // The argument bag the reference lives in, when the path carries one.
  bag: "inputs" | "outputs" | "variables" | null;
  // The input/output reference key or variable name suffix, when present.
  referenceKey: string | null;
}

/**
 * Parses a backend validation `Path` (slash-delimited with an optional input/output/variable
 * suffix). Examples: `{nodeId}`, `{nodeId}/inputs/{key}`, `$workflow/variables/{name}`, `$workflow`.
 */
export function parseValidationErrorPath(path: string | null | undefined): ParsedValidationPath {
  const segments = String(path ?? "").trim().split("/").filter(Boolean);
  const [head, bag, ...rest] = segments;
  const isWorkflowScope = head === "$workflow";
  const knownBag = bag === "inputs" || bag === "outputs" || bag === "variables" ? bag : null;
  return {
    nodeId: !head || isWorkflowScope ? null : head,
    isWorkflowScope,
    bag: knownBag,
    referenceKey: rest.length > 0 ? rest.join("/") : null
  };
}

export interface VariableRepairItem {
  error: ValidationError;
  path: ParsedValidationPath;
  message: string;
}

/** Selects the unresolved-variable errors and pairs each with its parsed, navigable path. */
export function collectVariableRepairItems(errors: ValidationError[] | null | undefined): VariableRepairItem[] {
  return (errors ?? [])
    .filter(isUnresolvedVariableError)
    .map(error => ({
      error,
      path: parseValidationErrorPath(error.path),
      message: error.message ?? "Variable reference is not visible from this activity's scope."
    }));
}
