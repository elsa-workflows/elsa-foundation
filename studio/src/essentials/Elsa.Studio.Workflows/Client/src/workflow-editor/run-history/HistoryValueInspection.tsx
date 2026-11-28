import { useEffect, useMemo, useRef } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { getActivityExecutionInspection } from "../../api/runtime";
import { RuntimeHistoryIdentityError } from "../../api/runtimeHistory";
import { evaluationPhase, evaluationSequence } from "../../inputInspectionRows";
import { RuntimeValueEvidenceCard, RuntimeValueEvidenceResolutionProvider } from "../RuntimeValueEvidence";
import { historyInstant } from "./historyPresentation";
import { useHistoryResource } from "./useHistoryResource";

/** Entered only by an explicit value action. Resolve the referenced observation, never a later value with the same name. */
export function HistoryValueInspection({ context, workflowExecutionId, activityExecutionId, evidenceId, onClose }: {
  context: StudioEndpointContext; workflowExecutionId: string; activityExecutionId: string; evidenceId: string; onClose(): void;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  const resource = useHistoryResource(context, JSON.stringify([workflowExecutionId, activityExecutionId, "event-value", evidenceId]), async signal => {
    const inspection = await getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId, signal);
    if (inspection.workflowExecutionId !== workflowExecutionId || inspection.activityExecutionId !== activityExecutionId)
      throw new RuntimeHistoryIdentityError("The value observation did not match the recorded owning execution.");
    return inspection;
  }, false);
  const scope = useMemo(() => ({ context, workflowExecutionId, activityExecutionId }), [context, workflowExecutionId, activityExecutionId]);
  const snapshot = resource.value?.valueSnapshots?.find(item => item.evidenceId === evidenceId);
  useEffect(() => { heading.current?.focus(); heading.current?.scrollIntoView?.({ block: "nearest" }); }, [evidenceId]);
  return <section className="wf-history-recorded-value" aria-label="Recorded value Data">
    <h4 ref={heading} tabIndex={-1}>Recorded value · Data</h4>
    <p><code>{evidenceId}</code></p><StudioButton type="button" onClick={onClose}>Close recorded value</StudioButton>
    {resource.error ? <p role="status">{resource.error} <StudioButton type="button" onClick={resource.retry}>Retry value observation</StudioButton></p> : null}
    {snapshot ? <><dl className="wf-history-facts">
      <dt>Subject</dt><dd>{snapshot.subject}</dd><dt>Phase</dt><dd>{evaluationPhase(snapshot) || "Not recorded"}</dd>
      <dt>Observation sequence</dt><dd>{evaluationSequence(snapshot) ?? "Not recorded"}</dd><dt>Captured</dt><dd>{historyInstant(snapshot.capturedAt)}</dd>
    </dl><RuntimeValueEvidenceResolutionProvider scope={scope}><RuntimeValueEvidenceCard snapshot={snapshot} listItem={false} /></RuntimeValueEvidenceResolutionProvider></>
      : resource.value ? <p>This exact captured observation is unavailable. A current or later value is not substituted.</p>
        : resource.status === "loading" ? <p role="status">Loading the exact value observation…</p> : null}
  </section>;
}
