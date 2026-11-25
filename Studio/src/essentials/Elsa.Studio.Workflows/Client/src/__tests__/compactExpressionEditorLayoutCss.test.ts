// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, "");
const styles = stripComments(readFileSync(new URL("../styles.css", import.meta.url), "utf8"));
const editorStyles = stripComments(readFileSync(new URL(
  "../../../../Elsa.Studio.CodeEditor/Client/src/styles.css",
  import.meta.url
), "utf8"));

const rule = (css: string, selector: string) => {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/\s+/g, "\\s+");
  const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css);
  return match?.[1] ?? null;
};

// #4741: the field's syntax and expand controls must not cover the compact source line once the editor
// shows its own actions row (Insert data), so inserted text and the cursor stay visible.
describe("compact expression editor with an actions row", () => {
  const withActions = ".wf-expression-field:has(.studio-code-editor-compact > .studio-code-editor-actions)";

  it("places the syntax and expand controls on the actions row", () => {
    const controls = rule(styles, `${withActions} > .wf-syntax-picker.inline,\n${withActions} > .wf-expression-expand-button`);
    expect(controls).not.toBeNull();
    expect(controls).toMatch(/top:\s*0;/);
    expect(controls).toMatch(/height:\s*1\.75rem;/);
    const row = rule(styles, ".wf-expression-field .studio-code-editor-compact > .studio-code-editor-actions");
    expect(row).toMatch(/min-block-size:\s*1\.75rem;/);
    expect(row).toMatch(/padding-inline-end:\s*var\(--wf-expression-chrome-width\);/);
  });

  it("gives the source line and its preview their full width", () => {
    expect(rule(styles, ".wf-expression-field .studio-code-editor-compact:has(> .studio-code-editor-actions) .cm-scroller"))
      .toMatch(/margin-inline-end:\s*0;/);
    expect(rule(styles, ".wf-expression-field .studio-code-editor-compact:has(> .studio-code-editor-actions) .studio-code-editor-preview"))
      .toMatch(/padding-inline-end:\s*0\.5rem;/);
  });

  it("keeps the actions row clear of the focused line's ring", () => {
    expect(rule(editorStyles, ".studio-code-editor-compact > .studio-code-editor-actions")).toMatch(/margin-block-end:\s*0\.375rem;/);
    expect(editorStyles).toMatch(/\.studio-code-editor-rich \.cm-editor:focus-within[^{}]*\{[^}]*outline-offset:\s*2px/);
  });
});
