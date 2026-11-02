// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const styles = readFileSync(new URL("../styles.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");


function ruleBody(css: string, selector: string) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return css.match(new RegExp(`(?:^|\\n)\\s*${escapedSelector}\\s*\\{([^{}]*)\\}`))?.[1] ?? "";
}

describe("run workbench responsive layout styles", () => {
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
