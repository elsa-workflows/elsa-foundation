import type { AuthSession } from "../auth/types";
export * from "./backends";
export * from "./backendConnection";
export * from "./moduleHost";
import type {StudioConnectionController, StudioModuleHostContext, StudioModuleInstance, StudioShellContribution} from "./moduleHost";
import type { StudioBackendAwareContribution, StudioBackendSession } from "./backends";
import type { ComponentType } from "react";
import type { StudioActivityIconComponent } from "./activityIcons";

export * from "../auth";
export * from "./themeLayout";
export * from "./hosts";
export * from "./activityIcons";
export * from "./useDialogFocus";

export type StudioModuleStatus = "available" | "loaded" | "disabled" | "incompatible" | "failed";

export interface StudioModuleManifest {
  id: string;
  displayName: string;
  version: string;
  entry: string;
  styles: string[];
  /** Explicit producer guarantee; omitted legacy/custom manifests retain version cache busting. */
  stylesHaveContentHashes?: boolean;
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
  /** Disables the authenticated transport's automatic 401 refresh retry for endpoint-defined responses. */
  retryOnUnauthorized?: boolean;
}

/**
 * A parsed JSON response together with the response metadata the JSON verbs discard. Returned by
 * {@link StudioHttpClient.getJsonWithHeaders} for the rare endpoint whose contract lives partly in a
 * header — a download name in `Content-Disposition`, for instance.
 */
export interface StudioJsonResponse<T> {
  value: T;
  status: number;
  headers: Headers;
}

export interface StudioHttpClient {
  /**
   * Sends a request through the configured backend transport and returns the untouched response. Optional so
   * existing module-owned and test clients remain compatible; callers that need status, headers, or a non-JSON
   * body should check for its presence rather than falling back to global fetch.
   */
  requestResponse?(url: string, init?: StudioRequestInit): Promise<Response>;
  requestJson<T>(url: string, init?: StudioRequestInit): Promise<T>;
  getJson<T>(url: string, init?: StudioRequestInit): Promise<T>;
  postJson<T>(url: string, body: unknown, init?: StudioRequestInit): Promise<T>;
  putJson<T>(url: string, body: unknown, init?: StudioRequestInit): Promise<T>;
  deleteJson<T>(url: string, init?: StudioRequestInit): Promise<T>;
  postForm<T>(url: string, body: FormData, init?: StudioRequestInit): Promise<T>;
  /**
   * GETs JSON and also hands back the response headers. Optional so existing client implementations and
   * test doubles stay valid: callers must fall back to {@link getJson} when it is absent. Note that a
   * cross-origin response only exposes a header the server names in `Access-Control-Expose-Headers`.
   */
  getJsonWithHeaders?<T>(url: string, init?: StudioRequestInit): Promise<StudioJsonResponse<T>>;
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

export interface StudioRouteContribution extends StudioBackendAwareContribution {
  id: string;
  path: string;
  label: string;
  component: ComponentType<StudioRouteComponentProps>;
}

/** The sidebar sections the Studio host groups navigation into. */
export type StudioNavigationSection = "workspace" | "settings";

/**
 * Where a nav contribution appears in the Studio sidebar.
 * Unset defaults to `more` at the top level (extensions land under More tools). A child with no
 * placement inherits its parent's resolved placement so it stays visible; a child that declares a
 * different placement is listed there instead of vanishing. `section: "settings"` still maps to
 * `admin` so older modules keep their Settings grouping until they declare `placement`.
 * `admin` and `settings` stay visible to anonymous/demo users because the server allows admin
 * calls when sign-in is off; authenticated users need host-control read.
 */
export type StudioNavigationPlacement = "main" | "more" | "settings" | "admin";

export const defaultStudioNavigationPlacement: StudioNavigationPlacement = "more";
export const defaultStudioNavigationOrder = 1000;

export type StudioNavigationRequirement = string | readonly string[];

export interface StudioNavigationContribution extends StudioBackendAwareContribution {
  id: string;
  label: string;
  path: string;
  activePathPrefix?: string;
  order?: number;
  iconColor?: string;
  parentId?: string;
  /**
   * The sidebar section the item is listed under. When omitted the host infers it: its own settings-style paths
   * (modules, feeds, features, themes) land under Settings and everything else under Workspace.
   * Prefer `placement` for new contributions.
   */
  section?: StudioNavigationSection;
  /**
   * Sidebar placement. `main` is the curated Home / Workflows / Runs / Tasks list; `more` is More tools;
   * `settings` is a Settings shell section (Appearance; available to every session); `admin` is
   * Settings › Administration and uses the host-control read gate. Unset defaults to `more` at the
   * top level, or to the parent placement when this item is a child. The sidebar keeps one Settings
   * entry.
   */
  placement?: StudioNavigationPlacement;
  /** Hide the item when any listed feature is disabled. The contributing module's own gate still applies. */
  requiresFeature?: StudioNavigationRequirement;
  /** Hide the item from authenticated users who lack every listed role. */
  requiresRole?: StudioNavigationRequirement;
  /** Hide the item from authenticated users who lack every listed permission. `admin` implies host-control read. */
  requiresPermission?: StudioNavigationRequirement;
  /** The icon rendered in the item's navigation tile. When omitted the host picks one from the item id. */
  icon?: ComponentType<{ size?: number }>;
}

export type StudioDashboardWidgetSize = "small" | "medium" | "wide" | "full";
export type StudioHomeWidgetSizeAlias = "S" | "M" | "L";
export type StudioHomeWidgetSize = StudioDashboardWidgetSize | StudioHomeWidgetSizeAlias;

export const homeWidgetIds = {
  getStarted: "home.get-started",
  quickActions: "home.quick-actions",
  recentWorkflows: "home.recent-workflows",
  needsAttention: "home.needs-attention",
  recentRuns: "home.recent-runs",
  tasks: "home.tasks"
} as const;

export const homeRunDetailOpenedStorageKey = "elsa-studio.ui.home.run-detail-opened";

export function normalizeHomeWidgetSize(size?: string): StudioDashboardWidgetSize {
  switch (size) {
    case "S":
    case "small":
      return "small";
    case "M":
    case "medium":
      return "medium";
    case "wide":
      return "wide";
    case "L":
    case "full":
      return "full";
    default:
      return "medium";
  }
}

export function recordHomeRunDetailOpened(storage: Pick<Storage, "setItem"> | undefined = defaultHomeStorage()) {
  try {
    storage?.setItem(homeRunDetailOpenedStorageKey, "true");
  } catch {
    /* device storage is best effort */
  }
}

export function hasHomeRunDetailOpened(storage: Pick<Storage, "getItem"> | undefined = defaultHomeStorage()) {
  try {
    return storage?.getItem(homeRunDetailOpenedStorageKey) === "true";
  } catch {
    return false;
  }
}

function defaultHomeStorage(): Storage | undefined {
  try {
    return typeof window === "undefined" ? undefined : window.localStorage;
  } catch {
    return undefined;
  }
}

/** True when `path` is a workflow run detail (`/workflows/instances/:id`), not the Runs list. */
export function isHomeRunDetailPath(path: string) {
  const segments = path.split("/").filter(Boolean);
  return segments.length === 3 && segments[0] === "workflows" && segments[1] === "instances" && segments[2] !== "";
}

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

export interface StudioDashboardWidgetContribution<TSnapshot = unknown, TSettings = unknown> extends StudioBackendAwareContribution {
  id: string;
  moduleId: string;
  title: string;
  description?: string;
  order?: number;
  defaultVisible: boolean;
  defaultSize: StudioDashboardWidgetSize;
  supportedSizes: StudioDashboardWidgetSize[];
  permissions?: string[];
  /** Hide the widget unless every listed feature/capability is enabled on the host. */
  requiresFeature?: string | readonly string[];
  /** Core widgets may appear on the server default Home layout. */
  core?: boolean;
  minimumRefreshIntervalMs?: number;
  cacheLifetimeMs?: number;
  timeoutMs?: number;
  settings?: StudioDashboardWidgetSettings<TSettings>;
  load?(context: StudioDashboardWidgetLoadContext<TSettings>): Promise<TSnapshot>;
  isEmpty?(snapshot: TSnapshot | undefined, settings: TSettings): boolean;
  emptyState?: StudioDashboardWidgetEmptyState;
  component: ComponentType<StudioDashboardWidgetBodyProps<TSnapshot, TSettings>>;
}

export interface StudioHomeWidgetContribution<TSnapshot = unknown, TSettings = unknown>
  extends Omit<StudioDashboardWidgetContribution<TSnapshot, TSettings>, "defaultSize" | "supportedSizes" | "defaultVisible"> {
  defaultSize?: StudioHomeWidgetSize;
  supportedSizes?: StudioHomeWidgetSize[];
  defaultVisible?: boolean;
}

export function registerHomeWidget<TSnapshot = unknown, TSettings = unknown>(
  api: Pick<ElsaStudioModuleApi, "dashboardWidgets" | "homeWidgets">,
  contribution: StudioHomeWidgetContribution<TSnapshot, TSettings>
) {
  const defaultSize = normalizeHomeWidgetSize(contribution.defaultSize);
  const supportedSizes = (contribution.supportedSizes?.length ? contribution.supportedSizes : [defaultSize])
    .map(normalizeHomeWidgetSize);
  const widget: StudioDashboardWidgetContribution<TSnapshot, TSettings> = {
    ...contribution,
    defaultVisible: contribution.defaultVisible ?? true,
    defaultSize,
    supportedSizes
  };
  (api.homeWidgets ?? api.dashboardWidgets).add(widget);
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

export interface StudioPanelContribution extends StudioBackendAwareContribution {
  id: string;
  title: string;
  order?: number;
  component: ComponentType;
}

export type StudioWorkflowDesignerPanelSide = "left" | "right";
export type StudioGraphAuthoringResourceKind = "workflow-definition" | "activity-definition-graph";

export interface StudioWorkflowDesignerPanelProps<TContext = unknown> {
  context: TContext;
}

export interface StudioWorkflowDesignerPanelContribution<TContext = unknown> {
  id: string;
  title: string;
  side: StudioWorkflowDesignerPanelSide;
  order?: number;
  /**
   * Resource hosts this panel supports. Omission preserves the pre-existing workflow-only contract;
   * extensions must opt in before they can receive an Activity Definition graph context.
   */
  supportedResourceKinds?: StudioGraphAuthoringResourceKind[];
  component: ComponentType<StudioWorkflowDesignerPanelProps<TContext>>;
}

export interface StudioWorkflowContextConnection {
  source: string;
  target: string;
  sourcePort?: string;
  targetPort?: string;
}

export interface StudioFeatureAreaNavLeaf extends StudioBackendAwareContribution {
  id?: string;
  title: string;
  path: string;
  iconColor?: string;
  placeholder?: boolean;
  placement?: StudioNavigationPlacement;
  order?: number;
  requiresFeature?: StudioNavigationRequirement;
  requiresRole?: StudioNavigationRequirement;
  requiresPermission?: StudioNavigationRequirement;
}

export interface StudioFeatureAreaNavParent extends StudioBackendAwareContribution {
  id?: string;
  title: string;
  path: string;
  iconColor?: string;
  items: StudioFeatureAreaNavLeaf[];
  placement?: StudioNavigationPlacement;
  order?: number;
  requiresFeature?: StudioNavigationRequirement;
  requiresRole?: StudioNavigationRequirement;
  requiresPermission?: StudioNavigationRequirement;
}

export type StudioFeatureAreaNavContribution = StudioFeatureAreaNavLeaf | StudioFeatureAreaNavParent;

export interface StudioFeatureAreaRouteContribution extends StudioBackendAwareContribution {
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
  /** Presentation tier from the activity catalog; unknown values remain forward-compatible. */
  tier?: string | null;
  /** Whether the activity requires this input at runtime; independent of its presentation tier. */
  isRequired?: boolean | null;
  /** Collection shape of the CLR type (elsa-foundation#945); absent on older backends. */
  collectionKind?: StudioActivityCollectionKind | null;
  isWrapped?: boolean;
  uiHint?: string | null;
  defaultValue?: unknown;
  defaultSyntax?: string | null;
  isReadOnly?: boolean | null;
  storageDriverType?: string | null;
  uiSpecifications?: StudioActivityInputUISpecifications | null;
  /** Activity-declared: the value must not be shown or logged. Absent on older backends. */
  isSensitive?: boolean | null;
  /** Activity-declared: accepts only a Secret Reference. Implies isSensitive. Absent on older backends. */
  isCredential?: boolean | null;
  /** Activity-declared: the required connection type. Absent on older backends. */
  connectionType?: string | null;
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
  /** Stable identity used to correlate output captures with the activity's published contract. */
  referenceKey?: string | null;
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
  /** The node id of the activity being edited, so an editor can tell one activity from the next. Absent on older hosts. */
  activityId?: string | null;
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
  /** Reports only whether this mounted editor currently has a local blocking error. */
  onValidationChange?: (hasError: boolean) => void;
}

export interface StudioActivityPropertyEditorContribution extends StudioBackendAwareContribution {
  id: string;
  order?: number;
  /** Declares that all local blocking errors are reported through onValidationChange, or that none exist. */
  supportsValidationReporting?: true;
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

/** A backend-discovered trigger mode available while driving a workflow Test Run. */
export interface StudioTestRunModeDescriptor extends Record<string, unknown> {
  modeId: string;
  label: string;
  description?: string;
  isDefault?: boolean;
  configurationVersion?: number;
  /** Safe JSON Schema for the user-supplied stimulus value. */
  configurationSchema?: unknown;
  /** Read-only module context, such as a resolved route or authored schedule. */
  configuration?: unknown;
}

/** One eligible, backend-validated workflow start option. */
export interface StudioTestRunOptionDescriptor extends Record<string, unknown> {
  optionId: string;
  driverId?: string | null;
  nodeId: string;
  label: string;
  isAvailable?: boolean;
  reason?: string | null;
  configuration?: unknown;
  modes: StudioTestRunModeDescriptor[];
}

/** One active, module-supported wait on a workflow Test Run execution. */
export interface StudioTestRunWaitDescriptor extends Record<string, unknown> {
  waitId: string;
  nodeId: string;
  label: string;
  driverId?: string | null;
  isAvailable: boolean;
  reason?: string | null;
  modes: StudioTestRunModeDescriptor[];
}

/** Opaque module-owned action returned by a Test Run preparation response. */
export interface StudioTestRunDriverAction extends Record<string, unknown> {
  kind: string;
  /** Server-resolved path from the API origin; includes the deployment PathBase when one is configured. */
  relativeUrl?: string;
  method?: string;
  details?: unknown;
}

export interface StudioTestStimulusEditorContext {
  option: StudioTestRunOptionDescriptor;
  mode: StudioTestRunModeDescriptor;
  draft: string;
}

export interface StudioTestStimulusControlProps {
  id: string;
  "aria-label": string;
  "aria-describedby"?: string;
  "aria-invalid": boolean;
}

export interface StudioTestStimulusEditorProps extends StudioTestStimulusEditorContext {
  disabled?: boolean;
  controlProps: StudioTestStimulusControlProps;
  onChange(draft: string): void;
}

export interface StudioTestStimulusActionContext {
  backend: StudioEndpointContext;
  option: StudioTestRunOptionDescriptor;
  mode: StudioTestRunModeDescriptor;
  action: StudioTestRunDriverAction;
  data: unknown;
  editorDraft: string;
}

export interface StudioTestStimulusActionResult extends Record<string, unknown> {
  kind: string;
}

export interface StudioTestStimulusResultProps {
  action: StudioTestRunDriverAction;
  result: StudioTestStimulusActionResult;
}

/**
 * Edits a module's one-off trigger data and may execute a prepared module action through Studio's
 * authenticated backend transport. Match on stable driver/mode identifiers, not workflow activity types.
 */
export interface StudioTestStimulusEditorContribution {
  id: string;
  order?: number;
  startLabel?: string;
  actionLabel?: string;
  supports(context: Omit<StudioTestStimulusEditorContext, "draft">): boolean;
  component: ComponentType<StudioTestStimulusEditorProps>;
  validate(context: StudioTestStimulusEditorContext): string | undefined;
  serialize(context: StudioTestStimulusEditorContext): unknown;
  executeAction?(context: StudioTestStimulusActionContext): Promise<StudioTestStimulusActionResult>;
  actionResultComponent?: ComponentType<StudioTestStimulusResultProps>;
}

export type StudioExpressionEditorSurface = "inline" | "expanded";

/** A stable, editor-engine-neutral identity for one authored expression. */
export interface StudioExpressionDocument {
  id: string;
  uri: string;
  draftId: string;
  activityId: string;
  propertyKey: string;
  expressionType: string;
  source: string;
  sourceVersion: number;
  contextVersion?: string;
}

export type StudioExpressionToolingState =
  | "ready"
  | "supported-empty"
  | "unavailable"
  | "unauthorized"
  | "incompatible"
  | "stale"
  | "canceled";
export const expressionEditorSessionEndedEvent = "elsa:expression-editor-session-ended";
export const expressionToolingAuthorizationRevokedEvent = "elsa:expression-tooling-authorization-revoked";
export const expressionToolingAuthorizationRestoredEvent = "elsa:expression-tooling-authorization-restored";
export type StudioExpressionSymbolKind = "value" | "function" | "filter" | "tag" | "namespace" | "member" | "keyword";
export type StudioExpressionValueShapeKind = "unknown" | "scalar" | "object" | "collection" | "callable";

export interface StudioExpressionToolingCapabilities {
  highlighting: boolean;
  completion: boolean;
  hover: boolean;
  signatures: boolean;
  formatting: boolean;
  localDiagnostics: boolean;
  semanticValidation: boolean;
}

export interface StudioExpressionToolingDescriptor {
  expressionType: string;
  moduleId: string;
  moduleVersion: string;
  contractMinVersion: number;
  contractMaxVersion: number;
  capabilities: StudioExpressionToolingCapabilities;
  catalogVersion?: string;
  permissionRevision?: string;
  hostPolicyRevision?: string;
}

export interface StudioExpressionPosition {
  line: number;
  column: number;
}

export interface StudioExpressionRange {
  start: StudioExpressionPosition;
  end: StudioExpressionPosition;
}

export interface StudioExpressionSignatureParameter {
  name: string;
  documentation?: string;
  shapeId?: string;
  optional?: boolean;
}

export interface StudioExpressionSignature {
  label: string;
  documentation?: string;
  returnShapeId?: string;
  parameters: StudioExpressionSignatureParameter[];
}

/** Metadata-only symbol. It intentionally never includes a runtime value or evaluated source. */
export interface StudioExpressionSymbol {
  id: string;
  name: string;
  kind: StudioExpressionSymbolKind;
  documentation?: string;
  shapeId?: string;
  parentId?: string;
  sortText?: string;
  signatures?: StudioExpressionSignature[];
}

export interface StudioExpressionValueShapeMember {
  name: string;
  documentation?: string;
  shapeId: string;
}

export interface StudioExpressionValueShape {
  id: string;
  kind: StudioExpressionValueShapeKind;
  displayName?: string;
  nullable: boolean;
  scalarType?: string;
  elementShapeId?: string;
  additionalMembers?: boolean;
  members: StudioExpressionValueShapeMember[];
}

/** Permission-filtered, language-neutral design-time facts for an expression location. */
export interface StudioExpressionAuthoringContext {
  version: string;
  catalogVersion?: string;
  permissionRevision?: string;
  hostPolicyRevision?: string;
  capabilities?: StudioExpressionToolingCapabilities;
  expectedResultType?: string;
  expectedResultShape?: StudioExpressionValueShape;
  target?: StudioExpressionSymbol;
  rootSymbols?: StudioExpressionSymbol[];
  workflowInputs: StudioExpressionSymbol[];
  visibleVariables: StudioExpressionSymbol[];
  visibleActivityOutputs: StudioExpressionSymbol[];
  shapeReferences?: string[];
}

export interface StudioExpressionToolingResult<T> {
  state: StudioExpressionToolingState;
  contractVersion: number;
  expressionType: string;
  moduleVersion?: string;
  catalogVersion?: string;
  contextVersion?: string;
  data?: T;
}

export interface StudioExpressionSymbolCatalogPage {
  symbols: StudioExpressionSymbol[];
  nextCursor?: string;
}

export interface StudioExpressionValidationDiagnostic {
  severity: StudioExpressionEditorDiagnosticSeverity;
  code?: string;
  message: string;
  range?: StudioExpressionRange;
  documentId: string;
  sourceVersion: number;
  contextVersion: string;
  catalogVersion?: string;
}

export interface StudioExpressionValidationResult {
  documentId: string;
  sourceVersion: number;
  contextVersion: string;
  diagnostics: StudioExpressionValidationDiagnostic[];
}

export interface StudioExpressionCompletionItem {
  label: string;
  detail?: string;
  documentation?: string;
  insertText?: string;
  kind?: StudioExpressionSymbolKind;
}

export interface StudioExpressionCompletionResult {
  items: StudioExpressionCompletionItem[];
}

export interface StudioExpressionHoverResult {
  contents: string;
  range?: StudioExpressionRange;
}

export interface StudioExpressionToolingClient {
  describe(signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionToolingDescriptor[]>>;
  getCatalog(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, query?: string, cursor?: string, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionSymbolCatalogPage>>;
  getValueShape(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, shapeId: string, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionValueShape>>;
  getAuthoringContext(document: StudioExpressionDocument, state: unknown, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionAuthoringContext>>;
  getCompletions(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, position: StudioExpressionPosition, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionCompletionResult>>;
  getHover(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, position: StudioExpressionPosition, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionHoverResult>>;
  validate(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionValidationResult>>;
  invalidateAuthorization(revisions?: { permissionRevision?: string; hostPolicyRevision?: string }): void;
  /** Permanently stops source-aware requests for this workflow-lifetime client. */
  revokeAuthorization?(): void;
  /** Allows a freshly authenticated session to establish new permission-scoped tooling context. */
  restoreAuthorization?(): void;
  dispose(): void;
}

export interface StudioExpressionEditorContext {
  syntax: string;
  surface: StudioExpressionEditorSurface;
  descriptor: StudioActivityInputDescriptor;
  activity: unknown;
  expressionDescriptors: StudioExpressionDescriptor[];
  readOnly?: boolean;
  /** Optional while older hosts and generic editors remain supported. */
  document?: StudioExpressionDocument;
  /** Optional, permission-filtered design-time facts for the active expression. */
  authoringContext?: StudioExpressionToolingResult<StudioExpressionAuthoringContext>;
  /** Optional host-owned semantic validation result for the current document version. */
  validation?: StudioExpressionToolingResult<StudioExpressionValidationResult>;
  /** Optional engine-neutral transport owned by the workflow host. */
  tooling?: StudioExpressionToolingClient;
  /** Workflow-editor lifetime scope used to isolate and dispose engine history. */
  editorSessionScope?: string;
  /** Notifies the host that this editor surface became the active tooling target. */
  onFocus?(): void;
  /** Requests immediate validation when editing focus leaves the surface. */
  onBlur?(): void;
}

export interface StudioExpressionEditorProps {
  descriptor: StudioActivityInputDescriptor;
  syntax: string;
  value: unknown;
  disabled?: boolean;
  initialFocus?: boolean;
  context: StudioExpressionEditorContext;
  onChange(value: unknown): void;
  /** Requests the host to reveal the same document in its expanded editor surface. */
  onExpand?(): void;
}

export type StudioExpressionEditorDiagnosticSeverity = "info" | "warning" | "error";

export interface StudioExpressionEditorDiagnostic {
  severity?: StudioExpressionEditorDiagnosticSeverity;
  code?: string;
  message: string;
  origin?: "local" | "semantic";
  range?: StudioExpressionRange;
  documentId?: string;
  sourceVersion?: number;
  contextVersion?: string;
  catalogVersion?: string;
}

export interface StudioExpressionEditorMetadata {
  displayName?: string;
  installHint?: string;
  packageId?: string;
  /** Editor-owned capabilities composed with, but never attributed to, the server descriptor. */
  toolingCapabilities?: Partial<StudioExpressionToolingCapabilities>;
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

export interface StudioExpressionEditorContribution extends StudioBackendAwareContribution {
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
  signal?: AbortSignal;
}

/** Backend payload enums retain camelCase wire spelling; presentation may normalize labels. */
export type StudioAgentContextOrigin = "clientSnapshot" | "resourceData" | "authoritativeRead" | "installedGuidance";
export type StudioAgentExecutionLocation = "server" | "clientWorkingState";
export type StudioAgentEffectScope = "clientWorkingState" | "serverPersisted";
export type StudioAgentPreparedRisk = "readOnly" | "reviewRequired" | "destructive" | "admin";
export type StudioAgentClientOutcome = "applied" | "refused" | "uncertain";

/** Module-defined data is opaque to the shell and provider transports. */
export interface StudioAgentPayload<TData = unknown> {
  typeId: string;
  schemaVersion: string;
  data: TData;
}

/** Identifies an originating registry and module/editor lifetime; it grants no authority. */
export interface StudioAgentClientBinding {
  clientId: string;
  generationId: string;
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
  summary?: string;
  references?: Record<string, string>;
  moduleId?: string;
  schemaVersion?: string;
  target?: StudioAgentResourceTarget | null;
  /** Caller values are input to installed backend admission, never self-asserted trust. */
  origin?: StudioAgentContextOrigin;
  client?: StudioAgentClientBinding | null;
  agentScopes?: string[];
}

export interface StudioAgentContributionDiagnostic {
  contributorId: string;
  code: string;
  message: string;
  required: boolean;
}

export interface StudioAgentContextCollection {
  attachments: StudioAgentContextAttachment[];
  diagnostics: StudioAgentContributionDiagnostic[];
}

export interface StudioAgentContextProviderContribution extends StudioBackendAwareContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  order?: number;
  surfaces: string[];
  sensitivity: StudioAgentSensitivity;
  required?: boolean;
  collect(context: StudioAgentContextRequest): Promise<StudioAgentContextCollection | StudioAgentContextAttachment[]>;
}

export interface StudioAgentPromptStarterContribution extends StudioBackendAwareContribution {
  id: string;
  moduleId?: string;
  label: string;
  prompt: string;
  surfaces: string[];
  order?: number;
  requiredCapabilities?: string[];
}

export interface StudioAgentCapabilityContribution extends StudioBackendAwareContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  description: string;
  kind: StudioAgentCapabilityKind;
  risk: StudioAgentRisk;
  surfaces: string[];
  requiredPermissions?: string[];
}

export interface StudioAgentActionContribution extends StudioBackendAwareContribution {
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

export interface StudioAgentToolSlotContribution extends StudioBackendAwareContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  description?: string;
  order?: number;
  surfaces: string[];
  invocationModes?: StudioAgentToolInvocationMode[];
}

export interface StudioAgentToolContractContribution extends StudioBackendAwareContribution {
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
  contractVersion?: string;
  executionLocation?: StudioAgentExecutionLocation;
  /** Invoked only for a matching backend-prepared, explicitly reviewed client request. */
  executeClient?(invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientExecutionResult>;
}

export interface StudioAgentResourceTarget {
  moduleId: string;
  resourceType: string;
  resourceId: string | null;
  revision: string | null;
  displayName?: string;
  route?: string;
  summary?: string;
}

/** Frozen server preparation; callback code must not replace target, arguments or approval digest. */
export interface StudioAgentPreparedInvocation {
  invocationId: string;
  sessionId: string;
  turnId: string;
  toolCallId: string;
  toolName: string;
  toolContractVersion: string;
  moduleId: string;
  actorId: string;
  tenantId: string;
  target: StudioAgentResourceTarget | null;
  arguments: unknown;
  approvalDigest: string;
  risk: StudioAgentPreparedRisk;
  preview: StudioAgentPayload;
  executionLocation: StudioAgentExecutionLocation;
  requiresApproval: boolean;
  client: StudioAgentClientBinding | null;
  expiresAt: string;
}

/** Observed module outcome. Returning from a dispatch call alone does not prove application. */
export interface StudioAgentClientExecutionResult {
  outcome: StudioAgentClientOutcome;
  summary: string;
  after?: StudioAgentResourceTarget | null;
  result?: StudioAgentPayload | null;
}

export interface StudioAgentClientAcknowledgement extends StudioAgentClientExecutionResult {
  client: StudioAgentClientBinding;
  approvalDigest: string;
}

export interface StudioAgentEffectReceipt {
  invocationId: string;
  before: StudioAgentResourceTarget;
  after: StudioAgentResourceTarget;
  scope: StudioAgentEffectScope;
  evidence: StudioAgentPayload;
}

export interface StudioAgentToolResult {
  toolCallId: string;
  toolName: string;
  succeeded: boolean;
  content: string;
  payload?: StudioAgentPayload | null;
  receipt?: StudioAgentEffectReceipt | null;
}

export interface StudioAgentResultRendererProps {
  proposal: unknown;
  resourceTarget?: StudioAgentResourceTarget;
  result: unknown;
  resultType?: string;
  preparedInvocation?: StudioAgentPreparedInvocation;
  toolResult?: StudioAgentToolResult;
}

export interface StudioAgentResultRendererContribution extends StudioBackendAwareContribution {
  id: string;
  moduleId?: string;
  displayName: string;
  order?: number;
  resourceTypes?: string[];
  resultTypes?: string[];
  schemaVersions?: string[];
  component: ComponentType<StudioAgentResultRendererProps>;
  supports?(props: StudioAgentResultRendererProps): boolean;
}

export interface StudioAgentRegistry extends StudioAgentPromptDispatcher {
  /** Stable per registry instance; modules pair it with a private editor generation. */
  readonly clientId: string;
  readonly promptActions: StudioContributionRegistry<StudioAgentPromptActionContribution>;
  readonly providerAvailability: StudioAiProviderAvailability;
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

export interface StudioAgentPromptRequest {
  message: string;
  agent?: string | null;
  mode?: StudioAiPromptMode;
  attachments?: StudioAgentContextAttachment[];
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
export interface StudioAgentPromptResult {
  requestId: string;
  status: "completed" | "cancelled" | "failed";
  /** The assistant's final message text (empty for cancelled/failed turns). */
  text: string;
  /** True when the originating Weaver session is in an autonomous mode (Autopilot), so callers may apply without asking. */
  autoApply?: boolean;
}

export interface StudioAgentPromptActionContribution<TContext = unknown> extends StudioBackendAwareContribution {
  id: string;
  label: string;
  description?: string;
  moduleId?: string;
  order?: number;
  placement: StudioAiPromptPlacement;
  contextKind: string;
  createPrompt(context: TContext): StudioAgentPromptRequest | null;
}

/** Compatibility names for callers migrating to the canonical agent dispatcher. */
export type StudioAiPromptRequest = StudioAgentPromptRequest;
export type StudioAiPromptResult = StudioAgentPromptResult;
export type StudioAiPromptActionContribution<TContext = unknown> = StudioAgentPromptActionContribution<TContext>;

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
  shell: "studio.shell",
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
  workflowTestStimulusEditors: "workflow.test-stimulus.editors",
  settingEditors: "studio.setting-editors",
  agentContextProviders: "studio.weaver.context-providers",
  agentPromptStarters: "studio.weaver.prompt-starters",
  agentPromptActions: "studio.weaver.prompt-actions",
  agentCapabilities: "studio.weaver.capabilities",
  agentActions: "studio.weaver.actions",
  agentToolSlots: "studio.weaver.tool-slots",
  agentToolContracts: "studio.weaver.tool-contracts",
  agentResultRenderers: "studio.weaver.result-renderers",
  workflowDesignerNodeRenderers: "workflow.designer.node-renderers",
  workflowDesignerToolboxItems: "workflow.designer.toolbox-items",
  workflowDesignerPanels: "workflow.designer.panels",
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
  shell: defineStudioSlot({id:studioSlotIds.shell,kind:"shell",title:"Shell",owner:hostSlotOwner()}),
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
  workflowTestStimulusEditors: defineStudioSlot({ id: studioSlotIds.workflowTestStimulusEditors, kind: "workflow-test-stimulus-editor", title: "Workflow Test Run stimulus editors", owner: hostSlotOwner() }),
  settingEditors: defineStudioSlot({ id: studioSlotIds.settingEditors, kind: "setting-editor", title: "Setting editors", owner: hostSlotOwner() }),
  agentContextProviders: defineStudioSlot({ id: studioSlotIds.agentContextProviders, kind: "weaver-context-provider", title: "Weaver context providers", owner: hostSlotOwner() }),
  agentPromptStarters: defineStudioSlot({ id: studioSlotIds.agentPromptStarters, kind: "weaver-prompt-starter", title: "Weaver prompt starters", owner: hostSlotOwner() }),
  agentPromptActions: defineStudioSlot({ id: studioSlotIds.agentPromptActions, kind: "weaver-prompt-action", title: "Weaver prompt actions", owner: hostSlotOwner() }),
  agentCapabilities: defineStudioSlot({ id: studioSlotIds.agentCapabilities, kind: "weaver-capability", title: "Weaver capabilities", owner: hostSlotOwner() }),
  agentActions: defineStudioSlot({ id: studioSlotIds.agentActions, kind: "weaver-action", title: "Weaver actions", owner: hostSlotOwner() }),
  agentToolSlots: defineStudioSlot({ id: studioSlotIds.agentToolSlots, kind: "weaver-tool-slot", title: "Weaver tool slots", owner: hostSlotOwner() }),
  agentToolContracts: defineStudioSlot({ id: studioSlotIds.agentToolContracts, kind: "weaver-tool-contract", title: "Weaver tool contracts", owner: hostSlotOwner() }),
  agentResultRenderers: defineStudioSlot({ id: studioSlotIds.agentResultRenderers, kind: "weaver-result-renderer", title: "Weaver result renderers", owner: hostSlotOwner() }),
  workflowDesignerNodeRenderers: defineStudioSlot({ id: studioSlotIds.workflowDesignerNodeRenderers, kind: "workflow-designer-node-renderer", title: "Workflow designer node renderers", owner: hostSlotOwner() }),
  workflowDesignerToolboxItems: defineStudioSlot({ id: studioSlotIds.workflowDesignerToolboxItems, kind: "workflow-designer-toolbox-item", title: "Workflow designer toolbox items", owner: hostSlotOwner() }),
  workflowDesignerPanels: defineStudioSlot({ id: studioSlotIds.workflowDesignerPanels, kind: "workflow-designer-panel", title: "Workflow designer panels", owner: hostSlotOwner() }),
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

export interface StudioAgentPromptDispatcher {
  dispatchPrompt(request: StudioAgentPromptRequest): void;
  onPrompt(listener: (request: StudioAgentPromptRequest) => void): () => void;
  /** Publishes the completed outcome of a correlated prompt back to interested callers. */
  publishPromptResult(result: StudioAgentPromptResult): void;
  /** Subscribes to correlated prompt results. Returns an unsubscribe function. */
  onPromptResult(listener: (result: StudioAgentPromptResult) => void): () => void;
}

export type StudioAiPromptDispatcher = StudioAgentPromptDispatcher;

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

/** Compatibility facade: contribution registration is exclusively on api.agent. */
export type StudioAiContributionApi = StudioAgentPromptDispatcher;

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
  /** Loaded module ids and capabilities. Home widgets with `requiresFeature` consult this list. */
  enabledFeatures?: readonly string[];
}

export const studioNavigationGuardReleasedEvent = "elsa:studio-navigation-guard-released";

/** Signal that a draft or write guard may have released a queued host lifecycle reset. */
export function notifyStudioNavigationGuardReleased() {
  if (typeof window !== "undefined") window.dispatchEvent(new Event(studioNavigationGuardReleasedEvent));
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
  inputs?: Array<Record<string, unknown> & {
    referenceKey: string;
    name: string;
    displayName?: string | null;
    description?: string | null;
    type?: Record<string, unknown>;
  }>;
  outputs?: Array<Record<string, unknown> & {
    referenceKey: string;
    name: string;
    displayName?: string | null;
    description?: string | null;
    type?: Record<string, unknown>;
    isRequired?: boolean;
  }>;
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
  propertyEditors?: StudioActivityPropertyEditorContribution[];
  expressionEditors?: StudioExpressionEditorContribution[];
  graphAuthoringPanels?: StudioWorkflowDesignerPanelContribution[];
  historyResetKey?: string;
  value: StudioActivityDefinitionImplementationState;
  readOnly: boolean;
  onChange(value: StudioActivityDefinitionImplementationState): void;
}

export interface StudioActivityDefinitionImplementationEditorContribution extends StudioBackendAwareContribution {
  id: string;
  providerKey: string;
  providerSchemaVersion: string;
  createInitialImplementation(): StudioActivityDefinitionImplementationState;
  focusDiagnosticLocation?(
    request: StudioActivityDiagnosticFocusRequest
  ): StudioActivityDiagnosticFocusResult | Promise<StudioActivityDiagnosticFocusResult>;
  component: ComponentType<StudioActivityDefinitionImplementationEditorProps>;
  /** Optional provider-owned boundary mappings shown beside the public contract, not on the graph canvas. */
  publicInterfaceComponent?: ComponentType<StudioActivityDefinitionImplementationEditorProps>;
}

export interface ElsaStudioModuleApi {
  readonly connection?: StudioConnectionController;
  readonly shell?: StudioContributionRegistry<StudioShellContribution>;
  readonly backendSession?: StudioBackendSession;
  readonly hostSession?: AuthSession;
  readonly host: ElsaStudioHostContext;
  readonly backend: ElsaStudioBackendContext;
  readonly runtime: StudioRuntimeSettings;
  readonly featureAreas: StudioContributionRegistry<StudioFeatureAreaContribution>;
  readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
  readonly routes: StudioContributionRegistry<StudioRouteContribution>;
  readonly dashboardWidgets: StudioDashboardWidgetRegistry;
  /** Alias of {@link dashboardWidgets}. Modules should prefer {@link registerHomeWidget}. */
  readonly homeWidgets: StudioDashboardWidgetRegistry;
  readonly diagnosticsWidgets: StudioContributionRegistry<StudioDiagnosticsWidgetContribution>;
  readonly panels: StudioContributionRegistry<StudioPanelContribution>;
  readonly toolbarActions: StudioContributionRegistry<unknown>;
  readonly activityEditors: StudioContributionRegistry<StudioActivityDefinitionImplementationEditorContribution>;
  readonly propertyEditors: StudioContributionRegistry<StudioActivityPropertyEditorContribution>;
  readonly expressionEditors: StudioContributionRegistry<StudioExpressionEditorContribution>;
  /** Available on hosts that support the workflow run-input editor Slot. */
  readonly workflowRunInputEditors?: StudioContributionRegistry<StudioWorkflowRunInputEditorContribution>;
  /** Available on hosts that support module-owned workflow Test Run stimulus editors. */
  readonly workflowTestStimulusEditors?: StudioContributionRegistry<StudioTestStimulusEditorContribution>;
  readonly settingEditors: StudioContributionRegistry<StudioSettingEditorContribution>;
  readonly agent: StudioAgentRegistry;
  readonly workflowDesigner: {
    readonly nodeRenderers: StudioContributionRegistry<unknown>;
    readonly toolboxItems: StudioContributionRegistry<unknown>;
    readonly panels: StudioContributionRegistry<StudioWorkflowDesignerPanelContribution>;
    /** Optional host alias of the standalone `registerActivityIcons` export. Callers must guard when it is missing. */
    registerActivityIcons?: (icons: Record<string, StudioActivityIconComponent | null | undefined>) => void;
  };
  readonly ai: StudioAiContributionApi;
  readonly dialogs: StudioDialogApi;
  readonly diagnostics: StudioContributionRegistry<StudioModuleDiagnostic>;
}

export type ElsaStudioModule = {register(api: ElsaStudioModuleApi): void | Promise<void>}
  | {initialize(host: StudioModuleHostContext): StudioModuleInstance | Promise<StudioModuleInstance>};

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

export function createDashboardWidgetRegistry(options: Pick<CreateStudioContributionRegistryOptions<unknown>, "hostPolicy"> = {}): StudioDashboardWidgetRegistry {
  // Widget snapshot/settings types are contribution-local. The registry stores them opaquely and the
  // Dashboard host invokes each contribution through its own paired loader/body contract.
  return createContributionRegistry<StudioDashboardWidgetContribution>({ ...options, slot: studioSlots.dashboardWidgets }) as unknown as StudioDashboardWidgetRegistry;
}

/** Shared prompt transport while contribution producers migrate from the legacy AI facade. */
export function createAiContributionApi(): StudioAiContributionApi {
  const listeners = new Set<(request: StudioAgentPromptRequest) => void>();
  const results = new Set<(result: StudioAgentPromptResult) => void>();
  return {
    dispatchPrompt(request) { for (const listener of listeners) listener(request); },
    onPrompt(listener) { listeners.add(listener); return () => listeners.delete(listener); },
    publishPromptResult(result) { for (const listener of results) listener(result); },
    onPromptResult(listener) { results.add(listener); return () => results.delete(listener); }
  };
}

export function createAgentRegistry(options: Pick<CreateStudioContributionRegistryOptions<unknown>, "hostPolicy"> = {}): StudioAgentRegistry {
  const prompts = createAiContributionApi();
  const listeners = new Set<(available: boolean) => void>();
  let available = true;
  return {
    ...prompts,
    clientId: globalThis.crypto?.randomUUID?.() ?? `studio-${Date.now()}-${Math.random().toString(36).slice(2)}`,
    providerAvailability: {
      get: () => available,
      set(value) { if (value !== available) { available = value; for (const listener of listeners) listener(value); } },
      subscribe(listener) { listeners.add(listener); listener(available); return () => listeners.delete(listener); }
    },
    contextProviders: createContributionRegistry<StudioAgentContextProviderContribution>({ ...options, slot: studioSlots.agentContextProviders }),
    promptActions: createContributionRegistry<StudioAgentPromptActionContribution>({ ...options, slot: studioSlots.agentPromptActions }),
    promptStarters: createContributionRegistry<StudioAgentPromptStarterContribution>({ ...options, slot: studioSlots.agentPromptStarters }),
    capabilities: createContributionRegistry<StudioAgentCapabilityContribution>({ ...options, slot: studioSlots.agentCapabilities }),
    actions: createContributionRegistry<StudioAgentActionContribution>({ ...options, slot: studioSlots.agentActions }),
    toolSlots: createContributionRegistry<StudioAgentToolSlotContribution>({ ...options, slot: studioSlots.agentToolSlots }),
    toolContracts: createContributionRegistry<StudioAgentToolContractContribution>({ ...options, slot: studioSlots.agentToolContracts }),
    resultRenderers: createContributionRegistry<StudioAgentResultRendererContribution>({ ...options, slot: studioSlots.agentResultRenderers })
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
    async requestResponse(url: string, init?: StudioRequestInit) {
      const { response } = await sendRequest(baseUrl, url, withDefaultHeaders(defaultHeaders, init), options);
      return response;
    },
    requestJson<T>(url: string, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, withJsonAccept(init)));
    },
    async getJson<T>(url: string, init?: StudioRequestInit) {
      return send<T>(url, withDefaultHeaders(defaultHeaders, withJsonAccept(init)));
    },
    async getJsonWithHeaders<T>(url: string, init?: StudioRequestInit) {
      return requestJsonWithHeaders<T>(baseUrl, url, withDefaultHeaders(defaultHeaders, withJsonAccept(init)), options);
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
  const { requestUrl, response } = await sendRequest(baseUrl, url, init, options);
  return parseJsonResponse<T>(requestUrl, response);
}

async function requestJsonWithHeaders<T>(
  baseUrl: string,
  url: string,
  init?: StudioRequestInit,
  options: StudioHttpClientOptions = {}
): Promise<StudioJsonResponse<T>> {
  const { requestUrl, response } = await sendRequest(baseUrl, url, init, options);
  return {
    value: await parseJsonResponse<T>(requestUrl, response),
    status: response.status,
    headers: response.headers
  };
}

async function sendRequest(
  baseUrl: string,
  url: string,
  init?: StudioRequestInit,
  options: StudioHttpClientOptions = {}
): Promise<{ requestUrl: string; response: Response }> {
  const requestUrl = resolveStudioUrl(baseUrl, url);
  const request = options.fetch ?? fetch;
  // applyTimeout: false (the authenticated client) opts out of the implicit default timeout only; an explicit
  // per-request timeoutMs is a caller contract and is honored regardless of the client's default-timeout policy.
  const response = options.applyTimeout === false && init?.timeoutMs === undefined
    ? await request(requestUrl, init)
    : await fetchWithTimeout(request, requestUrl, init);

  return { requestUrl, response };
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

  if (typeof payload.error === "string" && payload.error.trim().length > 0) return payload.error;
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
