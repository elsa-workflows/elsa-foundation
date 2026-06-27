import type { StudioCodeLanguageAdapter } from "../types";

export const javaScriptLanguageAdapter: StudioCodeLanguageAdapter = {
  language: "javascript",
  displayName: "JavaScript",
  async loadSupport() {
    return { language: "javascript" };
  }
};
