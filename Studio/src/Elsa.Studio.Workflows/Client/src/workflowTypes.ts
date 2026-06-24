import type { StudioActivityDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";

export interface WorkflowDefinitionsResponse {
  definitions: WorkflowDefinitionSummary[];
  page?: number;
  pageSize?: number;
  totalCount?: number;
}

export interface WorkflowDefinitionSummary {
  id: string;
  name: string;
  description?: string | null;
  createdAt: string;
  lastModifiedAt: string;
  deletedAt?: string | null;
  draftId?: string | null;
  latestVersionId?: string | null;
  latestVersion?: string | null;
  versionCount: number;
}

export type DefinitionListState = "active" | "deleted";

export interface WorkflowDefinitionDetails {
  definition: WorkflowDefinitionSummary;
  draft?: WorkflowDraft | null;
  versions: WorkflowVersionSummary[];
}

export interface WorkflowVersionSummary {
  id: string;
  version: string;
  createdAt: string;
}

export interface WorkflowDraft {
  id: string;
  definitionId: string;
  sourceVersionId?: string | null;
  state: WorkflowDefinitionState;
  layout: DesignMetadataRecord[];
  validationErrors: ValidationError[];
}

export interface WorkflowDefinitionState {
  variables?: unknown[];
  rootActivity?: ActivityNode | null;
  inputs?: unknown[];
  outputs?: unknown[];
  workflowActivityOptions?: unknown;
  strategyOptions?: unknown;
}

export interface ActivityNode {
  nodeId: string;
  activityVersionId: string;
  inputs: unknown[];
  outputs: unknown[];
  structure?: ActivityNodeStructure | null;
  [key: string]: unknown;
}

export interface ActivityNodeStructure {
  kind: string;
  schemaVersion: string;
  payload: Record<string, unknown>;
}

export interface DesignMetadataRecord {
  nodeId: string;
  x: number;
  y: number;
  width?: number | null;
  height?: number | null;
  additionalProperties?: Record<string, unknown> | null;
}

export interface ValidationError {
  path?: string | null;
  code?: string | null;
  message?: string | null;
  [key: string]: unknown;
}

export interface ActivityCatalogResponse {
  activities: ActivityCatalogItem[];
}

export interface ActivityCatalogItem {
  activityVersionId: string;
  activityTypeKey: string;
  version: string;
  category: string;
  displayName: string;
  description?: string | null;
  icon?: string | null;
  iconColor?: string | null;
  executionType: string;
  icon?: string | null;
  iconName?: string | null;
  iconColor?: string | null;
  inputs: unknown[];
  outputs: unknown[];
  designFacets: unknown[];
}

export type ActivityDescriptor = StudioActivityDescriptor;
export type ExpressionDescriptor = StudioExpressionDescriptor;

export interface ActivityDescriptorsResponse {
  items?: ActivityDescriptor[];
  activities?: ActivityDescriptor[];
  descriptors?: ActivityDescriptor[];
}

export interface ExpressionDescriptorsResponse {
  items?: ExpressionDescriptor[];
  descriptors?: ExpressionDescriptor[];
  expressionDescriptors?: ExpressionDescriptor[];
}

export interface CreateDefinitionRequest {
  name: string;
  description?: string | null;
  rootKind: "sequence" | "flowchart";
  rootActivityVersionId?: string | null;
}

export interface PromoteDraftResponse {
  versionId: string;
}

export interface PublishedWorkflowResponse {
  artifactId: string;
  definitionId: string;
  definitionVersionId: string;
  artifactVersion: string;
  artifactHash: string;
  rootActivityId: string;
  nodeCount: number;
}

export interface StartWorkflowDraftTestRunRequest {
  definitionId: string;
  snapshotId: string;
  state: WorkflowDefinitionState;
  artifactVersion?: string | null;
}

export interface WorkflowTestRunView {
  testRunId: string;
  definitionId: string;
  definitionVersionId: string;
  artifactId?: string | null;
  workflowExecutionId?: string | null;
  status: string;
  commandDispatchStatus?: string | null;
  reason?: string | null;
  expiresAt?: string | null;
}

export interface WorkflowExecutableSummary {
  artifactId: string;
  artifactVersion: string;
  artifactHash: string;
  definitionId: string;
  definitionVersionId: string;
  createdAt: string;
  publishedAt?: string | null;
  sourceKind?: string | null;
  sourceId?: string | null;
  sourceVersion?: string | null;
  rootActivityType: string;
  rootActivityVersion: string;
  nodeCount: number;
  resumeTargetCount: number;
}

export interface WorkflowInstanceSummary {
  workflowExecutionId: string;
  artifactId: string;
  definitionId: string;
  definitionVersionId: string;
  artifactVersion: string;
  artifactHash: string;
  status: string;
  subStatus?: string | null;
  createdAt: string;
  startedAt?: string | null;
  updatedAt?: string | null;
  completedAt?: string | null;
  correlationId?: string | null;
  parentWorkflowExecutionId?: string | null;
  tenantId?: string | null;
  activityCount: number;
  incidentCount: number;
}

export interface WorkflowInstanceDetails {
  instance: WorkflowInstanceSummary;
  activities: ActivityExecutionStateSummary[];
  incidents: IncidentStateSummary[];
}

export interface ActivityExecutionStateSummary {
  activityExecutionId: string;
  workflowExecutionId: string;
  executableNodeId: string;
  authoredActivityId: string;
  activityType: string;
  activityTypeVersion: string;
  status: string;
  subStatus?: string | null;
  scheduledAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  schedulingActivityExecutionId?: string | null;
  parentActivityExecutionId?: string | null;
  branchId?: string | null;
  iterationId?: string | null;
  callStackDepth?: number | null;
  bookmarkIds: string[];
  incidentIds: string[];
  faultCount: number;
  aggregateFaultCount: number;
  metadata: Record<string, string>;
}

export interface IncidentStateSummary {
  incidentId: string;
  workflowExecutionId: string;
  activityExecutionId?: string | null;
  executableNodeId?: string | null;
  severity: string;
  status: string;
  resolutionAction: string;
  failureType: string;
  message: string;
  createdAt: string;
  resolvedAt?: string | null;
  isBlocking: boolean;
  metadata: Record<string, string>;
}
