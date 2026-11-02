import React, { useCallback, useEffect, useId, useRef, useState } from "react";
import { CheckCircle2, RefreshCw, UserCheck, XCircle } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioPage, StudioPageHeader, StudioResultState } from "@elsa-workflows/studio-ui";
import {
  decideReview, getReview, newRequestId, readReviewReference,
  type HumanReviewView, type ReceiptStatus, type ReviewDecision, type ReviewReference
} from "./reviewsApi";

type Load = { kind: "loading" } | { kind: "missing" } | { kind: "error"; message: string } | { kind: "ready"; review: HumanReviewView };
type Pending = { decision: ReviewDecision; requestId: string; comment: string };

const receiptMessages: Record<ReceiptStatus, string> = {
  Accepted: "Your decision was recorded.",
  Pending: "Your decision is queued and will be applied shortly.",
  AlreadyResolved: "Another decision was already recorded for this review.",
  Conflict: "The content changed or this request conflicts with an earlier one. Reload to see the current review.",
  Forbidden: "You do not currently have permission to decide this review.",
  NotFound: "This review is no longer visible.",
  Closed: "This review ended without a decision.",
  Unavailable: "The decision could not be submitted right now. Try again; your earlier attempt is retried, not duplicated.",
  Refused: "Your decision was not admitted. Check your permission and submit again."
};

export function HumanReviewPage({ context, reference: given }: { context: StudioEndpointContext; reference?: ReviewReference | null }) {
  const reference = given ?? readReviewReference(window.location.pathname);
  // Each exact wait gets its own page state, so content, comment, receipt and request ID never carry over to another review.
  const key = reference ? `${reference.workflowExecutionId}/${reference.activityExecutionId}` : "";
  return <ReviewScreen key={key} context={context} reference={reference} />;
}

function ReviewScreen({ context, reference }: { context: StudioEndpointContext; reference: ReviewReference | null }) {
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [receipt, setReceipt] = useState<{ status: ReceiptStatus; reason?: string | null } | null>(null);
  const pending = useRef<Pending | null>(null);
  const commentId = useId();

  const refresh = useCallback(async () => {
    if (!reference) { setLoad({ kind: "missing" }); return; }
    try {
      setLoad({ kind: "ready", review: await getReview(context, reference) });
    } catch (error) {
      const status = (error as { status?: number }).status;
      setLoad(status === 404 ? { kind: "missing" } : { kind: "error", message: error instanceof Error ? error.message : "The review could not be loaded." });
    }
  }, [context, reference?.workflowExecutionId, reference?.activityExecutionId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const decide = useCallback(async (decision: ReviewDecision) => {
    if (load.kind !== "ready" || !reference || busy) return;
    // Retrying the same decision and comment reuses its request ID, so a lost response never decides twice.
    const previous = pending.current;
    const attempt = previous && previous.decision === decision && previous.comment === comment ? previous : { decision, comment, requestId: newRequestId() };
    pending.current = attempt;
    setBusy(true);
    try {
      const result = await decideReview(context, reference, { contentHash: load.review.contentHash, decision, comment, requestId: attempt.requestId });
      setReceipt(result);
      // A queued decision may still be replayed, so only a final receipt releases its request ID.
      if (result.status !== "Unavailable" && result.status !== "Pending") pending.current = null;
      if (result.status !== "Unavailable" && result.status !== "Forbidden") await refresh();
    } catch (error) {
      setReceipt({ status: "Unavailable", reason: error instanceof Error ? error.message : null });
    } finally {
      setBusy(false);
    }
  }, [busy, comment, context, load, reference, refresh]);

  if (load.kind === "loading") return <StudioPage aria-busy="true"><p>Loading review…</p></StudioPage>;
  if (load.kind === "missing")
    return <StudioPage><StudioResultState title="Review not found" description="No visible review matches this link. It may belong to another tenant or no longer exist." /></StudioPage>;
  if (load.kind === "error")
    return <StudioPage><StudioResultState tone="danger" title="Review unavailable" description={load.message}
      actions={<StudioButton onClick={() => void refresh()}>Retry</StudioButton>} /></StudioPage>;

  const review = load.review;
  const open = review.status === "Pending";
  return (
    <StudioPage className="agent-review" data-status={review.status}>
      <StudioPageHeader
        icon={<UserCheck aria-hidden="true" size={18} />}
        eyebrow="Human review"
        title={review.title?.trim() || "Content awaiting review"}
        description={<span data-testid="review-status">{statusLabel(review)}</span>}
        actions={<StudioButton aria-label="Refresh review" onClick={() => void refresh()}><RefreshCw aria-hidden="true" size={14} /> Refresh</StudioButton>} />
      <section aria-label="Content under review" className="agent-review__content">
        <ReviewContent content={review.content} />
      </section>
      {review.notes ? <section aria-label="Reviewer notes" className="agent-review__notes"><h2>Notes</h2><p>{review.notes}</p></section> : null}
      {receipt ? <p role="status" data-receipt={receipt.status} className="agent-review__receipt">{receiptMessages[receipt.status]}</p> : null}
      {open ? (
        <form className="agent-review__decision" aria-label="Decision" onSubmit={event => { event.preventDefault(); void decide("Approve"); }}>
          <label htmlFor={commentId}>Comment (optional)</label>
          <textarea id={commentId} value={comment} maxLength={4096} onChange={event => setComment(event.target.value)} disabled={busy} />
          <div className="agent-review__actions">
            <StudioButton type="submit" variant="primary" disabled={busy}><CheckCircle2 aria-hidden="true" size={14} /> Approve</StudioButton>
            <StudioButton disabled={busy} onClick={() => void decide("Deny")}><XCircle aria-hidden="true" size={14} /> Deny</StudioButton>
          </div>
        </form>
      ) : (
        <p className="agent-review__resolution" data-testid="review-resolution">
          {review.decidedBy ? `Decided by ${review.decidedBy}.` : null}
          {review.decisionComment ? ` “${review.decisionComment}”` : null}
        </p>
      )}
    </StudioPage>
  );
}

function statusLabel(review: HumanReviewView) {
  switch (review.status) {
    case "Pending": return "Awaiting your decision";
    case "Approved": return "Approved";
    case "Rejected": return "Rejected";
    default: return "Closed without a decision";
  }
}

function ReviewContent({ content }: { content: unknown }) {
  if (typeof content === "string") return <div className="agent-review__text" data-testid="review-content">{content}</div>;
  return <pre className="agent-review__json" data-testid="review-content">{JSON.stringify(content, null, 2)}</pre>;
}
