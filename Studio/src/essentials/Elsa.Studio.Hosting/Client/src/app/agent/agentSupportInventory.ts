import type { ElsaStudioModuleApi, StudioAgentContextAttachment } from "../../sdk";
import { isAgentRegistryCurrent, observeAgentContributionOrigin, observeAgentRegistrations, retireAgentModuleRegistration } from "./agentRegistrationOrigins";

type Completeness = "complete" | "incomplete" | "unknown" | "unavailable";
type ModuleStatus = "available" | "disabled" | "failed" | "incompatible" | "unknown";
type ModuleRow = { id: string; displayName: string; status: ModuleStatus };
type RegistrationRow = { kind: string; id: string; originModuleId?: string; declaredModuleId?: string; resourceModuleIds?: string[]; status: "available" | "unavailable" | "unknown" };
type LoaderRow = { moduleId: string; status: "registering" | "loaded" | "failed" | "retired" };

export type StudioSupportSnapshot = {
  content: {
    side: "studio";
    clientAsserted: true;
    completeness: Completeness;
    modules: ModuleRow[];
    registrationCompleteness: "complete" | "incomplete";
    loader: LoaderRow[];
    registrations: RegistrationRow[];
  };
  disclosureSupported: boolean;
};

const MAX_ROWS = 128;
const MAX_CONTENT_LENGTH = 24_576;
const CONTEXT_OWNER = "Elsa.Agent";
const CONTEXT_KIND = "agent.host-support";
const CONTEXT_SCHEMA = "1";
const snapshots = new WeakMap<StudioSupportSnapshot, {
  agent: ElsaStudioModuleApi["agent"];
  host: ElsaStudioModuleApi["host"];
  backend: ElsaStudioModuleApi["backend"];
  backendSession: ElsaStudioModuleApi["backendSession"];
  backendKey: string | undefined;
  backendIdentity: unknown;
  hostHttp: ElsaStudioModuleApi["host"]["http"];
  backendHttp: ElsaStudioModuleApi["backend"]["http"];
  revision: number;
}>();

/** Current observations are discovery evidence only; browser claims grant no backend authority. */
export function isStudioSupportSnapshotCurrent(api: ElsaStudioModuleApi, snapshot: StudioSupportSnapshot) {
  const origin = snapshots.get(snapshot);
  return !!origin && isAgentRegistryCurrent(api) && origin.agent === api.agent && origin.host === api.host
    && origin.backend === api.backend && origin.backendSession === api.backendSession
    && origin.backendKey === api.backendSession?.key && origin.backendIdentity === api.backendSession?.identity
    && api.backendSession?.disposed !== true
    && origin.hostHttp === api.host.http && origin.backendHttp === api.backend.http
    && origin.revision === observeAgentRegistrations(api).revision;
}

/** Fresh reads use each actual endpoint context. No route, manifest capability or URL assigns an owner. */
export async function refreshStudioSupportInventory(api: ElsaStudioModuleApi, signal?: AbortSignal,
  options: { timeoutMs?: number } = {}): Promise<StudioSupportSnapshot> {
  const timeoutMs = options.timeoutMs ?? 10_000;
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new Error("Support discovery deadline must be positive and finite.");
  signal?.throwIfAborted();
  const origin = { agent: api.agent, host: api.host, backend: api.backend, backendSession: api.backendSession,
    backendKey: api.backendSession?.key, backendIdentity: api.backendSession?.identity,
    hostHttp: api.host.http, backendHttp: api.backend.http, revision: observeAgentRegistrations(api).revision };
  const current = () => isAgentRegistryCurrent(api) && origin.agent === api.agent && origin.host === api.host
    && origin.backend === api.backend && origin.backendSession === api.backendSession
    && origin.backendKey === api.backendSession?.key && origin.backendIdentity === api.backendSession?.identity
    && api.backendSession?.disposed !== true
    && origin.hostHttp === api.host.http && origin.backendHttp === api.backend.http
    && origin.revision === observeAgentRegistrations(api).revision;
  const assertCurrent = () => { signal?.throwIfAborted(); if (!current()) throw new Error("Module context changed; try the request again."); };
  assertCurrent();
  const [host, backend] = await Promise.all([
    readBounded(token => origin.hostHttp.getJson<unknown>("/_elsa/studio/module-registry", { signal: token }), timeoutMs, signal),
    // This is contract discovery, not a browser assertion of the backend's support or authority.
    readBounded(token => origin.backendHttp.getJson<unknown>("/_elsa/agent/support", { signal: token }), timeoutMs, signal)
  ]);
  assertCurrent();
  const projected = projectModules(host);
  if (projected.completeness === "complete") {
    const installed = new Map(projected.modules.map(module => [module.id.toLowerCase(), module]));
    for (const registration of observeAgentRegistrations(api).modules) {
      const observed = installed.get(registration.moduleId.toLowerCase());
      if (!observed || observed.status === "disabled" || observed.status === "failed" || observed.status === "incompatible")
        retireAgentModuleRegistration(api, registration.moduleId);
    }
    origin.revision = observeAgentRegistrations(api).revision;
  }
  const registrationProjection = projectRegistrations(api, projected);
  const snapshot: StudioSupportSnapshot = {
    content: { side: "studio", clientAsserted: true, ...projected, ...registrationProjection },
    disclosureSupported: supportsContext(backend)
  };
  // A bound applies to the entire plain JSON observation, not just individual rows. Dropped rows
  // explicitly invalidate completeness; they never prove a missing module or registration absent.
  while (JSON.stringify(snapshot.content).length > MAX_CONTENT_LENGTH) {
    if (snapshot.content.registrations.length) {
      snapshot.content.registrations.pop(); snapshot.content.registrationCompleteness = "incomplete";
    } else if (snapshot.content.loader.length) {
      snapshot.content.loader.pop(); snapshot.content.registrationCompleteness = "incomplete";
    } else {
      snapshot.content.modules.pop(); snapshot.content.completeness = "incomplete";
    }
  }
  assertCurrent();
  snapshots.set(snapshot, origin);
  return snapshot;
}

/** Only an installed server source may admit this versioned, explicitly client-asserted claim. */
export function studioSupportAttachment(api: ElsaStudioModuleApi, snapshot: StudioSupportSnapshot): StudioAgentContextAttachment | undefined {
  if (!isStudioSupportSnapshotCurrent(api, snapshot)) throw new Error("Module context changed; try the request again.");
  if (!snapshot.disclosureSupported) return undefined;
  return { id: "agent.studio-support", moduleId: CONTEXT_OWNER, source: "studio", label: "Studio support observation",
    contentType: CONTEXT_KIND, schemaVersion: CONTEXT_SCHEMA, sensitivity: "internal", scope: "host",
    summary: "Client-asserted Studio discovery and registration observations; these grant no resource or execution authority.",
    content: snapshot.content, origin: "clientSnapshot", agentScopes: [] };
}

function projectModules(input: unknown): { completeness: Completeness; modules: ModuleRow[] } {
  if (input === undefined) return { completeness: "unavailable", modules: [] };
  const body = record(input);
  if (!body || !Array.isArray(body.modules)) return { completeness: "unknown", modules: [] };
  let completeness: Completeness = body.complete === true ? "complete" : body.complete === false ? "incomplete" : "unknown";
  if (body.modules.length > MAX_ROWS) completeness = "incomplete";
  const modules: ModuleRow[] = []; const ids = new Set<string>();
  for (const value of body.modules.slice(0, MAX_ROWS)) {
    const item = record(value); const id = identifier(item?.id); const displayName = label(item?.displayName);
    const status = moduleStatus(item?.status);
    if (!id || !displayName || !status || ids.has(id.toLowerCase())) { completeness = "incomplete"; continue; }
    ids.add(id.toLowerCase()); modules.push({ id, displayName, status });
  }
  return { completeness, modules };
}

function projectRegistrations(api: ElsaStudioModuleApi, manifest: { completeness: Completeness; modules: ModuleRow[] }) {
  const registrations: RegistrationRow[] = []; let registrationCompleteness: "complete" | "incomplete" = "complete";
  const observed = observeAgentRegistrations(api).modules;
  const loader: LoaderRow[] = [];
  for (const module of observed.slice(0, MAX_ROWS)) {
    const moduleId = identifier(module.moduleId);
    if (!moduleId) { registrationCompleteness = "incomplete"; continue; }
    loader.push({ moduleId, status: module.status });
  }
  if (observed.length > MAX_ROWS || observed.some(module => module.status === "registering") || manifest.completeness !== "complete"
    || manifest.modules.some(module => module.status === "available" && !loader.some(loaded => loaded.moduleId.toLowerCase() === module.id.toLowerCase())))
    registrationCompleteness = "incomplete";
  for (const [kind, candidate] of Object.entries(api.agent)) {
    if (!candidate || typeof candidate !== "object" || !("compose" in candidate) || typeof candidate.compose !== "function") continue;
    const ids = new Set<string>();
    // The existing host/origin veto runs before runtime availability, so retired callbacks are not invoked.
    let composed: unknown;
    try { composed = candidate.compose({ includeHidden: true, includeUnavailable: true }); }
    catch { registrationCompleteness = "incomplete"; continue; }
    if (!Array.isArray(composed)) { registrationCompleteness = "incomplete"; continue; }
    if (composed.length > MAX_ROWS) registrationCompleteness = "incomplete";
    for (const value of composed.slice(0, MAX_ROWS)) {
      if (registrations.length === MAX_ROWS) { registrationCompleteness = "incomplete"; break; }
      const entry = record(value); const contribution = record(entry?.contribution); const id = identifier(contribution?.id);
      const safeKind = identifier(kind); const origin = observeAgentContributionOrigin(api, contribution);
      const originModuleId = identifier(origin?.moduleId); const declaredModuleId = identifier(contribution?.moduleId);
      const resources = contribution?.resourceModuleIds;
      const resourceModuleIds = Array.isArray(resources) && resources.length <= 8 && resources.every(value => identifier(value)) ? resources as string[] : undefined;
      if (!id || !safeKind || ids.has(id)) { registrationCompleteness = "incomplete"; continue; }
      ids.add(id);
      const state = record(entry?.availability)?.state;
      const status = state === "available" ? "available" : state === "hidden" || state === "unavailable" ? "unavailable" : "unknown";
      if (!originModuleId || status === "unknown" || contribution?.moduleId !== undefined && !declaredModuleId
        || resources !== undefined && !resourceModuleIds) registrationCompleteness = "incomplete";
      registrations.push({ kind: safeKind, id, ...(originModuleId ? { originModuleId } : {}),
        ...(declaredModuleId ? { declaredModuleId } : {}), ...(resourceModuleIds ? { resourceModuleIds: [...resourceModuleIds] } : {}), status });
    }
  }
  return { registrationCompleteness, loader, registrations };
}

function supportsContext(input: unknown) {
  const support = record(record(input)?.data)?.support;
  const body = record(support);
  if (body?.completeness !== "complete" || !Array.isArray(body.contexts) || body.contexts.length > MAX_ROWS) return false;
  const matching = body.contexts.map(record).filter(item => item?.moduleId === CONTEXT_OWNER
    && Array.isArray(item.contextKinds) && item.contextKinds.includes(CONTEXT_KIND));
  // Presence alone is insufficient: a matching required source withheld by feature/schema admission
  // would refuse the entire prompt. This hint only selects a supported candidate; it grants no authority.
  return matching.length > 0 && matching.every(item => item?.admission === "availableHint"
    && Array.isArray(item.schemaVersions) && item.schemaVersions.includes(CONTEXT_SCHEMA));
}

function record(value: unknown): Record<string, unknown> | undefined { return !!value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : undefined; }
// This is a disclosure projection, not a restriction on SDK registration. Scoped opaque IDs remain
// opaque; URL/path-shaped or otherwise unrepresentable metadata makes discovery incomplete.
function identifier(value: unknown) { return typeof value === "string" && value.length <= 160 && /^@?[A-Za-z0-9_-]+(?:[./:][A-Za-z0-9_-]+)*$/.test(value) ? value : undefined; }
function label(value: unknown) { return typeof value === "string" && value.trim() && value.length <= 256 && !/[\u0000-\u001f\u007f]/.test(value) ? value : undefined; }
function moduleStatus(value: unknown): ModuleStatus | undefined { return value === "available" || value === "disabled" || value === "failed" || value === "incompatible" || value === "unknown" ? value : undefined; }

async function readBounded(read: (signal: AbortSignal) => Promise<unknown>, timeoutMs: number, signal?: AbortSignal) {
  const controller = new AbortController(); let timer: ReturnType<typeof setTimeout> | undefined; let cancel: (() => void) | undefined;
  try {
    const interrupted = new Promise<never>((_resolve, reject) => {
      cancel = () => { controller.abort(signal?.reason); reject(signal?.reason ?? new DOMException("Support discovery was cancelled.", "AbortError")); };
      signal?.addEventListener("abort", cancel, { once: true });
      timer = setTimeout(() => { controller.abort(); reject(new Error("Support discovery deadline elapsed.")); }, timeoutMs);
    });
    return await Promise.race([Promise.resolve().then(() => { signal?.throwIfAborted(); return read(controller.signal); }), interrupted]);
  } catch { signal?.throwIfAborted(); return undefined; }
  finally { if (timer !== undefined) clearTimeout(timer); if (cancel) signal?.removeEventListener("abort", cancel); }
}
