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

export interface StudioHttpClient {
  getJson<T>(url: string, init?: RequestInit): Promise<T>;
  postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
}

export interface StudioEndpointContext {
  baseUrl: string;
  headers?: HeadersInit;
  http: StudioHttpClient;
}

export interface StudioRouteContribution {
  id: string;
  path: string;
  label: string;
  component: ComponentType;
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

export interface StudioDashboardWidgetContribution {
  id: string;
  title: string;
  order?: number;
  component: ComponentType;
}

export interface StudioPanelContribution {
  id: string;
  title: string;
  order?: number;
  component: ComponentType;
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

export interface StudioContributionRegistry<T> {
  add(contribution: T): void;
  list(): T[];
}

export interface StudioAiPromptDispatcher {
  dispatchPrompt(request: StudioAiPromptRequest): void;
  onPrompt(listener: (request: StudioAiPromptRequest) => void): () => void;
}

export interface StudioAiContributionApi extends StudioAiPromptDispatcher {
  readonly contextProviders: StudioContributionRegistry<StudioAiContextProviderContribution>;
  readonly promptActions: StudioContributionRegistry<StudioAiPromptActionContribution>;
  readonly tools: StudioContributionRegistry<StudioAiToolContribution>;
  readonly proposalRenderers: StudioContributionRegistry<StudioAiProposalRendererContribution>;
  readonly surfaces: StudioContributionRegistry<StudioAiSurfaceContribution>;
}

export interface ElsaStudioHostContext extends StudioEndpointContext {
  hostVersion: string;
  sdkVersion: string;
}

export interface ElsaStudioBackendContext extends StudioEndpointContext {
}

export interface ElsaStudioModuleApi {
  readonly host: ElsaStudioHostContext;
  readonly backend: ElsaStudioBackendContext;
  readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
  readonly routes: StudioContributionRegistry<StudioRouteContribution>;
  readonly dashboardWidgets: StudioContributionRegistry<StudioDashboardWidgetContribution>;
  readonly panels: StudioContributionRegistry<StudioPanelContribution>;
  readonly toolbarActions: StudioContributionRegistry<unknown>;
  readonly activityEditors: StudioContributionRegistry<unknown>;
  readonly propertyEditors: StudioContributionRegistry<unknown>;
  readonly settingEditors: StudioContributionRegistry<StudioSettingEditorContribution>;
  readonly workflowDesigner: {
    readonly nodeRenderers: StudioContributionRegistry<unknown>;
    readonly toolboxItems: StudioContributionRegistry<unknown>;
  };
  readonly ai: StudioAiContributionApi;
  readonly diagnostics: StudioContributionRegistry<StudioModuleDiagnostic>;
}

export interface ElsaStudioModule {
  register(api: ElsaStudioModuleApi): void | Promise<void>;
}

export function createContributionRegistry<T>(): StudioContributionRegistry<T> {
  const contributions: T[] = [];

  return {
    add(contribution) {
      contributions.push(contribution);
    },
    list() {
      return [...contributions];
    }
  };
}

export function createAiContributionApi(): StudioAiContributionApi {
  const listeners = new Set<(request: StudioAiPromptRequest) => void>();

  return {
    contextProviders: createContributionRegistry(),
    promptActions: createContributionRegistry(),
    tools: createContributionRegistry(),
    proposalRenderers: createContributionRegistry(),
    surfaces: createContributionRegistry(),
    dispatchPrompt(request) {
      for (const listener of listeners) {
        listener(request);
      }
    },
    onPrompt(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  };
}

export function createEndpointContext(baseUrl: string, options: { headers?: HeadersInit } = {}): StudioEndpointContext {
  return {
    baseUrl,
    headers: options.headers,
    http: createHttpClient(baseUrl, options.headers)
  };
}

export function createHttpClient(baseUrl: string, defaultHeaders?: HeadersInit): StudioHttpClient {
  return {
    async getJson<T>(url: string, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, withDefaultHeaders(defaultHeaders, withJsonAccept(init)));
    },
    async postJson<T>(url: string, body: unknown, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, withDefaultHeaders(defaultHeaders, {
        ...init,
        method: "POST",
        headers: withJsonContentTypeAndAccept(init?.headers),
        body: JSON.stringify(body)
      }));
    }
  };
}

export function withDefaultHeaders(defaultHeaders: HeadersInit | undefined, init: RequestInit = {}): RequestInit {
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

async function requestJson<T>(baseUrl: string, url: string, init?: RequestInit) {
  const requestUrl = resolveStudioUrl(baseUrl, url);
  const response = await fetch(requestUrl, init);
  if (!response.ok) {
    throw new StudioHttpError(response.status, await readStudioHttpErrorMessage(response));
  }

  const text = await response.text();
  try {
    return JSON.parse(text) as T;
  } catch {
    throw new StudioHttpError(
      response.status,
      `Expected JSON from ${requestUrl}, but received ${describeResponseContent(response, text)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`);
  }
}

export async function readStudioHttpErrorMessage(response: Response) {
  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("application/json") || contentType.includes("+json")) {
    try {
      const payload = await response.json() as Record<string, unknown>;
      return getProblemDetailsMessage(payload) ?? `Request failed with ${response.status}.`;
    } catch {
      return `Request failed with ${response.status}.`;
    }
  }

  const text = await response.text();
  return text.trim() || `Request failed with ${response.status}.`;
}

function getProblemDetailsMessage(payload: Record<string, unknown>) {
  if (typeof payload.detail === "string" && payload.detail.length > 0) return payload.detail;
  if (typeof payload.title === "string" && payload.title.length > 0) return payload.title;
  if (Array.isArray(payload.errors) && payload.errors.length > 0) return payload.errors.map(String).join(" ");
  if (payload.errors && typeof payload.errors === "object") {
    const messages = Object.values(payload.errors as Record<string, unknown>)
      .flatMap(value => Array.isArray(value) ? value : [value])
      .map(String);
    if (messages.length > 0) return messages.join(" ");
  }

  return null;
}

function resolveStudioUrl(baseUrl: string, url: string) {
  return new URL(url, baseUrl).toString();
}

function withJsonAccept(init?: RequestInit): RequestInit | undefined {
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
    message: string
  ) {
    super(message);
    this.name = "StudioHttpError";
  }
}
