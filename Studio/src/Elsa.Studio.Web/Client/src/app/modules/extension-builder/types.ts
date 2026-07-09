// "unavailable" is distinct from "failed": it means the Studio management bridge reported that backend management is
// not usable (unconfigured/unreachable/unauthorized/degraded), so the UI shows an explicit backend-management-unavailable
// surface and gates actions instead of surfacing a raw fetch error.
export type BuilderState = "loading" | "ready" | "failed" | "unavailable";

// The backend-management availability kinds the bridge reports (mirrors StudioBackendManagementStatusKind). Threaded to
// the view so the unavailable surface can name the real reason rather than a generic message.
export type BackendManagementUnavailableKind = "unconfigured" | "unreachable" | "unauthorized" | "degraded";
export type InspectorTab = "build" | "source" | "promote" | "runtime";

export interface ProjectDraft {
  name: string;
  packageId: string;
  packageVersion: string;
  templateId: string;
}

export interface TemplateApplicationDraft {
  templateId: string;
  targetPath: string;
  parameters: Record<string, string>;
}

export interface EditorTab {
  path: string;
  content: string;
  savedContent: string;
}

export const dockTabs: { id: InspectorTab; label: string }[] = [
  { id: "build", label: "Build output" },
  { id: "source", label: "Source control" },
  { id: "promote", label: "Promote" },
  { id: "runtime", label: "Runtime" }
];
