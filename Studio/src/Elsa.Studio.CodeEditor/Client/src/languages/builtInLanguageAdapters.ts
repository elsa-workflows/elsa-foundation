import { javaScriptLanguageAdapter } from "./javascript";

export function getBuiltInLanguageAdapter(language: string) {
  const normalized = language.trim().toLowerCase();
  if (normalized === "javascript" || normalized === "typescript") return javaScriptLanguageAdapter;
  return null;
}
