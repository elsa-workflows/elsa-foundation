import type {
  WorkflowExecutableCompiledInput,
  WorkflowExecutableDetails,
  WorkflowExecutableInputSources,
  WorkflowExecutableNode
} from "../workflowTypes";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveRelativeCapabilityUrl } from "../api/capabilities";
import { assertSafePathSegments } from "../api/pathSegments";

export const httpEndpointActivityType = "Elsa.Activities.Http.Activities.HttpEndpoint";
const defaultMethods = ["GET"];
export const publishedHttpEndpointInputsUnavailableMessage =
  "Studio could not read the published endpoint settings. Check your permission to inspect published workflow inputs, then try again.";
export const workflowHttpBasePathCapabilityRelation = "workflow-http-base";
export const workflowHttpBasePathUnavailableMessage =
  "Studio could not determine the server's configured workflow HTTP base path. Check the server capability and try again.";

export interface PublishedHttpEndpoint {
  authoredActivityId: string;
  routeTemplate: string;
  supportedMethods: string[];
  responseMode: "async" | "sync" | null;
}

/** Reads the authoritative HTTP mount advertised by the active shell, preserving any backend URL path prefix. */
export async function getWorkflowHttpBasePath(context: StudioEndpointContext): Promise<string> {
  try {
    const url = await resolveRelativeCapabilityUrl(
      context,
      capabilityIds.workflowHttpEndpoints,
      workflowHttpBasePathCapabilityRelation
    );
    return normalizePathPrefix(url.pathname, "Configured workflow HTTP base path");
  } catch {
    throw new Error(workflowHttpBasePathUnavailableMessage);
  }
}

/** Finds one endpoint in the exact published executable tree and reads only its compiled literal inputs. */
export function findPublishedHttpEndpoint(
  executable: WorkflowExecutableDetails,
  authoredActivityId: string,
  inputSources: WorkflowExecutableInputSources
): PublishedHttpEndpoint | null {
  const sourceReferenceId = executable.chosenReference?.sourceReferenceId;
  if (!sourceReferenceId
    || inputSources.artifactId !== executable.artifactId
    || inputSources.sourceReferenceId !== sourceReferenceId
    || !Array.isArray(inputSources.compiledInputs)
    || !isAllowedAccessState(inputSources.accessState ?? inputSources.access)) {
    throw new Error(publishedHttpEndpointInputsUnavailableMessage);
  }

  const node = findNode(executable.rootActivity, authoredActivityId);
  if (!node || !isHttpEndpointType(node.activityType)) {
    throw new Error("This endpoint is not in the latest published workflow. Publish your changes, then try again.");
  }

  const routeTemplate = readLiteralInput(node, "Path", inputSources.compiledInputs);
  if (typeof routeTemplate !== "string" || !routeTemplate.trim()) return null;

  const methodsBinding = findCompiledInputBinding(node, "SupportedMethods", inputSources.compiledInputs);
  const authoredMethods = methodsBinding ? readMethods(readLiteralBinding(methodsBinding)) : defaultMethods;
  const supportedMethods = methodsBinding && Array.isArray(methodsBinding.binding.literalValue) && methodsBinding.binding.literalValue.length === 0
    ? defaultMethods
    : authoredMethods;
  if (supportedMethods.length === 0) return null;

  const responseMode = readResponseMode(readLiteralInput(node, "ResponseMode", inputSources.compiledInputs));
  return {
    authoredActivityId,
    routeTemplate: routeTemplate.trim(),
    supportedMethods,
    responseMode
  };
}

export function isHttpEndpointType(activityType: string | null | undefined): boolean {
  const normalized = activityType?.trim().toLowerCase();
  return normalized === httpEndpointActivityType.toLowerCase()
    || normalized === "elsa.httpendpoint"
    || normalized?.endsWith(".httpendpoint") === true;
}

/** Produces an editable endpoint path with sample values for ASP.NET route parameters. */
export function makeInitialHttpTestEndpointPath(routeTemplate: string): string {
  const exampleRoute = routeTemplate
    .trim()
    .replace(/^\/+/, "")
    .replace(/\{(\*{0,2})?([^}:?]+)(?::([^}?]+))?(\?)?\}/g, (_match, _catchAll: string, _name: string, constraint: string | undefined) => {
      const kinds = constraint?.toLowerCase().split(":") ?? [];
      if (kinds.some(kind => ["int", "long", "short", "byte", "uint", "ulong"].includes(kind))) return "1";
      if (kinds.includes("guid")) return "00000000-0000-0000-0000-000000000001";
      return "sample";
    });

  if (!exampleRoute) throw new Error("The published HTTP endpoint does not have a request path.");
  return `/${exampleRoute}`;
}

/**
 * Validates an editable full request path and returns a same-origin absolute path suitable for `context.http`.
 * The path must stay beneath the host's configured workflow HTTP base path. Path segments are checked before
 * URL parsing because WHATWG URL parsing canonicalizes dot segments (including encoded dots) before callers
 * can inspect them. Encoded separators are rejected at every decoding layer so proxies and the server cannot
 * reinterpret an apparently contained path as a sibling or parent route.
 */
export function resolveHttpTestRequestPath(baseUrl: string, editedPath: string, workflowHttpBasePath: string): string {
  const backend = new URL(baseUrl);
  if (backend.protocol !== "https:" && backend.protocol !== "http:") {
    throw new Error("The configured backend URL must use HTTP or HTTPS.");
  }

  const basePath = normalizePathPrefix(workflowHttpBasePath, "Configured workflow HTTP base path");
  const path = readLocalHttpTestPath(editedPath);
  const resolved = new URL(path.startsWith("/") ? path : `/${path}`, backend.origin);
  if (resolved.origin !== backend.origin) {
    throw new Error("Requests can only be sent to the configured backend.");
  }

  if (!hasRouteWithinPrefix(resolved.pathname, basePath)) {
    throw new Error(`Enter an endpoint route beneath the configured workflow HTTP base path (${basePath}).`);
  }

  return `${resolved.pathname}${resolved.search}`;
}

/** Joins an editable endpoint route to the authoritative, non-editable server mount. */
export function resolveHttpTestEndpointPath(baseUrl: string, endpointPath: string, workflowHttpBasePath: string): string {
  const route = readLocalHttpTestPath(endpointPath);
  const mount = normalizePathPrefix(workflowHttpBasePath, "Configured workflow HTTP base path");
  return resolveHttpTestRequestPath(baseUrl, `${mount}/${route.replace(/^\//, "")}`, mount);
}

function readLocalHttpTestPath(value: string): string {
  const path = value.trim();
  if (!path) throw new Error("Enter a request path.");
  if (path.startsWith("//") || path.includes("\\") || /^[a-z][a-z\d+.-]*:/i.test(path) || path.includes("#")) {
    throw new Error("Enter a path under the configured workflow HTTP base path.");
  }
  assertSafePathSegments(path.split("?", 1)[0]);
  return path;
}

export interface HttpEndpointResponseMetadata {
  mode?: "async" | "sync" | string;
  reason?: "background-accepted" | "workflow-response-not-produced" | string;
  message?: string;
}

export function readHttpEndpointResponseMetadata(body: string): HttpEndpointResponseMetadata | null {
  try {
    const payload: unknown = JSON.parse(body);
    if (!isRecord(payload) || !isRecord(payload.response)) return null;
    const response = payload.response;
    if (typeof response.mode !== "string" && typeof response.reason !== "string" && typeof response.message !== "string") return null;
    return {
      ...(typeof response.mode === "string" ? { mode: response.mode } : {}),
      ...(typeof response.reason === "string" ? { reason: response.reason } : {}),
      ...(typeof response.message === "string" ? { message: response.message } : {})
    };
  } catch {
    return null;
  }
}

export function describeHttpEndpointAcceptedResponse(metadata: HttpEndpointResponseMetadata | null): string {
  if (metadata?.reason === "workflow-response-not-produced") {
    return "The workflow did not produce a response while this request was waiting. It may have paused, failed, continued elsewhere, or finished without a response activity.";
  }
  if (metadata?.message?.trim()) return metadata.message.trim();
  if (metadata?.reason === "background-accepted") {
    return "The backend accepted the request and queued the workflow to continue in the background.";
  }
  return "The backend accepted the request with HTTP 202. Workflow work may continue in the background, or the workflow may not have written a response during this exchange.";
}

/** A single dispatched run can be inspected directly without guessing which run handled the request. */
export function readHttpEndpointExecutionId(body: string): string | null {
  try {
    const payload: unknown = JSON.parse(body);
    if (!isRecord(payload)) return null;
    const ids = new Set([payload.started, payload.resumed].flatMap(value =>
      Array.isArray(value) ? value.filter((id): id is string => typeof id === "string" && id.trim().length > 0) : []));
    return ids.size === 1 ? [...ids][0] : null;
  } catch {
    return null;
  }
}

function findNode(node: WorkflowExecutableNode, authoredActivityId: string): WorkflowExecutableNode | null {
  if (node.authoredActivityId === authoredActivityId) return node;
  for (const slot of node.childSlots) {
    for (const child of slot.activities) {
      const found = findNode(child, authoredActivityId);
      if (found) return found;
    }
  }
  return null;
}

function findCompiledInputBinding(
  node: WorkflowExecutableNode,
  inputKey: string,
  compiledInputs: WorkflowExecutableCompiledInput[]
): WorkflowExecutableCompiledInput | undefined {
  const target = inputKey.toLowerCase();
  return compiledInputs.find(input => input.executableNodeId === node.executableNodeId
    && ((input.binding.inputKey?.trim().toLowerCase() ?? "") === target
      || input.binding.inputName.trim().toLowerCase() === target));
}

function readLiteralInput(
  node: WorkflowExecutableNode,
  inputKey: string,
  compiledInputs: WorkflowExecutableCompiledInput[]
): unknown {
  const compiled = findCompiledInputBinding(node, inputKey, compiledInputs);
  return compiled ? readLiteralBinding(compiled) : undefined;
}

function readLiteralBinding(compiled: WorkflowExecutableCompiledInput): unknown {
  if (!isAllowedAccessState(compiled.accessState ?? compiled.access) || compiled.binding.isSensitive === true) {
    throw new Error(publishedHttpEndpointInputsUnavailableMessage);
  }
  if (compiled.binding.source.toLowerCase() !== "literal") return undefined;
  return compiled.binding.literalValue;
}

function readMethods(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  const methods = new Set<string>();
  for (const entry of value) {
    if (typeof entry !== "string") continue;
    const method = entry.trim().toUpperCase();
    if (/^[!#$%&'*+.^_`|~0-9A-Z-]+$/.test(method)) methods.add(method);
  }
  return [...methods];
}

function readResponseMode(value: unknown): "async" | "sync" | null {
  if (value === 0) return "async";
  if (value === 1) return "sync";
  if (typeof value !== "string") return null;
  const mode = value.trim().toLowerCase();
  return mode === "async" || mode === "sync" ? mode : null;
}

function isAllowedAccessState(value: string | null | undefined): boolean {
  return value?.trim().toLowerCase() === "allowed";
}

function normalizePathPrefix(path: string, label: string): string {
  const value = path.trim();
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.includes("\\") || value.includes("?") || value.includes("#") || /^[a-z][a-z\d+.-]*:/i.test(value)) {
    throw new Error(`${label} must be a local path.`);
  }
  const normalized = `/${value.replace(/^\/+|\/+$/g, "")}`;
  assertSafePathSegments(normalized);
  if (normalized === "/") {
    throw new Error(`${label} must be a non-root path.`);
  }
  return normalized;
}

function hasRouteWithinPrefix(pathname: string, prefix: string): boolean {
  const normalizedPathname = pathname.replace(/\/+$/, "") || "/";
  const normalizedPrefix = prefix.replace(/\/+$/, "");
  if (!normalizedPathname.toLowerCase().startsWith(`${normalizedPrefix.toLowerCase()}/`)) return false;
  return normalizedPathname.slice(normalizedPrefix.length + 1).replace(/^\/+/, "").length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
