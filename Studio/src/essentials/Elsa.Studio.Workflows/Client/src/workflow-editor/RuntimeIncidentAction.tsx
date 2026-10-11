import React from "react";
import { AlertCircle, AlertTriangle } from "lucide-react";
import type { WorkflowRuntimeNodeOverlay } from "../workflowAdapter";

/** Shared, keyboard-accessible incident cue for both Elsa activity and BPMN canvas nodes. */
export function RuntimeIncidentAction({
  runtime,
  onOpenIncident,
  activityLabel,
  className = ""
}: {
  runtime: WorkflowRuntimeNodeOverlay;
  onOpenIncident?(incidentId: string, targetNodeId?: string | null): void;
  activityLabel?: string;
  className?: string;
}) {
  const incidentId = runtime.primaryIncidentId;
  const containedIncidentId = runtime.containedPrimaryIncidentId;
  if (!incidentId && !containedIncidentId) return null;

  const label = activityLabel?.trim() || "this activity";
  const isActive = runtime.incidentCount > 0;
  const accessibleLabel = isActive
    ? `Open incidents for ${label}, ${runtime.incidentCount} active${runtime.hasBlockingIncident ? ", needs intervention" : ", non-blocking"}`
    : `Open incident history for ${label}, ${runtime.historicalIncidentCount ?? 0} resolved or suppressed incidents`;
  const affectedActivityCount = runtime.containedAffectedActivityCount ?? 0;
  const containedIncidentCount = runtime.containedIncidentCount ?? 0;
  const containedAccessibleLabel = containedIncidentId
    ? `Open ${containedIncidentCount} ${containedIncidentCount === 1 ? "incident" : "incidents"} inside ${label}, affecting ${affectedActivityCount} child ${affectedActivityCount === 1 ? "activity" : "activities"}${runtime.containsBlockingIncident ? ", needs intervention" : ", non-blocking"}`
    : "";
  const interactionClasses = [className, "nodrag", "nopan", "nokey"].filter(Boolean).join(" ");

  return (
    <>
      {incidentId ? (
        <button
          type="button"
          className={["wf-node-incident-action", interactionClasses].filter(Boolean).join(" ")}
          data-health={isActive ? runtime.hasBlockingIncident ? "blocking" : "active" : "history"}
          aria-label={accessibleLabel}
          title={accessibleLabel}
          onClick={event => {
            event.preventDefault();
            event.stopPropagation();
            onOpenIncident?.(incidentId);
          }}
        >
          {isActive ? <AlertTriangle size={12} aria-hidden="true" /> : <AlertCircle size={12} aria-hidden="true" />}
          {isActive ? `${runtime.incidentCount} active` : `${runtime.historicalIncidentCount ?? 0} history`}
        </button>
      ) : null}
      {containedIncidentId ? (
        <button
          type="button"
          className={["wf-node-incident-action", "wf-node-contained-incident-action", interactionClasses].filter(Boolean).join(" ")}
          data-health={runtime.containsBlockingIncident ? "contained-blocking" : "contained"}
          aria-label={containedAccessibleLabel}
          title={containedAccessibleLabel}
          onClick={event => {
            event.preventDefault();
            event.stopPropagation();
            onOpenIncident?.(containedIncidentId, null);
          }}
        >
          <AlertCircle size={12} aria-hidden="true" />
          {runtime.containedIncidentCount ?? 0} inside
        </button>
      ) : null}
    </>
  );
}
