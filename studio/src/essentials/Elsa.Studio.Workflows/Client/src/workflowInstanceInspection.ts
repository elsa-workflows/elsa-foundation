import type { InspectionGranularity, PersistenceMode, WorkflowInstanceDetails, WorkflowInstanceSummary } from "./workflowTypes";

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

/** A retained child can carry executable producer metadata without an attributed authoring source. */
export function hasUnattributedChildDefinition(instance: Pick<WorkflowInstanceSummary, "parentWorkflowExecutionId" | "sourceReferenceId"> | null | undefined): boolean {
  return Boolean(instance?.parentWorkflowExecutionId?.trim()) && instance?.sourceReferenceId === null;
}
