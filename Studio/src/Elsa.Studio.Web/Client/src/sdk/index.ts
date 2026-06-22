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

export type StudioValidationErrors = Record<string, string[]>;

export interface StudioHttpClient {
  requestJson<T>(url: string, init?: RequestInit): Promise<T>;
  getJson<T>(url: string, init?: RequestInit): Promise<T>;
  postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
  putJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
  deleteJson<T>(url: string, init?: RequestInit): Promise<T>;
  postForm<T>(url: string, body: FormData, init?: RequestInit): Promise<T>;
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
  component: ComponentType;
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

export interface StudioContributionRegistry<T> {
  add(contribution: T): void;
  list(): T[];
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
  readonly featureAreas: StudioContributionRegistry<StudioFeatureAreaContribution>;
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

export function createEndpointContext(baseUrl: string, options: { headers?: HeadersInit } = {}): StudioEndpointContext {
  return {
    baseUrl,
    headers: options.headers,
    http: createHttpClient(baseUrl, options.headers)
  };
}

export function createHttpClient(baseUrl: string, defaultHeaders?: HeadersInit): StudioHttpClient {
  return {
    requestJson<T>(url: string, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, withDefaultHeaders(defaultHeaders, withJsonAccept(init)));
    },
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
    },
    async putJson<T>(url: string, body: unknown, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, withDefaultHeaders(defaultHeaders, {
        ...init,
        method: "PUT",
        headers: withJsonContentTypeAndAccept(init?.headers),
        body: JSON.stringify(body)
      }));
    },
    async deleteJson<T>(url: string, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, withDefaultHeaders(defaultHeaders, withJsonAccept({
        ...init,
        method: "DELETE"
      })));
    },
    async postForm<T>(url: string, body: FormData, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, withDefaultHeaders(defaultHeaders, withJsonAccept({
        ...init,
        method: "POST",
        body
      })));
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

async function readErrorMessage(response: Response) {
  return (await readError(response)).message;
}

export async function createStudioHttpError(response: Response) {
  const error = await readError(response);
  return new StudioHttpError(response.status, error.message, error.validationErrors);
}

async function readError(response: Response): Promise<{ message: string; validationErrors: StudioValidationErrors | null }> {
  const contentType = response.headers.get("content-type") ?? "";
  if (isJsonContentType(contentType)) {
    try {
      const payload = await response.json() as Record<string, unknown>;
      const validationErrors = extractValidationErrors(payload);
      return {
        message: getProblemDetailsMessage(payload) ?? getValidationErrorMessage(validationErrors) ?? `Request failed with ${response.status}.`,
        validationErrors
      };
    } catch {
      return { message: `Request failed with ${response.status}.`, validationErrors: null };
    }
  }

  const text = await response.text();
  return { message: text.trim() || `Request failed with ${response.status}.`, validationErrors: null };
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
      return await readErrorMessage(error.response.clone());
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
    message: string,
    public readonly validationErrors: StudioValidationErrors | null = null
  ) {
    super(message);
    this.name = "StudioHttpError";
  }
}
