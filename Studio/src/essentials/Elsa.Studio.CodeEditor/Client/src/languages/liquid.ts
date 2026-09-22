import type { StudioCodeLanguageAdapter } from "../types";

export const liquidLanguageAdapter: StudioCodeLanguageAdapter = {
  language: "liquid",
  displayName: "Liquid",
  async loadEditor() {
    const module = await import("../engines/CodeMirrorStudioCodeEditor");
    return { default: module.CodeMirrorStudioCodeEditor };
  },
  async loadSupport() {
    return { language: "liquid" };
  }
};
