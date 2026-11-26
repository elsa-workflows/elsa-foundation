// @vitest-environment node

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const styles = readFileSync(new URL("../styles.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const runWorkbenchStyles = readFileSync(new URL("../workflow-editor/runWorkbench.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const runHistoryStyles = readFileSync(new URL("../workflow-editor/run-history/runHistory.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const activityInspectionStyles = readFileSync(new URL("../workflow-editor/activityInspection.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");


function ruleBody(css: string, selector: string) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return css.match(new RegExp(`(?:^|\\n)\\s*${escapedSelector}\\s*\\{([^{}]*)\\}`))?.[1] ?? "";
}

describe("run workbench responsive layout styles", () => {
  it("keeps Run and Executable detail identity in a wrapping tokenized header", () => {
    const header = ruleBody(styles, ".wf-detail-header");

    expect(header).toMatch(/display:\s*flex/);
    expect(header).toMatch(/flex-wrap:\s*wrap/);
    expect(header).toMatch(/border-bottom:\s*1px solid var\(--studio-border\)/);
    expect(header).not.toMatch(/#[\da-f]{3,8}\b/i);

    const provenance = ruleBody(styles, ".wf-instance-meta dd.wf-executable-layout-provenance");
    expect(provenance).toMatch(/overflow-wrap:\s*anywhere/);
    expect(provenance).toMatch(/white-space:\s*normal/);
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

  it("docks run workbench panes flush without outer rounded borders and uses hairline splitters", () => {
    const inspector = ruleBody(styles, ".wf-instance-inspector");
    const canvasShell = ruleBody(runWorkbenchStyles, ".wf-instance-canvas-shell");
    const dockedHistory = ruleBody(runHistoryStyles, ".wf-instance-detail-workbench.timeline-docked > .wf-run-history-view");
    const leftHandle = ruleBody(runWorkbenchStyles, ".wf-instance-detail-workbench > .wf-side-resize-handle.left");
    const rightHandle = ruleBody(runWorkbenchStyles, ".wf-instance-detail-workbench > .wf-side-resize-handle.right");

    for (const pane of [inspector, canvasShell, dockedHistory]) {
      expect(pane).toMatch(/border:\s*0;/);
      expect(pane).toMatch(/border-radius:\s*0;/);
    }

    expect(leftHandle).toMatch(/border-left:\s*1px solid var\(--wf-border\);/);
    expect(rightHandle).toMatch(/border-right:\s*1px solid var\(--wf-border\);/);
  });

  it("integrates the selected activity overview header flush inside the inspector", () => {
    const overview = ruleBody(activityInspectionStyles, ".wf-instance-section.wf-activity-overview");
    const heading = ruleBody(activityInspectionStyles, ".wf-activity-overview-heading");

    expect(overview).toMatch(/border:\s*0;/);
    expect(overview).toMatch(/border-bottom:\s*1px solid var\(--studio-border\);/);
    expect(overview).toMatch(/border-radius:\s*0;/);
    expect(heading).not.toMatch(/border-bottom:/);
    expect(activityInspectionStyles.indexOf(".wf-activity-inspection-tab-panel--flush"))
      .toBeGreaterThan(activityInspectionStyles.indexOf(".wf-activity-inspection-tab-panel {"));
  });
});
