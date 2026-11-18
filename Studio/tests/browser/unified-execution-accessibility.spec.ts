import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const host of ["editor", "instance"]) for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`keeps live inspection keyboard accessible in the ${host} (${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=unique&execution-motion=causal&theme=${theme}`);
    if (host === "editor") {
      await page.getByRole("button", { name: "Test Run", exact: true }).press("Enter");
      await page.getByRole("button", { name: "Start Test Run", exact: true }).press("Enter");
    }
    // A phone-width editor inspector is a sheet over the canvas (#4695), like the run page's narrow
    // details: the canvas and the Runtime panel are visited in turn rather than side by side.
    const editorSheet = host === "editor" && width <= 600;
    const showCanvas = async () => { if (editorSheet) await page.getByRole("button", { name: "Back to canvas", exact: true }).press("Enter"); };
    const showInspector = async () => { if (editorSheet) await page.getByRole("button", { name: "Expand inspector panel", exact: true }).press("Enter"); };
    await showCanvas();
    const canvas = host === "instance"
      ? page.getByRole("region", { name: "Workflow run canvas", exact: true })
      : page.getByLabel("Workflow canvas", { exact: true });
    const source = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    const narrow = width === 390;
    await expect(source).toHaveAccessibleName(/Run state: Completed/);
    await source.press("Enter");
    await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
    if (narrow) {
      await page.getByRole("button", { name: host === "instance" ? "Back to workflow run canvas" : "Back to canvas", exact: true }).press("Enter");
      await expect(source).toBeFocused();
    }
    await expect(source).toHaveAccessibleName(/Selected/);

    await showInspector();
    if (host === "editor") await page.getByRole("tab", { name: "Runtime", exact: true }).press("Enter");
    await page.getByRole("button", { name: "Pause live updates", exact: true }).press("Enter");
    const follow = page.getByRole("button", { name: "Resume live updates", exact: true });
    await expect(follow).toBeFocused();
    await expect(page.getByText(/^Live updates paused/)).toBeVisible();
    await showCanvas();
    await expect(source).toHaveAccessibleName(/Selected/);
    await showInspector();
    await follow.press("Enter");
    await expect(page.getByRole("button", { name: "Pause live updates", exact: true })).toBeFocused();

    await page.getByRole("button", { name: "Complete fixture execution", exact: true }).press("Enter");
    await showCanvas();
    await expect(canvas.getByRole("button", { name: /^Write Line\. Node ID: write-two/ })).toHaveAccessibleName(/Run state: Completed/);
    await expect(source).toHaveAccessibleName(/Selected/);
    await source.press(" ");
    await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
    await expect(page.getByText("Completion receipt: 1", { exact: true })).toBeVisible();
    if (!narrow) await expect(source).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);

    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const result = await (window as Window & {
        axe: { run(root: Element, options: object): Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: Array<{ target: unknown[] }> }> }> };
      }).axe.run(document.querySelector(".activity-duplicate-editor")!, { resultTypes: ["violations"] });
      return result.violations.filter(value => value.impact === "critical" || value.impact === "serious")
        .map(value => ({ id: value.id, help: value.help, targets: value.nodes.map(node => node.target) }));
    });
    expect(violations).toEqual([]);
  });
}
