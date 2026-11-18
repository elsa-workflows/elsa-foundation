// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, "");
const styles = stripComments(readFileSync(new URL("../styles.css", import.meta.url), "utf8"));

const hintField = String.raw`\.wf-expression-field:has\(> \.wf-expression-editor > \.wf-expression-editor-hint\)`;

function rule(selectorPattern: string) {
  const match = new RegExp(`${selectorPattern}\\s*\\{([^}]*)\\}`).exec(styles);
  expect(match, `missing rule for ${selectorPattern}`).not.toBeNull();
  return match![1];
}

// The inline syntax picker and expand button are absolutely positioned over a text field. A hint has no
// field to overlay, so the picker covered words of "No editor is available for …" (#4742).
describe("Expression field that shows a hint instead of an editor", () => {
  it("lays the hint and its buttons out in one row", () => {
    expect(rule(hintField)).toMatch(/display:\s*flex/);
    expect(rule(`${hintField} > \\.wf-expression-editor`)).toMatch(/flex:\s*1 1 auto/);
  });

  it("takes the syntax and expand buttons out of absolute positioning", () => {
    const buttons = rule(`${hintField} > \\.wf-syntax-picker\\.inline,\\s*${hintField} > \\.wf-expression-expand-button`);
    expect(buttons).toMatch(/position:\s*relative/);
    expect(buttons).toMatch(/inset:\s*auto/);
    expect(buttons).toMatch(/flex:\s*0 0 auto/);
  });

  it("keeps a visible focus state on the standalone buttons", () => {
    const focus = rule(`${hintField} \\.wf-syntax-picker-trigger\\.inline:is\\(:focus-visible, \\.open\\),\\s*${hintField} > \\.wf-expression-expand-button:focus-visible`);
    expect(focus).toMatch(/box-shadow:\s*var\(--wf-focus-ring\)/);
  });
});
