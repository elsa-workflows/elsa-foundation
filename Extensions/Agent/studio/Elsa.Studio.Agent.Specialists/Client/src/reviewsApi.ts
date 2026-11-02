import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";

// Matches Elsa.Agent.Specialists.Review.Api (capability elsa.api.agent-reviews v1).
export type ReviewStatus = "Pending" | "Approved" | "Rejected" | "Closed";
export type ReviewDecision = "Approve" | "Deny";
export type ReceiptStatus =
  | "Accepted" | "Pending" | "AlreadyResolved" | "Conflict" | "Forbidden" | "NotFound" | "Closed" | "Unavailable" | "Refused";

export interface HumanReviewView {
  workflowExecutionId: string;
  activityExecutionId: string;
  title?: string | null;
  content: unknown;
  notes?: string | null;
  contentHash: string;
  status: ReviewStatus;
  decidedBy?: string | null;
  decisionComment?: string | null;
  requestId?: string | null;
}

export interface ReviewDecisionReceipt {
  status: ReceiptStatus;
  reason?: string | null;
}

export interface ReviewReference {
  workflowExecutionId: string;
  activityExecutionId: string;
}

const path = (reference: ReviewReference) =>
  `/agent/reviews/${encodeURIComponent(reference.workflowExecutionId)}/${encodeURIComponent(reference.activityExecutionId)}`;

export function getReview(context: StudioEndpointContext, reference: ReviewReference) {
  return context.http.getJson<HumanReviewView>(path(reference));
}

/** The request ID makes a retry replay the same decision; the actor is always derived by the server. */
export function decideReview(context: StudioEndpointContext, reference: ReviewReference,
  request: { contentHash: string; decision: ReviewDecision; comment?: string; requestId: string }) {
  return context.http.postJson<ReviewDecisionReceipt>(`${path(reference)}/decision`, {
    contentHash: request.contentHash,
    decision: request.decision,
    comment: request.comment?.trim() ? request.comment.trim() : null,
    requestId: request.requestId
  });
}

/** Reads the exact wait identity from the page route `/agents/reviews/:workflowExecutionId/:activityExecutionId`. */
export function readReviewReference(pathname: string): ReviewReference | null {
  const match = /\/agents\/reviews\/([^/]+)\/([^/]+)\/?$/.exec(pathname);
  return match ? { workflowExecutionId: decodeURIComponent(match[1]), activityExecutionId: decodeURIComponent(match[2]) } : null;
}

export function newRequestId() {
  return `review-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`}`;
}
