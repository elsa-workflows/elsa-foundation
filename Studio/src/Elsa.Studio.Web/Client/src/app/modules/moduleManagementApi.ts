import {
  studioBackendManagementRegistryPath,
  type ElsaStudioModuleApi,
  type StudioBackendManagementRegistryEnvelope,
  type StudioBackendManagementStatusKind,
  type StudioEndpointContext
} from "../../sdk";

export type HostId = "studio" | "server";
export type RegistryState = "loading" | "ready" | "failed";

export interface HostModel {
  id: HostId;
  label: string;
  runtime: string;
  context: StudioEndpointContext;
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

// The bridge's non-available statuses, plus "unknown" for a Studio-origin failure reaching the bridge itself.
export type BackendRegistryUnavailableKind = Exclude<StudioBackendManagementStatusKind, "available"> | "unknown";

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

export function createModuleManagementHosts(api: ElsaStudioModuleApi): HostModel[] {
  return [
    {
      id: "studio",
      label: "Studio",
      runtime: "Elsa.Studio.Web",
      context: api.host,
      // Studio reads its own registry directly from the Studio origin. This path never touches the backend, so the
      // Studio tab keeps working when the backend host is down.
      readRegistry: () => readStudioRegistry(api.host)
    },
    {
      id: "server",
      label: "Server",
      runtime: "Elsa.Server",
      // The Server tab still keeps the backend context for the read-only slice's out-of-scope mutations (upload,
      // reconcile, prune, feed edits, retention) which stay on their current direct paths. Registry READS, however,
      // now route through the Studio management bridge via api.host — the browser no longer calls the backend registry.
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
    return { kind: "unavailable", status: "unknown", detail: bridgeErrorDetail(error) };
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
    return { kind: "unavailable", status: "unknown", detail: bridgeErrorDetail(error) };
  }
}

function bridgeErrorDetail(error: unknown) {
  return `The registry could not be read: ${getErrorMessage(error)}`;
}

// A human label for the backend-management state a Server-tab registry read landed in. Mirrors the host-health strip's
// vocabulary so the Server tab names the real state instead of a generic "Unavailable".
export function labelForBackendRegistryStatus(status: BackendRegistryUnavailableKind): string {
  switch (status) {
    case "unconfigured": return "Not configured";
    case "unauthorized": return "Unauthorized";
    case "unreachable": return "Unreachable";
    case "degraded": return "Degraded";
    default: return "Unavailable";
  }
}

function defaultBackendRegistryDetail(status: BackendRegistryUnavailableKind): string {
  switch (status) {
    case "unconfigured": return "Backend management is not configured on the Studio host.";
    case "unauthorized": return "The backend rejected the Studio management credential.";
    case "unreachable": return "The backend management surface could not be reached.";
    case "degraded": return "The backend management surface is degraded.";
    default: return "Backend management status is unknown.";
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
