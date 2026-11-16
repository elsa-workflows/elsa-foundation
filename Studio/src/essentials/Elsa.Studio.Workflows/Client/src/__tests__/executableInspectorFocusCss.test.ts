// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, "");
const styles = stripComments(readFileSync(new URL("../styles.css", import.meta.url), "utf8"));
const panelStyles = stripComments(readFileSync(new URL("../graph-authoring/workbenchPanelStyles.css", import.meta.url), "utf8"));
const workspaceStyles = stripComments(readFileSync(new URL("../workspaceAppearance.css", import.meta.url), "utf8"));
const hostStyles = stripComments(readFileSync(new URL(
  "../../../../Elsa.Studio.Hosting/Client/src/app/styles.css",
  import.meta.url
), "utf8"));

const focusRing = /outline:\s*2px solid var\(--studio-focus\)[\s\S]*?outline-offset:\s*2px/;

describe("Executable Inspector keyboard focus", () => {
  it("gives toolbar, identity-rail and React Flow controls the studio focus ring", () => {
    expect(styles).toMatch(/\.wf-toolbar button:focus-visible[\s\S]*?\.wf-panel-action-button:focus-visible[\s\S]*?\.wf-panel-tab-list button:focus-visible[\s\S]*?\.wf-instance-meta button:focus-visible[\s\S]*?\.wf-instance-inspector \.studio-copyable-identifier__button:focus-visible[\s\S]*?\.wf-instance-canvas \.react-flow__controls button:focus-visible[\s\S]*?\.wf-instance-canvas \.react-flow__controls-button:focus-visible[\s\S]*?outline:\s*2px solid var\(--studio-focus\)[\s\S]*?outline-offset:\s*2px/);
    expect(panelStyles).toMatch(/\.wf-panel-tab-list button:focus-visible[\s\S]*?outline:\s*2px solid var\(--studio-focus\)/);
    expect(panelStyles).toMatch(/\.wf-panel-action-button:focus-visible[\s\S]*?outline:\s*2px solid var\(--studio-focus\)/);
    expect(panelStyles).not.toMatch(/\.wf-panel-tab-list button:focus-visible[\s\S]{0,120}outline:\s*none/);
    expect(panelStyles).not.toMatch(/\.wf-panel-action-button:focus-visible[\s\S]{0,120}outline:\s*none/);
    expect(workspaceStyles).toMatch(/html\[data-workspace-style\] :is\([^)]*\.wf-toolbar button[\s\S]*?\):focus-visible[\s\S]*?outline:\s*2px solid var\(--studio-focus\)/);
  });

  it("does not let shared toolbar chrome flatten StudioButton, including the primary Run", () => {
    expect(styles).toMatch(/\.wf-toolbar button:where\(:not\(\.studio-button\)\)/);
    expect(hostStyles).toMatch(/html\[data-theme-material\] \.wf-toolbar button:where\(:not\(\.studio-button\)\)/);
    expect(focusRing.test(styles)).toBe(true);
  });
});

describe("prefers-reduced-motion for portaled workflow dialogs", () => {
  it("covers body-portaled .wf-tokens roots, not only .wf-page and .wf-editor", () => {
    expect(styles).toMatch(/@media \(prefers-reduced-motion: reduce\) \{[\s\S]*\.wf-tokens,[\s\S]*\.wf-tokens \*,[\s\S]*\.wf-tokens \*::before,[\s\S]*\.wf-tokens \*::after/);
  });
});
