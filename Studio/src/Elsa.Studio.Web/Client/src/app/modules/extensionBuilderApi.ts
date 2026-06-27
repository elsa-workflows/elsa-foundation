import { withDefaultHeaders, type StudioEndpointContext } from "../../sdk";

const root = "/_elsa/extension-builder";

export type BuildStatus = "Pending" | "Running" | "Succeeded" | "Failed" | "queued" | "running" | "succeeded" | "failed" | string;
export type RuntimeState = "Loaded" | "PendingRestart" | "FailedReconciliation" | string;
export type PromotionRejectionCategory = "Duplicate" | "InvalidManifest" | "DependencyPolicy" | "MalformedPackage" | string;
export type ProjectFileType = "file" | "folder" | "Source" | "Project" | "Manifest" | "Configuration" | "Other" | string;
export type DiagnosticSeverity = "Error" | "Warning" | "Info" | "error" | "warning" | "info" | string;

export interface ExtensionBuilderCapabilities {
  canCreateWorkspace: boolean;
  canEditFiles: boolean;
  canBuild: boolean;
  canPromote: boolean;
  canRollback: boolean;
}

export interface ExtensionWorkspace {
  id: string;
  name: string;
  owner?: string | null;
  createdAt?: string | null;
  updatedAt?: string | null;
  projects: ExtensionProject[];
}

export interface ExtensionRepositorySummary {
  id: string;
  name: string;
  owner?: string | null;
  activeBranch?: string | null;
  isDirty: boolean;
  remoteState: string;
  latestBuildStatus?: BuildStatus | null;
  attentionCount: number;
  projectCount: number;
  updatedAt?: string | null;
}

export interface ExtensionProject {
  id: string;
  workspaceId?: string | null;
  name: string;
  templateId?: string | null;
  packageId: string;
  packageVersion: string;
  currentRevision?: string | null;
  latestBuildStatus?: BuildStatus | null;
  runtimeStatus?: RuntimeState | null;
  builds?: BuildResult[];
}

export interface ExtensionTemplate {
  id: string;
  name: string;
  description?: string | null;
  tags?: string[];
  primary?: boolean;
  defaultPackageId?: string | null;
  defaultPackageVersion?: string | null;
  defaultTargetFramework?: string | null;
}

export interface ProjectFile {
  path: string;
  type: ProjectFileType;
  size?: number | null;
  updatedAt?: string | null;
  content?: string | null;
}

export interface BuildRequest {
  projectId?: string;
  revision?: string | null;
}

export interface BuildResult {
  id: string;
  projectId?: string;
  revision?: string | null;
  status: BuildStatus;
  startedAt?: string | null;
  finishedAt?: string | null;
  diagnostics: BuildDiagnostic[];
  artifact?: BuildArtifact | null;
}

export interface BuildDiagnostic {
  severity: DiagnosticSeverity;
  message: string;
  filePath?: string | null;
  line?: number | null;
  column?: number | null;
}

export interface BuildArtifact {
  id: string;
  buildId?: string | null;
  packageId: string;
  version: string;
  fileName?: string | null;
  size?: number | null;
}

export interface PackagePromotionRequest {
  buildId: string;
  targetFeed?: string | null;
}

export interface PackagePromotionResult {
  accepted: boolean;
  category?: PromotionRejectionCategory | null;
  message?: string | null;
  publishedPackage?: PublishedPackage | null;
  requiresReload?: boolean;
  requiresRestart?: boolean;
}

export interface ExtensionRuntimeStatus {
  projectId?: string;
  packageId: string;
  version?: string | null;
  state: RuntimeState;
  features: RuntimeFeature[];
  history: RuntimeVersion[];
  diagnostics: RuntimeDiagnostic[];
}

export interface RuntimeFeature {
  id: string;
  label: string;
  type?: string | null;
  status?: string | null;
}

export interface RuntimeVersion {
  version: string;
  state?: RuntimeState | null;
  available: boolean;
  promotedAt?: string | null;
}

export interface RuntimeDiagnostic {
  severity?: DiagnosticSeverity | null;
  message: string;
  source?: string | null;
}

export interface PublishedPackage {
  packageId: string;
  version: string;
  feedName: string;
  path: string;
}

export interface CreateWorkspaceRequest {
  name: string;
}

export interface SelectWorkingCopyRequest {
  sessionId: string;
  branchName?: string | null;
  allowProtectedBranchEdit: boolean;
}

export interface ExtensionWorkingCopySummary {
  id: string;
  workspaceId: string;
  owner?: string | null;
  sessionId: string;
  branchName: string;
  isActive: boolean;
  isProtectedBranch: boolean;
  isDirty: boolean;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface CreateProjectRequest {
  templateId: string;
  name: string;
  packageId: string;
  packageVersion: string;
}

export interface WriteProjectFileRequest {
  content: string;
}

export async function getCapabilities(context: StudioEndpointContext) {
  return normalizeCapabilities(await context.http.getJson<Partial<ExtensionBuilderCapabilities>>(`${root}/capabilities`));
}

export async function listWorkspaces(context: StudioEndpointContext) {
  const response = await context.http.getJson<RawExtensionWorkspace[] | { workspaces: RawExtensionWorkspace[] }>(`${root}/workspaces`);
  const workspaces = Array.isArray(response) ? response : response.workspaces;
  return Promise.all(workspaces.map(workspace => hydrateWorkspace(context, workspace)));
}

export async function listRepositories(context: StudioEndpointContext) {
  const response = await context.http.getJson<RawExtensionRepositorySummary[] | { repositories: RawExtensionRepositorySummary[] }>(`${root}/repositories`);
  const repositories = Array.isArray(response) ? response : response.repositories;
  return repositories.map(normalizeRepositorySummary);
}

export async function createWorkspace(context: StudioEndpointContext, request: CreateWorkspaceRequest) {
  return normalizeWorkspace(await context.http.postJson<RawExtensionWorkspace>(`${root}/workspaces`, { displayName: request.name }), []);
}

export async function getWorkspace(context: StudioEndpointContext, workspaceId: string) {
  return hydrateWorkspace(context, await context.http.getJson<RawExtensionWorkspace>(`${root}/workspaces/${segment(workspaceId)}`));
}

export async function deleteWorkspace(context: StudioEndpointContext, workspaceId: string) {
  return requestJson(context, `${root}/workspaces/${segment(workspaceId)}`, { method: "DELETE" });
}

export async function listWorkingCopies(context: StudioEndpointContext, workspaceId: string, sessionId?: string | null) {
  const query = sessionId ? `?sessionId=${encodeURIComponent(sessionId)}` : "";
  const response = await context.http.getJson<RawExtensionWorkingCopySummary[] | { workingCopies: RawExtensionWorkingCopySummary[] }>(`${root}/workspaces/${segment(workspaceId)}/working-copies${query}`);
  const workingCopies = Array.isArray(response) ? response : response.workingCopies;
  return workingCopies.map(normalizeWorkingCopySummary);
}

export async function selectWorkingCopy(context: StudioEndpointContext, workspaceId: string, request: SelectWorkingCopyRequest) {
  return normalizeWorkingCopySummary(await context.http.postJson<RawExtensionWorkingCopySummary>(`${root}/workspaces/${segment(workspaceId)}/working-copies/select`, request));
}

export async function listTemplates(context: StudioEndpointContext) {
  const response = await context.http.getJson<RawExtensionTemplate[] | { templates: RawExtensionTemplate[] }>(`${root}/templates`);
  const templates = Array.isArray(response) ? response : response.templates;
  return templates.map(normalizeTemplate);
}

export async function createProject(context: StudioEndpointContext, workspaceId: string, request: CreateProjectRequest) {
  return normalizeProject(await context.http.postJson<RawExtensionProject>(`${root}/workspaces/${segment(workspaceId)}/projects`, {
    templateId: request.templateId,
    packageId: request.packageId,
    packageVersion: request.packageVersion,
    displayName: request.name
  }));
}

export async function getProject(context: StudioEndpointContext, _workspaceId: string, projectId: string) {
  return normalizeProject(await context.http.getJson<RawExtensionProject>(`${root}/projects/${segment(projectId)}`));
}

export async function deleteProject(context: StudioEndpointContext, _workspaceId: string, projectId: string) {
  return requestJson(context, `${root}/projects/${segment(projectId)}`, { method: "DELETE" });
}

export async function listProjectFiles(context: StudioEndpointContext, _workspaceId: string, projectId: string) {
  const response = await context.http.getJson<RawProjectFile[] | { files: RawProjectFile[] }>(`${projectRoot(projectId)}/files`);
  const files = Array.isArray(response) ? response : response.files;
  return files.map(normalizeProjectFile);
}

export async function readProjectFile(context: StudioEndpointContext, _workspaceId: string, projectId: string, path: string) {
  return normalizeProjectFile(await context.http.getJson<RawProjectFile>(`${projectRoot(projectId)}/files/${filePath(path)}`));
}

export async function writeProjectFile(context: StudioEndpointContext, _workspaceId: string, projectId: string, path: string, request: WriteProjectFileRequest) {
  return normalizeProjectFile(await requestJson<RawProjectFile>(context, `${projectRoot(projectId)}/files/${filePath(path)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", "Accept": "application/json" },
    body: JSON.stringify(request)
  }));
}

export async function deleteProjectFile(context: StudioEndpointContext, _workspaceId: string, projectId: string, path: string) {
  return requestJson(context, `${projectRoot(projectId)}/files/${filePath(path)}`, { method: "DELETE" });
}

export async function submitBuild(context: StudioEndpointContext, _workspaceId: string, projectId: string, _request: BuildRequest = {}) {
  return normalizeBuild(await context.http.postJson<RawBuildResult>(`${projectRoot(projectId)}/builds`, {}));
}

export async function getBuild(context: StudioEndpointContext, _workspaceId: string, _projectId: string, buildId: string) {
  return normalizeBuild(await context.http.getJson<RawBuildResult>(`${root}/builds/${segment(buildId)}`));
}

export async function getBuildLog(context: StudioEndpointContext, _workspaceId: string, _projectId: string, buildId: string) {
  const response = await fetch(new URL(`${root}/builds/${segment(buildId)}/log`, context.baseUrl).toString(), withDefaultHeaders(context.headers, {
    cache: "no-store",
    headers: { "Accept": "text/plain, application/json" }
  }));
  if (!response.ok) throw new Error(await response.text() || `Request failed with ${response.status}.`);
  const text = await response.text();
  if (response.headers.get("content-type")?.includes("application/json")) {
    const payload = JSON.parse(text) as { log?: string; lines?: string[] };
    return payload.log ?? payload.lines?.join("\n") ?? "";
  }
  return text;
}

export async function promoteBuild(context: StudioEndpointContext, _workspaceId: string, _projectId: string, buildId: string, request: PackagePromotionRequest) {
  return normalizePromotionResult(await context.http.postJson<RawPackagePromotionResult>(`${root}/builds/${segment(buildId)}/promote`, request.targetFeed ? { targetFeed: request.targetFeed } : {}));
}

export async function getRuntimeStatus(context: StudioEndpointContext, _workspaceId: string, projectId: string) {
  return normalizeRuntimeStatus(await context.http.getJson<RawExtensionRuntimeStatus>(`${projectRoot(projectId)}/runtime-status`));
}

export async function rollbackPackage(context: StudioEndpointContext, workspaceId: string, projectId: string, version: string) {
  await context.http.postJson<RawPackagePromotionResult>(`${projectRoot(projectId)}/rollback`, { version });
  return getRuntimeStatus(context, workspaceId, projectId);
}

export async function retryReconciliation(context: StudioEndpointContext, workspaceId: string, projectId: string) {
  await context.http.postJson<RawExtensionBuilderOperationResponse>(`${projectRoot(projectId)}/retry-reconcile`, {});
  return getRuntimeStatus(context, workspaceId, projectId);
}

export function isTrusted(capabilities: ExtensionBuilderCapabilities | null) {
  return !!capabilities && Object.values(capabilities).some(Boolean);
}

function normalizeCapabilities(value: Partial<ExtensionBuilderCapabilities>): ExtensionBuilderCapabilities {
  return {
    canCreateWorkspace: value.canCreateWorkspace ?? false,
    canEditFiles: value.canEditFiles ?? false,
    canBuild: value.canBuild ?? false,
    canPromote: value.canPromote ?? false,
    canRollback: value.canRollback ?? false
  };
}

async function requestJson<T = Record<string, never>>(context: StudioEndpointContext, url: string, init: RequestInit) {
  const response = await fetch(new URL(url, context.baseUrl).toString(), withDefaultHeaders(context.headers, init));
  if (!response.ok) throw new Error(await response.text() || `Request failed with ${response.status}.`);
  const text = await response.text();
  return (text ? JSON.parse(text) : {}) as T;
}

async function hydrateWorkspace(context: StudioEndpointContext, workspace: RawExtensionWorkspace) {
  if (workspace.projects) {
    return normalizeWorkspace(workspace, workspace.projects.map(normalizeProject));
  }

  const projects = await Promise.all((workspace.projectIds ?? []).map(projectId => getProject(context, workspace.id, projectId).catch(() => null)));
  return normalizeWorkspace(workspace, projects.filter((project): project is ExtensionProject => project !== null));
}

function normalizeWorkspace(workspace: RawExtensionWorkspace, projects: ExtensionProject[]): ExtensionWorkspace {
  return {
    id: workspace.id,
    name: workspace.name ?? workspace.displayName,
    owner: workspace.owner ?? workspace.ownerId ?? workspace.trustContext,
    createdAt: workspace.createdAt,
    updatedAt: workspace.updatedAt,
    projects
  };
}

function normalizeRepositorySummary(repository: RawExtensionRepositorySummary): ExtensionRepositorySummary {
  return {
    id: repository.id,
    name: repository.name ?? repository.displayName,
    owner: repository.owner ?? repository.ownerId,
    activeBranch: repository.activeBranch,
    isDirty: repository.isDirty ?? false,
    remoteState: repository.remoteState ?? "unknown",
    latestBuildStatus: repository.latestBuildStatus == null ? repository.latestBuildStatus : normalizeBuildStatus(repository.latestBuildStatus),
    attentionCount: repository.attentionCount ?? 0,
    projectCount: repository.projectCount ?? repository.projects ?? 0,
    updatedAt: repository.updatedAt
  };
}

function normalizeWorkingCopySummary(workingCopy: RawExtensionWorkingCopySummary): ExtensionWorkingCopySummary {
  return {
    id: workingCopy.id,
    workspaceId: workingCopy.workspaceId,
    owner: workingCopy.owner ?? workingCopy.ownerId,
    sessionId: workingCopy.sessionId,
    branchName: workingCopy.branchName,
    isActive: workingCopy.isActive ?? false,
    isProtectedBranch: workingCopy.isProtectedBranch ?? false,
    isDirty: workingCopy.isDirty ?? false,
    createdAt: workingCopy.createdAt,
    updatedAt: workingCopy.updatedAt
  };
}

function normalizeProject(project: RawExtensionProject): ExtensionProject {
  return {
    id: project.id,
    workspaceId: project.workspaceId,
    name: project.name ?? project.displayName ?? project.manifest?.displayName ?? project.packageId,
    templateId: project.templateId,
    packageId: project.packageId,
    packageVersion: project.packageVersion,
    currentRevision: project.currentRevision ?? project.currentSourceRevisionId,
    latestBuildStatus: project.latestBuildStatus == null ? project.latestBuildStatus : normalizeBuildStatus(project.latestBuildStatus),
    runtimeStatus: project.runtimeStatus == null ? project.runtimeStatus : normalizeRuntimeState(project.runtimeStatus),
    builds: project.builds?.map(normalizeBuild)
  };
}

function normalizeTemplate(template: RawExtensionTemplate): ExtensionTemplate {
  const kind = normalizeTemplateKind(template.kind);
  return {
    id: template.id,
    name: template.name ?? template.displayName,
    description: template.description,
    tags: template.tags ?? [kind],
    primary: template.primary ?? kind === "ElsaActivityModule",
    defaultPackageId: template.defaultPackageId,
    defaultPackageVersion: template.defaultPackageVersion,
    defaultTargetFramework: template.defaultTargetFramework
  };
}

function normalizeProjectFile(file: RawProjectFile): ProjectFile {
  return {
    path: file.path,
    type: normalizeProjectFileType(file.type ?? file.kind),
    size: file.size,
    updatedAt: file.updatedAt,
    content: file.content
  };
}

function normalizeBuild(build: RawBuildResult): BuildResult {
  return {
    id: build.id,
    projectId: build.projectId,
    revision: build.revision ?? build.sourceRevisionId,
    status: normalizeBuildStatus(build.status),
    startedAt: build.startedAt ?? build.createdAt,
    finishedAt: build.finishedAt ?? build.completedAt,
    diagnostics: (build.diagnostics ?? []).map(diagnostic => ({
      severity: normalizeDiagnosticSeverity(diagnostic.severity),
      message: diagnostic.message,
      filePath: diagnostic.filePath ?? diagnostic.file,
      line: diagnostic.line,
      column: diagnostic.column
    })),
    artifact: build.artifact
  };
}

function normalizePromotionResult(result: RawPackagePromotionResult): PackagePromotionResult {
  const status = normalizePromotionStatus(result.status);
  return {
    accepted: (result.accepted ?? status === "Accepted") === true,
    category: result.category ?? normalizePromotionRejection(result.rejectionReason),
    message: result.message ?? result.reconcileOutcome?.reason ?? null,
    publishedPackage: result.publishedPackage,
    requiresReload: result.requiresReload,
    requiresRestart: result.requiresRestart
  };
}

function normalizeRuntimeStatus(status: RawExtensionRuntimeStatus): ExtensionRuntimeStatus {
  if ("state" in status && "features" in status && "history" in status && "diagnostics" in status) {
    const normalized = status as RuntimeStatusShape;
    return {
      ...normalized,
      state: normalizeRuntimeState(normalized.state) ?? "PendingRestart",
      features: normalized.features ?? [],
      history: (normalized.history ?? []).map(version => ({
        ...version,
        state: normalizeRuntimeState(version.state) ?? null
      })),
      diagnostics: (normalized.diagnostics ?? []).map(diagnostic => ({
        ...diagnostic,
        severity: diagnostic.severity == null ? diagnostic.severity : normalizeDiagnosticSeverity(diagnostic.severity)
      }))
    };
  }

  const activePackage = status.packages.find(packageStatus => packageStatus.version === status.activeVersion) ?? status.packages[0] ?? null;
  const activeState = normalizeRuntimeState(activePackage?.state);
  return {
    projectId: status.projectId,
    packageId: status.packageId,
    version: status.activeVersion ?? activePackage?.version ?? null,
    state: activeState ?? "PendingRestart",
    features: activePackage?.contributions.map(contribution => ({
      id: contribution.id,
      label: contribution.label,
      type: contribution.type,
      status: contribution.status
    })) ?? [],
    history: status.availableRollbackVersions.map(version => ({
      version,
      state: normalizeRuntimeState(status.packages.find(packageStatus => packageStatus.version === version)?.state) ?? null,
      available: true
    })),
    diagnostics: [
      ...status.packages.filter(packageStatus => packageStatus.reason).map(packageStatus => ({
        severity: normalizeRuntimeState(packageStatus.state) === "FailedReconciliation" ? "error" : "warning",
        message: packageStatus.reason!,
        source: `${packageStatus.packageId} ${packageStatus.version}`
      })),
      ...(status.lastReconcileReason ? [{ severity: "warning", message: status.lastReconcileReason, source: status.lastReconcileOutcome?.correlationId ?? "reconcile" }] : [])
    ]
  };
}

function normalizeBuildStatus(value: BuildStatus | number): BuildStatus {
  return enumName(value, ["Pending", "Running", "Succeeded", "Failed"]);
}

function normalizeDiagnosticSeverity(value?: DiagnosticSeverity | number): DiagnosticSeverity {
  return enumName(value, ["Info", "Warning", "Error"]);
}

function normalizeProjectFileType(value?: ProjectFileType | number | null): ProjectFileType {
  if (typeof value === "string" && ["folder", "directory"].includes(value.toLowerCase())) return "folder";
  return "file";
}

function normalizePromotionStatus(value?: string | number | null) {
  return enumName(value, ["Accepted", "Rejected"]);
}

function normalizePromotionRejection(value?: PromotionRejectionCategory | number | null): PromotionRejectionCategory | null | undefined {
  return value == null ? value : enumName(value, ["Duplicate", "InvalidManifest", "DependencyPolicy", "MalformedPackage"]);
}

function normalizeRuntimeState(value?: RuntimeState | number | null): RuntimeState | null | undefined {
  return value == null ? value : enumName(value, ["Loaded", "PendingRestart", "FailedReconciliation"]);
}

function normalizeTemplateKind(value?: string | number | null) {
  return enumName(value, ["ElsaActivityModule", "GenericDotNet"]);
}

function enumName<T extends string>(value: T | number | null | undefined, names: readonly T[]): T {
  if (typeof value === "number") return names[value] ?? (String(value) as T);
  return (value ?? "") as T;
}

function projectRoot(projectId: string) {
  return `${root}/projects/${segment(projectId)}`;
}

function segment(value: string) {
  return encodeURIComponent(value);
}

function filePath(path: string) {
  return encodeURIComponent(path);
}

interface RawExtensionWorkspace {
  id: string;
  name?: string;
  displayName: string;
  owner?: string | null;
  ownerId?: string | null;
  trustContext?: string | null;
  createdAt?: string | null;
  updatedAt?: string | null;
  projectIds?: string[];
  projects?: RawExtensionProject[];
}

interface RawExtensionRepositorySummary {
  id: string;
  name?: string;
  displayName?: string;
  owner?: string | null;
  ownerId?: string | null;
  activeBranch?: string | null;
  isDirty?: boolean | null;
  remoteState?: string | null;
  latestBuildStatus?: BuildStatus | number | null;
  attentionCount?: number | null;
  projectCount?: number | null;
  projects?: number | null;
  updatedAt?: string | null;
}

interface RawExtensionWorkingCopySummary {
  id: string;
  workspaceId: string;
  owner?: string | null;
  ownerId?: string | null;
  sessionId: string;
  branchName: string;
  isActive?: boolean | null;
  isProtectedBranch?: boolean | null;
  isDirty?: boolean | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

interface RawExtensionProject {
  id: string;
  workspaceId?: string | null;
  name?: string;
  displayName?: string;
  templateId?: string | null;
  packageId: string;
  packageVersion: string;
  currentRevision?: string | null;
  currentSourceRevisionId?: string | null;
  latestBuildStatus?: BuildStatus | number | null;
  runtimeStatus?: RuntimeState | number | null;
  manifest?: { displayName?: string | null };
  builds?: RawBuildResult[];
}

interface RawExtensionTemplate {
  id: string;
  kind?: string | number;
  name?: string;
  displayName: string;
  description?: string | null;
  defaultPackageId?: string | null;
  defaultPackageVersion?: string | null;
  defaultTargetFramework?: string | null;
  tags?: string[];
  primary?: boolean;
}

interface RawProjectFile {
  path: string;
  type?: ProjectFileType | number;
  kind?: ProjectFileType | number;
  size?: number | null;
  updatedAt?: string | null;
  content?: string | null;
}

interface RawBuildResult {
  id: string;
  projectId?: string;
  revision?: string | null;
  sourceRevisionId?: string | null;
  status: BuildStatus | number;
  startedAt?: string | null;
  finishedAt?: string | null;
  createdAt?: string | null;
  completedAt?: string | null;
  diagnostics?: RawBuildDiagnostic[];
  artifact?: BuildArtifact | null;
}

interface RawPackagePromotionResult {
  status?: "Accepted" | "Rejected" | string | number;
  accepted?: boolean;
  category?: PromotionRejectionCategory | null;
  rejectionReason?: PromotionRejectionCategory | number | null;
  message?: string | null;
  publishedPackage?: PublishedPackage | null;
  reconcileOutcome?: RawReconcileOutcome | null;
  requiresReload?: boolean;
  requiresRestart?: boolean;
}

interface RawExtensionRuntimeStatus {
  projectId?: string;
  packageId: string;
  activeVersion?: string | null;
  packages: RawExtensionRuntimePackageStatus[];
  availableRollbackVersions: string[];
  lastReconcileOutcome?: RawReconcileOutcome | null;
  lastReconcileReason?: string | null;
}

interface RawExtensionRuntimePackageStatus {
  packageId: string;
  version: string;
  state: RuntimeState | number;
  contributions: RuntimeFeature[];
  requiresReload?: boolean;
  requiresRestart?: boolean;
  reason?: string | null;
}

interface RawBuildDiagnostic {
  severity?: DiagnosticSeverity | number;
  message: string;
  filePath?: string | null;
  file?: string | null;
  line?: number | null;
  column?: number | null;
}

interface RawReconcileOutcome {
  outcome: string;
  correlationId: string;
  reason?: string | null;
  isDegraded?: boolean;
  failedPackages?: string[];
}

interface RawExtensionBuilderOperationResponse {
  operation: string;
  reconcileOutcome: RawReconcileOutcome;
  requiresReload: boolean;
  requiresRestart: boolean;
  message: string;
}

type RuntimeStatusShape = Omit<ExtensionRuntimeStatus, "state" | "history" | "diagnostics"> & {
  state: RuntimeState | number;
  history?: Array<RuntimeVersion & { state?: RuntimeState | number | null }>;
  diagnostics?: Array<RuntimeDiagnostic & { severity?: DiagnosticSeverity | number | null }>;
};
