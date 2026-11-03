import { useId } from "react";
import {
  readWorkflowInstanceInspectionGranularity,
  readWorkflowInstancePersistenceMode
} from "../workflowInstanceInspection";
import type { InspectionGranularity, PersistenceMode, WorkflowInstanceDetails } from "../workflowTypes";

const checkpointCadenceHelp: Record<PersistenceMode, string> = {
  Durable: "This run checkpoints after every activity. Inspection shows exact per-activity evidence.",
  Balanced: "This run checkpoints at boundaries, so inspection shows boundary-level detail.",
  Ephemeral: "This run checkpoints only at key events, so inspection shows boundary-level detail."
};

const inspectionGranularityHelp: Record<InspectionGranularity, string> = {
  "activity-level": "Per-activity evidence is exact.",
  "boundary-level": "Per-activity evidence inside a segment is folded to the commit boundary, so timeline values reflect the durable boundary."
};

const inspectionGranularityLabel: Record<InspectionGranularity, string> = {
  "activity-level": "Activity-level",
  "boundary-level": "Boundary-level"
};

export function formatInspectionGranularity(value: InspectionGranularity) {
  return inspectionGranularityLabel[value];
}

export function WorkflowInstanceInspectionFacts({
  persistenceMode,
  inspectionGranularity
}: Pick<WorkflowInstanceDetails, "persistenceMode" | "inspectionGranularity">) {
  const cadenceHelpId = useId();
  const inspectionHelpId = useId();
  const mode = readWorkflowInstancePersistenceMode(persistenceMode);
  const granularity = readWorkflowInstanceInspectionGranularity(inspectionGranularity);
  if (!mode && !granularity) return null;

  return (
    <>
      {mode ? (
        <>
          <dt>Checkpoint cadence</dt>
          <dd className="wf-identity-cell">
            <span>{mode}</span>
            <small id={cadenceHelpId}>{checkpointCadenceHelp[mode]}</small>
          </dd>
        </>
      ) : null}
      {granularity ? (
        <>
          <dt>Inspection detail</dt>
          <dd className="wf-identity-cell">
            <span>{formatInspectionGranularity(granularity)}</span>
            {granularity === "boundary-level"
              ? null
              : <small id={inspectionHelpId}>{inspectionGranularityHelp[granularity]}</small>}
          </dd>
        </>
      ) : null}
    </>
  );
}

export function WorkflowInstanceInspectionNote({
  inspectionGranularity
}: Pick<WorkflowInstanceDetails, "inspectionGranularity">) {
  const inspectionHelpId = useId();
  const granularity = readWorkflowInstanceInspectionGranularity(inspectionGranularity);
  if (granularity !== "boundary-level") return null;
  return (
    <p id={inspectionHelpId} className="wf-instance-note wf-muted" role="note">
      {inspectionGranularityHelp[granularity]}
    </p>
  );
}
