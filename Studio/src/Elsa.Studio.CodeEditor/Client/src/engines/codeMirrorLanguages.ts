import type { Extension } from "@codemirror/state";

export async function loadCodeMirrorLanguageExtensions(language: string): Promise<Extension[]> {
  const normalized = language.trim().toLowerCase();
  if (normalized === "javascript" || normalized === "typescript") {
    const module = await import("../languages/javascriptCodeMirror");
    return module.createJavaScriptCodeMirrorExtensions();
  }

  return [];
}
