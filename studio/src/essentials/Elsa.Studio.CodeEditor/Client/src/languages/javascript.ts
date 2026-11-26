import type { StudioCodeLanguageAdapter } from "../types";

export const javaScriptLanguageAdapter: StudioCodeLanguageAdapter = {
  language: "javascript",
  displayName: "JavaScript",
  async loadEditor() {
    const module = await import("../engines/CodeMirrorStudioCodeEditor");
    return { default: module.CodeMirrorStudioCodeEditor };
  },
  async loadSupport() {
    return { language: "javascript" };
  }
};
