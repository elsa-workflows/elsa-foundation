import { withDefaultHeaders, type StudioEndpointContext } from "../../sdk";

const root = "/_elsa/extension-builder";

export type BuildStatus = "queued" | "running" | "succeeded" | "failed" | string;
export type RuntimeState = "Loaded" | "PendingRestart" | "FailedReconciliation" | string;
export type PromotionRejectionCategory = "duplicate" | "invalid-manifest" | "dependency-policy" | "malformed-package" | string;
export type ProjectFileType = "file" | "folder" | string;
export type DiagnosticSeverity = "error" | "warning" | "info" | string;

export interface ExtensionBuilderCapabilities {
  "can-create-workspace": boolean;
  "can-edit-files": boolean;
  "can-build": boolean;
  "can-promote": boolean;
  "can-rollback": boolean;
}

export interface ExtensionWorkspace {
  id: string;
  name: string;
  owner?: string | null;
  createdAt?: string | null;
  updatedAt?: string | null;
  projects: ExtensionProject[];
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
  artifactId?: string | null;
}

export interface PackagePromotionResult {
  accepted: boolean;
  category?: PromotionRejectionCategory | null;
  message?: string | null;
  runtimeStatus?: ExtensionRuntimeStatus | null;
}

export interface ExtensionRuntimeStatus {
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

export interface CreateWorkspaceRequest {
  name: string;
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
  const response = await context.http.getJson<ExtensionWorkspace[] | { workspaces: ExtensionWorkspace[] }>(`${root}/workspaces`);
  return Array.isArray(response) ? response : response.workspaces;
}

export async function createWorkspace(context: StudioEndpointContext, request: CreateWorkspaceRequest) {
  return context.http.postJson<ExtensionWorkspace>(`${root}/workspaces`, request);
}

export async function getWorkspace(context: StudioEndpointContext, workspaceId: string) {
  return context.http.getJson<ExtensionWorkspace>(`${root}/workspaces/${segment(workspaceId)}`);
}

export async function deleteWorkspace(context: StudioEndpointContext, workspaceId: string) {
  return requestJson(context, `${root}/workspaces/${segment(workspaceId)}`, { method: "DELETE" });
}

export async function listTemplates(context: StudioEndpointContext) {
  const response = await context.http.getJson<ExtensionTemplate[] | { templates: ExtensionTemplate[] }>(`${root}/templates`);
  return Array.isArray(response) ? response : response.templates;
}

export async function createProject(context: StudioEndpointContext, workspaceId: string, request: CreateProjectRequest) {
  return context.http.postJson<ExtensionProject>(`${root}/workspaces/${segment(workspaceId)}/projects`, request);
}

export async function getProject(context: StudioEndpointContext, workspaceId: string, projectId: string) {
  return context.http.getJson<ExtensionProject>(`${root}/workspaces/${segment(workspaceId)}/projects/${segment(projectId)}`);
}

export async function deleteProject(context: StudioEndpointContext, workspaceId: string, projectId: string) {
  return requestJson(context, `${root}/workspaces/${segment(workspaceId)}/projects/${segment(projectId)}`, { method: "DELETE" });
}

export async function listProjectFiles(context: StudioEndpointContext, workspaceId: string, projectId: string) {
  const response = await context.http.getJson<ProjectFile[] | { files: ProjectFile[] }>(`${projectRoot(workspaceId, projectId)}/files`);
  return Array.isArray(response) ? response : response.files;
}

export async function readProjectFile(context: StudioEndpointContext, workspaceId: string, projectId: string, path: string) {
  return context.http.getJson<ProjectFile>(`${projectRoot(workspaceId, projectId)}/files/${filePath(path)}`);
}

export async function writeProjectFile(context: StudioEndpointContext, workspaceId: string, projectId: string, path: string, request: WriteProjectFileRequest) {
  return requestJson<ProjectFile>(context, `${projectRoot(workspaceId, projectId)}/files/${filePath(path)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", "Accept": "application/json" },
    body: JSON.stringify(request)
  });
}

export async function deleteProjectFile(context: StudioEndpointContext, workspaceId: string, projectId: string, path: string) {
  return requestJson(context, `${projectRoot(workspaceId, projectId)}/files/${filePath(path)}`, { method: "DELETE" });
}

export async function submitBuild(context: StudioEndpointContext, workspaceId: string, projectId: string, request: BuildRequest = {}) {
  return context.http.postJson<BuildResult>(`${projectRoot(workspaceId, projectId)}/builds`, request);
}

export async function getBuild(context: StudioEndpointContext, workspaceId: string, projectId: string, buildId: string) {
  return context.http.getJson<BuildResult>(`${projectRoot(workspaceId, projectId)}/builds/${segment(buildId)}`);
}

export async function getBuildLog(context: StudioEndpointContext, workspaceId: string, projectId: string, buildId: string) {
  const response = await fetch(new URL(`${projectRoot(workspaceId, projectId)}/builds/${segment(buildId)}/log`, context.baseUrl).toString(), withDefaultHeaders(context.headers, {
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

export async function getBuildArtifact(context: StudioEndpointContext, workspaceId: string, projectId: string, buildId: string) {
  return context.http.getJson<BuildArtifact>(`${projectRoot(workspaceId, projectId)}/builds/${segment(buildId)}/artifact`);
}

export async function promoteBuild(context: StudioEndpointContext, workspaceId: string, projectId: string, buildId: string, request: PackagePromotionRequest) {
  return context.http.postJson<PackagePromotionResult>(`${projectRoot(workspaceId, projectId)}/builds/${segment(buildId)}/promote`, request);
}

export async function getRuntimeStatus(context: StudioEndpointContext, workspaceId: string, projectId: string) {
  return context.http.getJson<ExtensionRuntimeStatus>(`${projectRoot(workspaceId, projectId)}/runtime-status`);
}

export async function rollbackPackage(context: StudioEndpointContext, workspaceId: string, projectId: string, version: string) {
  return context.http.postJson<ExtensionRuntimeStatus>(`${projectRoot(workspaceId, projectId)}/runtime-status/rollback`, { version });
}

export async function retryReconciliation(context: StudioEndpointContext, workspaceId: string, projectId: string) {
  return context.http.postJson<ExtensionRuntimeStatus>(`${projectRoot(workspaceId, projectId)}/runtime-status/retry-reconciliation`, {});
}

export function isTrusted(capabilities: ExtensionBuilderCapabilities | null) {
  return !!capabilities && Object.values(capabilities).some(Boolean);
}

function normalizeCapabilities(value: Partial<ExtensionBuilderCapabilities>): ExtensionBuilderCapabilities {
  return {
    "can-create-workspace": value["can-create-workspace"] ?? false,
    "can-edit-files": value["can-edit-files"] ?? false,
    "can-build": value["can-build"] ?? false,
    "can-promote": value["can-promote"] ?? false,
    "can-rollback": value["can-rollback"] ?? false
  };
}

async function requestJson<T = Record<string, never>>(context: StudioEndpointContext, url: string, init: RequestInit) {
  const response = await fetch(new URL(url, context.baseUrl).toString(), withDefaultHeaders(context.headers, init));
  if (!response.ok) throw new Error(await response.text() || `Request failed with ${response.status}.`);
  const text = await response.text();
  return (text ? JSON.parse(text) : {}) as T;
}

function projectRoot(workspaceId: string, projectId: string) {
  return `${root}/workspaces/${segment(workspaceId)}/projects/${segment(projectId)}`;
}

function segment(value: string) {
  return encodeURIComponent(value);
}

function filePath(path: string) {
  return encodeURIComponent(path);
}
