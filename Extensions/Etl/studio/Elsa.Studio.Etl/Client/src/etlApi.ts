import { isLosslessNumber, isSafeNumber, parse, stringify } from "lossless-json";
import { StudioHttpError, type StudioEndpointContext, type StudioHttpClient, type StudioRequestInit } from "@elsa-workflows/studio-sdk";
import type {
  EtlApiCapability,
  EtlApiCapabilityDocument,
  EtlCreateDefinitionRequest,
  EtlConnectorBindingSummary,
  EtlConnectorCreateDefinitionRequest,
  EtlConnectorDraftView,
  EtlConnectorPublishedArtifactView,
  EtlConnectorUpdateDraftRequest,
  EtlDraftView,
  EtlPage,
  EtlPageRequest,
  EtlBindingSummary,
  EtlDefinitionSummaryView,
  EtlPublishedArtifactView,
  EtlPublishedVersionView,
  EtlPublicationView,
  EtlPublishDefinitionRequest,
  EtlRunCancellationView,
  EtlRunDetailView,
  EtlRunConnectorPartitionView,
  EtlRunPageRequest,
  EtlRunPartitionView,
  EtlRunSummaryView,
  EtlUpdateDraftRequest,
  EtlValidateDraftRequest,
  EtlValidationView
} from "./etlTypes";

export type EtlApiArea = "design" | "design-v2" | "runtime";
export type EtlApiFailureKind = "unavailable" | "forbidden" | "not-found" | "error";

const int64LimitKeys = new Set([
  "maxBatchBytes", "maxOutstandingRecords", "maxOutstandingBytes", "maxLookupRecords", "maxLookupBytes",
  "maxOutputBatchBytes", "maxRetainedOutputBytes", "maxSnapshotBytes", "maxPageBytes", "maxRetainedInputBytes",
  "maxRetainedMetadataBytes"
]);

const capabilityIds: Record<EtlApiArea, string> = {
  design: "elsa.api.etl-design",
  "design-v2": "elsa.api.etl-design-v2",
  runtime: "elsa.api.etl-runtime"
};

const requiredRelations: Record<EtlApiArea, readonly string[]> = {
  design: ["etl-bindings", "etl-definitions", "etl-definition", "etl-draft", "etl-validation", "etl-publication", "etl-versions", "etl-version"],
  "design-v2": ["etl-v2-bindings", "etl-v2-definitions", "etl-v2-definition", "etl-v2-draft", "etl-v2-validation", "etl-v2-publication", "etl-v2-versions", "etl-v2-version"],
  runtime: ["etl-runs", "etl-run", "etl-run-partitions", "etl-run-cancel"]
};

let capabilityDocuments = new WeakMap<StudioHttpClient, Map<string, Promise<EtlApiCapabilityDocument>>>();

/** Status and stable problem code from an ETL HTTP response, without forwarding server exception text. */
export class EtlApiError extends Error {
  constructor(
    message: string,
    public readonly status: number | null = null,
    public readonly code: string | null = null
  ) {
    super(message);
    this.name = "EtlApiError";
  }
}

export function classifyEtlApiError(error: unknown): EtlApiFailureKind {
  if (!(error instanceof EtlApiError)) return "error";
  if (error.code === "etl.capability-unavailable" || error.code === "etl.capability-version-unsupported" || error.status === 503 || error.status === null)
    return "unavailable";
  if (error.status === 401 || error.status === 403) return "forbidden";
  if (error.status === 404) return "not-found";
  return "error";
}

/** Loads and validates one ETL capability, including every relation in its advertised contract. */
export async function getEtlApiCapability(context: StudioEndpointContext, area: EtlApiArea): Promise<EtlApiCapability> {
  const capability = await findEtlCapability(context, area);
  if (capability.contractVersion !== "1") {
    throw new EtlApiError("This ETL API contract version is not supported by this Studio module.", null, "etl.capability-version-unsupported");
  }
  const relations = new Set(capability.links.map(link => link.rel));
  const missing = requiredRelations[area].find(relation => !relations.has(relation));
  if (missing) {
    throw new EtlApiError(`The ETL ${area} API does not advertise a required operation.`, null, "etl.capability-unavailable");
  }
  return capability;
}

/** Clears the shell-scoped capability cache after the host changes its advertised APIs. */
export function invalidateEtlApiCapabilityCache(context: StudioEndpointContext): void {
  capabilityDocuments.get(context.http)?.delete(context.baseUrl.replace(/\/+$/, ""));
}

/** Test seam for isolating cached capability documents between host contexts. */
export function clearEtlApiCapabilityCache(): void {
  capabilityDocuments = new WeakMap();
}

export async function listEtlBindings(context: StudioEndpointContext, request: EtlPageRequest = {}, signal?: AbortSignal) {
  const url = await pageUrl(context, "design", "etl-bindings", request);
  return getJson<EtlPage<EtlBindingSummary>>(context, url, signal);
}

export async function listEtlDefinitions(context: StudioEndpointContext, request: EtlPageRequest = {}, signal?: AbortSignal) {
  const url = await pageUrl(context, "design", "etl-definitions", request);
  return getJson<EtlPage<EtlDefinitionSummaryView>>(context, url, signal);
}

export async function getEtlDefinition(context: StudioEndpointContext, definitionId: string, signal?: AbortSignal) {
  const url = await resourceUrl(context, "design", "etl-definition", { id: definitionId });
  return losslessJsonRequest<EtlDraftView>(context, url, { method: "GET", signal });
}

export async function listEtlConnectorBindings(context: StudioEndpointContext, request: EtlPageRequest = {}, signal?: AbortSignal) {
  const url = await pageUrl(context, "design-v2", "etl-v2-bindings", request);
  return getJson<EtlPage<EtlConnectorBindingSummary>>(context, url, signal);
}

export async function listEtlConnectorDefinitions(context: StudioEndpointContext, request: EtlPageRequest = {}, signal?: AbortSignal) {
  const url = await pageUrl(context, "design-v2", "etl-v2-definitions", request);
  return getJson<EtlPage<EtlDefinitionSummaryView>>(context, url, signal);
}

export async function getEtlConnectorDefinition(context: StudioEndpointContext, definitionId: string, signal?: AbortSignal) {
  const url = await resourceUrl(context, "design-v2", "etl-v2-definition", { id: definitionId });
  return losslessJsonRequest<EtlConnectorDraftView>(context, url, { method: "GET", signal });
}

export async function createEtlDefinition(context: StudioEndpointContext, request: EtlCreateDefinitionRequest, signal?: AbortSignal) {
  const url = await resourceUrl(context, "design", "etl-definitions");
  return losslessJsonRequest<EtlDraftView>(context, url, { method: "POST", body: request, signal });
}

export async function createEtlConnectorDefinition(
  context: StudioEndpointContext,
  request: EtlConnectorCreateDefinitionRequest,
  signal?: AbortSignal
) {
  const url = await resourceUrl(context, "design-v2", "etl-v2-definitions");
  return losslessJsonRequest<EtlConnectorDraftView>(context, url, { method: "POST", body: request, signal });
}

export async function updateEtlDraft(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlUpdateDraftRequest,
  signal?: AbortSignal
) {
  const url = await resourceUrl(context, "design", "etl-draft", { id: definitionId });
  return losslessJsonRequest<EtlDraftView>(context, url, { method: "PUT", body: request, signal });
}

export async function updateEtlConnectorDraft(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlConnectorUpdateDraftRequest,
  signal?: AbortSignal
) {
  const url = await resourceUrl(context, "design-v2", "etl-v2-draft", { id: definitionId });
  return losslessJsonRequest<EtlConnectorDraftView>(context, url, { method: "PUT", body: request, signal });
}

export async function validateEtlDraft(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlValidateDraftRequest,
  signal?: AbortSignal
) {
  const url = await resourceUrl(context, "design", "etl-validation", { id: definitionId });
  return postJson<EtlValidationView>(context, url, request, signal);
}

export async function validateEtlConnectorDraft(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlValidateDraftRequest,
  signal?: AbortSignal
) {
  const url = await resourceUrl(context, "design-v2", "etl-v2-validation", { id: definitionId });
  return postJson<EtlValidationView>(context, url, request, signal);
}

export async function publishEtlDefinition(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlPublishDefinitionRequest,
  signal?: AbortSignal
) {
  const url = await resourceUrl(context, "design", "etl-publication", { id: definitionId });
  return postJson<EtlPublicationView>(context, url, request, signal);
}

export async function publishEtlConnectorDefinition(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlPublishDefinitionRequest,
  signal?: AbortSignal
) {
  const url = await resourceUrl(context, "design-v2", "etl-v2-publication", { id: definitionId });
  return postJson<EtlPublicationView>(context, url, request, signal);
}

export async function listEtlVersions(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlPageRequest = {},
  signal?: AbortSignal
) {
  const url = await pageUrl(context, "design", "etl-versions", request, { id: definitionId });
  return getJson<EtlPage<EtlPublishedVersionView>>(context, url, signal);
}

export async function listEtlConnectorVersions(
  context: StudioEndpointContext,
  definitionId: string,
  request: EtlPageRequest = {},
  signal?: AbortSignal
) {
  const url = await pageUrl(context, "design-v2", "etl-v2-versions", request, { id: definitionId });
  return getJson<EtlPage<EtlPublishedVersionView>>(context, url, signal);
}

export async function getEtlVersion(context: StudioEndpointContext, definitionId: string, version: number, signal?: AbortSignal) {
  if (!Number.isSafeInteger(version) || version <= 0) throw new TypeError("The ETL version must be a positive safe integer.");
  const url = await resourceUrl(context, "design", "etl-version", { id: definitionId, version: String(version) });
  return losslessJsonRequest<EtlPublishedArtifactView>(context, url, { method: "GET", signal });
}

export async function getEtlConnectorVersion(context: StudioEndpointContext, definitionId: string, version: number, signal?: AbortSignal) {
  if (!Number.isSafeInteger(version) || version <= 0) throw new TypeError("The ETL version must be a positive safe integer.");
  const url = await resourceUrl(context, "design-v2", "etl-v2-version", { id: definitionId, version: String(version) });
  return losslessJsonRequest<EtlConnectorPublishedArtifactView>(context, url, { method: "GET", signal });
}

export async function listEtlRuns(context: StudioEndpointContext, request: EtlRunPageRequest = {}, signal?: AbortSignal) {
  const url = await pageUrl(context, "runtime", "etl-runs", request);
  return getJson<EtlPage<EtlRunSummaryView>>(context, url, signal);
}

export async function getEtlRun(context: StudioEndpointContext, runId: string, signal?: AbortSignal) {
  const url = await resourceUrl(context, "runtime", "etl-run", { id: runId });
  return getJson<EtlRunDetailView>(context, url, signal);
}

export async function listEtlRunPartitions(
  context: StudioEndpointContext,
  runId: string,
  request: EtlRunPageRequest = {},
  signal?: AbortSignal
) {
  const url = await pageUrl(context, "runtime", "etl-run-partitions", request, { id: runId });
  return getJson<EtlPage<EtlRunPartitionView>>(context, url, signal);
}

/** Connector progress has a separate advertised relation so older runtime hosts keep file-run compatibility. */
export async function listEtlRunConnectorPartitions(
  context: StudioEndpointContext,
  runId: string,
  request: EtlRunPageRequest = {},
  signal?: AbortSignal
) {
  const url = await pageUrl(context, "runtime", "etl-run-connector-partitions", request, { id: runId });
  return getJson<EtlPage<EtlRunConnectorPartitionView>>(context, url, signal);
}

/** Cancellation is intentionally bodyless; the API resolves the trusted persisted invocation itself. */
export async function cancelEtlRun(context: StudioEndpointContext, runId: string, signal?: AbortSignal) {
  const url = await resourceUrl(context, "runtime", "etl-run-cancel", { id: runId });
  return requestJson<EtlRunCancellationView>(context, url, { method: "POST", signal });
}

async function findEtlCapability(context: StudioEndpointContext, area: EtlApiArea): Promise<EtlApiCapability> {
  const document = await getCapabilities(context);
  const capabilityId = capabilityIds[area];
  const capability = document.capabilities.find(item => item.id === capabilityId);
  if (!capability) {
    throw new EtlApiError(`The ETL ${area} API is not available on this host.`, null, "etl.capability-unavailable");
  }
  return capability;
}

function getCapabilities(context: StudioEndpointContext): Promise<EtlApiCapabilityDocument> {
  const key = context.baseUrl.replace(/\/+$/, "");
  const clientDocuments = capabilityDocuments.get(context.http) ?? new Map<string, Promise<EtlApiCapabilityDocument>>();
  capabilityDocuments.set(context.http, clientDocuments);

  const cached = clientDocuments.get(key);
  if (cached) return cached;

  const request = context.http.getJson<EtlApiCapabilityDocument>(capabilityDocumentUrl(context))
    .then(document => ({ capabilities: Array.isArray(document?.capabilities) ? document.capabilities : [] }))
    .catch(error => {
      clientDocuments.delete(key);
      throw normalizeError(error);
    });
  clientDocuments.set(key, request);
  return request;
}

async function pageUrl(
  context: StudioEndpointContext,
  area: EtlApiArea,
  relation: string,
  request: EtlPageRequest,
  parameters: Record<string, string> = {}
): Promise<string> {
  const limit = request.limit ?? 50;
  if (!Number.isInteger(limit) || limit < 1 || limit > 200) {
    throw new EtlApiError("ETL page size must be between 1 and 200.", 400, "etl.page-limit-invalid");
  }
  const url = await resourceUrl(context, area, relation, parameters);
  const query = new URLSearchParams();
  query.set("limit", String(limit));
  if (request.cursor != null) query.set("cursor", request.cursor);
  return `${url}?${query.toString()}`;
}

async function resourceUrl(
  context: StudioEndpointContext,
  area: EtlApiArea,
  relation: string,
  parameters: Record<string, string> = {}
): Promise<string> {
  const capability = await getEtlApiCapability(context, area);
  const link = capability.links.find(candidate => candidate.rel === relation);
  if (!link) {
    throw new EtlApiError(`The ETL ${area} API does not advertise this operation.`, null, "etl.capability-unavailable");
  }
  let path = link.href;
  for (const [name, value] of Object.entries(parameters))
    path = path.replaceAll(`{${name}}`, encodeURIComponent(value));

  if (!path || path.startsWith("/") || path.includes("\\") || path.includes("?") || path.includes("#") || /^[a-z][a-z\d+.-]*:/i.test(path) || /\{[^}]+\}/.test(path)) {
    throw new EtlApiError("The ETL API advertised an invalid route.", null, "etl.capability-unavailable");
  }
  const segments = path.split("/");
  if (segments.some(segment => segment === "." || segment === "..")) {
    throw new EtlApiError("The ETL API advertised an invalid route.", null, "etl.capability-unavailable");
  }
  return `${shellPathPrefix(context)}/${path.replace(/^\/+/, "")}`;
}

function capabilityDocumentUrl(context: StudioEndpointContext): string {
  return `${shellPathPrefix(context)}/capabilities`;
}

function shellPathPrefix(context: StudioEndpointContext): string {
  try {
    const base = new URL(context.baseUrl, "http://studio.invalid");
    return base.pathname.replace(/\/+$/, "");
  } catch {
    return "";
  }
}

async function getJson<T>(context: StudioEndpointContext, url: string, signal?: AbortSignal): Promise<T> {
  return normalizeRequest(() => context.http.getJson<T>(url, { signal }));
}

async function postJson<T>(context: StudioEndpointContext, url: string, body: unknown, signal?: AbortSignal): Promise<T> {
  return normalizeRequest(() => context.http.postJson<T>(url, body, { signal }));
}

async function requestJson<T>(context: StudioEndpointContext, url: string, init: StudioRequestInit): Promise<T> {
  return normalizeRequest(() => context.http.requestJson<T>(url, init));
}

interface LosslessJsonRequestInit {
  method: "GET" | "POST" | "PUT";
  body?: unknown;
  signal?: AbortSignal;
}

async function losslessJsonRequest<T>(context: StudioEndpointContext, url: string, init: LosslessJsonRequestInit): Promise<T> {
  const send = context.http.requestResponse;
  if (!send) {
    throw new EtlApiError("This Studio transport cannot preserve ETL numeric literals for editing.", null, "etl.lossless-transport-unavailable");
  }

  let body: string | undefined;
  if (init.body !== undefined) {
    try {
      body = stringify(init.body);
      if (body === undefined) throw new Error("Missing JSON request body.");
    } catch {
      throw new EtlApiError("The ETL request contains a value that cannot be encoded as JSON.", 400, "etl.request-invalid");
    }
  }
  const headers = new Headers({ Accept: "application/json" });
  if (body !== undefined) headers.set("Content-Type", "application/json");

  return normalizeRequest(async () => {
    const response = await send.call(context.http, url, { method: init.method, headers, body, signal: init.signal });
    const text = await response.text();
    if (!response.ok) throw responseError(response.status, text);
    if (!text.trim()) return {} as T;

    try {
      return parse(text, (_key, value) => {
        if (!isLosslessNumber(value)) return value;
        // Filter `value` is the JSON scalar in the ETL contract. Keep every numeric spelling exact;
        // convert bounded DTO counters, revisions and limits only after proving they are JS-safe.
        if (_key === "value" || int64LimitKeys.has(_key) && !isSafeNumber(value.toString())) return value;
        if (!isSafeNumber(value.toString())) throw new Error("Unsafe ETL metadata number.");
        return Number(value.toString());
      }) as T;
    } catch {
      throw new EtlApiError("The ETL API returned invalid or unsafe JSON.", response.status, "etl.response-invalid");
    }
  });
}

async function normalizeRequest<T>(request: () => Promise<T>): Promise<T> {
  try {
    return await request();
  } catch (error) {
    if (isAbortError(error) || error instanceof EtlApiError) throw error;
    throw normalizeError(error);
  }
}

function normalizeError(error: unknown): EtlApiError {
  if (error instanceof EtlApiError) return error;
  if (error instanceof StudioHttpError) {
    const code = readProblemCode(error.payload);
    return new EtlApiError("The ETL request failed.", error.status, code);
  }
  return new EtlApiError("The ETL service could not be reached.", null, null);
}

function responseError(status: number, text: string): EtlApiError {
  let code: string | null = null;
  try { code = readProblemCode(JSON.parse(text)); } catch { /* A response without a valid problem code remains status-typed. */ }
  return new EtlApiError("The ETL request failed.", status, code);
}

function readProblemCode(value: unknown): string | null {
  if (!value || typeof value !== "object") return null;
  const code = (value as { code?: unknown }).code;
  return typeof code === "string" && code.length > 0 ? code : null;
}

function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === "AbortError";
}
