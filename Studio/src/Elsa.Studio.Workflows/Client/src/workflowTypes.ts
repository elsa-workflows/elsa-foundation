import type { StudioActivityDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";

export interface WorkflowDefinitionsResponse {
  definitions: WorkflowDefinitionSummary[];
  page?: number;
  pageSize?: number;
  totalCount?: number;
}

export interface WorkflowExecutablesResponse {
  executables: WorkflowExecutableSummary[];
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

export interface WorkflowDefinitionVersionDetails {
  id: string;
  version: string;
  definition: Pick<WorkflowDefinitionSummary, "id" | "name" | "description" | "createdAt" | "lastModifiedAt">;
  state: WorkflowDefinitionState;
  layout: DesignMetadataRecord[];
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

// Activity availability policy stack (mirrors Elsa.Activities.Design.Core.Models).
// Enum-valued fields arrive as either the numeric or string form depending on backend JSON config,
// so unions keep `number` alongside the named values; normalize with the helpers in activityAvailability.ts.
export type ActivityAvailabilityMode = "AllExcept" | "Only";

export interface ActivityAvailabilityRuleSet {
  activityTypes: string[];
  sets: string[];
}

export interface ActivityAvailabilitySettings {
  scope: string;
  mode: ActivityAvailabilityMode | number;
  rules: ActivityAvailabilityRuleSet;
}

export type ActivityAvailabilityDiagnosticState =
  | "Available"
  | "BlockedByHostBaseline"
  | "HiddenByManagementSettings"
  | "RemovedFromCatalog"
  | "UnresolvedReference";

export type ActivityAvailabilityPolicyLayer = "Catalog" | "HostBaseline" | "ManagementSettings";

export type ActivityAvailabilityReferenceKind = "ActivityType" | "ActivitySet";

export interface ActivityAvailabilityDiagnosticEntry {
  activityDefinitionId?: string | null;
  activityTypeKey?: string | null;
  displayName?: string | null;
  category?: string | null;
  state: ActivityAvailabilityDiagnosticState | number;
  layer: ActivityAvailabilityPolicyLayer | number;
  referenceKind: ActivityAvailabilityReferenceKind | number;
  referenceName?: string | null;
  reason: string;
}

export interface ActivityAvailabilitySetDiagnostic {
  name: string;
  activityTypeKeys: string[];
}

export interface ActivityAvailabilityDiagnostics {
  items: ActivityAvailabilityDiagnosticEntry[];
  sets: ActivityAvailabilitySetDiagnostic[];
}

export interface SaveActivityAvailabilitySettingsRequest {
  scope?: string;
  mode: number;
  rules: ActivityAvailabilityRuleSet;
}

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

// Canonical workflow variable shape (matches the Code-tab JSON the backend round-trips).
// Unknown fields on existing entries are preserved through edits, so the index signature
// keeps them addressable without losing the well-known keys.
export interface WorkflowVariable {
  id: string;
  name: string;
  typeName: string;
  isArray: boolean;
  value: string | null;
  storageDriverTypeName: string | null;
  [key: string]: unknown;
}

// Workflow inputs carry the full argument-definition field set plus binding metadata.
export interface WorkflowInput {
  name: string;
  type: string;
  displayName: string;
  description: string;
  category: string;
  isArray: boolean;
  uiHint: string;
  storageDriverType: string | null;
  defaultValue: string | null;
  defaultSyntax: string | null;
  isReadOnly: boolean | null;
  [key: string]: unknown;
}

// Outputs are produced by activities, so they have a smaller field set than inputs.
export interface WorkflowOutput {
  name: string;
  type: string;
  displayName: string;
  description: string;
  category: string;
  isArray: boolean;
  [key: string]: unknown;
}

export interface VariableTypeDescriptor {
  typeName: string;
  displayName?: string | null;
  category?: string | null;
  description?: string | null;
}

export interface StorageDriverDescriptor {
  typeName: string;
  displayName?: string | null;
  deprecated?: boolean;
  priority?: number;
}

export interface VariableTypeDescriptorsResponse {
  items?: VariableTypeDescriptor[];
  descriptors?: VariableTypeDescriptor[];
}

export interface StorageDriverDescriptorsResponse {
  items?: StorageDriverDescriptor[];
  descriptors?: StorageDriverDescriptor[];
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
  activityCount?: number | null;
  incidentCount?: number | null;
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
  deletedAt?: string | null;
  sourceKind?: string | null;
  sourceId?: string | null;
  sourceVersion?: string | null;
  rootActivityType: string;
  rootActivityVersion: string;
  nodeCount: number;
  resumeTargetCount: number;
}

export interface WorkflowExecutableRunResponse {
  workflowExecutionId?: string | null;
  runId?: string | null;
  executionId?: string | null;
}

export interface WorkflowInstanceSummary {
  workflowExecutionId: string;
  artifactId: string;
  definitionId: string;
  definitionVersionId: string;
  artifactVersion: string;
  artifactHash: string;
  runKind?: string | null;
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
