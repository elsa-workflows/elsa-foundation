import type { WorkflowExecutableReference, WorkflowExecutableRunResponse } from "../workflowTypes";

export function readExecutableRunWorkflowExecutionId(response: WorkflowExecutableRunResponse | null | undefined) {
  const workflowExecutionId = response?.workflowExecutionId ?? response?.runId ?? response?.executionId;
  return typeof workflowExecutionId === "string" && workflowExecutionId.trim() ? workflowExecutionId : null;
}

// Surfaces the reference-gate refusal behind a failed executable run (ADR 0040): the backend answers
// 409 with { error } distinguishing an expired test-run reference from a retired one. The backend
// sentence is shown verbatim, followed by the recovery hint matching the structured reason.
export function formatExecutableRunError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  const payload = (typeof error === "object" && error && "payload" in error ? (error as { payload?: unknown }).payload : null)
    ?? tryParseJson(message);
  const backendError = payload && typeof payload === "object" && typeof (payload as { error?: unknown }).error === "string"
    ? (payload as { error: string }).error
    : null;
  if (!backendError) return message;

  if (/no live .* reference/i.test(backendError)) {
    return /expired/i.test(backendError)
      ? `${backendError} Start a new test run from the definition editor to mint a fresh reference.`
      : `${backendError} Restore the executable or republish its definition to run it.`;
  }

  return backendError;
}

function tryParseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

export type ExecutableReferenceStatus = "live" | "retired" | "expired";

export function getExecutableReferenceStatus(reference: WorkflowExecutableReference, now: Date = new Date()): ExecutableReferenceStatus {
  if (reference.deletedAt) return "retired";
  if (reference.expiresAt && new Date(reference.expiresAt).getTime() <= now.getTime()) return "expired";
  return "live";
}
