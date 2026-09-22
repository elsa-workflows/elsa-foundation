import {
  studioBackendManagementRegistryPath,
  type ElsaStudioModuleApi,
  type StudioBackendManagementRegistryEnvelope,
  type StudioBackendManagementStatusKind,
  type StudioEndpointContext
} from "../../sdk";
import { isPermissionDenied } from "../hostControlPermissions";

export type HostId = "studio" | "server";
export type RegistryState = "loading" | "ready" | "failed";

export interface HostModel {
  id: HostId;
  label: string;
  runtime: string;
  context: StudioEndpointContext;
  // Whether module-management MUTATIONS (upload, delete, reconcile, prune, feed add/edit/delete, retention save) are
  // available from the browser for this host. Studio mutates its OWN host via the Studio-origin bridge gate. The
  // Server host's mutations go directly to backend host-control endpoints, which the browser can no longer authorize
  // now that it carries no host management key (ADR 0037 / #248) — those are a later bridge slice. Until then the UI
  // hides/disables Server-tab mutation affordances instead of issuing doomed backend requests.
  mutationsAvailable: boolean;
  // Reads this host's module registry. Studio reads its own registry directly; the Server host reads the backend
  // registry through the Studio management bridge (#246, ADR 0037) so the browser never calls backend host-control
  // endpoints and never carries the management key. Either read resolves to a discriminated result so callers render
  // the explicit backend-management state instead of inferring an outage from a thrown fetch.
  readRegistry(): Promise<HostRegistryResult>;
}

// A host registry read outcome. `ready` carries a normalized registry; `unavailable` carries the explicit bridge state
// (unconfigured / unreachable / unauthorized / degraded, or "unknown" when the bridge itself could not be reached) so
// the Server tab can render the real reason and keep the Studio tab working when the backend is down.
export type HostRegistryResult =
  | { kind: "ready"; registry: ModuleManagementRegistryResponse }
  | { kind: "unavailable"; status: BackendRegistryUnavailableKind; detail: string };

// The bridge's non-available statuses, plus "unknown" for a Studio-origin failure reaching the bridge itself, plus
// "forbidden" for a Studio authorization failure (403 — the signed-in user lacks the host-control permission). The
// "forbidden" case is deliberately distinct from every backend-status state: it is "Studio denied you", never "backend
// unavailable" and never a login prompt (#249, ADR 0037).
export type BackendRegistryUnavailableKind = Exclude<StudioBackendManagementStatusKind, "available"> | "unknown" | "forbidden";

export interface ModuleManagementRegistryResponse {
  host: ModuleManagementHost;
  generatedAt: string;
  modules: ModuleManagementModule[];
  packages: ModuleManagementPackage[];
  dropFolderPackages: ModuleManagementDropFolderPackage[];
  feeds: ModuleManagementFeed[];
  retentionPolicy: ModuleManagementRetentionPolicy;
  capabilities: ModuleManagementCapabilities;
  diagnostics: ModuleManagementDiagnostic[];
}

export interface ModuleManagementHost {
  id: string;
  displayName: string;
  runtime: string;
  contentRootPath: string;
}

export interface ModuleManagementModule {
  id: string;
  displayName: string;
  surface: string;
  runtime: string;
  sourceKind: string;
  scope: string;
  version: string;
  status: string;
  compatibility: string;
  packageId?: string | null;
  packageVersion?: string | null;
  contributions: ModuleManagementContribution[];
  diagnostics: ModuleManagementDiagnostic[];
  manifest?: ModuleManagementStudioManifest | null;
}

export interface ModuleManagementStudioManifest {
  entry: string;
  styles: string[];
  capabilities: string[];
}

export interface ModuleManagementContribution {
  type: string;
  id: string;
  label: string;
  status: string;
}

export interface ModuleManagementPackage {
  id: string;
  version: string;
  feedName: string;
  sourceName: string;
  installPath: string;
  manifest?: ModuleManagementPackageManifest | null;
  dependencies: ModuleManagementPackageDependency[];
}

export interface ModuleManagementPackageManifest {
  kind: string;
  path: string;
  content?: unknown;
  text?: string | null;
}

export interface ModuleManagementPackageDependency {
  id: string;
  versionRange: string;
  group?: string | null;
  source: string;
}

export interface ModuleManagementDropFolderPackage {
  fileName: string;
  path: string;
  size: number;
  lastWriteTimeUtc: string;
}

export interface ModuleManagementFeed {
  name: string;
  serviceIndex?: string | null;
  directoryPath?: string | null;
  credentials?: string | null;
  includeAll: boolean;
  includePatterns: string[];
  directory: {
    watch: boolean;
    debounceWindow: string;
  };
}

export interface ModuleManagementRetentionPolicy {
  retainLastNVersions?: number | null;
  retainYoungerThanDays?: number | null;
  mode: string;
  protectLastKnownGood: boolean;
}

export interface ModuleManagementCapabilities {
  canUploadPackages: boolean;
  canManageFeeds: boolean;
  canReconcile: boolean;
  canPrunePackages: boolean;
  feedChangesRequireRestart: boolean;
}

export interface ModuleManagementDiagnostic {
  source: string;
  status: string;
  reason: string;
}

export const hostTabs = [
  { id: "studio", label: "Studio" },
  { id: "server", label: "Server" }
];

export const moduleManagementKeys = {
  all: ["module-management"] as const,
  host: (hostId: HostId) => [...moduleManagementKeys.all, hostId] as const,
  registry: (hostId: HostId) => [...moduleManagementKeys.all, hostId, "registry"] as const,
  packages: (hostId: HostId) => [...moduleManagementKeys.all, hostId, "packages"] as const,
  feeds: (hostId: HostId) => [...moduleManagementKeys.all, hostId, "feeds"] as const
};

const backendRegistryPath = "/_elsa/module-management/registry";

// `canManageStudioHost` reflects whether the signed-in user holds module-management.manage (from usePermissions).
// It PROACTIVELY hides/disables the Studio host's mutation affordances a user can't use; the server-side manage gate
// remains the authority (a stale session never grants a write). Defaults to true so demo/auth-disabled mode — where
// permission checks do not apply and usePermissions reports no permissions — keeps full mutation access.
export function createModuleManagementHosts(api: ElsaStudioModuleApi, canManageStudioHost = true): HostModel[] {
  return [
    {
      id: "studio",
      label: "Studio",
      runtime: "Elsa.Studio.Web",
      context: api.host,
      // Studio mutates its OWN host through the Studio-origin gate — available when the user holds manage.
      mutationsAvailable: canManageStudioHost,
      // Studio reads its own registry directly from the Studio origin. This path never touches the backend, so the
      // Studio tab keeps working when the backend host is down.
      readRegistry: () => readStudioRegistry(api.host)
    },
    {
      id: "server",
      label: "Server",
      runtime: "Elsa.Server",
      // The Server tab keeps the backend context for read-only bridge reads only. Its MUTATIONS (upload, reconcile,
      // prune, feed edits, retention) would go DIRECT to backend host-control endpoints, which the browser can no
      // longer authorize without a host management key (ADR 0037 / #248). They are a later bridge slice, so they are
      // marked unavailable and the UI hides/disables them rather than issuing doomed backend requests.
      mutationsAvailable: false,
      context: api.backend,
      readRegistry: () => readServerRegistryViaBridge(api.host)
    }
  ];
}

// Studio host registry read: direct, from the Studio origin. A thrown fetch (Studio origin unreachable) surfaces as an
// "unknown" unavailable result so the page renders an explicit state rather than a blank surface.
async function readStudioRegistry(host: StudioEndpointContext): Promise<HostRegistryResult> {
  try {
    const registry = await host.http.getJson<Partial<ModuleManagementRegistryResponse>>(backendRegistryPath);
    return { kind: "ready", registry: normalizeModuleManagementRegistry(registry) };
  } catch (error) {
    return registryFailureResult(error);
  }
}

// Server (backend) host registry read: through the Studio management bridge (#246, ADR 0037). The bridge answers with a
// status-bearing envelope, so a non-available backend surfaces its explicit reason (unconfigured / unreachable /
// unauthorized / degraded) instead of a thrown fetch. A failure to reach the bridge itself (a Studio-origin problem)
// surfaces as "unknown".
async function readServerRegistryViaBridge(host: StudioEndpointContext): Promise<HostRegistryResult> {
  try {
    const envelope = await host.http.getJson<StudioBackendManagementRegistryEnvelope<Partial<ModuleManagementRegistryResponse>>>(studioBackendManagementRegistryPath);
    if (envelope.status === "available" && envelope.registry) {
      return { kind: "ready", registry: normalizeModuleManagementRegistry(envelope.registry) };
    }

    return {
      kind: "unavailable",
      status: envelope.status === "available" ? "degraded" : envelope.status,
      detail: envelope.detail?.trim() || defaultBackendRegistryDetail(envelope.status === "available" ? "degraded" : envelope.status)
    };
  } catch (error) {
    return registryFailureResult(error);
  }
}

// Maps a thrown registry read to an explicit unavailable result. A 403 is a Studio authorization failure (the user
// lacks module-management.read), surfaced as the distinct "forbidden" state so the UI renders "you lack permission"
// rather than "backend unavailable". Any other throw is a Studio-origin failure reaching the bridge, surfaced as
// "unknown".
function registryFailureResult(error: unknown): Extract<HostRegistryResult, { kind: "unavailable" }> {
  if (isPermissionDenied(error)) {
    return { kind: "unavailable", status: "forbidden", detail: defaultBackendRegistryDetail("forbidden") };
  }

  return { kind: "unavailable", status: "unknown", detail: `The registry could not be read: ${getErrorMessage(error)}` };
}

// A human label for the backend-management state a Server-tab registry read landed in. Mirrors the host-health strip's
// vocabulary so the Server tab names the real state instead of a generic "Unavailable".
export function labelForBackendRegistryStatus(status: BackendRegistryUnavailableKind): string {
  switch (status) {
    case "unconfigured": return "Not configured";
    case "unauthorized": return "Unauthorized";
    case "unreachable": return "Unreachable";
    case "degraded": return "Degraded";
    case "forbidden": return "Permission required";
    default: return "Unavailable";
  }
}

function defaultBackendRegistryDetail(status: BackendRegistryUnavailableKind): string {
  switch (status) {
    case "unconfigured": return "Privileged host management is not configured on the Studio host. Set Studio:BackendServerBaseUrl (or Studio:BackendBaseUrl for a shared URL) and Studio:BackendModuleManagementApiKey to enable Server module management.";
    case "unauthorized": return "The backend rejected the Studio management credential.";
    case "unreachable": return "The privileged host-management surface could not be reached.";
    case "degraded": return "The privileged host-management surface is degraded.";
    // A Studio authorization failure (403): the signed-in user lacks the module-management.read permission. This is
    // "Studio denied you", not a backend problem.
    case "forbidden": return "You do not have permission to read module management. This requires the module-management.read permission.";
    default: return "Privileged host-management status is unknown.";
  }
}

export function defaultRetentionPolicy(): ModuleManagementRetentionPolicy {
  return {
    retainLastNVersions: 3,
    retainYoungerThanDays: 30,
    mode: "Automatic",
    protectLastKnownGood: true
  };
}

export function normalizeModuleManagementRegistry(value?: Partial<ModuleManagementRegistryResponse> | null): ModuleManagementRegistryResponse {
  const registry = value ?? {};

  return {
    host: normalizeHost(registry.host),
    generatedAt: registry.generatedAt ?? new Date().toISOString(),
    modules: asArray(registry.modules).map(normalizeModule),
    packages: asArray(registry.packages).map(normalizePackage),
    dropFolderPackages: asArray(registry.dropFolderPackages),
    feeds: asArray(registry.feeds).map(normalizeFeed),
    retentionPolicy: normalizeRetentionPolicy(registry.retentionPolicy),
    capabilities: normalizeCapabilities(registry.capabilities),
    diagnostics: asArray(registry.diagnostics).map(normalizeDiagnostic)
  };
}

export function formatFileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export async function uploadPackage(context: StudioEndpointContext, file: File) {
  const body = new FormData();
  body.append("package", file);
  return context.http.postForm("/_elsa/module-management/packages/upload", body);
}

export async function deleteDropFolderPackage(context: StudioEndpointContext, fileName: string) {
  return context.http.deleteJson(`/_elsa/module-management/packages/drop-folder/${encodeURIComponent(fileName)}`);
}

export async function deleteFeed(context: StudioEndpointContext, feedName: string) {
  return context.http.deleteJson(`/_elsa/module-management/feeds/${encodeURIComponent(feedName)}`);
}

export async function addFeed(context: StudioEndpointContext, feed: ModuleManagementFeed) {
  return context.http.postJson("/_elsa/module-management/feeds", feed);
}

export async function updateFeed(context: StudioEndpointContext, feedName: string, feed: ModuleManagementFeed) {
  return context.http.putJson(`/_elsa/module-management/feeds/${encodeURIComponent(feedName)}`, feed);
}

export async function saveRetentionPolicy(context: StudioEndpointContext, policy: ModuleManagementRetentionPolicy) {
  return context.http.putJson("/_elsa/module-management/retention-policy", policy);
}

export async function postJson(context: StudioEndpointContext, url: string, body: unknown) {
  return context.http.postJson(url, body);
}

export function numberOrNull(value: string) {
  if (value.trim() === "") return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

export function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

function normalizeModule(module: Partial<ModuleManagementModule>): ModuleManagementModule {
  return {
    id: module.id ?? "unknown-module",
    displayName: module.displayName ?? module.id ?? "Unknown module",
    surface: module.surface ?? "",
    runtime: module.runtime ?? "",
    sourceKind: module.sourceKind ?? "",
    scope: module.scope ?? "",
    version: module.version ?? "",
    status: module.status ?? "unknown",
    compatibility: module.compatibility ?? "unknown",
    packageId: module.packageId ?? null,
    packageVersion: module.packageVersion ?? null,
    contributions: asArray(module.contributions),
    diagnostics: asArray(module.diagnostics).map(normalizeDiagnostic),
    manifest: module.manifest ? normalizeStudioManifest(module.manifest) : null
  };
}

function normalizeHost(host?: Partial<ModuleManagementHost> | null): ModuleManagementHost {
  return {
    id: host?.id ?? "unknown",
    displayName: host?.displayName ?? host?.id ?? "Unknown host",
    runtime: host?.runtime ?? "unknown",
    contentRootPath: host?.contentRootPath ?? ""
  };
}

function normalizeStudioManifest(manifest: Partial<ModuleManagementStudioManifest>): ModuleManagementStudioManifest {
  return {
    entry: manifest.entry ?? "",
    styles: asArray(manifest.styles),
    capabilities: asArray(manifest.capabilities)
  };
}

function normalizePackage(pkg: Partial<ModuleManagementPackage>): ModuleManagementPackage {
  return {
    id: pkg.id ?? "unknown-package",
    version: pkg.version ?? "",
    feedName: pkg.feedName ?? "",
    sourceName: pkg.sourceName ?? "",
    installPath: pkg.installPath ?? "",
    manifest: pkg.manifest ?? null,
    dependencies: asArray(pkg.dependencies)
  };
}

function normalizeFeed(feed: Partial<ModuleManagementFeed>): ModuleManagementFeed {
  return {
    name: feed.name ?? "unnamed-feed",
    serviceIndex: feed.serviceIndex ?? null,
    directoryPath: feed.directoryPath ?? null,
    credentials: feed.credentials ?? null,
    includeAll: feed.includeAll ?? false,
    includePatterns: asArray(feed.includePatterns),
    directory: {
      watch: feed.directory?.watch ?? false,
      debounceWindow: feed.directory?.debounceWindow ?? "00:00:01"
    }
  };
}

function normalizeRetentionPolicy(policy?: Partial<ModuleManagementRetentionPolicy> | null): ModuleManagementRetentionPolicy {
  const defaults = defaultRetentionPolicy();
  return {
    retainLastNVersions: policy?.retainLastNVersions ?? defaults.retainLastNVersions,
    retainYoungerThanDays: policy?.retainYoungerThanDays ?? defaults.retainYoungerThanDays,
    mode: policy?.mode ?? defaults.mode,
    protectLastKnownGood: policy?.protectLastKnownGood ?? defaults.protectLastKnownGood
  };
}

function normalizeCapabilities(capabilities?: Partial<ModuleManagementCapabilities> | null): ModuleManagementCapabilities {
  return {
    canUploadPackages: capabilities?.canUploadPackages ?? false,
    canManageFeeds: capabilities?.canManageFeeds ?? false,
    canReconcile: capabilities?.canReconcile ?? false,
    canPrunePackages: capabilities?.canPrunePackages ?? false,
    feedChangesRequireRestart: capabilities?.feedChangesRequireRestart ?? false
  };
}

function normalizeDiagnostic(diagnostic: Partial<ModuleManagementDiagnostic>): ModuleManagementDiagnostic {
  return {
    source: diagnostic.source ?? "module-management",
    status: diagnostic.status ?? "unknown",
    reason: diagnostic.reason ?? ""
  };
}

function asArray<T>(value: T[] | readonly T[] | null | undefined): T[] {
  return Array.isArray(value) ? [...value] : [];
}
