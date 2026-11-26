import type { StudioCodeDocumentation } from "./types";

/**
 * Keeps server-supplied documentation useful without allowing it to become a
 * markup execution surface. Markdown punctuation is retained as text and raw
 * HTML is removed before render.
 */
export function StudioCodeDocumentation({ documentation }: { documentation: StudioCodeDocumentation }) {
  return <span className="studio-code-editor-documentation">{sanitizeStudioCodeMarkdown(documentation.markdown)}</span>;
}

export function sanitizeStudioCodeMarkdown(markdown: string) {
  return markdown
    .replace(/<[^>]*>/g, "")
    .replace(/!?(?:\[[^\]]*\])\([^)]*\)/g, match => match.replace(/\([^)]*\)/, ""))
    .trim();
}
