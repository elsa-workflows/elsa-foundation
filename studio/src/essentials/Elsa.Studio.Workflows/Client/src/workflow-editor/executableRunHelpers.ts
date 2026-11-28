import type { WorkflowExecutableReference, WorkflowExecutableRunResponse } from "../workflowTypes";

export function readExecutableRunWorkflowExecutionId(response: WorkflowExecutableRunResponse | null | undefined) {
  const workflowExecutionId = response?.workflowExecutionId ?? response?.runId ?? response?.executionId;
  return typeof workflowExecutionId === "string" && workflowExecutionId.trim() ? workflowExecutionId : null;
}

// Surfaces the reference-gate refusal behind a failed executable run (ADR 0040): the backend answers
// 409 with { error } distinguishing an expired test-run reference from a retired one. The backend
// sentence is shown verbatim, followed by the recovery hint matching the structured reason.
export function formatExecutableRunError(error: unknown): string {
  return describeExecutableRunError(error).message;
}

export function describeExecutableRunError(error: unknown): { message: string; outcomeUnknown?: true } {
  const message = error instanceof Error ? error.message : String(error);
  const payload = (typeof error === "object" && error && "payload" in error ? (error as { payload?: unknown }).payload : null)
    ?? tryParseJson(message);
  const problem = payload && typeof payload === "object"
    ? payload as { error?: unknown; persistenceOutcome?: unknown; reason?: unknown }
    : null;
  if (problem?.persistenceOutcome === "Unknown" && problem.reason === "InlineStartOutcomeUnknown")
    return { outcomeUnknown: true, message: "The workflow may still run. Check Runs before starting it again." };

  const backendError = typeof problem?.error === "string" ? problem.error : null;
  if (!backendError) return { message };

  if (/no live .* reference/i.test(backendError)) {
    return { message: /expired/i.test(backendError)
      ? `${backendError} Start a new test run from the definition editor to mint a fresh reference.`
      : `${backendError} Restore the executable or republish its definition to run it.` };
  }

  return { message: backendError };
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
