import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";

export const capabilityIds = {
  workflowDesign: "elsa.api.workflow-design",
  activityDesign: "elsa.api.activity-design",
  expressions: "elsa.api.expressions",
  publishing: "elsa.api.publishing",
  runtime: "elsa.api.runtime"
} as const;

export type ApiCapabilityId = typeof capabilityIds[keyof typeof capabilityIds];

const supportedContractVersions: Record<ApiCapabilityId, string> = {
  [capabilityIds.workflowDesign]: "1",
  [capabilityIds.activityDesign]: "1",
  [capabilityIds.expressions]: "1",
  [capabilityIds.publishing]: "1",
  [capabilityIds.runtime]: "1"
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

const documentsByShell = new Map<string, Promise<ApiCapabilityDocument>>();

function shellKey(context: StudioEndpointContext) {
  return context.baseUrl?.replace(/\/+$/, "") ?? "";
}

/** Loads the authenticated, permission-neutral shell capability document once. */
export function getApiCapabilities(context: StudioEndpointContext): Promise<ApiCapabilityDocument> {
  const key = shellKey(context);
  const cached = documentsByShell.get(key);
  if (cached) return cached;

  const request = context.http.getJson<ApiCapabilityDocument>("/capabilities")
    .then(document => ({ capabilities: Array.isArray(document?.capabilities) ? document.capabilities : [] }))
    .catch(error => {
      documentsByShell.delete(key);
      throw error;
    });
  documentsByShell.set(key, request);
  return request;
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
  return capability?.contractVersion === supportedContractVersions[capabilityId];
}

export async function resolveCapabilityLink(
  context: StudioEndpointContext,
  capabilityId: ApiCapabilityId,
  relation: string,
  parameters: Record<string, string> = {}
) {
  const capability = await getApiCapability(context, capabilityId);
  if (!capability) throw new ApiCapabilityUnavailableError(capabilityId);

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
  return path.startsWith("/") ? path : `/${path}`;
}

/** Test seam; production callers should rely on automatic error eviction. */
export function clearApiCapabilityCache() {
  documentsByShell.clear();
}
