import type {
  StudioActivityDescriptor,
  StudioActivityInputOption,
  StudioExpressionDescriptor,
  StudioWorkflowRunInputDescriptor,
  StudioWorkflowRunInputTypeMetadata
} from "@elsa-workflows/studio-sdk";

export interface WorkflowDefinitionsResponse {
  definitions: WorkflowDefinitionSummary[];
  page?: number;
  pageSize?: number;
  totalCount?: number;
  nextContinuationToken?: string | null;
  isPaged?: boolean;
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
  /** Organizational placement, intentionally separate from the authored workflow state. */
  folderId?: string | null;
}

export interface WorkflowFolder {
  id: string;
  parentId?: string | null;
  name: string;
  normalizedName: string;
  createdAt: string;
  lastModifiedAt: string;
}

export interface WorkflowFolderDetail {
  folder: WorkflowFolder;
  ancestors: WorkflowFolder[];
}

export type DefinitionListState = "active" | "deleted" | "all";

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
  designFacets?: unknown[];
  available?: boolean;
  availabilityReason?: string | null;
  ports?: unknown[];
  containerStructure?: Record<string, unknown> | null;
  authoringTemplate?: ActivityNode;
}

export type ActivityDescriptor = StudioActivityDescriptor;
export type ExpressionDescriptor = StudioExpressionDescriptor;

export interface ActivityInputOptionsResponse {
  options: StudioActivityInputOption[];
}

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

export type RuntimeDiagnosticsEvidenceLevel = "Off" | "Metadata" | "DiagnosticSnapshot" | "Payload";

export type RuntimeDiagnosticsSubject =
  | "workflowInputs"
  | "workflowOutputs"
  | "activityInputs"
  | "activityOutputs"
  | "incidents"
  | "diagnostics"
  | "containerVariables"
  | "durableValues";

export type RuntimeDiagnosticsSubjectOverrides = Partial<Record<RuntimeDiagnosticsSubject, RuntimeDiagnosticsEvidenceLevel>>;

export interface RuntimeDiagnosticsSettings {
  scope?: string | null;
  defaultLevel: RuntimeDiagnosticsEvidenceLevel;
  subjectOverrides?: RuntimeDiagnosticsSubjectOverrides | null;
  updatedAt?: string | null;
  updatedBy?: string | null;
}

export interface EffectiveRuntimeDiagnosticsSettings {
  defaultLevel: RuntimeDiagnosticsEvidenceLevel;
  subjectOverrides?: RuntimeDiagnosticsSubjectOverrides | null;
  limitationReasons?: string[] | null;
}

export interface RuntimeDiagnosticsSnapshotLimits {
  maxDepth: number;
  maxObjectProperties: number;
  maxArrayItems: number;
  maxStringLength: number;
  maxTotalBytes: number;
}

export interface RuntimeDiagnosticsHostPolicy {
  maximumLevel: RuntimeDiagnosticsEvidenceLevel;
  subjectMaximums?: RuntimeDiagnosticsSubjectOverrides | null;
  limitationReasons?: string[] | null;
  snapshotLimits?: RuntimeDiagnosticsSnapshotLimits | null;
}

export interface RuntimeDiagnosticsPermissions {
  canManage: boolean;
  canEnableFullPayloads: boolean;
}

export interface RuntimeDiagnosticsSettingsView {
  requested: RuntimeDiagnosticsSettings;
  effective: EffectiveRuntimeDiagnosticsSettings;
  hostPolicy: RuntimeDiagnosticsHostPolicy;
  permissions: RuntimeDiagnosticsPermissions;
}

export interface SaveRuntimeDiagnosticsSettingsRequest {
  scope?: string | null;
  defaultLevel: RuntimeDiagnosticsEvidenceLevel;
  subjectOverrides?: RuntimeDiagnosticsSubjectOverrides | null;
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
export interface ArgumentType extends StudioWorkflowRunInputTypeMetadata {
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

export interface WorkflowManagementCapabilities {
  scopedVariableAnalysis?: boolean;
}

// Workflow inputs carry the argument-definition field set plus binding metadata. Collection-ness lives
// on `type.collectionKind` (the legacy `isArray` boolean is gone). `referenceKey` is the stable identity
// (a required positional on the backend InputDefinition). The backend InputDefinition has no default
// value member, so no `defaultValue`/`defaultSyntax` is carried; `isRequired` is a first-class flag.
export interface WorkflowInput extends StudioWorkflowRunInputDescriptor {
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

// A workflow Input expression stores the declaration's stable identity, never its mutable display
// name or an unvalidated text expression.
export interface InputReference {
  referenceKey: string;
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

// A selectable argument element type from the Expressions domain's variable-types capability.
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

export interface VariableTypeDescriptorsResponse {
  items?: VariableTypeDescriptor[];
  descriptors?: VariableTypeDescriptor[];
}

export interface CreateDefinitionRequest {
  name: string;
  description?: string | null;
  initialState?: WorkflowDefinitionState | null;
  layout?: DesignMetadataRecord[];
  folderId?: string | null;
}

export interface PromoteDraftResponse {
  id: string;
  version: string;
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
  inputs?: WorkflowExecutionInputs;
}

export type WorkflowExecutionInputs = Record<string, unknown>;

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

// An artifact row in the executables list (backend WorkflowExecutableRowView, elsa-foundation#598 P1).
// Rows are content-addressed artifacts; the flat Definition*/Published*/Source* fields mirror the newest
// reference for backward compatibility, and `references` carries every Source Reference newest-first.
// `references` remains optional while Runtime's canonical summary and detailed provenance views converge.
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
  references?: WorkflowExecutableReference[];
}

// The executables list scope filter (plan §3): the default view shows only artifacts with at least one
// live Published reference.
export type WorkflowExecutableListScope = "published" | "test-runs" | "all";

// One Source Reference (ADR 0038/0040): a self-contained per-publish record pointing at an artifact.
// `scope` is "Published" or "TestRun"; a reference with `deletedAt` is retired, one past `expiresAt`
// is expired — either way it no longer keeps the artifact runnable.
export interface WorkflowExecutableReference {
  sourceReferenceId: string;
  artifactId: string;
  /**
   * Canonical executable-source discriminator. Runtime API v1 may also emit the deprecated `sourceType`
   * compatibility alias, but Studio deliberately does not model or consume it; the alias is removed in v2.
   */
  sourceKind?: string | null;
  sourceId?: string | null;
  sourceVersion?: string | null;
  definitionId: string;
  definitionVersionId: string;
  artifactVersion: string;
  createdAt: string;
  publishedAt?: string | null;
  scope: string;
  expiresAt?: string | null;
  deletedAt?: string | null;
  deletedReason?: string | null;
  publicationId?: string | null;
  slotId?: string | null;
  authoredInputs?: WorkflowExecutableAuthoredInput[] | null;
  authoredInputsAccess?: string | null;
}

export interface WorkflowExecutableAuthoredInput {
  executableNodeId: string;
  inputKey: string;
  expressionType?: string | null;
  value?: unknown;
  isSensitive?: boolean;
  accessState?: string | null;
  access?: string | null;
}

export interface WorkflowExecutableCompiledInput {
  executableNodeId: string;
  binding: WorkflowExecutableInputBinding;
  accessState?: string | null;
  access?: string | null;
}

export interface WorkflowExecutableInputSources {
  artifactId: string;
  sourceReferenceId: string;
  accessState?: string | null;
  /** Compatibility alias emitted by early Foundation previews. */
  access?: string | null;
  authoredInputs: WorkflowExecutableAuthoredInput[];
  compiledInputs: WorkflowExecutableCompiledInput[];
}

// Compact flowchart wiring projected with an executable node. Endpoints refer to authored node ids;
// geometry remains owned by the chosen reference's Layout Sidecar.
export interface WorkflowExecutableConnectionEndpoint {
  nodeId: string;
  port?: string | null;
}

export interface WorkflowExecutableConnection {
  source: WorkflowExecutableConnectionEndpoint;
  target: WorkflowExecutableConnectionEndpoint;
}

// A node in the Execution Material tree served by GET /executables/{artifactId}. Input bindings are
// compact summaries (secrets stay references, never literals); only the structure kind and compact
// flowchart connections are projected, never the authored structure or descriptor payload.
export interface WorkflowExecutableNode {
  executableNodeId: string;
  authoredActivityId: string;
  activityType: string;
  activityTypeVersion: string;
  structureKind?: string | null;
  inputBindings: WorkflowExecutableInputBinding[];
  childSlots: WorkflowExecutableChildSlot[];
  connections?: WorkflowExecutableConnection[];
}

export interface WorkflowExecutableChildSlot {
  name: string;
  activities: WorkflowExecutableNode[];
}

export interface WorkflowExecutableInputBinding {
  inputName: string;
  inputKey?: string | null;
  source: string;
  isSensitive?: boolean;
  literalValue?: unknown;
  expression?: WorkflowExecutableExpressionBinding | null;
  activityOutput?: Record<string, unknown> | null;
  durableValue?: Record<string, unknown> | null;
  reference?: Record<string, unknown> | null;
  metadata?: Record<string, unknown> | null;
  summary?: string | null;
  [key: string]: unknown;
}

export interface WorkflowExecutableExpressionBinding extends Record<string, unknown> {
  language: string;
  expression: string;
  resultType?: unknown;
  metadata?: Record<string, unknown> | null;
}

// The reference the detail endpoint rendered the Layout Sidecar from, and how it was chosen:
// "requested" (?ref= resolved), "newest-live", or "newest" (no live reference remains).
export interface WorkflowExecutableChosenReference {
  sourceReferenceId: string;
  selection: string;
  layout: DesignMetadataRecord[];
  authoredInputs?: WorkflowExecutableAuthoredInput[] | null;
  authoredInputsAccess?: string | null;
}

// The executable detail response (plan §3): identity block, Execution Material node tree, the chosen
// reference's layout, and the full reference list. Assembled without consulting the definition table,
// so it stays inspectable where its source definition does not exist.
export interface WorkflowExecutableDetails {
  artifactId: string;
  artifactHash: string;
  createdAt: string;
  rootActivityType: string;
  rootActivityVersion: string;
  nodeCount: number;
  resumeTargetCount: number;
  rootActivity: WorkflowExecutableNode;
  chosenReference?: WorkflowExecutableChosenReference | null;
  references: WorkflowExecutableReference[];
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
  sourceReferenceId?: string | null;
  publicationId?: string | null;
  slotId?: string | null;
  sourceKind?: string | null;
  sourceId?: string | null;
  sourceVersion?: string | null;
  activityCount: number;
  incidentCount: number;
}

export interface WorkflowInstanceDetails {
  instance: WorkflowInstanceSummary;
  activities: ActivityExecutionStateSummary[];
  incidents: IncidentStateSummary[];
}

export interface ActivityExecutionInspection {
  activityExecutionId: string;
  workflowExecutionId: string;
  executableNodeId: string;
  authoredActivityId: string;
  activityType: string;
  activityTypeVersion: string;
  status: string;
  subStatus?: string | null;
  executionSequence: number;
  scheduledAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  firstCheckpointId?: string | null;
  lastCheckpointId?: string | null;
  lastCommittedAt?: string | null;
  provenance: ActivitySchedulingProvenance;
  outcomeNames: string[];
  bookmarks: ActivityExecutionBookmarkSummary[];
  incidents: ActivityExecutionIncidentSummary[];
  valueSnapshots: ActivityExecutionInspectionValueSnapshot[];
  metadata: Record<string, string>;
}

export interface ActivitySchedulingProvenance {
  parentActivityExecutionId?: string | null;
  schedulingActivityExecutionId?: string | null;
  schedulingWorkflowExecutionId?: string | null;
  branchId?: string | null;
  iterationId?: string | null;
  executionPathId?: string | null;
  executionScopeId?: string | null;
  schedulingCause?: string | null;
  metadata: Record<string, string>;
}

export interface ActivityExecutionBookmarkSummary {
  bookmarkId: string;
  resumeTargetId: string;
  stimulusType: string;
  stimulusHash: string;
  createdAt: string;
  expiresAt?: string | null;
  metadata: Record<string, string>;
  payload?: unknown;
}

export interface ActivityExecutionIncidentSummary {
  incidentId: string;
  severity: string;
  status: string;
  resolutionAction: string;
  failureType: string;
  message: string;
  stackTrace?: string | null;
  exceptionStackTrace?: string | null;
  createdAt: string;
  resolvedAt?: string | null;
  isBlocking: boolean;
  metadata: Record<string, string>;
}

export interface ActivityExecutionInspectionValueSnapshot {
  name: string;
  subject: string;
  captureMode: string;
  state?: string | null;
  type?: RuntimeValueTypeDescriptor | null;
  capturedAt: string;
  payload?: unknown;
  snapshot?: DiagnosticSnapshotNode | null;
  captureReason: string;
  isSensitive: boolean;
  metadata: Record<string, string>;
  inputKey?: string | null;
  evaluationId?: string | null;
  evaluationPhase?: string | null;
  evaluationSequence?: number | null;
  /** Compatibility aliases emitted by early Foundation previews. */
  phase?: string | null;
  sequence?: number | null;
  accessState?: string | null;
  access?: string | null;
  failure?: ActivityInputEvaluationFailure | null;
}

export interface ActivityInputEvaluationFailure {
  code?: string | null;
  message?: string | null;
  incidentId?: string | null;
}

export type DiagnosticSnapshotNode =
  | DiagnosticSnapshotNullNode
  | DiagnosticSnapshotScalarNode
  | DiagnosticSnapshotNumberNode
  | DiagnosticSnapshotStringNode
  | DiagnosticSnapshotObjectNode
  | DiagnosticSnapshotArrayNode
  | DiagnosticSnapshotMarkerNode
  | DiagnosticSnapshotPayloadReferenceNode
  | DiagnosticSnapshotUnknownNode;

export interface DiagnosticSnapshotBaseNode {
  kind: string;
  typeName?: string | null;
  displayName?: string | null;
  metadata?: Record<string, string> | null;
}

export interface DiagnosticSnapshotNullNode extends DiagnosticSnapshotBaseNode {
  kind: "null";
}

export interface DiagnosticSnapshotScalarNode extends DiagnosticSnapshotBaseNode {
  kind: "scalar";
  value?: string | number | boolean | null;
}

export interface DiagnosticSnapshotNumberNode extends DiagnosticSnapshotBaseNode {
  kind: "number";
  value?: number | null;
}

export interface DiagnosticSnapshotStringNode extends DiagnosticSnapshotBaseNode {
  kind: "string";
  preview?: string | null;
  length?: number | null;
  truncated?: boolean | null;
}

export interface DiagnosticSnapshotObjectNode extends DiagnosticSnapshotBaseNode {
  kind: "object";
  properties?: DiagnosticSnapshotProperty[] | null;
  truncated?: boolean | null;
}

export interface DiagnosticSnapshotArrayNode extends DiagnosticSnapshotBaseNode {
  kind: "array";
  items?: DiagnosticSnapshotNode[] | null;
  itemCount?: number | null;
  truncated?: boolean | null;
}

export interface DiagnosticSnapshotMarkerNode extends DiagnosticSnapshotBaseNode {
  kind: "redacted" | "truncated" | "unsupported" | "error" | "permissionHidden";
  reason?: string | null;
  omittedCount?: number | null;
  message?: string | null;
  requiredPermission?: string | null;
}

export interface DiagnosticSnapshotPayloadReferenceNode extends DiagnosticSnapshotBaseNode {
  kind: "payloadReference";
  referenceKind?: string | null;
  referenceId?: string | null;
  contentType?: string | null;
  size?: number | null;
  resolution?: {
    canResolve: boolean;
    reason?: string | null;
  } | null;
}

export interface DiagnosticSnapshotUnknownNode extends DiagnosticSnapshotBaseNode {
  kind: string;
  [key: string]: unknown;
}

export interface DiagnosticSnapshotProperty {
  name: string;
  value: DiagnosticSnapshotNode;
}

export interface RuntimeValueTypeDescriptor {
  typeName?: string | null;
  displayName?: string | null;
  alias?: string | null;
  isCollection?: boolean | null;
  elementType?: RuntimeValueTypeDescriptor | null;
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
  stackTrace?: string | null;
  exceptionStackTrace?: string | null;
  createdAt: string;
  resolvedAt?: string | null;
  isBlocking: boolean;
  metadata: Record<string, string>;
}
