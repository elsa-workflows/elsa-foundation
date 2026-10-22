import { useEffect, useState } from "react";
import "./IncidentNextStep.css";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier } from "@elsa-workflows/studio-ui";
import { getDefinition } from "../api/workflowDesign";
import { isActiveIncident } from "../workflowRuntimeOverlays";
import { formatDate } from "../workflowFormatting";
import type { IncidentStateSummary, WorkflowInstanceSummary } from "../workflowTypes";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import type { IncidentNextStepGuidance } from "./useOperatorActions";

export interface DefinitionAccessObservation {
  context: StudioEndpointContext | null;
  definitionId: string | null;
  status: "checking" | "readable" | "unconfirmed";
}

export function canReviewDefinition(
  observation: DefinitionAccessObservation,
  context: StudioEndpointContext | undefined,
  definitionId: string | null | undefined
) {
  const exactDefinitionId = definitionId?.trim() || null;
  return !!exactDefinitionId
    && observation.context === context
    && observation.definitionId === exactDefinitionId
    && observation.status === "readable";
}

export function useDefinitionReviewAccess(
  context: StudioEndpointContext | undefined,
  definitionId: string | null | undefined,
  enabled = true
): DefinitionAccessObservation {
  const exactDefinitionId = definitionId?.trim() || null;
  const [observation, setObservation] = useState<DefinitionAccessObservation>({
    context: null,
    definitionId: null,
    status: "unconfirmed"
  });

  useEffect(() => {
    let active = true;
    if (!enabled || !context || !exactDefinitionId) {
      setObservation({ context: context ?? null, definitionId: exactDefinitionId, status: "unconfirmed" });
      return () => { active = false; };
    }

    setObservation({ context, definitionId: exactDefinitionId, status: "checking" });
    void getDefinition(context, exactDefinitionId).then(details => {
      if (!active) return;
      setObservation({
        context,
        definitionId: exactDefinitionId,
        status: details?.definition?.id === exactDefinitionId ? "readable" : "unconfirmed"
      });
    }, () => {
      if (active) setObservation({ context, definitionId: exactDefinitionId, status: "unconfirmed" });
    });

    return () => { active = false; };
  }, [context, enabled, exactDefinitionId]);

  if (!enabled) return { context: context ?? null, definitionId: exactDefinitionId, status: "unconfirmed" };
  if (observation.context !== context || observation.definitionId !== exactDefinitionId) {
    return { context: context ?? null, definitionId: exactDefinitionId, status: exactDefinitionId && context ? "checking" : "unconfirmed" };
  }
  return observation;
}

export function IncidentNextStep({
  context,
  summary,
  incident,
  guidance,
  onOpenDefinition,
  definitionAccess: suppliedDefinitionAccess
}: {
  context?: StudioEndpointContext;
  summary?: WorkflowInstanceSummary | null;
  incident: IncidentStateSummary;
  guidance: IncidentNextStepGuidance;
  onOpenDefinition?(definitionId: string): void;
  definitionAccess?: DefinitionAccessObservation;
}) {
  const sameRun = !!summary && incident.workflowExecutionId === summary.workflowExecutionId;
  const definitionId = sameRun ? summary.definitionId?.trim() || null : null;
  const runHasEnded = sameRun && !!summary
    && ["completed", "faulted", "cancelled", "canceled"].includes(summary.status.trim().toLowerCase());
  const localDefinitionAccess = useDefinitionReviewAccess(context, definitionId, !suppliedDefinitionAccess);
  const definitionAccess = suppliedDefinitionAccess ?? localDefinitionAccess;

  const canReview = canReviewDefinition(definitionAccess, context, definitionId);
  const hasCurrentObservation = definitionAccess.context === context && definitionAccess.definitionId === definitionId;
  const checkingDefinition = !!definitionId && !!context
    && (!hasCurrentObservation || definitionAccess.status === "checking");

  let nextStep: string;
  if (!summary) {
    nextStep = "The current run context is unavailable here. Inspect this incident's recorded evidence or reopen its run for guidance.";
  } else if (!sameRun) {
    nextStep = "This incident does not match the run currently open. Its run guidance is unavailable here.";
  } else if (!isActiveIncident(incident)) {
    nextStep = "This incident is historical. Review its recorded outcome and the current run status before taking any action.";
  } else if (runHasEnded && summary) {
    nextStep = `This run has ended (${summary.status}). Review its recorded outcome; do not start another action from this incident.`;
  } else if (guidance.requesterCancelled) {
    nextStep = "The requesting activity was canceled. This incident cannot start its handler or accept a decision; inspect the run evidence or share the incident with an operator.";
  } else if (guidance.pendingIncidentId) {
    nextStep = guidance.pendingIncidentId === incident.incidentId
      ? "An action for this incident is being followed. Wait for the run to update; a second action is paused."
      : "An action for another incident in this run is being followed. Wait for it to finish before considering another action.";
  } else if (guidance.retryNotBefore) {
    nextStep = guidance.retryDue
      ? `An automatic retry is due${formatDate(guidance.retryNotBefore) ? ` (${formatDate(guidance.retryNotBefore)})` : ""} and will run at the next scheduling pass. Do not submit another retry.`
      : `An automatic retry is scheduled${formatDate(guidance.retryNotBefore) ? ` for ${formatDate(guidance.retryNotBefore)}` : ""}. Wait for it; do not submit another retry.`;
  } else if (!incident.isBlocking) {
    nextStep = "This incident is active but does not block the run. Review its exact activity or input evidence before changing the workflow.";
  } else if (guidance.retryReviewEligibility.reason === "source-not-faulted") {
    nextStep = `The source activity is ${guidance.retryReviewEligibility.observedStatus}; Retry applies only after it is Faulted. Inspect the exact activity or input evidence before changing the workflow.`;
  } else if (guidance.retryReviewEligibility.reason === "parent-not-running") {
    nextStep = "The recorded parent is not running, so Retry is not available for this scope. Review the activity evidence or share the incident with an operator.";
  } else if (guidance.retryReviewEligibility.reason === "blocking-descendant-incident") {
    nextStep = "Another blocking incident is recorded on a descendant activity. Review that incident before retrying this activity.";
  } else if (guidance.retryReviewEligibility.reason === "descendant-ancestry-not-loaded") {
    nextStep = "A blocking incident is recorded, but its activity ancestry is incomplete. Inspect the run evidence or share this incident before retrying.";
  } else if (guidance.retryReviewEligibility.reason === "same-source-blocking-incident-not-waiting") {
    nextStep = "Another blocking incident on this activity is not ready for Retry. Inspect its evidence or share the incident with an operator.";
  } else if (guidance.canAct && guidance.retryReviewEligibility.status === "eligible" && guidance.retryJournalAvailability === "checking") {
    nextStep = "Checking whether a retry is already scheduled. Wait for this read before choosing Retry.";
  } else if (guidance.canAct && guidance.retryReviewEligibility.status === "eligible" && guidance.retryJournalAvailability === "unknown") {
    nextStep = "The run has not confirmed whether a retry is already scheduled. Check Retry availability before retrying.";
  } else if (guidance.retryReviewEligibility.status === "unknown") {
    nextStep = "The exact activity state needed to confirm Retry is unavailable here. Inspect the selected run evidence or share the incident with an operator.";
  } else if (guidance.actionAvailability === "checking") {
    nextStep = "Checking whether a run action is available. You can inspect or copy the incident while this check completes.";
  } else if (guidance.actionAvailability === "unknown") {
    nextStep = "A run action could not be confirmed. Inspect the available run evidence or share the incident with an operator.";
  } else if (guidance.actionAvailability === "unavailable") {
    nextStep = "No run action is available in this view for this incident. Inspect the run evidence or share the incident with an operator.";
  } else if (!guidance.canAct) {
    nextStep = "This view does not confirm permission to change the run. Inspect the evidence or share the incident with an operator.";
  } else {
    nextStep = "Review the run evidence and any available actions below; choose one only if it matches the situation.";
  }

  return (
    <section className="wf-incident-next-step" aria-labelledby="wf-incident-next-step-title">
      <h4 id="wf-incident-next-step-title">Next step</h4>
      {sameRun && summary ? (
        <p className="wf-incident-next-step-run"><span>Current run</span> <WorkflowStatusBadge status={summary.status} subStatus={summary.subStatus} /></p>
      ) : null}
      <p className="wf-incident-next-step-copy">{nextStep}</p>
      {definitionId ? (
        <div className="wf-incident-next-step-definition">
          <CopyableIdentifier label="Definition ID" value={definitionId} />
          {canReview && onOpenDefinition ? (
            <>
              <button type="button" className="wf-incident-next-step-open-definition" onClick={() => onOpenDefinition(definitionId)}>
                Review workflow definition
              </button>
              <p>This run keeps using the workflow version it started with. Definition changes affect future runs.</p>
            </>
          ) : checkingDefinition ? (
            <p role="status">Checking whether this definition is available to review.</p>
          ) : (
            <p>Definition access could not be confirmed here. Inspect the current run evidence or copy and share this incident.</p>
          )}
        </div>
      ) : null}
    </section>
  );
}
