import type { ComponentType } from "react";

export * from "../auth";

export type StudioModuleStatus = "available" | "loaded" | "disabled" | "incompatible" | "failed";

export interface StudioModuleManifest {
  id: string;
  displayName: string;
  version: string;
  entry: string;
  styles: string[];
  requiredHostVersion: string;
  requiredSdkVersion: string;
  capabilities: string[];
}

export interface StudioModuleDiagnostic {
  moduleId: string;
  status: StudioModuleStatus | string;
  reason: string;
}

export interface StudioModulesResponse {
  hostVersion: string;
  sdkVersion: string;
  modules: StudioModuleManifest[];
  diagnostics: StudioModuleDiagnostic[];
}

export type StudioModuleRegistryStatus = "available" | "loaded" | "disabled" | "incompatible" | "failed" | "pending" | "unknown";
export type StudioModuleRegistryCompatibility = "compatible" | "warning" | "incompatible" | "unknown";
const requestTimeoutMs = 10000;

export interface StudioModuleRegistryResponse {
  hostVersion: string;
  sdkVersion: string;
  generatedAt: string;
  modules: StudioModuleRegistryItem[];
}

export interface StudioModuleRegistryItem {
  id: string;
  displayName: string;
  sourceKind: string;
  scope: string;
  version: string;
  requiredHostVersion: string;
  requiredSdkVersion: string;
  compatibility: StudioModuleRegistryCompatibility | string;
  status: StudioModuleRegistryStatus | string;
  manifest: StudioModuleRegistryManifest;
  contributions: StudioModuleContributionSummary[];
  diagnostics: StudioModuleDiagnostic[];
}

export interface StudioModuleRegistryManifest {
  entry: string;
  styles: string[];
  capabilities: string[];
}

export interface StudioModuleContributionSummary {
  type: string;
  id: string;
  label: string;
  status: string;
}

export type StudioValidationErrors = Record<string, string[]>;

/**
 * Backend management availability as reported by the Studio management bridge (ADR 0037). The browser reads this from
 * the Studio origin (`api.host`) instead of probing backend host-control endpoints directly, so the backend host
 * management key never reaches the browser and the SPA never issues doomed, 401-noisy backend requests.
 */
export type StudioBackendManagementStatusKind =
  | "available"
  | "unconfigured"
  | "unreachable"
  | "unauthorized"
  | "degraded";

export interface StudioBackendManagementStatus {
  status: StudioBackendManagementStatusKind;
  detail: string;
  backendBaseUrl?: string | null;
  checkedAt: string;
}

/** The Studio-owned bridge route the browser reads backend management status from (served by the Studio host). */
export const studioBackendManagementStatusPath = "/_elsa/studio/backend-management/status";

/**
 * Backend Extension Builder capability flags as reported by the Studio management bridge (ADR 0037). Mirrors the
 * backend's capability contract but is a Studio-owned DTO ("host capabilities"). Server enforcement on the backend
 * remains authoritative regardless of what the browser is shown.
 */
export interface StudioExtensionBuilderCapabilities {
  canCreateWorkspace: boolean;
  canEditFiles: boolean;
  canBuild: boolean;
  canPromote: boolean;
  canRollback: boolean;
}

/**
 * The Studio management bridge's answer for the backend Extension Builder capabilities read. `status` carries the same
 * explicit envelope as {@link StudioBackendManagementStatus} so the frontend branches on state, not on an HTTP failure;
 * `capabilities` is present only when `status` is `"available"`. The browser reads this from the Studio origin
 * (`api.host`) instead of probing the backend Extension Builder capabilities endpoint directly, so the backend host
 * management key never reaches the browser and the SPA never issues doomed backend requests.
 */
export interface StudioExtensionBuilderCapabilitiesResult {
  status: StudioBackendManagementStatusKind;
  detail: string;
  capabilities?: StudioExtensionBuilderCapabilities | null;
  backendBaseUrl?: string | null;
  checkedAt: string;
}

/** The Studio-owned bridge route the browser reads backend Extension Builder capabilities from (served by the Studio host). */
export const studioExtensionBuilderCapabilitiesPath = "/_elsa/studio/backend-management/extension-builder/capabilities";

/**
 * The Studio-owned bridge route group the browser reaches every backend Extension Builder endpoint through
 * (ADR 0037, #256). Each backend route suffix is preserved verbatim under this root — same methods, query params, and
 * payload bodies — with Studio attaching the server-side management key on the Studio→backend relay.
 */
export const studioExtensionBuilderBridgeRoot = "/_elsa/studio/backend-management/extension-builder";

/**
 * The camelCase error body the Studio management bridge returns for infrastructure failures: 503, or 504 for a relay
 * timeout — never for backend domain logic (400/404/409 bodies relay verbatim). `management` carries the bridge's
 * backend-management status (unconfigured/unauthorized/unreachable/degraded) when the failure could be attributed,
 * `null` otherwise.
 */
export interface StudioManagementBridgeErrorBody {
  detail: string;
  management: StudioBackendManagementStatus | null;
}

/**
 * The backend host registry as surfaced by the Studio management bridge (#246, ADR 0037). The browser reads this from
 * the Studio origin (`api.host`) with only its normal credentials; Studio attaches the server-side management key on
 * the Studio→backend call. The envelope carries the same explicit backend-management `status` as the status endpoint,
 * so the frontend branches on it directly (rendering unconfigured/unreachable/unauthorized/degraded states) instead of
 * inferring an outage from a failed fetch. `registry` is the raw backend registry payload, present only when
 * `status === "available"`, and `null` otherwise.
 */
export interface StudioBackendManagementRegistryEnvelope<TRegistry = unknown> {
  status: StudioBackendManagementStatusKind;
  detail: string;
  backendBaseUrl?: string | null;
  checkedAt: string;
  registry?: TRegistry | null;
}

/** The Studio-owned bridge route the browser reads the backend host registry from (served by the Studio host). */
export const studioBackendManagementRegistryPath = "/_elsa/studio/backend-management/registry";

/**
 * Per-request options for the Studio http client. `timeoutMs` overrides the endpoint-wide request timeout for a single
 * call (e.g. long-running management relays); everything else is standard fetch `RequestInit`.
 */
export interface StudioRequestInit extends RequestInit {
  timeoutMs?: number;
}

export interface StudioHttpClient {
  requestJson<T>(url: string, init?: StudioRequestInit): Promise<T>;
  getJson<T>(url: string, init?: StudioRequestInit): Promise<T>;
  postJson<T>(url: string, body: unknown, init?: StudioRequestInit): Promise<T>;
  putJson<T>(url: string, body: unknown, init?: StudioRequestInit): Promise<T>;
  deleteJson<T>(url: string, init?: StudioRequestInit): Promise<T>;
  postForm<T>(url: string, body: FormData, init?: StudioRequestInit): Promise<T>;
}

export interface StudioEndpointContext {
  baseUrl: string;
  headers?: HeadersInit;
  http: StudioHttpClient;
  /**
   * Optional access-token factory for non-HTTP transports (e.g. SignalR hub connections) that can't use
   * {@link http}. Present only on the authenticated context, where the shell builds it from the auth manager
   * via `createSignalRAccessTokenFactory`; modules assign it to a SignalR connection's `accessTokenFactory`
   * so the hub carries the same bearer token the HTTP client attaches. Absent on the anonymous context.
   */
  accessTokenFactory?: () => Promise<string>;
}

export interface StudioRouteComponentProps {
  /** Requests an in-app navigation through the Studio host router. */
  navigate(path: string): void;
}

export interface StudioRouteContribution {
  id: string;
  path: string;
  label: string;
  component: ComponentType<StudioRouteComponentProps>;
}

export interface StudioNavigationContribution {
  id: string;
  label: string;
  path: string;
  activePathPrefix?: string;
  order?: number;
  iconColor?: string;
  parentId?: string;
}

export type StudioDashboardWidgetSize = "small" | "medium" | "wide" | "full";

export interface StudioDashboardWidgetLoadContext<TSettings> {
  settings: TSettings;
  signal: AbortSignal;
}

export interface StudioDashboardWidgetSettings<TSettings> {
  schemaVersion: number;
  defaults: TSettings;
  descriptors: StudioSettingDescriptor[];
  validate(value: unknown): TSettings | null;
  migrate?(value: unknown, fromVersion: number): TSettings | null;
}

export interface StudioDashboardWidgetBodyProps<TSnapshot, TSettings> {
  snapshot: TSnapshot | undefined;
  settings: TSettings;
  size: StudioDashboardWidgetSize;
}

export interface StudioDashboardWidgetEmptyState {
  title?: string;
  description?: string;
}

export interface StudioDashboardWidgetContribution<TSnapshot = unknown, TSettings = unknown> {
  id: string;
  moduleId: string;
  title: string;
  description?: string;
  order?: number;
  defaultVisible: boolean;
  defaultSize: StudioDashboardWidgetSize;
  supportedSizes: StudioDashboardWidgetSize[];
  permissions?: string[];
  minimumRefreshIntervalMs?: number;
  cacheLifetimeMs?: number;
  timeoutMs?: number;
  settings?: StudioDashboardWidgetSettings<TSettings>;
  load?(context: StudioDashboardWidgetLoadContext<TSettings>): Promise<TSnapshot>;
  isEmpty?(snapshot: TSnapshot | undefined, settings: TSettings): boolean;
  emptyState?: StudioDashboardWidgetEmptyState;
  component: ComponentType<StudioDashboardWidgetBodyProps<TSnapshot, TSettings>>;
}

export type StudioDiagnosticsWidgetMode = "snapshot" | "live";
export type StudioDiagnosticsWidgetStatus = "idle" | "loading" | "ready" | "streaming" | "error";

export interface StudioDiagnosticsWidgetState<TSnapshot = unknown> {
  status: StudioDiagnosticsWidgetStatus;
  snapshot?: TSnapshot;
  error?: string;
}

export interface StudioDiagnosticsWidgetProps<TSnapshot = unknown> {
  state: StudioDiagnosticsWidgetState<TSnapshot>;
}

export interface StudioDiagnosticsWidgetContribution<TSnapshot = unknown> {
  id: string;
  title: string;
  order?: number;
  mode?: StudioDiagnosticsWidgetMode;
  load?: () => Promise<TSnapshot>;
  subscribe?: (publish: (snapshot: TSnapshot) => void) => void | (() => void);
  component: ComponentType<StudioDiagnosticsWidgetProps<TSnapshot>>;
}

export interface StudioPanelContribution {
  id: string;
  title: string;
  order?: number;
  component: ComponentType;
}

export type StudioWorkflowDesignerPanelSide = "left" | "right";

export interface StudioWorkflowDesignerPanelProps<TContext = unknown> {
  context: TContext;
}

export interface StudioWorkflowDesignerPanelContribution<TContext = unknown> {
  id: string;
  title: string;
  side: StudioWorkflowDesignerPanelSide;
  order?: number;
  component: ComponentType<StudioWorkflowDesignerPanelProps<TContext>>;
}

export interface StudioWorkflowContextActivity {
  id: string;
  type: string;
  displayName?: string;
}

export interface StudioWorkflowContextConnection {
  source: string;
  target: string;
  sourcePort?: string;
  targetPort?: string;
}

export interface StudioWorkflowContextDiagnostic {
  severity: string;
  message: string;
}

// Snapshot of the open workflow that the designer publishes onto `window.__ELSA_STUDIO_WORKFLOW_CONTEXT__`
// for out-of-band consumers (the host's agent context provider, Weaver tooling). Defined once here so the
// writer (the Workflows module's context bridge) and every reader share a single contract.
export interface StudioWorkflowContextSnapshot {
  workflowId: string;
  workflowDefinitionId?: string;
  workflowVersionId?: string | null;
  draftId?: string | null;
  revision?: string | null;
  selectedNodeId?: string | null;
  selectedActivityType?: string | null;
  /** Node the inspector shows: the selection, or the scope owner when nothing is selected. */
  inspectedNodeId?: string | null;
  inspectedActivityType?: string | null;
  /**
   * True only on the owner FALLBACK — nothing is selected and the scope owner is shown. False when the
   * owner itself is explicitly selected (possible for unsupported designers); compare inspectedNodeId
   * against the scope to detect that case.
   */
  inspectedIsScopeOwner?: boolean;
  summary?: string;
  activities?: StudioWorkflowContextActivity[];
  connections?: StudioWorkflowContextConnection[];
  diagnostics?: StudioWorkflowContextDiagnostic[];
}

declare global {
  interface Window {
    __ELSA_STUDIO_WORKFLOW_CONTEXT__?: StudioWorkflowContextSnapshot;
  }
}

export interface StudioFeatureAreaNavLeaf {
  id?: string;
  title: string;
  path: string;
  iconColor?: string;
  placeholder?: boolean;
}

export interface StudioFeatureAreaNavParent {
  id?: string;
  title: string;
  path: string;
  iconColor?: string;
  items: StudioFeatureAreaNavLeaf[];
}

export type StudioFeatureAreaNavContribution = StudioFeatureAreaNavLeaf | StudioFeatureAreaNavParent;

export interface StudioFeatureAreaRouteContribution {
  id: string;
  path: string;
  label: string;
  component: ComponentType<StudioRouteComponentProps>;
}

export interface StudioFeatureAreaContribution {
  id: string;
  title: string;
  description?: string;
  navGroup?: string;
  ownedPaths: string[];
  required?: boolean;
  defaultEnabled?: boolean;
  order?: number;
  nav: StudioFeatureAreaNavContribution;
  routes: StudioFeatureAreaRouteContribution[];
}

export interface StudioSettingDescriptor {
  name: string;
  displayName: string;
  description?: string | null;
  category?: string | null;
  group?: string | null;
  clrType?: string | null;
  jsonType?: string | null;
  required: boolean;
  defaultValue?: unknown;
  secret: boolean;
  sensitive: boolean;
  restartRequired: boolean;
  advanced: boolean;
  experimental: boolean;
  uiHint?: string | null;
  optionsProvider?: string | null;
  options: StudioSettingOptionDescriptor[];
}

export interface StudioSettingOptionDescriptor {
  label: string;
  value: unknown;
  description?: string | null;
}

export interface StudioSettingEditorProps {
  setting: StudioSettingDescriptor;
  value: unknown;
  disabled?: boolean;
  onChange(value: unknown): void;
}

export interface StudioSettingEditorContribution {
  id: string;
  order?: number;
  supports(setting: StudioSettingDescriptor): boolean;
  component: ComponentType<StudioSettingEditorProps>;
}

export type StudioActivityKind = "Action" | "Trigger" | "Job" | "Task" | string;
export type StudioActivityPortType = "Embedded" | "Flow" | string;

export interface StudioActivityPropertyDescriptor {
  name: string;
  typeName: string;
  displayName?: string | null;
  description?: string | null;
  order?: number;
  category?: string | null;
  isBrowsable?: boolean | null;
  isSynthetic?: boolean;
}

export type StudioActivityInputOptionValue = string | number | boolean;

export interface StudioActivityInputOption {
  label: string;
  value: StudioActivityInputOptionValue;
}

export interface StudioActivityInputOptionsProviderDescriptor {
  key: string;
  dependsOn: string[];
}

export type StudioDictionaryKeyComparison = "ordinal" | "ordinalIgnoreCase";

export interface StudioActivityInputDictionarySpecifications {
  keyComparison?: StudioDictionaryKeyComparison;
  keyLabel?: string;
  valueLabel?: string;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
}

/** Known input-editor metadata plus an open bag for module-specific specifications. */
export interface StudioActivityInputUISpecifications extends Record<string, unknown> {
  dictionary?: StudioActivityInputDictionarySpecifications;
  options?: StudioActivityInputOption[];
  optionsProvider?: StudioActivityInputOptionsProviderDescriptor;
}

/**
 * Collection shape of an input's CLR type, reported by the authoring catalog (elsa-foundation#945).
 * When present with a collection value, `typeName` is the element-type alias and this field selects the
 * list/dictionary editor; when absent (older backends) the editor falls back to parsing `typeName`.
 */
export type StudioActivityCollectionKind = "Single" | "Array" | "List" | "HashSet" | "Dictionary" | (string & {});

export interface StudioActivityInputDescriptor extends StudioActivityPropertyDescriptor {
  /** Stable identity used to correlate authored bindings and runtime input evidence. */
  referenceKey?: string | null;
  /** Collection shape of the CLR type (elsa-foundation#945); absent on older backends. */
  collectionKind?: StudioActivityCollectionKind | null;
  isWrapped?: boolean;
  uiHint?: string | null;
  defaultValue?: unknown;
  defaultSyntax?: string | null;
  isReadOnly?: boolean | null;
  storageDriverType?: string | null;
  uiSpecifications?: StudioActivityInputUISpecifications | null;
}

export function readActivityInputOptionsProvider(input: StudioActivityInputDescriptor): StudioActivityInputOptionsProviderDescriptor | null {
  const value = input.uiSpecifications?.optionsProvider;
  if (!value || typeof value !== "object") return null;
  const record = value as unknown as Record<string, unknown>;
  if (typeof record.key !== "string" || record.key.trim().length === 0) return null;
  return {
    key: record.key,
    dependsOn: Array.isArray(record.dependsOn) ? record.dependsOn.filter((item): item is string => typeof item === "string") : []
  };
}

export interface StudioActivityOutputDescriptor extends StudioActivityPropertyDescriptor {
}

export interface StudioActivityPortDescriptor {
  name: string;
  referenceKey?: string | null;
  displayName?: string | null;
  type: StudioActivityPortType;
  isBrowsable?: boolean | null;
}

/** Optional activity-type metadata that labels and orders property categories in the inspector. */
export interface StudioActivityPropertyGroupDescriptor {
  category: string;
  label?: string;
  order?: number;
}

export interface StudioActivityCustomProperties extends Record<string, unknown> {
  propertyGroups?: StudioActivityPropertyGroupDescriptor[];
}

export interface StudioActivityDescriptor {
  typeName: string;
  namespace?: string;
  name?: string;
  version?: number;
  category?: string;
  displayName?: string | null;
  description?: string | null;
  icon?: string | null;
  iconName?: string | null;
  iconColor?: string | null;
  kind?: StudioActivityKind;
  inputs: StudioActivityInputDescriptor[];
  outputs: StudioActivityOutputDescriptor[];
  ports: StudioActivityPortDescriptor[];
  customProperties?: StudioActivityCustomProperties;
  constructionProperties?: Record<string, unknown>;
  isContainer?: boolean;
  isBrowsable?: boolean;
  isStart?: boolean;
  isTerminal?: boolean;
}

export type StudioExpressionEditingMode = "literal" | "text" | "structured" | "reference";

export interface StudioExpressionDescriptor {
  type: string;
  displayName?: string | null;
  description?: string | null;
  editingMode: StudioExpressionEditingMode;
}

/**
 * Whether an editor renders a whole property value ("element", the default) or a single element of a
 * collection-typed property ("collection"). The property panel resolves collection-typed inputs in
 * "collection" scope first (so an editor can own the entire collection, e.g. a multi-select); when no
 * collection-scoped editor claims it, each repeater row is resolved in "element" scope.
 */
export type StudioActivityPropertyEditorScope = "element" | "collection";

export interface StudioActivityPropertyEditorContext {
  activity: unknown;
  expressionDescriptors: StudioExpressionDescriptor[];
  readOnly?: boolean;
  scope?: StudioActivityPropertyEditorScope;
}

export interface StudioActivityPropertyEditorProps {
  descriptor: StudioActivityInputDescriptor;
  value: unknown;
  disabled?: boolean;
  context: StudioActivityPropertyEditorContext;
  onChange(value: unknown): void;
}

export interface StudioActivityPropertyEditorContribution {
  id: string;
  order?: number;
  supports(descriptor: StudioActivityInputDescriptor, context: StudioActivityPropertyEditorContext): boolean;
  component: ComponentType<StudioActivityPropertyEditorProps>;
}

/** Open, persisted metadata describing one declared workflow-run input type. */
export interface StudioWorkflowRunInputTypeMetadata extends Record<string, unknown> {
  alias: string;
  collectionKind: string;
}

/** Declarative option consumed by the Workflows module's enum contribution factory. */
export interface StudioWorkflowRunInputEnumOption extends Record<string, unknown> {
  /** Stable draft value used by the picker. */
  value: string;
  label?: string;
  /** Optional value placed on the execution wire payload; `value` is used when omitted. */
  wireValue?: unknown;
}

/** The stable subset of a workflow input exposed to run-input editor contributions. */
export interface StudioWorkflowRunInputDescriptor extends Record<string, unknown> {
  referenceKey: string;
  name: string;
  displayName: string;
  description: string;
  type: StudioWorkflowRunInputTypeMetadata;
  isRequired?: boolean;
}

export interface StudioWorkflowRunInputEditorContext {
  input: StudioWorkflowRunInputDescriptor;
  draft: string;
}

export interface StudioWorkflowRunInputEditorControlProps {
  id: string;
  "aria-label": string;
  "aria-describedby"?: string;
  "aria-invalid": boolean;
  "aria-required": boolean;
}

export interface StudioWorkflowRunInputEditorProps extends StudioWorkflowRunInputEditorContext {
  disabled?: boolean;
  controlProps: StudioWorkflowRunInputEditorControlProps;
  onChange(draft: string): void;
}

/**
 * Contributes a complete run-input editing boundary. A contribution owns its visible control,
 * draft validation, and conversion to the value placed on the execution wire payload.
 */
export interface StudioWorkflowRunInputEditorContribution {
  id: string;
  order?: number;
  supports(input: StudioWorkflowRunInputDescriptor): boolean;
  component: ComponentType<StudioWorkflowRunInputEditorProps>;
  validate(context: StudioWorkflowRunInputEditorContext): string | undefined;
  serialize(context: StudioWorkflowRunInputEditorContext): unknown;
}

export type StudioExpressionEditorSurface = "inline" | "expanded";

export interface StudioExpressionEditorContext {
  syntax: string;
  surface: StudioExpressionEditorSurface;
  descriptor: StudioActivityInputDescriptor;
  activity: unknown;
  expressionDescriptors: StudioExpressionDescriptor[];
  readOnly?: boolean;
}

export interface StudioExpressionEditorProps {
  descriptor: StudioActivityInputDescriptor;
  syntax: string;
  value: unknown;
  disabled?: boolean;
  initialFocus?: boolean;
  context: StudioExpressionEditorContext;
  onChange(value: unknown): void;
}

export type StudioExpressionEditorDiagnosticSeverity = "info" | "warning" | "error";

export interface StudioExpressionEditorDiagnostic {
  severity?: StudioExpressionEditorDiagnosticSeverity;
  code?: string;
  message: string;
}

export interface StudioExpressionEditorMetadata {
  displayName?: string;
  installHint?: string;
  packageId?: string;
}

export type StudioExpressionSourceRendererSurface = "compact" | "expanded";

/** Authorized, read-only authored expression source supplied by the owning feature module. */
export interface StudioExpressionSourceRendererContext {
  expressionType: string;
  value: unknown;
  metadata: Readonly<Record<string, unknown>>;
  isSensitive: boolean;
  surface: StudioExpressionSourceRendererSurface;
}

export interface StudioExpressionSourceRendererProps {
  context: StudioExpressionSourceRendererContext;
}

export interface StudioExpressionSourceRenderer {
  compact: ComponentType<StudioExpressionSourceRendererProps>;
  expanded: ComponentType<StudioExpressionSourceRendererProps>;
}

export interface StudioExpressionEditorContribution {
  id: string;
  order?: number;
  supports(context: StudioExpressionEditorContext): boolean;
  surfaces: Partial<Record<StudioExpressionEditorSurface, ComponentType<StudioExpressionEditorProps>>>;
  /** Creates a valid empty value when authoring switches to this expression syntax. */
  createDefaultValue?(context: StudioExpressionEditorContext): unknown;
  diagnostics?(context: StudioExpressionEditorContext, value: unknown): StudioExpressionEditorDiagnostic[];
  metadata?: StudioExpressionEditorMetadata;
  /** Optional semantic rendering for authorized authored source, selected through this contribution's supports policy. */
  sourceRenderer?: StudioExpressionSourceRenderer;
}

export type StudioAgentMode = "explain" | "build" | "troubleshoot" | "operate" | "administer";
export type StudioAgentSensitivity = "public" | "internal" | "sensitive" | "secret-redacted";
export type StudioAgentCapabilityKind = "answer" | "context" | "prompt-starter" | "proposal" | "action";
export type StudioAgentRisk = "read-only" | "review-required" | "destructive" | "admin";
export type StudioAgentToolInvocationMode = "read-only" | "direct" | "proposal" | "privileged";
export type StudioAgentToolAvailabilityStatus = "available" | "disabled" | "policy-denied" | "unavailable";

export interface StudioAgentSurface {
  route: string;
  resourceType?: string;
  resourceId?: string;
  selection?: unknown;
}

export interface StudioAgentContextRequest {
  surface: StudioAgentSurface;
  mode?: StudioAgentMode;
  sessionId?: string;
}

export interface StudioAgentContextAttachment {
  id: string;
  source: string;
  sourceId?: string;
  label: string;
  contentType: string;
  sensitivity: StudioAgentSensitivity;
  scope: string;
  content?: unknown;
}

export interface StudioAgentContextProviderContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  order?: number;
  surfaces: string[];
  sensitivity: StudioAgentSensitivity;
  collect(context: StudioAgentContextRequest): Promise<StudioAgentContextAttachment[]>;
}

export interface StudioAgentPromptStarterContribution {
  id: string;
  moduleId?: string;
  label: string;
  prompt: string;
  surfaces: string[];
  order?: number;
  requiredCapabilities?: string[];
}

export interface StudioAgentCapabilityContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  description: string;
  kind: StudioAgentCapabilityKind;
  risk: StudioAgentRisk;
  surfaces: string[];
  requiredPermissions?: string[];
}

export interface StudioAgentActionContribution {
  id: string;
  capabilityId: string;
  displayName: string;
  description: string;
  risk: Exclude<StudioAgentRisk, "read-only">;
  surfaces: string[];
  proposalSchema: unknown;
}

export interface StudioAgentToolAvailability {
  status: StudioAgentToolAvailabilityStatus;
  reason?: string;
}

export interface StudioAgentToolSlotContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  description?: string;
  order?: number;
  surfaces: string[];
  invocationModes?: StudioAgentToolInvocationMode[];
}

export interface StudioAgentToolContractContribution {
  id: string;
  slotId: string;
  moduleId?: string;
  displayName: string;
  description: string;
  order?: number;
  surfaces: string[];
  inputSchema: unknown;
  resourceTargetSchema: unknown;
  resultSchema: unknown;
  risk: StudioAgentRisk;
  requiredPermissions?: string[];
  availability?: StudioAgentToolAvailability;
  invocationModes: StudioAgentToolInvocationMode[];
  resultRendererIds?: string[];
}

export interface StudioAgentResourceTarget {
  resourceType: string;
  resourceId?: string;
  displayName?: string;
  moduleId?: string;
  route?: string;
  summary?: string;
}

export interface StudioAgentResultRendererProps {
  proposal: unknown;
  resourceTarget?: StudioAgentResourceTarget;
  result: unknown;
  resultType?: string;
}

export interface StudioAgentResultRendererContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  order?: number;
  resourceTypes?: string[];
  resultTypes?: string[];
  component: ComponentType<StudioAgentResultRendererProps>;
  supports?(props: StudioAgentResultRendererProps): boolean;
}

export interface StudioAgentRegistry {
  readonly contextProviders: StudioContributionRegistry<StudioAgentContextProviderContribution>;
  readonly promptStarters: StudioContributionRegistry<StudioAgentPromptStarterContribution>;
  readonly capabilities: StudioContributionRegistry<StudioAgentCapabilityContribution>;
  readonly actions: StudioContributionRegistry<StudioAgentActionContribution>;
  readonly toolSlots: StudioContributionRegistry<StudioAgentToolSlotContribution>;
  readonly toolContracts: StudioContributionRegistry<StudioAgentToolContractContribution>;
  readonly resultRenderers: StudioContributionRegistry<StudioAgentResultRendererContribution>;
}

export type StudioAiPromptMode = "enqueue" | "steer";
export type StudioAiPromptPlacement = "shell" | "toolbar" | "inspector" | "empty-state" | "field-adornment" | "selection";
export type StudioAiToolMutability = "read-only" | "proposal" | "administrative";
export type StudioAiToolDangerLevel = "low" | "medium" | "high" | "critical";
export type StudioAiTenantBehavior = "tenant-scoped" | "host-scoped" | "cross-tenant-denied";
export type StudioAiProposalStatus = "draft" | "validated" | "blocked" | "approved" | "rejected" | "applied" | "expired";

export interface StudioAiContextAttachment {
  id?: string;
  kind: string;
  referenceId?: string | null;
  scope?: string | null;
  activityId?: string | null;
  metadata?: Record<string, unknown>;
}

export interface StudioAiPromptRequest {
  message: string;
  agent?: string | null;
  mode?: StudioAiPromptMode;
  attachments?: StudioAiContextAttachment[];
  source?: {
    moduleId?: string;
    actionId?: string;
    label?: string;
  };
  /**
   * Correlation id set by a caller that wants the assistant's completed response routed back to it
   * via {@link StudioAiPromptDispatcher.onPromptResult}. Omit for fire-and-forget prompts.
   */
  requestId?: string;
}

/** The completed outcome of a prompt dispatched with a {@link StudioAiPromptRequest.requestId}. */
export interface StudioAiPromptResult {
  requestId: string;
  status: "completed" | "cancelled" | "failed";
  /** The assistant's final message text (empty for cancelled/failed turns). */
  text: string;
  /** True when the originating Weaver session is in an autonomous mode (Autopilot), so callers may apply without asking. */
  autoApply?: boolean;
}

export interface StudioAiContextProviderContribution {
  id: string;
  kind: string;
  label: string;
  description?: string;
  moduleId?: string;
  order?: number;
  createAttachment(reference: unknown): StudioAiContextAttachment | null;
}

export interface StudioAiPromptActionContribution<TContext = unknown> {
  id: string;
  label: string;
  description?: string;
  moduleId?: string;
  order?: number;
  placement: StudioAiPromptPlacement;
  contextKind: string;
  createPrompt(context: TContext): StudioAiPromptRequest | null;
}

export interface StudioAiToolContribution {
  name: string;
  displayName: string;
  description?: string;
  mutability: StudioAiToolMutability;
  dangerLevel: StudioAiToolDangerLevel;
  tenantBehavior?: StudioAiTenantBehavior;
  permissions?: string[];
  agentScopes?: string[];
  moduleId?: string;
  /** JSON Schema describing the tool's input arguments, surfaced to the agent harness. */
  inputSchema?: unknown;
  /**
   * Client-side executor backing the tool. Modules capture their own authenticated endpoint context
   * (`api.backend`) in this closure, so every invocation carries the signed-in user's credentials and
   * the backend's permission gating applies — the tool can never read further than the user could
   * through the same bridge endpoints (no bridge-key bypass).
   */
  execute?(input: Record<string, unknown>): Promise<unknown>;
}

export interface StudioAiProposalRendererProps {
  proposal: StudioAiProposalSummary;
}

export interface StudioAiProposalSummary {
  id: string;
  kind: string;
  status: StudioAiProposalStatus | string;
  conversationId?: string | null;
  rationale?: string | null;
  warnings?: string[];
  diagnostics?: Array<{ code?: string; message: string; severity?: string; path?: string | null }>;
  graphDiff?: {
    addedActivityIds?: string[];
    removedActivityIds?: string[];
    changedActivityIds?: string[];
    data?: Record<string, unknown>;
  } | null;
}

export interface StudioAiProposalRendererContribution {
  id: string;
  kind: string;
  moduleId?: string;
  component: ComponentType<StudioAiProposalRendererProps>;
}

export interface StudioAiSurfaceContribution {
  id: string;
  title: string;
  placement: "route" | "panel" | "drawer" | "inline";
  moduleId?: string;
  order?: number;
}

export type StudioDialogTone = "default" | "danger";
export type StudioDialogKind = "confirm" | "prompt" | "alert";

export interface StudioConfirmOptions {
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: StudioDialogTone;
}

export interface StudioPromptOptions extends Omit<StudioConfirmOptions, "tone"> {
  defaultValue?: string;
  placeholder?: string;
}

export type StudioAlertOptions = Omit<StudioConfirmOptions, "tone" | "cancelLabel">;

export interface StudioDialogApi {
  /** Resolves to true when confirmed, false when cancelled/dismissed. */
  confirm(options: StudioConfirmOptions): Promise<boolean>;
  /** Resolves to the entered string, or null when cancelled/dismissed. */
  prompt(options: StudioPromptOptions): Promise<string | null>;
  /** Resolves when acknowledged/dismissed. */
  alert(options: StudioAlertOptions): Promise<void>;
}

export type StudioDialogResult = boolean | string | null;

/** A dialog request as surfaced to the host UI. The pending promise is owned by the controller. */
export interface StudioDialogRequest {
  id: number;
  kind: StudioDialogKind;
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: StudioDialogTone;
  defaultValue?: string;
  placeholder?: string;
}

export interface StudioDialogController {
  readonly api: StudioDialogApi;
  subscribe(listener: (request: StudioDialogRequest | null) => void): () => void;
  getCurrent(): StudioDialogRequest | null;
  respond(id: number, result: StudioDialogResult): void;
  /** Settle every pending dialog as cancelled. Used when the dialog surface tears down so callers never hang. */
  cancelAll(): void;
}

export const studioSlotIds = {
  featureAreas: "studio.feature-areas",
  navigation: "studio.navigation",
  routes: "studio.routes",
  dashboardWidgets: "studio.dashboard.widgets",
  panels: "studio.panels",
  toolbarActions: "studio.toolbar.actions",
  activityEditors: "workflow.activity.editors",
  propertyEditors: "workflow.activity.property-editors",
  expressionEditors: "workflow.expression-editors",
  workflowRunInputEditors: "workflow.run-input.editors",
  settingEditors: "studio.setting-editors",
  agentContextProviders: "studio.weaver.context-providers",
  agentPromptStarters: "studio.weaver.prompt-starters",
  agentCapabilities: "studio.weaver.capabilities",
  agentActions: "studio.weaver.actions",
  agentToolSlots: "studio.weaver.tool-slots",
  agentToolContracts: "studio.weaver.tool-contracts",
  agentResultRenderers: "studio.weaver.result-renderers",
  workflowDesignerNodeRenderers: "workflow.designer.node-renderers",
  workflowDesignerToolboxItems: "workflow.designer.toolbox-items",
  workflowDesignerPanels: "workflow.designer.panels",
  aiContextProviders: "studio.ai.context-providers",
  aiPromptActions: "studio.ai.prompt-actions",
  aiTools: "studio.ai.tools",
  aiProposalRenderers: "studio.ai.proposal-renderers",
  aiSurfaces: "studio.ai.surfaces",
  diagnostics: "studio.diagnostics",
  diagnosticsWidgets: "studio.diagnostics.widgets"
} as const;

export type StudioSlotId = typeof studioSlotIds[keyof typeof studioSlotIds];
export type StudioSlotOwnerKind = "host" | "module";

export interface StudioSlotOwnerDescriptor {
  kind: StudioSlotOwnerKind;
  id: string;
  moduleId?: string;
}

export interface StudioSlotDefinition {
  id: StudioSlotId | string;
  kind: string;
  owner: StudioSlotOwnerDescriptor;
  title?: string;
  parentId?: StudioSlotId | string;
}

export type StudioContributionAvailabilityState = "available" | "hidden" | "unavailable";
export type StudioContributionAvailabilitySource = "slot-owner" | "host-policy" | "module" | "feature" | "runtime";

export interface StudioContributionAvailability {
  state: StudioContributionAvailabilityState;
  reason?: string;
  source?: StudioContributionAvailabilitySource;
}

export interface StudioContributionComposition<T> {
  contribution: T;
  slot: StudioSlotDefinition;
  availability: StudioContributionAvailability;
  order: number;
  stableKey: string;
}

export interface StudioContributionPolicyContext<T> {
  contribution: T;
  slot: StudioSlotDefinition;
  context?: unknown;
}

export type StudioContributionDecision = boolean | StudioContributionAvailability | null | undefined;
export type StudioSlotOwnerRule<T> = (context: StudioContributionPolicyContext<T>) => StudioContributionDecision;
export type StudioHostContributionPolicy<T> = (context: StudioContributionPolicyContext<T>) => StudioContributionDecision;

export interface StudioContributionListOptions<T> {
  context?: unknown;
  disabledModuleIds?: string[];
  disabledFeatureIds?: string[];
  hostPolicy?: StudioHostContributionPolicy<T>;
  includeUnavailable?: boolean;
  includeHidden?: boolean;
}

export interface CreateStudioContributionRegistryOptions<T> {
  slot?: StudioSlotDefinition;
  slotOwner?: StudioSlotOwnerRule<T>;
  hostPolicy?: StudioHostContributionPolicy<T>;
  getOrder?(contribution: T): number | undefined;
  getStableKey?(contribution: T): string | undefined;
}

export const studioSlots = {
  featureAreas: defineStudioSlot({ id: studioSlotIds.featureAreas, kind: "feature-area", title: "Feature areas", owner: hostSlotOwner() }),
  navigation: defineStudioSlot({ id: studioSlotIds.navigation, kind: "navigation", title: "Navigation", owner: hostSlotOwner() }),
  routes: defineStudioSlot({ id: studioSlotIds.routes, kind: "route", title: "Routes", owner: hostSlotOwner() }),
  dashboardWidgets: defineStudioSlot({ id: studioSlotIds.dashboardWidgets, kind: "dashboard-widget", title: "Dashboard widgets", owner: hostSlotOwner() }),
  panels: defineStudioSlot({ id: studioSlotIds.panels, kind: "panel", title: "Panels", owner: hostSlotOwner() }),
  toolbarActions: defineStudioSlot({ id: studioSlotIds.toolbarActions, kind: "toolbar-action", title: "Toolbar actions", owner: hostSlotOwner() }),
  activityEditors: defineStudioSlot({ id: studioSlotIds.activityEditors, kind: "activity-editor", title: "Activity editors", owner: hostSlotOwner() }),
  propertyEditors: defineStudioSlot({ id: studioSlotIds.propertyEditors, kind: "property-editor", title: "Activity property editors", owner: hostSlotOwner() }),
  expressionEditors: defineStudioSlot({ id: studioSlotIds.expressionEditors, kind: "expression-editor", title: "Expression editors", owner: hostSlotOwner() }),
  workflowRunInputEditors: defineStudioSlot({ id: studioSlotIds.workflowRunInputEditors, kind: "workflow-run-input-editor", title: "Workflow run input editors", owner: hostSlotOwner() }),
  settingEditors: defineStudioSlot({ id: studioSlotIds.settingEditors, kind: "setting-editor", title: "Setting editors", owner: hostSlotOwner() }),
  agentContextProviders: defineStudioSlot({ id: studioSlotIds.agentContextProviders, kind: "weaver-context-provider", title: "Weaver context providers", owner: hostSlotOwner() }),
  agentPromptStarters: defineStudioSlot({ id: studioSlotIds.agentPromptStarters, kind: "weaver-prompt-starter", title: "Weaver prompt starters", owner: hostSlotOwner() }),
  agentCapabilities: defineStudioSlot({ id: studioSlotIds.agentCapabilities, kind: "weaver-capability", title: "Weaver capabilities", owner: hostSlotOwner() }),
  agentActions: defineStudioSlot({ id: studioSlotIds.agentActions, kind: "weaver-action", title: "Weaver actions", owner: hostSlotOwner() }),
  agentToolSlots: defineStudioSlot({ id: studioSlotIds.agentToolSlots, kind: "weaver-tool-slot", title: "Weaver tool slots", owner: hostSlotOwner() }),
  agentToolContracts: defineStudioSlot({ id: studioSlotIds.agentToolContracts, kind: "weaver-tool-contract", title: "Weaver tool contracts", owner: hostSlotOwner() }),
  agentResultRenderers: defineStudioSlot({ id: studioSlotIds.agentResultRenderers, kind: "weaver-result-renderer", title: "Weaver result renderers", owner: hostSlotOwner() }),
  workflowDesignerNodeRenderers: defineStudioSlot({ id: studioSlotIds.workflowDesignerNodeRenderers, kind: "workflow-designer-node-renderer", title: "Workflow designer node renderers", owner: hostSlotOwner() }),
  workflowDesignerToolboxItems: defineStudioSlot({ id: studioSlotIds.workflowDesignerToolboxItems, kind: "workflow-designer-toolbox-item", title: "Workflow designer toolbox items", owner: hostSlotOwner() }),
  workflowDesignerPanels: defineStudioSlot({ id: studioSlotIds.workflowDesignerPanels, kind: "workflow-designer-panel", title: "Workflow designer panels", owner: hostSlotOwner() }),
  aiContextProviders: defineStudioSlot({ id: studioSlotIds.aiContextProviders, kind: "ai-context-provider", title: "AI context providers", owner: hostSlotOwner() }),
  aiPromptActions: defineStudioSlot({ id: studioSlotIds.aiPromptActions, kind: "ai-prompt-action", title: "AI prompt actions", owner: hostSlotOwner() }),
  aiTools: defineStudioSlot({ id: studioSlotIds.aiTools, kind: "ai-tool", title: "AI tools", owner: hostSlotOwner() }),
  aiProposalRenderers: defineStudioSlot({ id: studioSlotIds.aiProposalRenderers, kind: "ai-proposal-renderer", title: "AI proposal renderers", owner: hostSlotOwner() }),
  aiSurfaces: defineStudioSlot({ id: studioSlotIds.aiSurfaces, kind: "ai-surface", title: "AI surfaces", owner: hostSlotOwner() }),
  diagnostics: defineStudioSlot({ id: studioSlotIds.diagnostics, kind: "diagnostic", title: "Diagnostics", owner: hostSlotOwner() }),
  diagnosticsWidgets: defineStudioSlot({ id: studioSlotIds.diagnosticsWidgets, kind: "diagnostics-widget", title: "Diagnostics widgets", owner: hostSlotOwner() })
};

export interface StudioContributionRegistry<T> {
  readonly slot: StudioSlotDefinition;
  add(contribution: T): void;
  list(options?: StudioContributionListOptions<T>): T[];
  compose(options?: StudioContributionListOptions<T>): StudioContributionComposition<T>[];
}

export interface StudioDashboardWidgetRegistry extends Omit<StudioContributionRegistry<StudioDashboardWidgetContribution>, "add"> {
  add<TSnapshot = unknown, TSettings = unknown>(contribution: StudioDashboardWidgetContribution<TSnapshot, TSettings>): void;
}

export interface StudioAiPromptDispatcher {
  dispatchPrompt(request: StudioAiPromptRequest): void;
  onPrompt(listener: (request: StudioAiPromptRequest) => void): () => void;
  /** Publishes the completed outcome of a correlated prompt back to interested callers. */
  publishPromptResult(result: StudioAiPromptResult): void;
  /** Subscribes to correlated prompt results. Returns an unsubscribe function. */
  onPromptResult(listener: (result: StudioAiPromptResult) => void): () => void;
}

/**
 * Observable availability of the AI provider backing Weaver surfaces. The host reports the latest state
 * (from the agent bootstrap's provider status); modules subscribe to gate provider-dependent affordances
 * — e.g. disabling an AI-only action with an explanatory tooltip when the provider is not configured.
 * Defaults to available so affordances stay enabled until the host reports otherwise.
 */
export interface StudioAiProviderAvailability {
  get(): boolean;
  /** Reports the latest provider availability; notifies subscribers only when the value changes. */
  set(available: boolean): void;
  /** Subscribes to availability changes; the listener is invoked immediately with the current value. Returns an unsubscribe function. */
  subscribe(listener: (available: boolean) => void): () => void;
}

export interface StudioAiContributionApi extends StudioAiPromptDispatcher {
  readonly contextProviders: StudioContributionRegistry<StudioAiContextProviderContribution>;
  readonly promptActions: StudioContributionRegistry<StudioAiPromptActionContribution>;
  readonly tools: StudioContributionRegistry<StudioAiToolContribution>;
  readonly proposalRenderers: StudioContributionRegistry<StudioAiProposalRendererContribution>;
  readonly surfaces: StudioContributionRegistry<StudioAiSurfaceContribution>;
  readonly providerAvailability: StudioAiProviderAvailability;
}

export interface ElsaStudioHostContext extends StudioEndpointContext {
  hostVersion: string;
  sdkVersion: string;
}

export interface ElsaStudioBackendContext extends StudioEndpointContext {
}

export interface StudioWorkflowRuntimeSettings {
  autosaveEnabledByDefault?: boolean;
}

export interface StudioActivityDefinitionRecoverySettings {
  enabled?: boolean;
  ttlMinutes?: number;
}

export interface StudioActivityDefinitionRuntimeSettings {
  localRecovery?: StudioActivityDefinitionRecoverySettings;
}

export interface StudioRuntimeIdentity {
  subject?: string | null;
  tenantId?: string | null;
}

export interface StudioRuntimeSettings {
  hostId?: string;
  workflows?: StudioWorkflowRuntimeSettings;
  activityDefinitions?: StudioActivityDefinitionRuntimeSettings;
  identity?: StudioRuntimeIdentity;
  attention?: {
    hostApiEnabled?: boolean;
  };
  dashboard?: {
    defaultRefreshIntervalMs?: number;
    widgetTimeoutMs?: number;
    pinnedWidgetIds?: string[];
  };
}

export const studioNavigationRequestedEvent = "elsa:studio-navigation-requested";

export interface StudioNavigationRequest {
  fromPath: string;
  toPath: string;
}

export function requestStudioNavigation(fromPath: string, toPath: string) {
  if (typeof window === "undefined") return true;
  return window.dispatchEvent(new CustomEvent<StudioNavigationRequest>(studioNavigationRequestedEvent, {
    cancelable: true,
    detail: { fromPath, toPath }
  }));
}

export interface StudioActivityDefinitionLayoutRecord {
  nodeId: string;
  data: unknown;
}

export interface StudioActivityDefinitionImplementationState {
  payload: unknown;
  layout: StudioActivityDefinitionLayoutRecord[];
}

export type StudioActivityDiagnosticSeverity = "Error" | "Warning" | "Info";

export interface StudioActivityDiagnosticSubject {
  kind: string;
  id: string;
  definitionId?: string | null;
  versionId?: string | null;
  revision?: number | null;
}

export interface StudioActivityDependencyPathItem {
  definitionId: string;
  versionId: string;
  version: string;
  templateHash: string;
}

export interface StudioActivityNodeOrigin {
  kind: string;
  id: string;
}

export interface StudioActivityDiagnosticLocation {
  providerKey?: string | null;
  jsonPointer?: string | null;
  referenceKey?: string | null;
  nodeOrigin?: StudioActivityNodeOrigin[] | null;
  dependencyPath?: StudioActivityDependencyPathItem[] | null;
}

export interface StudioActivityDiagnostic {
  code: string;
  severity: StudioActivityDiagnosticSeverity;
  message: string;
  subject: StudioActivityDiagnosticSubject;
  location?: StudioActivityDiagnosticLocation | null;
  remediation?: string | null;
  metadata: Record<string, string>;
}

export type StudioActivityDiagnosticFocusResult =
  | { kind: "focused"; announcement: string }
  | { kind: "unsupported"; announcement: string };

export interface StudioActivityDiagnosticFocusRequest {
  location: StudioActivityDiagnosticLocation;
  subject: StudioActivityDiagnosticSubject;
  editorElement: HTMLElement;
}

export interface StudioActivityOutcomeContract {
  referenceKey: string;
  name: string;
  isEmitted: boolean;
  description?: string | null;
}

export interface StudioActivityDefinitionContract {
  contractSchemaVersion: string;
  outcomes: StudioActivityOutcomeContract[];
}

export interface StudioActivityDefinitionImplementationEditorProps {
  context: StudioEndpointContext;
  definitionId: string;
  draftId: string;
  revision: number;
  providerKey: string;
  providerSchemaVersion: string;
  manifestFingerprint: string;
  contract?: StudioActivityDefinitionContract;
  value: StudioActivityDefinitionImplementationState;
  readOnly: boolean;
  onChange(value: StudioActivityDefinitionImplementationState): void;
}

export interface StudioActivityDefinitionImplementationEditorContribution {
  id: string;
  providerKey: string;
  providerSchemaVersion: string;
  createInitialImplementation(): StudioActivityDefinitionImplementationState;
  focusDiagnosticLocation?(
    request: StudioActivityDiagnosticFocusRequest
  ): StudioActivityDiagnosticFocusResult | Promise<StudioActivityDiagnosticFocusResult>;
  component: ComponentType<StudioActivityDefinitionImplementationEditorProps>;
}

export interface ElsaStudioModuleApi {
  readonly host: ElsaStudioHostContext;
  readonly backend: ElsaStudioBackendContext;
  readonly runtime: StudioRuntimeSettings;
  readonly featureAreas: StudioContributionRegistry<StudioFeatureAreaContribution>;
  readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
  readonly routes: StudioContributionRegistry<StudioRouteContribution>;
  readonly dashboardWidgets: StudioDashboardWidgetRegistry;
  readonly diagnosticsWidgets: StudioContributionRegistry<StudioDiagnosticsWidgetContribution>;
  readonly panels: StudioContributionRegistry<StudioPanelContribution>;
  readonly toolbarActions: StudioContributionRegistry<unknown>;
  readonly activityEditors: StudioContributionRegistry<StudioActivityDefinitionImplementationEditorContribution>;
  readonly propertyEditors: StudioContributionRegistry<StudioActivityPropertyEditorContribution>;
  readonly expressionEditors: StudioContributionRegistry<StudioExpressionEditorContribution>;
  /** Available on hosts that support the workflow run-input editor Slot. */
  readonly workflowRunInputEditors?: StudioContributionRegistry<StudioWorkflowRunInputEditorContribution>;
  readonly settingEditors: StudioContributionRegistry<StudioSettingEditorContribution>;
  readonly agent: StudioAgentRegistry;
  readonly workflowDesigner: {
    readonly nodeRenderers: StudioContributionRegistry<unknown>;
    readonly toolboxItems: StudioContributionRegistry<unknown>;
    readonly panels: StudioContributionRegistry<StudioWorkflowDesignerPanelContribution>;
  };
  readonly ai: StudioAiContributionApi;
  readonly dialogs: StudioDialogApi;
  readonly diagnostics: StudioContributionRegistry<StudioModuleDiagnostic>;
}

export interface ElsaStudioModule {
  register(api: ElsaStudioModuleApi): void | Promise<void>;
}

export function defineStudioSlot(definition: StudioSlotDefinition): StudioSlotDefinition {
  return definition;
}

export function hostSlotOwner(id = "studio-host"): StudioSlotOwnerDescriptor {
  return { kind: "host", id };
}

export function moduleSlotOwner(moduleId: string): StudioSlotOwnerDescriptor {
  return { kind: "module", id: moduleId, moduleId };
}

export function createContributionRegistry<T>(options: CreateStudioContributionRegistryOptions<T> = {}): StudioContributionRegistry<T> {
  const contributions: T[] = [];
  const slot = options.slot ?? defineStudioSlot({
    id: "studio.unknown",
    kind: "unknown",
    owner: hostSlotOwner(),
    title: "Unknown contributions"
  });

  return {
    slot,
    add(contribution) {
      contributions.push(contribution);
    },
    list(listOptions) {
      return composeContributions(contributions, slot, options, listOptions).map(item => item.contribution);
    },
    compose(listOptions) {
      return composeContributions(contributions, slot, options, listOptions);
    }
  };
}

export function createDashboardWidgetRegistry(): StudioDashboardWidgetRegistry {
  // Widget snapshot/settings types are contribution-local. The registry stores them opaquely and the
  // Dashboard host invokes each contribution through its own paired loader/body contract.
  return createContributionRegistry<StudioDashboardWidgetContribution>({ slot: studioSlots.dashboardWidgets }) as unknown as StudioDashboardWidgetRegistry;
}

export function createAiContributionApi(): StudioAiContributionApi {
  const listeners = new Set<(request: StudioAiPromptRequest) => void>();
  const resultListeners = new Set<(result: StudioAiPromptResult) => void>();
  const availabilityListeners = new Set<(available: boolean) => void>();
  let providerAvailable = true;

  return {
    contextProviders: createContributionRegistry({ slot: studioSlots.aiContextProviders }),
    promptActions: createContributionRegistry({ slot: studioSlots.aiPromptActions }),
    tools: createContributionRegistry({ slot: studioSlots.aiTools }),
    proposalRenderers: createContributionRegistry({ slot: studioSlots.aiProposalRenderers }),
    surfaces: createContributionRegistry({ slot: studioSlots.aiSurfaces }),
    providerAvailability: {
      get: () => providerAvailable,
      set(available) {
        if (available === providerAvailable) return;
        providerAvailable = available;
        for (const listener of availabilityListeners) {
          listener(available);
        }
      },
      subscribe(listener) {
        availabilityListeners.add(listener);
        listener(providerAvailable);
        return () => availabilityListeners.delete(listener);
      }
    },
    dispatchPrompt(request) {
      for (const listener of listeners) {
        listener(request);
      }
    },
    onPrompt(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    publishPromptResult(result) {
      for (const listener of resultListeners) {
        listener(result);
      }
    },
    onPromptResult(listener) {
      resultListeners.add(listener);
      return () => resultListeners.delete(listener);
    }
  };
}

export function createDialogController(): StudioDialogController {
  interface QueuedDialogRequest extends StudioDialogRequest {
    settle(result: StudioDialogResult): void;
  }

  const queue: QueuedDialogRequest[] = [];
  const listeners = new Set<(request: StudioDialogRequest | null) => void>();
  let nextId = 1;

  const current = () => queue[0] ?? null;

  const notify = () => {
    const head = current();
    for (const listener of listeners) {
      listener(head);
    }
  };

  function enqueue(request: Omit<QueuedDialogRequest, "id">) {
    queue.push({ ...request, id: nextId++ });
    if (queue.length === 1) {
      notify();
    }
  }

  const api: StudioDialogApi = {
    confirm(options) {
      return new Promise<boolean>(resolve => {
        enqueue({ ...options, kind: "confirm", settle: result => resolve(result === true) });
      });
    },
    prompt(options) {
      return new Promise<string | null>(resolve => {
        enqueue({ ...options, kind: "prompt", settle: result => resolve(typeof result === "string" ? result : null) });
      });
    },
    alert(options) {
      return new Promise<void>(resolve => {
        enqueue({ ...options, kind: "alert", settle: () => resolve() });
      });
    }
  };

  return {
    api,
    subscribe(listener) {
      listeners.add(listener);
      listener(current());
      return () => {
        listeners.delete(listener);
      };
    },
    getCurrent: current,
    respond(id, result) {
      const index = queue.findIndex(request => request.id === id);
      if (index === -1) {
        return;
      }

      const [request] = queue.splice(index, 1);
      request.settle(result);
      if (index === 0) {
        notify();
      }
    },
    cancelAll() {
      if (queue.length === 0) {
        return;
      }

      const pending = queue.splice(0);
      for (const request of pending) {
        request.settle(request.kind === "prompt" ? null : false);
      }
      notify();
    }
  };
}

function composeContributions<T>(
  contributions: T[],
  slot: StudioSlotDefinition,
  registryOptions: CreateStudioContributionRegistryOptions<T>,
  listOptions: StudioContributionListOptions<T> = {}
) {
  return contributions
    .map((contribution, index) => ({
      contribution,
      slot,
      availability: resolveContributionAvailability(contribution, slot, registryOptions, listOptions),
      order: registryOptions.getOrder?.(contribution) ?? getContributionOrder(contribution),
      stableKey: registryOptions.getStableKey?.(contribution) ?? getContributionStableKey(contribution, index),
      index
    }))
    .filter(item => shouldIncludeContribution(item.availability, listOptions))
    .sort((left, right) => left.order - right.order || left.stableKey.localeCompare(right.stableKey) || left.index - right.index)
    .map(({ index: _index, ...item }) => item);
}

function resolveContributionAvailability<T>(
  contribution: T,
  slot: StudioSlotDefinition,
  registryOptions: CreateStudioContributionRegistryOptions<T>,
  listOptions: StudioContributionListOptions<T>
): StudioContributionAvailability {
  const policyContext = { contribution, slot, context: listOptions.context };
  const slotDecision = normalizeContributionDecision(registryOptions.slotOwner?.(policyContext), "slot-owner");
  if (slotDecision.state !== "available") {
    return slotDecision;
  }

  const moduleId = getStringProperty(contribution, "moduleId");
  if (moduleId && listOptions.disabledModuleIds?.includes(moduleId)) {
    return { state: "hidden", reason: `Module ${moduleId} is disabled.`, source: "module" };
  }

  const featureId = getStringProperty(contribution, "featureId");
  if (featureId && listOptions.disabledFeatureIds?.includes(featureId)) {
    return { state: "hidden", reason: `Feature ${featureId} is disabled.`, source: "feature" };
  }

  const runtimeAvailability = normalizeContributionDecision(readContributionAvailability(contribution, listOptions.context), "runtime");
  const registryPolicyDecision = normalizeContributionDecision(registryOptions.hostPolicy?.(policyContext), "host-policy");
  if (registryPolicyDecision.state !== "available") {
    return registryPolicyDecision;
  }

  const listPolicyDecision = normalizeContributionDecision(listOptions.hostPolicy?.(policyContext), "host-policy");
  if (listPolicyDecision.state !== "available") {
    return listPolicyDecision;
  }

  return runtimeAvailability;
}

function normalizeContributionDecision(
  decision: StudioContributionDecision,
  source: StudioContributionAvailabilitySource
): StudioContributionAvailability {
  if (decision === false) {
    return { state: "hidden", source };
  }

  if (decision && typeof decision === "object") {
    // Runtime decisions may arrive as a { status, reason } shape rather than StudioContributionAvailability;
    // the double assertion is needed because TS rejects a direct conversion from the narrowed union member.
    const record = decision as unknown as Record<string, unknown>;

    if (!("state" in record) && typeof record.status === "string") {
      return {
        state: record.status === "available" ? "available" : "unavailable",
        reason: typeof record.reason === "string" ? record.reason : undefined,
        source
      };
    }

    return { ...decision, source: decision.source ?? source };
  }

  return { state: "available" };
}

function readContributionAvailability(contribution: unknown, context: unknown): StudioContributionDecision {
  if (!isRecord(contribution) || !("availability" in contribution)) {
    return true;
  }

  const availability = contribution.availability;
  return typeof availability === "function" ? availability(context) : availability as StudioContributionDecision;
}

function shouldIncludeContribution(availability: StudioContributionAvailability, options: { includeHidden?: boolean; includeUnavailable?: boolean }) {
  if (availability.state === "available") {
    return true;
  }

  if (availability.state === "hidden") {
    return options.includeHidden === true;
  }

  return options.includeUnavailable === true;
}

function getContributionOrder(contribution: unknown) {
  const order = getNumberProperty(contribution, "order");
  return order ?? 500;
}

function getContributionStableKey(contribution: unknown, index: number) {
  if (!isRecord(contribution)) {
    return `_${index.toString().padStart(4, "0")}`;
  }

  for (const key of ["id", "name", "label", "title", "path"]) {
    const value = contribution[key];
    if (typeof value === "string" && value.length > 0) {
      return value;
    }
  }

  return `_${index.toString().padStart(4, "0")}`;
}

function getStringProperty(value: unknown, key: string) {
  if (!isRecord(value)) {
    return undefined;
  }

  const property = value[key];
  return typeof property === "string" ? property : undefined;
}

function getNumberProperty(value: unknown, key: string) {
  if (!isRecord(value)) {
    return undefined;
  }

  const property = value[key];
  return typeof property === "number" ? property : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

export function createEndpointContext(baseUrl: string, options: { headers?: HeadersInit } = {}): StudioEndpointContext {
  return {
    baseUrl,
    headers: options.headers,
    http: createHttpClient(baseUrl, options.headers)
  };
}

/**
 * Options for {@link createHttpClient}. `fetch` and `applyTimeout` are seams that decorating clients
 * (e.g. the authenticated transport in `auth/transport/http.ts`) use to inject bearer-token/refresh
 * behaviour and to opt out of the endpoint-timeout without re-implementing the six JSON verbs.
 */
export interface StudioHttpClientOptions {
  defaultHeaders?: HeadersInit;
  /** Fetch implementation used for every request. Defaults to the global `fetch`. */
  fetch?: typeof fetch;
  /**
   * When false, requests are issued without the endpoint request-timeout AbortController. Decorating
   * clients that already manage their own retry/abort semantics set this to skip the timeout wrapper.
   */
  applyTimeout?: boolean;
}

export function createHttpClient(baseUrl: string, defaultHeadersOrOptions?: HeadersInit | StudioHttpClientOptions): StudioHttpClient {
  const options = normalizeHttpClientOptions(defaultHeadersOrOptions);
  const { defaultHeaders } = options;
  const send = <T>(url: string, init?: StudioRequestInit) => requestJson<T>(baseUrl, url, init, options);

  return {
    requestJson<T>(url: string, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, withJsonAccept(init)));
    },
    async getJson<T>(url: string, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, withJsonAccept(init)));
    },
    async postJson<T>(url: string, body: unknown, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, {
        ...init,
        method: "POST",
        headers: withJsonContentTypeAndAccept(init?.headers),
        body: JSON.stringify(body)
      }));
    },
    async putJson<T>(url: string, body: unknown, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, {
        ...init,
        method: "PUT",
        headers: withJsonContentTypeAndAccept(init?.headers),
        body: JSON.stringify(body)
      }));
    },
    async deleteJson<T>(url: string, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, withJsonAccept({
        ...init,
        method: "DELETE"
      })));
    },
    async postForm<T>(url: string, body: FormData, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, withJsonAccept({
        ...init,
        method: "POST",
        body
      })));
    }
  };
}

function normalizeHttpClientOptions(defaultHeadersOrOptions?: HeadersInit | StudioHttpClientOptions): StudioHttpClientOptions {
  if (isHttpClientOptions(defaultHeadersOrOptions)) {
    return defaultHeadersOrOptions;
  }

  return { defaultHeaders: defaultHeadersOrOptions };
}

function isHttpClientOptions(value: HeadersInit | StudioHttpClientOptions | undefined): value is StudioHttpClientOptions {
  return typeof value === "object"
    && value !== null
    && !(value instanceof Headers)
    && !Array.isArray(value)
    && ("fetch" in value || "applyTimeout" in value || "defaultHeaders" in value);
}

export function withDefaultHeaders(defaultHeaders: HeadersInit | undefined, init: StudioRequestInit = {}): StudioRequestInit {
  if (!defaultHeaders)
    return init;

  return {
    ...init,
    headers: mergeHeaders(defaultHeaders, init.headers)
  };
}

function mergeHeaders(defaultHeaders: HeadersInit, requestHeaders?: HeadersInit) {
  const headers = new Headers(defaultHeaders);
  new Headers(requestHeaders).forEach((value, key) => headers.set(key, value));
  return headers;
}

async function requestJson<T>(baseUrl: string, url: string, init?: StudioRequestInit, options: StudioHttpClientOptions = {}): Promise<T> {
  const requestUrl = resolveStudioUrl(baseUrl, url);
  const request = options.fetch ?? fetch;
  // applyTimeout: false (the authenticated client) opts out of the implicit default timeout only; an explicit
  // per-request timeoutMs is a caller contract and is honored regardless of the client's default-timeout policy.
  const response = options.applyTimeout === false && init?.timeoutMs === undefined
    ? await request(requestUrl, init)
    : await fetchWithTimeout(request, requestUrl, init);

  return parseJsonResponse<T>(requestUrl, response);
}

async function fetchWithTimeout(request: typeof fetch, requestUrl: string, init?: StudioRequestInit): Promise<Response> {
  const timeoutMs = init?.timeoutMs ?? requestTimeoutMs;
  const timeout = new AbortController();
  const timeoutId = globalThis.setTimeout(() => timeout.abort(), timeoutMs);

  try {
    return await request(requestUrl, {
      ...init,
      signal: combineAbortSignals(init?.signal, timeout.signal)
    });
  } catch (error) {
    if (timeout.signal.aborted && !init?.signal?.aborted) {
      throw new Error(`Request to ${requestUrl} timed out after ${timeoutMs / 1000} seconds. Check Studio:BackendBaseUrl and make sure the backend API is responding.`);
    }

    throw error;
  } finally {
    globalThis.clearTimeout(timeoutId);
  }
}

async function parseJsonResponse<T>(requestUrl: string, response: Response): Promise<T> {
  if (!response.ok) {
    throw await createStudioHttpError(response);
  }

  const text = await response.text();
  if (!text.trim()) {
    return {} as T;
  }

  try {
    return JSON.parse(text) as T;
  } catch {
    throw new StudioHttpError(
      response.status,
      `Expected JSON from ${requestUrl}, but received ${describeResponseContent(response, text)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`);
  }
}

function combineAbortSignals(requestSignal: AbortSignal | null | undefined, timeoutSignal: AbortSignal) {
  if (!requestSignal) {
    return timeoutSignal;
  }

  if (typeof AbortSignal.any === "function") {
    return AbortSignal.any([requestSignal, timeoutSignal]);
  }

  const controller = new AbortController();
  const abort = () => controller.abort();
  if (requestSignal.aborted || timeoutSignal.aborted) {
    controller.abort();
  } else {
    requestSignal.addEventListener("abort", abort, { once: true });
    timeoutSignal.addEventListener("abort", abort, { once: true });
  }

  return controller.signal;
}

export async function readStudioHttpErrorMessage(response: Response) {
  return (await readStudioHttpError(response)).message;
}

export async function createStudioHttpError(response: Response) {
  const error = await readStudioHttpError(response);
  return new StudioHttpError(response.status, error.message, error.validationErrors, error.payload);
}

async function readStudioHttpError(response: Response): Promise<{ message: string; validationErrors: StudioValidationErrors | null; payload: unknown | null }> {
  const contentType = response.headers.get("content-type") ?? "";
  if (isJsonContentType(contentType)) {
    try {
      const payload = await response.json() as Record<string, unknown>;
      const validationErrors = extractValidationErrors(payload);
      return {
        message: getProblemDetailsMessage(payload) ?? getValidationErrorMessage(validationErrors) ?? `Request failed with ${response.status}.`,
        validationErrors,
        payload
      };
    } catch {
      return { message: `Request failed with ${response.status}.`, validationErrors: null, payload: null };
    }
  }

  const text = await response.text();
  return { message: text.trim() || `Request failed with ${response.status}.`, validationErrors: null, payload: null };
}

function isJsonContentType(contentType: string) {
  return contentType.toLowerCase().includes("json");
}

export async function describeApiError(error: unknown): Promise<string> {
  if (error instanceof StudioHttpError) {
    return error.message;
  }

  if (isResponseLikeError(error)) {
    try {
      return await readStudioHttpErrorMessage(error.response.clone());
    } catch {
      return error.response.statusText || "Request failed.";
    }
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "Unknown error.";
}

export async function tryExtractValidationErrors(error: unknown): Promise<StudioValidationErrors | null> {
  if (error instanceof StudioHttpError) {
    return error.validationErrors;
  }

  if (!isResponseLikeError(error)) {
    return null;
  }

  try {
    const payload = await error.response.clone().json() as Record<string, unknown>;
    return extractValidationErrors(payload);
  } catch {
    return null;
  }
}

function getProblemDetailsMessage(payload: Record<string, unknown>) {
  if (typeof payload.detail === "string" && payload.detail.length > 0) return payload.detail;
  if (typeof payload.title === "string" && payload.title.length > 0) return payload.title;
  if (typeof payload.reason === "string" && payload.reason.length > 0) return payload.reason;
  if (Array.isArray(payload.errors) && payload.errors.length > 0) return payload.errors.map(String).join(" ");
  if (payload.errors && typeof payload.errors === "object") {
    const messages = Object.values(payload.errors as Record<string, unknown>)
      .flatMap(value => Array.isArray(value) ? value : [value])
      .map(String);
    if (messages.length > 0) return messages.join(" ");
  }

  return null;
}

function extractValidationErrors(payload: Record<string, unknown>): StudioValidationErrors | null {
  const errors = payload.errors;
  if (!errors || typeof errors !== "object" || Array.isArray(errors)) {
    return null;
  }

  const result: StudioValidationErrors = {};
  for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
    const messages = Array.isArray(value) ? value.map(String) : [String(value)];
    if (messages.length > 0) {
      result[key] = messages;
    }
  }

  return Object.keys(result).length > 0 ? result : null;
}

function getValidationErrorMessage(errors: StudioValidationErrors | null) {
  if (!errors) {
    return null;
  }

  return Object.values(errors).flat().join(" ");
}

function isResponseLikeError(error: unknown): error is { response: Response } {
  return typeof error === "object" && error !== null && "response" in error && (error as { response?: unknown }).response instanceof Response;
}

function resolveStudioUrl(baseUrl: string, url: string) {
  return new URL(url, baseUrl).toString();
}

function withJsonAccept(init?: StudioRequestInit): StudioRequestInit | undefined {
  const headers = new Headers(init?.headers);
  if (!headers.has("Accept")) {
    headers.set("Accept", "application/json");
  }

  return {
    ...init,
    cache: init?.cache ?? "no-store",
    headers
  };
}

function withJsonContentTypeAndAccept(headers?: HeadersInit) {
  const result = new Headers(headers);
  if (!result.has("Content-Type")) {
    result.set("Content-Type", "application/json");
  }
  if (!result.has("Accept")) {
    result.set("Accept", "application/json");
  }

  return result;
}

function describeResponseContent(response: Response, text: string) {
  const contentType = response.headers.get("content-type") ?? "an unknown content type";
  const trimmed = text.trim();
  const preview = trimmed.length > 0 ? `: ${trimmed.slice(0, 80)}` : "";
  return `${contentType}${preview}`;
}

export class StudioHttpError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly validationErrors: StudioValidationErrors | null = null,
    public readonly payload: unknown | null = null
  ) {
    super(message);
    this.name = "StudioHttpError";
  }
}
