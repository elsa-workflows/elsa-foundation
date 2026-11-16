import type { AuthSession, StudioContributionAvailability, StudioEndpointContext } from "./index";

/** Public connection metadata. Authentication secrets never belong in a profile. */
export interface StudioBackendProfile {
  id: string;
  name: string;
  baseUrl: string;
  environment: "Development" | "Test" | "Production";
  authenticationProfile?: string;
  source: "configuration" | "personal";
}

export interface StudioApiCapabilityLink { rel: string; href: string; templated?: boolean }
export interface StudioApiCapability {
  id: string;
  contractVersion: string;
  links: StudioApiCapabilityLink[];
  status?: "dormant";
  reason?: string;
}
export interface StudioApiCapabilityDocument { capabilities: StudioApiCapability[] }
export interface StudioBackendRequirement {
  id: string;
  contractVersion: string;
  relations?: readonly string[];
}
export interface StudioBackendAwareContribution { requiresBackend?: readonly StudioBackendRequirement[] }

/** An immutable connection lifetime; its capability snapshot may be explicitly refreshed. */
export interface StudioBackendSession {
  readonly key: string;
  readonly profile: StudioBackendProfile;
  readonly context: StudioEndpointContext;
  readonly identity: AuthSession;
  readonly capabilities: StudioApiCapabilityDocument | null;
  readonly discoveryError?: string;
  readonly pendingWrites: number;
  readonly disposed: boolean;
  refreshCapabilities(): Promise<StudioApiCapabilityDocument>;
}

export const studioBackendCapabilitiesChangedEvent = "elsa:backend-capabilities-changed";
let capabilityDocuments = new WeakMap<StudioEndpointContext, Promise<StudioApiCapabilityDocument>>();

/** Preserve a selected backend in resource links, reloads and new tabs. Explicit targets stay explicit. */
export function qualifyStudioBackendPath(path:string, locationHref=window.location.href):string {
  const current = new URL(locationHref);
  const target = new URL(path,current);
  if (target.origin !== current.origin) return path;
  const selected = current.searchParams.get("backend");
  if (!selected || target.searchParams.has("backend")) return path;
  target.searchParams.set("backend",selected);
  return `${target.pathname}${target.search}${target.hash}`;
}

/** Resolve a shell-relative API path without losing or duplicating the shell prefix. */
export function resolveBackendPath(baseUrl: string, path: string): string {
  const base = new URL(baseUrl);
  if (!/^https?:$/.test(base.protocol) || base.username || base.password || base.search || base.hash)
    throw new Error("A backend must use an HTTP or HTTPS URL without credentials, query or fragment.");
  base.pathname = `${base.pathname.replace(/\/+$/, "")}/`;
  if (/^https?:/i.test(path)) {
    const absolute = new URL(path);
    if (absolute.origin !== base.origin || absolute.username || absolute.password || !absolute.pathname.startsWith(base.pathname))
      throw new Error("Backend API paths must stay inside the selected shell.");
    path = `${absolute.pathname}${absolute.search}${absolute.hash}`;
  }
  if (/^[a-z][a-z\d+.-]*:|^\/\/|\\/i.test(path)) throw new Error("Backend API paths must stay inside the selected shell.");
  const prefix = base.pathname.replace(/\/$/, "");
  const relative = prefix && (path === prefix || path.startsWith(`${prefix}/`)) ? path.slice(prefix.length) : path;
  if (relative.split(/[?#]/,1)[0].split("/").some(segment => {
    let value = segment;
    for (let index = 0; index < 3; index++) {
      try { const decoded = decodeURIComponent(value); if (decoded === value) break; value = decoded; } catch { throw new Error("Invalid backend API path encoding."); }
    }
    return value === ".." || value === "." || value.includes("\\") || value.includes("/");
  })) throw new Error("Backend API paths must not escape the selected shell.");
  const resolved = new URL(relative.replace(/^\/+/, ""), base);
  if (resolved.origin !== base.origin || !resolved.pathname.startsWith(base.pathname)) throw new Error("Backend API path escaped the selected shell.");
  return `${resolved.pathname}${resolved.search}${resolved.hash}`;
}

export function validateApiCapabilities(value: unknown): StudioApiCapabilityDocument {
  if (!value || typeof value !== "object" || !Array.isArray((value as StudioApiCapabilityDocument).capabilities))
    throw new Error("The backend returned an invalid capability document.");
  const ids = new Set<string>();
  for (const capability of (value as StudioApiCapabilityDocument).capabilities) {
    if (!capability || typeof capability.id !== "string" || !capability.id.trim() || ids.has(capability.id)
      || typeof capability.contractVersion !== "string" || !/^[1-9]\d*$/.test(capability.contractVersion)
      || !Array.isArray(capability.links) || (capability.status !== undefined && capability.status !== "dormant")
      || (capability.status === "dormant" && (typeof capability.reason !== "string" || !capability.reason.trim())))
      throw new Error("The backend returned an invalid capability declaration.");
    ids.add(capability.id);
    const relations = new Set<string>();
    for (const link of capability.links) {
      if (!link || typeof link.rel !== "string" || !link.rel.trim() || relations.has(link.rel)
        || (link.templated !== undefined && typeof link.templated !== "boolean")
        || typeof link.href !== "string" || !link.href || /^(?:[a-z][a-z\d+.-]*:|[\\/])|[?#]/i.test(link.href))
        throw new Error("The backend returned an invalid capability link.");
      resolveBackendPath("https://capability.invalid/", link.href);
      relations.add(link.rel);
    }
  }
  return value as StudioApiCapabilityDocument;
}

/** Cache by immutable endpoint context, never by URL alone (identities may share a URL). */
export function getBackendCapabilities(context: StudioEndpointContext): Promise<StudioApiCapabilityDocument> {
  const existing = capabilityDocuments.get(context);
  if (existing) return existing;
  let path = "/capabilities";
  try { if (/^https?:$/.test(new URL(context.baseUrl).protocol)) path = resolveBackendPath(context.baseUrl,"capabilities"); } catch { /* Opaque custom transports retain their root route contract. */ }
  const request = context.http.getJson<unknown>(path)
    .then(validateApiCapabilities)
    .catch(error => { capabilityDocuments.delete(context); throw error; });
  capabilityDocuments.set(context, request);
  return request;
}
export function invalidateBackendCapabilities(context: StudioEndpointContext) { capabilityDocuments.delete(context); }
/** Test/host lifecycle seam; ordinary refresh should invalidate only its own endpoint context. */
export function clearBackendCapabilityCache() { capabilityDocuments = new WeakMap(); }

export function evaluateBackendRequirements(
  session: Pick<StudioBackendSession, "capabilities" | "discoveryError"> | undefined,
  requirements: readonly StudioBackendRequirement[] | undefined
): StudioContributionAvailability {
  // A host embedding legacy SDK modules can continue composing without a connection provider.
  if (!requirements?.length || !session) return { state: "available" };
  if (!session.capabilities) return { state: "unavailable", source: "runtime", reason: session.discoveryError || "Backend capabilities have not been discovered." };
  for (const requirement of requirements) {
    const capability = session.capabilities.capabilities.find(item => item.id === requirement.id);
    if (!capability) return { state: "unavailable", source: "runtime", reason: `This backend does not support ${requirement.id}.` };
    if (capability.contractVersion !== requirement.contractVersion)
      return { state: "unavailable", source: "runtime", reason: `${requirement.id} requires contract ${requirement.contractVersion}; this backend advertises ${capability.contractVersion}.` };
    if (capability.status === "dormant") return { state: "unavailable", source: "runtime", reason: capability.reason };
    const missing = requirement.relations?.find(relation => !capability.links.some(link => link.rel === relation));
    if (missing) return { state: "unavailable", source: "runtime", reason: `This backend does not advertise ${missing}.` };
  }
  return { state: "available" };
}
