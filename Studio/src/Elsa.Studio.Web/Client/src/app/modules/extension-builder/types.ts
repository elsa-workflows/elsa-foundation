export type BuilderState = "loading" | "ready" | "failed";
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
