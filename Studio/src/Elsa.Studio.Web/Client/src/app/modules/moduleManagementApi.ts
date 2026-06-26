import type { ElsaStudioModuleApi, StudioEndpointContext } from "../../sdk";

export type HostId = "studio" | "server";
export type RegistryState = "loading" | "ready" | "failed";

export interface HostModel {
  id: HostId;
  label: string;
  runtime: string;
  context: StudioEndpointContext;
}

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
  registry: (hostId: HostId) => [...moduleManagementKeys.all, hostId, "registry"] as const,
  packages: (hostId: HostId) => [...moduleManagementKeys.all, hostId, "packages"] as const,
  feeds: (hostId: HostId) => [...moduleManagementKeys.all, hostId, "feeds"] as const
};

export function createModuleManagementHosts(api: ElsaStudioModuleApi): HostModel[] {
  return [
    { id: "studio", label: "Studio", runtime: "Elsa.Studio.Web", context: api.host },
    { id: "server", label: "Server", runtime: "Elsa.Server", context: api.backend }
  ];
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
