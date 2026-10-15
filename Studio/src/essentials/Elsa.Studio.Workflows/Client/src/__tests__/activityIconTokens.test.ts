// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const styles = readFileSync(new URL("../styles.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const token = (name: string) => {
  const match = styles.match(new RegExp(`${name}\\s*:([^;]+);`));
  return match?.[1].replace(/\s+/g, " ").trim();
};

describe("activity icon host override", () => {
  it("routes category tones through the documented --studio-wf-activity-* host override", () => {
    expect(token("--wf-activity-terminal")).toBe("var(--studio-wf-activity-terminal, var(--studio-success))");
    expect(token("--wf-activity-composite")).toBe("var(--studio-wf-activity-composite, var(--studio-accent))");
    expect(token("--wf-activity-runtime")).toBe("var(--studio-wf-activity-runtime, var(--studio-warning))");
    expect(token("--wf-activity-trigger")).toBe("var(--studio-wf-activity-trigger, var(--studio-warning))");
    expect(token("--wf-icon-tone-terminal")).toBe("var(--wf-activity-terminal)");
    expect(token("--wf-icon-tone-composite")).toBe("var(--wf-activity-composite)");
    expect(token("--wf-icon-tone-runtime")).toBe("var(--wf-activity-runtime)");
    expect(token("--wf-icon-tone-trigger")).toBe("var(--wf-activity-trigger)");
  });

  it("colours data-tone palette and node rules through those host-overridable aliases", () => {
    expect(styles).toMatch(/\.wf-activity-icon\[data-tone="terminal"\][\s\S]*?color:\s*var\(--wf-activity-terminal\)/);
    expect(styles).toMatch(/\.wf-node\[data-tone="terminal"\] \.wf-node-icon[\s\S]*?color:\s*var\(--wf-activity-terminal\)/);
    expect(styles).toMatch(/\.wf-activity-icon\[data-tone="composite"\][\s\S]*?color:\s*var\(--wf-activity-composite\)/);
    expect(styles).toMatch(/\.wf-node\[data-tone="trigger"\] \.wf-node-icon[\s\S]*?color:\s*var\(--wf-activity-trigger\)/);
  });
});
