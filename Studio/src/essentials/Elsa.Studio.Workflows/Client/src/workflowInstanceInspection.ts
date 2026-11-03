import type { InspectionGranularity, PersistenceMode, WorkflowInstanceDetails } from "./workflowTypes";

const persistenceModes: readonly PersistenceMode[] = ["Durable", "Balanced", "Ephemeral"];
const inspectionGranularities: readonly InspectionGranularity[] = ["activity-level", "boundary-level"];

export function readWorkflowInstancePersistenceMode(value: unknown): PersistenceMode | undefined {
  return persistenceModes.find(mode => mode === value);
}

export function readWorkflowInstanceInspectionGranularity(value: unknown): InspectionGranularity | undefined {
  return inspectionGranularities.find(granularity => granularity === value);
}

/** Keeps only recognized cadence/granularity values so an older or unknown host omits them. */
export function mapWorkflowInstanceDetails(payload: WorkflowInstanceDetails): WorkflowInstanceDetails {
  const persistenceMode = readWorkflowInstancePersistenceMode(payload.persistenceMode);
  const inspectionGranularity = readWorkflowInstanceInspectionGranularity(payload.inspectionGranularity);
  return {
    ...payload,
    persistenceMode,
    inspectionGranularity
  };
}
