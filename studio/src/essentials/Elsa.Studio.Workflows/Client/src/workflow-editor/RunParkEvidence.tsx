import { useEffect, useRef, useState } from "react";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { readWorkflowParkEvidence, resolveWorkflowParkEvidence, type WorkflowParkEvidence } from "../api/parkEvidence";
import { formatDate } from "../workflowFormatting";
import "./runParkEvidence.css";

interface EvidenceState {
  context: StudioEndpointContext;
  path: string;
  page: WorkflowParkEvidence | null;
  message?: string;
}

export function RunParkEvidence({ context, workflowExecutionId }: { context: StudioEndpointContext; workflowExecutionId: string }) {
  const [state, setState] = useState<EvidenceState | null>(null);
  const [loading, setLoading] = useState(false);
  const request = useRef<AbortController | null>(null);
  const status = useRef<HTMLParagraphElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    request.current = controller;
    setLoading(false);
    void (async () => {
      let path: string | null = null;
      try {
        path = await resolveWorkflowParkEvidence(context, workflowExecutionId);
        if (!path || controller.signal.aborted) return;
        const page = await readWorkflowParkEvidence(context, path, null, controller.signal);
        if (!controller.signal.aborted) setState({ context, path, page });
      } catch (error) {
        if (path && !controller.signal.aborted && !isHidden(error))
          setState({ context, path, page: null, message: "Park details could not be loaded." });
      } finally {
        if (request.current === controller) request.current = null;
      }
    })();
    return () => {
      request.current?.abort();
      request.current = null;
    };
  }, [context, workflowExecutionId]);

  if (!state || state.context !== context) return null;
  const { path, page, message } = state;
  const cursor = page?.next ?? null;

  async function loadPage() {
    if (request.current) return;
    const controller = new AbortController();
    request.current = controller;
    setLoading(true);
    try {
      const next = await readWorkflowParkEvidence(context, path, cursor, controller.signal);
      if (controller.signal.aborted) return;
      if (!next.next) status.current?.focus();
      setState({ context, path, page: page ? { ...next, deliveries: [...page.deliveries, ...next.deliveries] } : next,
        message: page && !next.next ? "All kept deliveries loaded." : undefined });
    } catch (error) {
      if (controller.signal.aborted) return;
      if (isHidden(error)) setState(null);
      else if (error instanceof StudioHttpError && error.status === 409) {
        status.current?.focus();
        setState({ context, path, page: null, message: "The Park changed. Reload its current details." });
      } else setState({ context, path, page, message: "Park details could not be loaded." });
    } finally {
      if (request.current === controller) {
        request.current = null;
        setLoading(false);
      }
    }
  }

  return <details className="wf-run-park-evidence">
    <summary>Park details</summary>
    {page ? <>
      <p className="wf-run-park-evidence__fault">{page.faultSummary}</p>
      <p>Parked at <time dateTime={page.parkedAt}>{formatDate(page.parkedAt)}</time></p>
      {page.deliveries.length ? <ul aria-label="Kept deliveries" className="wf-run-park-evidence__deliveries">
        {page.deliveries.map(delivery => <li key={delivery.handle}>
          <strong>{delivery.kind}</strong>
          <span>{delivery.state} · {delivery.attempts} {delivery.attempts === 1 ? "attempt" : "attempts"}</span>
          {delivery.dueAt ? <span>Due <time dateTime={delivery.dueAt}>{formatDate(delivery.dueAt)}</time></span> : null}
        </li>)}
      </ul> : <p>No kept deliveries.</p>}
    </> : null}
    <p ref={status} role="status" tabIndex={-1}>{loading ? "Loading Park details…" : message ?? ""}</p>
    {cursor || !page ? <StudioButton type="button" size="sm" aria-disabled={loading} onClick={() => void loadPage()}>
      {page ? "Load more deliveries" : "Reload Park details"}
    </StudioButton> : null}
  </details>;
}

function isHidden(error: unknown) {
  return error instanceof StudioHttpError && [401, 403, 404].includes(error.status);
}
