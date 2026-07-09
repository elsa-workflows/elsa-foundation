// "unavailable" is distinct from "failed": it means the Studio management bridge reported that backend management is
// not usable (unconfigured/unreachable/unauthorized/degraded), so the UI shows an explicit backend-management-unavailable
// surface and gates actions instead of surfacing a raw fetch error.
export type BuilderState = "loading" | "ready" | "failed" | "unavailable";

// The backend-management availability kinds the bridge reports (mirrors StudioBackendManagementStatusKind), plus
// "forbidden" for a Studio authorization failure (403 — the signed-in user lacks extension-builder.read). "forbidden" is
// deliberately distinct from every backend state: it is "Studio denied you", not "backend unavailable" and not a login
// prompt (#249, ADR 0037). Threaded to the view so the unavailable surface names the real reason.
export type BackendManagementUnavailableKind = "unconfigured" | "unreachable" | "unauthorized" | "degraded" | "forbidden";
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
