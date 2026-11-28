import type { StudioCodeLanguageAdapter } from "../types";

async function loadEditor() {
  const module = await import("../engines/CodeMirrorStudioCodeEditor");
  return { default: module.CodeMirrorStudioCodeEditor };
}

function documentLanguage(language: string, displayName: string): StudioCodeLanguageAdapter {
  return { language, displayName, loadEditor };
}

export const jsonLanguageAdapter = documentLanguage("json", "JSON");
export const xmlLanguageAdapter = documentLanguage("xml", "XML");
export const htmlLanguageAdapter = documentLanguage("html", "HTML");
export const plainTextLanguageAdapter = documentLanguage("plaintext", "Plain text");
