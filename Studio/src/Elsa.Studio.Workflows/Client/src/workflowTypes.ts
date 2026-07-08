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
  variables?: VariableDefinition[];
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
  // Backend `ValidationError.Type` (e.g. "Expressions/UnresolvedVariable"); serialized as `type`.
  // `code` is kept for backward-compatible readers but the backend never populates it.
  type?: string | null;
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
  description?: string | null;
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

// A .NET type identity as the backend serializes it (Elsa.Primitives.Models.TypeInformation).
// The backend resolves a variable's CLR type from `namespace.typeName` via its well-known type
// registry, so those two fields carry the meaning; assembly fields round-trip but are not required
// for resolution of framework types.
export interface TypeInformation {
  typeName: string;
  namespace: string;
  assemblyName: string;
  assemblyVersion: string;
  [key: string]: unknown;
}

// A literal/expression argument value (Elsa.Activities.Design.Core.Models.ArgumentValue).
export interface ArgumentValue {
  value: unknown;
  expressionType: string;
  [key: string]: unknown;
}

// Collection-ness of an authored argument type (typed-argument-model wire contract).
// `Single` is a scalar; the rest wrap the element type. Absent on the wire ⇒ treated as `Single`.
export type CollectionKind = "Single" | "Array" | "List" | "HashSet";

export const collectionKinds: CollectionKind[] = ["Single", "Array", "List", "HashSet"];

// The type of an authored Variable/Input/Output. `alias` is a stable element-type identifier
// (bare for framework primitives like "String"/"Int32"; dotted for module types like
// "Elsa.Http.HttpRequest") — never a namespace/assembly/version. Unknown aliases round-trip unchanged.
export interface ArgumentType {
  alias: string;
  collectionKind: CollectionKind;
}

// Canonical scoped-variable declaration (Elsa.Expressions.Core.Models.VariableDefinition, ADR-0027).
// `referenceKey` is the stable identity; `name` is display-only. Workflow-scoped declarations live on
// `WorkflowDefinitionState.variables`; container-scoped declarations live on a container node's
// `structure.payload.variables` in this exact shape, so both round-trip through the state wire layer
// unchanged. The index signature preserves unknown fields authored elsewhere through edits.
export interface VariableDefinition {
  referenceKey: string;
  name: string;
  type: ArgumentType;
  storageDriverType?: string | null;
  default?: ArgumentValue | null;
  [key: string]: unknown;
}

// A structured reference to a scoped variable from an activity input (ADR-0027). `declaringScopeId`
// is the declaring container node id, or the workflow-scope sentinel for workflow-scoped variables.
export interface VariableReference {
  referenceKey: string;
  declaringScopeId?: string | null;
}

// A variable visible from a selected activity, nearest-scope first (backend VisibleVariableView).
export interface VisibleVariableView {
  referenceKey: string;
  name: string;
  scopeId: string;
  isWorkflowScope: boolean;
}

// A non-blocking advisory that a container scope declares a name that shadows a visible ancestor
// declaration (backend ScopedVariableShadowingWarning). Shadowing is allowed; never an error.
export interface ScopedVariableShadowingWarning {
  scopeId: string;
  shadowedScopeId: string;
  name: string;
  referenceKey: string;
}

// Response of the scoped-variable design endpoint (elsa-foundation#285), wrapping
// ScopedVariableAuthoringContract.GetVisibleVariables + GetShadowingWarnings.
export interface ScopedVariableAnalysisResponse {
  visibleVariables: VisibleVariableView[];
  shadowingWarnings: ScopedVariableShadowingWarning[];
}

// Workflow inputs carry the argument-definition field set plus binding metadata. Collection-ness lives
// on `type.collectionKind` (the legacy `isArray` boolean is gone). `referenceKey` is the stable identity
// (a required positional on the backend InputDefinition). The backend InputDefinition has no default
// value member, so no `defaultValue`/`defaultSyntax` is carried; `isRequired` is a first-class flag.
export interface WorkflowInput {
  referenceKey: string;
  name: string;
  type: ArgumentType;
  displayName: string;
  description: string;
  category: string;
  uiHint: string;
  storageDriverType: string | null;
  isRequired?: boolean;
  [key: string]: unknown;
}

// Outputs are produced by activities, so they have a smaller field set than inputs (no default/storage).
// `referenceKey` is the stable identity (a required positional on the backend OutputDefinition).
export interface WorkflowOutput {
  referenceKey: string;
  name: string;
  type: ArgumentType;
  displayName: string;
  description: string;
  category: string;
  [key: string]: unknown;
}

// A selectable argument element type from GET /descriptors/variables (typed-argument-model contract).
// `alias` is the join key against a stored `type.alias`; `defaultEditor` is an open hint for the
// default-value editor (text/number/checkbox/date/none — tolerate unknown values). `typeName` is a
// legacy fallback for older backends that returned it instead of `alias`.
export interface VariableTypeDescriptor {
  alias: string;
  displayName?: string | null;
  category?: string | null;
  defaultEditor?: string | null;
  typeName?: string | null;
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
