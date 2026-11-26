import type { Extension } from "@codemirror/state";

export async function loadCodeMirrorLanguageExtensions(language: string): Promise<Extension[]> {
  const normalized = language.trim().toLowerCase();
  if (normalized === "javascript" || normalized === "typescript") {
    const module = await import("../languages/javascriptCodeMirror");
    return module.createJavaScriptCodeMirrorExtensions();
  }

  if (normalized === "json") {
    const { json } = await import("@codemirror/lang-json");
    return [json()];
  }
  if (normalized === "xml") {
    const { xml } = await import("@codemirror/lang-xml");
    return [xml()];
  }
  if (normalized === "html") {
    const { html } = await import("@codemirror/lang-html");
    return [html()];
  }

  if (normalized === "liquid") {
    const module = await import("../languages/liquidCodeMirror");
    return module.createLiquidCodeMirrorExtensions();
  }

  return [];
}
