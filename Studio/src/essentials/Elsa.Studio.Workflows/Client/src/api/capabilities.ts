import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearBackendCapabilityCache, getBackendCapabilities, invalidateBackendCapabilities } from "@elsa-workflows/studio-sdk";
import { assertSafePathSegments } from "./pathSegments";

export const capabilityIds = {
  workflowDesign: "elsa.api.workflow-design",
  activityDesign: "elsa.api.activity-design",
  expressions: "elsa.api.expressions",
  expressionTooling: "expressions.tooling.v1",
  publishing: "elsa.api.publishing",
  runtime: "elsa.api.runtime",
  workflowHttpEndpoints: "elsa.http.workflow-endpoints"
} as const;

export type ApiCapabilityId = typeof capabilityIds[keyof typeof capabilityIds];

const supportedContractVersions: Record<ApiCapabilityId, string> = {
  [capabilityIds.workflowDesign]: "1",
  [capabilityIds.activityDesign]: "1",
  [capabilityIds.expressions]: "1",
  [capabilityIds.expressionTooling]: "1",
  [capabilityIds.publishing]: "1",
  [capabilityIds.runtime]: "1",
  [capabilityIds.workflowHttpEndpoints]: "1"
};

export interface ApiCapabilityLink {
  rel: string;
  href: string;
  templated?: boolean;
}

export interface ApiCapability {
  id: ApiCapabilityId | string;
  contractVersion: string;
  links: ApiCapabilityLink[];
  status?: "dormant";
  reason?: string;
}

export interface ApiCapabilityDocument {
  capabilities: ApiCapability[];
}

export class ApiCapabilityUnavailableError extends Error {
  constructor(
    public readonly capabilityId: ApiCapabilityId,
    public readonly relation?: string
  ) {
    super(relation
      ? `API capability '${capabilityId}' does not advertise '${relation}'.`
      : `API capability '${capabilityId}' is not available.`);
    this.name = "ApiCapabilityUnavailableError";
  }
}

export class ApiCapabilityVersionMismatchError extends Error {
  constructor(
    public readonly capabilityId: ApiCapabilityId,
    public readonly expectedVersion: string,
    public readonly actualVersion: string
  ) {
    super(`API capability '${capabilityId}' advertises contract major '${actualVersion}', but Studio supports '${expectedVersion}'.`);
    this.name = "ApiCapabilityVersionMismatchError";
  }
}

/** Loads the authenticated, permission-neutral shell capability document once. */
export function getApiCapabilities(context: StudioEndpointContext): Promise<ApiCapabilityDocument> {
  return getBackendCapabilities(context);
}

export async function getApiCapability(
  context: StudioEndpointContext,
  capabilityId: ApiCapabilityId
): Promise<ApiCapability | null> {
  const document = await getApiCapabilities(context);
  return document.capabilities.find(capability => capability.id === capabilityId) ?? null;
}

export async function hasApiCapability(context: StudioEndpointContext, capabilityId: ApiCapabilityId) {
  const capability = await getApiCapability(context, capabilityId);
  return capability?.contractVersion === supportedContractVersions[capabilityId] && capability.status !== "dormant";
}

export async function hasCapabilityLink(
  context: StudioEndpointContext,
  capabilityId: ApiCapabilityId,
  relation: string
) {
  const capability = await getApiCapability(context, capabilityId);
  if (!capability || capability.contractVersion !== supportedContractVersions[capabilityId] || capability.status === "dormant") return false;
  return capability.links.some(link => link.rel === relation);
}

export async function resolveCapabilityLink(
  context: StudioEndpointContext,
  capabilityId: ApiCapabilityId,
  relation: string,
  parameters: Record<string, string> = {}
) {
  const href = await resolveCapabilityHref(context, capabilityId, relation, parameters);
  const basePath = capabilityBasePath(context);
  return `${basePath}${href.replace(/^\/+/, "")}`;
}

/** Returns the advertised link without changing whether it is relative to the active shell route base. */
export async function resolveCapabilityHref(
  context: StudioEndpointContext,
  capabilityId: ApiCapabilityId,
  relation: string,
  parameters: Record<string, string> = {}
) {
  const capability = await getApiCapability(context, capabilityId);
  if (!capability) throw new ApiCapabilityUnavailableError(capabilityId);
  if (capability.status === "dormant") throw new Error(capability.reason ?? `API capability '${capabilityId}' is dormant.`);

  const expectedVersion = supportedContractVersions[capabilityId];
  if (capability.contractVersion !== expectedVersion) {
    throw new ApiCapabilityVersionMismatchError(capabilityId, expectedVersion, capability.contractVersion);
  }

  const link = capability.links.find(candidate => candidate.rel === relation);
  if (!link) throw new ApiCapabilityUnavailableError(capabilityId, relation);

  let path = link.href;
  for (const [name, value] of Object.entries(parameters)) {
    path = path.replaceAll(`{${name}}`, encodeURIComponent(value));
  }
  if (/\{[^}]+\}/.test(path)) {
    throw new Error(`Capability link '${relation}' requires additional template parameters.`);
  }
  return path;
}

/** Resolves a relative capability link against the backend's active shell route base. */
export async function resolveRelativeCapabilityUrl(
  context: StudioEndpointContext,
  capabilityId: ApiCapabilityId,
  relation: string,
  parameters: Record<string, string> = {}
): Promise<URL> {
  const href = await resolveCapabilityHref(context, capabilityId, relation, parameters);
  if (!href || href.startsWith("/") || href.startsWith("\\") || /^[a-z][a-z\d+.-]*:/i.test(href) || href.includes("?") || href.includes("#")) {
    throw new Error(`Capability link '${relation}' must be relative to the active shell route base.`);
  }
  assertSafePathSegments(href);

  const base = shellBaseUrl(context);
  if (base.protocol !== "http:" && base.protocol !== "https:") {
    throw new Error("The configured backend URL must use HTTP or HTTPS.");
  }
  const resolved = new URL(href, base);
  if (resolved.origin !== base.origin) {
    throw new Error(`Capability link '${relation}' resolved outside the configured backend.`);
  }

  const shellBasePath = base.pathname.replace(/\/+$/, "");
  if (shellBasePath && resolved.pathname.toLowerCase() !== shellBasePath.toLowerCase()
    && !resolved.pathname.toLowerCase().startsWith(`${shellBasePath.toLowerCase()}/`)) {
    throw new Error(`Capability link '${relation}' resolved outside the active shell route base.`);
  }
  return resolved;
}

/** Custom transports may use opaque connection keys; only HTTP URLs contribute a shell route prefix. */
function capabilityBasePath(context: StudioEndpointContext): string {
  try {
    const base = shellBaseUrl(context);
    return base.protocol === "http:" || base.protocol === "https:" ? base.pathname : "/";
  } catch {
    return "/";
  }
}

/** Give the active shell route base directory semantics when composing relative capability paths. */
function shellBaseUrl(context: StudioEndpointContext): URL {
  const base = new URL(context.baseUrl);
  base.pathname = `${base.pathname.replace(/\/+$/, "")}/`;
  base.search = "";
  base.hash = "";
  return base;
}

/** Drop one backend's cached document after an explicit refresh action. */
export function invalidateApiCapabilityCache(context: StudioEndpointContext) {
  invalidateBackendCapabilities(context);
}

/** Test seam for isolating cached capability documents between tests. */
export function clearApiCapabilityCache() {
  clearBackendCapabilityCache();
}
