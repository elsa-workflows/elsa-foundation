import type { StudioCodeDiagnostic, StudioCodeDiagnosticSeverity, StudioCodeDocument } from "@elsa-workflows/studio-code-editor";
import type { StudioStatusTone } from "../../ui";
import { isPermissionDenied } from "../../hostControlPermissions";
import {
  readManagementBridgeFailure,
  type BuildDiagnostic,
  type BuildResult,
  type ExtensionProject,
  type ExtensionRuntimeStatus,
  type ExtensionTemplate,
  type ExtensionTemplateParameter,
  type ExtensionWorkspace,
  type PackagePromotionResult,
  type RepositoryFileSummary
} from "../extensionBuilderApi";
import { getErrorMessage } from "../moduleManagementApi";
import type { EditorTab, ProjectDraft, TemplateApplicationDraft } from "./types";

// Maps a thrown Extension Builder error to a user-facing message: a Studio-management-bridge infrastructure failure
// (503/504 envelope) names the backend-management state, a Studio 403 names the missing host-control permission, and
// anything else falls through to the generic error message the module already used.
export function describeExtensionBuilderError(error: unknown): string {
  const bridgeFailure = readManagementBridgeFailure(error);
  if (bridgeFailure) return `Backend management is ${bridgeFailure.kind}: ${bridgeFailure.detail}`;
  if (isPermissionDenied(error)) {
    return "You do not have permission to perform this Extension Builder action. This requires the extension-builder.read permission for reads and extension-builder.manage for changes.";
  }
  return getErrorMessage(error);
}

export function patchProject(workspaces: ExtensionWorkspace[], workspaceId: string, project: ExtensionProject) {
  return workspaces.map(workspace => workspace.id === workspaceId
    ? { ...workspace, projects: workspace.projects.map(item => item.id === project.id ? project : item) }
    : workspace);
}

export function isCurrentSelection(selected: { workspaceId: string; projectId: string }, workspaceId: string, projectId: string) {
  return selected.workspaceId === workspaceId && selected.projectId === projectId;
}

export function canApplyProjectState(
  mounted: boolean,
  requestId: number,
  currentRequestId: number,
  selected: { workspaceId: string; projectId: string },
  workspaceId: string,
  projectId: string
) {
  return mounted && requestId === currentRequestId && isCurrentSelection(selected, workspaceId, projectId);
}

export function upsertFile<T extends { path: string }>(files: T[], file: T) {
  return files.some(item => item.path === file.path) ? files.map(item => item.path === file.path ? file : item) : [...files, file];
}

export function upsertEditorTab(tabs: EditorTab[], tab: EditorTab) {
  return tabs.some(item => item.path === tab.path) ? tabs.map(item => item.path === tab.path ? tab : item) : [...tabs, tab];
}

export function mergeBuildHistory(builds: BuildResult[], build: BuildResult) {
  return [build, ...builds.filter(item => item.id !== build.id)];
}

export function validateProjectDraft(draft: ProjectDraft) {
  if (!draft.templateId) return "Select a project template.";
  if (!draft.name.trim()) return "Project name is required.";
  if (!/^[A-Za-z][A-Za-z0-9_.-]*$/.test(draft.packageId.trim())) return "Package id must start with a letter and contain only letters, numbers, dots, underscores, or hyphens.";
  if (!/^\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$/.test(draft.packageVersion.trim())) return "Package version must use a SemVer-like format such as 1.0.0.";
  return null;
}

// The default Project template to pre-select / bootstrap an extension with: an explicitly-flagged
// `primary`, else the first Elsa-named template, else the first available. Single source of truth for
// the heuristic shared by the project-draft defaults (context) and the one-click extension creator.
export function findPrimaryProjectTemplate(projectTemplates: ExtensionTemplate[]): ExtensionTemplate | undefined {
  return projectTemplates.find(template => template.primary || /elsa/i.test(`${template.name} ${template.id}`)) ?? projectTemplates[0];
}

export function applyTemplateDefaults(draft: ProjectDraft, template?: ExtensionTemplate) {
  if (!template) return draft;
  return {
    ...draft,
    name: draft.name || defaultProjectName(template),
    packageId: draft.packageId || defaultPackageId(template),
    packageVersion: draft.packageVersion || template.defaultPackageVersion || "1.0.0"
  };
}

export function createTemplateApplicationDraft(template: ExtensionTemplate, current?: TemplateApplicationDraft): TemplateApplicationDraft {
  return {
    templateId: template.id,
    targetPath: current?.targetPath ?? defaultTemplateTargetPath(template),
    parameters: Object.fromEntries(template.parameters.map(parameter => [
      parameter.name,
      current?.parameters[parameter.name] ?? parameter.defaultValue ?? defaultTemplateParameterValue(parameter, template)
    ]))
  };
}

export function collectTemplateParameters(template: ExtensionTemplate, draft: TemplateApplicationDraft) {
  const parameters = Object.fromEntries(template.parameters.map(parameter => [
    parameter.name,
    (draft.parameters[parameter.name] ?? parameter.defaultValue ?? "").trim()
  ]));
  const missing = template.parameters.find(parameter => parameter.required && !parameters[parameter.name]);
  if (missing) return `${missing.displayName} is required.`;
  return parameters;
}

export function defaultTemplateTargetPath(template: ExtensionTemplate | null) {
  if (!template) return "";
  if (template.scope === "Project") return `src/${defaultProjectName(template)}`;
  if (template.scope === "Item") return "src";
  if (template.scope === "Solution") return "solutions";
  return "";
}

export function defaultTemplateParameterValue(parameter: ExtensionTemplateParameter, template: ExtensionTemplate) {
  if (parameter.name === "name") return template.scope === "Item" ? "NewClass" : defaultProjectName(template);
  if (parameter.name === "packageId") return defaultPackageId(template);
  if (parameter.name === "packageVersion") return template.defaultPackageVersion ?? "1.0.0";
  if (parameter.name === "targetFramework") return template.defaultTargetFramework ?? "net10.0";
  return "";
}

export function defaultProjectName(template: ExtensionTemplate) {
  return /generic/i.test(`${template.name} ${template.id}`) ? "GenericExtension" : "ElsaActivityExtension";
}

export function defaultPackageId(template: ExtensionTemplate) {
  if (template.defaultPackageId) return template.defaultPackageId;
  return /generic/i.test(`${template.name} ${template.id}`) ? "Company.Extensions.Generic" : "Company.Extensions.ElsaActivity";
}

export function derivePackageId(name: string) {
  const segments = name
    .split(/[^A-Za-z0-9]+/)
    .filter(Boolean)
    .map(segment => segment.charAt(0).toUpperCase() + segment.slice(1));
  const id = segments.join(".");
  return /^[A-Za-z]/.test(id) ? id : "";
}

export function fileSortKey(file: RepositoryFileSummary) {
  return `${file.type === "folder" ? "0" : "1"}:${file.path}`;
}

export function isBuildRunning(build: BuildResult | null) {
  return build?.status === "queued" || build?.status === "running" || build?.status === "Pending" || build?.status === "Running";
}

export function isBuildForCurrentRevision(build: BuildResult | null, project: ExtensionProject | null) {
  if (!build || !project || !isBuildSucceeded(build)) return false;
  return !project.currentRevision || build.revision === project.currentRevision;
}

export function isBuildSucceeded(build: BuildResult | null) {
  return build?.status === "succeeded" || build?.status === "Succeeded";
}

export function isProtectedBranchName(value: string) {
  return value.toLowerCase() === "main" || value.toLowerCase() === "master";
}

export const ADVANCED_PREFERENCE_KEY = "elsa.extensionBuilder.advanced";

export function readAdvancedPreference() {
  try {
    return window.localStorage.getItem(ADVANCED_PREFERENCE_KEY) === "true";
  } catch {
    return false;
  }
}

export function writeAdvancedPreference(value: boolean) {
  try {
    window.localStorage.setItem(ADVANCED_PREFERENCE_KEY, value ? "true" : "false");
  } catch {
    // Ignore persistence failures (private mode, disabled storage); the toggle still works in-session.
  }
}

export const AUTO_SAVE_PREFERENCE_KEY = "elsa.extensionBuilder.autoSave";

export function readAutoSavePreference() {
  try {
    // Auto-save is on by default; only an explicit "false" turns it off.
    return window.localStorage.getItem(AUTO_SAVE_PREFERENCE_KEY) !== "false";
  } catch {
    return true;
  }
}

export function writeAutoSavePreference(value: boolean) {
  try {
    window.localStorage.setItem(AUTO_SAVE_PREFERENCE_KEY, value ? "true" : "false");
  } catch {
    // Ignore persistence failures; the toggle still works in-session.
  }
}

export function getExtensionBuilderSessionId() {
  const key = "elsa-extension-builder-session-id";
  try {
    const existing = window.sessionStorage.getItem(key);
    if (existing) return existing;
    const created = `studio-${crypto.randomUUID()}`;
    window.sessionStorage.setItem(key, created);
    return created;
  } catch {
    return `studio-${Math.random().toString(36).slice(2)}`;
  }
}

export function formatRemoteState(value?: string | null) {
  if (!value) return "unknown remote";
  if (value.includes("://") || value.includes("/") || value.includes("@")) return value;
  return value
    .split(/[-_\s]+/)
    .filter(Boolean)
    .join(" ");
}

export function buildTone(status?: string | null): StudioStatusTone {
  if (status === "succeeded" || status === "Succeeded") return "success";
  if (status === "failed" || status === "Failed") return "danger";
  if (status === "queued" || status === "running" || status === "Pending" || status === "Running") return "warning";
  return "neutral";
}

export function runtimeTone(status?: string | null): StudioStatusTone {
  if (status === "Loaded") return "success";
  if (status === "PendingRestart") return "warning";
  if (status === "FailedReconciliation") return "danger";
  return "neutral";
}

export function diagnosticTone(severity?: string | null): StudioStatusTone {
  if (severity === "error" || severity === "Error") return "danger";
  if (severity === "warning" || severity === "Warning") return "warning";
  if (severity === "info" || severity === "Info") return "accent";
  return "neutral";
}

export function runtimeMessageTone(state: string): "success" | "warning" | "danger" | "info" {
  if (state === "Loaded") return "success";
  if (state === "PendingRestart") return "warning";
  if (state === "FailedReconciliation") return "danger";
  return "info";
}

export function runtimeMessage(status: ExtensionRuntimeStatus) {
  if (status.state === "Loaded") return `${status.packageId} ${status.version ?? ""} is loaded at runtime.`;
  if (status.state === "PendingRestart") return `${status.packageId} ${status.version ?? ""} is staged and requires a runtime restart before it is loaded.`;
  if (status.state === "FailedReconciliation") return `${status.packageId} ${status.version ?? ""} failed reconciliation. Retry reconciliation or roll back to an available prior version.`;
  return `${status.packageId} is ${status.state}.`;
}

export function promotionMessage(result: PackagePromotionResult) {
  if (result.accepted) return result.message ?? "Package validation passed and promotion was accepted.";
  if (result.category === "Duplicate") return result.message ?? "Duplicate package id and version rejected. Bump the version before promoting; existing packages are never silently overwritten.";
  if (result.category === "InvalidManifest") return result.message ?? "Package manifest validation failed. Fix the manifest metadata and rebuild.";
  if (result.category === "DependencyPolicy") return result.message ?? "Package dependencies violate policy. Review the rejected dependency and rebuild with an allowed version.";
  if (result.category === "MalformedPackage") return result.message ?? "The package artifact is malformed or corrupt. Rebuild the project before promoting.";
  if (result.category === "UncommittedSource") return result.message ?? "Package artifact was built from uncommitted source. Commit and pack again before promoting.";
  return result.message ?? "Promotion was rejected by server-side validation.";
}

export function formatDiagnosticLocation(diagnostic: BuildDiagnostic) {
  const path = diagnostic.filePath ?? "project";
  const line = diagnostic.line ? `:${diagnostic.line}` : "";
  const column = diagnostic.column ? `:${diagnostic.column}` : "";
  return `${path}${line}${column}`;
}

export function createProjectFileDocument(workspace: ExtensionWorkspace, project: ExtensionProject | null, path: string, value: string): StudioCodeDocument {
  return {
    uri: createProjectFileUri(workspace.id, project?.id ?? null, path),
    language: getProjectFileLanguage(path),
    value
  };
}

export function createEditorDiagnostics(workspace: ExtensionWorkspace, project: ExtensionProject | null, diagnostics: BuildDiagnostic[]): StudioCodeDiagnostic[] {
  return diagnostics.map(diagnostic => ({
    uri: diagnostic.filePath ? createProjectFileUri(workspace.id, project?.id ?? null, diagnostic.filePath) : undefined,
    severity: toStudioCodeDiagnosticSeverity(diagnostic.severity),
    code: extractDiagnosticCode(diagnostic.message),
    message: diagnostic.message,
    startLineNumber: diagnostic.line ?? undefined,
    startColumn: diagnostic.column ?? undefined
  }));
}

export function createProjectFileUri(workspaceId: string, projectId: string | null, path: string) {
  const projectSegment = projectId ? `/projects/${encodeURIComponent(projectId)}` : "";
  return `elsa://extension-builder/workspaces/${encodeURIComponent(workspaceId)}${projectSegment}/files/${encodeURIComponent(path)}`;
}

export function getProjectFileLanguage(path: string) {
  const extension = path.toLowerCase().split(".").pop();
  if (extension === "cs") return "csharp";
  if (extension === "csproj" || extension === "props" || extension === "targets" || extension === "xml") return "xml";
  if (extension === "json") return "json";
  if (extension === "js" || extension === "mjs" || extension === "cjs") return "javascript";
  if (extension === "ts" || extension === "tsx") return "typescript";
  if (extension === "razor" || extension === "cshtml") return "razor";
  if (extension === "md") return "markdown";
  return "text";
}

export function isJavaScriptLikeDocument(document: StudioCodeDocument) {
  const normalized = document.language.trim().toLowerCase();
  return normalized === "javascript" || normalized === "typescript";
}

export function toStudioCodeDiagnosticSeverity(severity: string): StudioCodeDiagnosticSeverity {
  const normalized = severity.toLowerCase();
  if (normalized === "error") return "error";
  if (normalized === "warning") return "warning";
  return "info";
}

export function extractDiagnosticCode(message: string) {
  return /^[A-Z]{1,10}\d{2,6}\b/.exec(message)?.[0];
}

export function formatDate(value?: string | null) {
  if (!value) return "n/a";
  return new Date(value).toLocaleString();
}

export function formatArtifactSize(size?: number | null) {
  if (!size) return "0 B";
  if (size < 1024) return `${size} B`;
  return `${Math.round(size / 1024)} KB`;
}
