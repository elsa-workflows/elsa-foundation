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
  const mode = readWorkflowInstancePersistenceMode(persistenceMode);
  const granularity = readWorkflowInstanceInspectionGranularity(inspectionGranularity);
  if (!mode && !granularity) return null;

  return (
    <>
      {mode ? (
        <>
          <dt>Checkpoint cadence</dt>
          <dd title={checkpointCadenceHelp[mode]}>{mode}</dd>
        </>
      ) : null}
      {granularity ? (
        <>
          <dt>Inspection</dt>
          <dd title={inspectionGranularityHelp[granularity]}>{formatInspectionGranularity(granularity)}</dd>
        </>
      ) : null}
    </>
  );
}

export function WorkflowInstanceInspectionNote({
  inspectionGranularity
}: Pick<WorkflowInstanceDetails, "inspectionGranularity">) {
  const granularity = readWorkflowInstanceInspectionGranularity(inspectionGranularity);
  if (granularity !== "boundary-level") return null;
  return (
    <p className="wf-instance-note wf-muted" role="note">
      {inspectionGranularityHelp[granularity]}
    </p>
  );
}
