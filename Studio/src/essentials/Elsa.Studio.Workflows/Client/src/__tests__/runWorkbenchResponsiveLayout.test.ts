// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const styles = readFileSync(new URL("../styles.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const phoneStyles = [...styles.matchAll(/@media \(max-width: 560px\) \{([\s\S]*?)^\}/gm)].map(([, body]) => body).join("\n");

function ruleBody(css: string, selector: string) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return css.match(new RegExp(`(?:^|\\n)\\s*${escapedSelector}\\s*\\{([^{}]*)\\}`))?.[1] ?? "";
}

describe("run workbench responsive layout styles", () => {
  it("lets the run page and inspector shrink to their host width on phones", () => {
    const page = ruleBody(styles, ".wf-page--run-workbench");
    const workbench = ruleBody(styles, ".wf-instance-detail-workbench");
    const toolbar = ruleBody(phoneStyles, ".wf-page--run-workbench > .wf-toolbar");

    expect(page).toMatch(/min-width:\s*0;/);
    expect(page).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\);/);
    expect(workbench).toMatch(/min-width:\s*0;/);
    expect(toolbar).toMatch(/flex-wrap:\s*wrap;/);
  });

  it("pins the shared workbench to the last 1fr track so run feedback cannot collapse it", () => {
    const page = ruleBody(styles, ".wf-page--run-workbench");
    const workbench = ruleBody(styles, ".wf-page--run-workbench > .wf-instance-detail-workbench");

    expect(page).toMatch(/grid-template-rows:\s*auto auto auto auto minmax\(0,\s*1fr\);/);
    expect(workbench).toMatch(/grid-row:\s*-2\s*\/\s*-1;/);
  });

  it("styles the shared read-only MiniMap as a compact tokenized overview", () => {
    const minimap = ruleBody(styles, ".wf-instance-canvas .react-flow__minimap");

    expect(minimap).not.toMatch(/width:/);
    expect(minimap).not.toMatch(/height:/);
    expect(minimap).toMatch(/background:\s*var\(--studio-surface\);/);
    expect(minimap).toMatch(/border:\s*1px solid var\(--studio-border\);/);
    expect(minimap).toMatch(/border-radius:\s*var\(--studio-radius-sm\);/);
    expect(styles).toMatch(/html\[data-theme-material\][\s\S]*\.react-flow__minimap/);
  });
});
