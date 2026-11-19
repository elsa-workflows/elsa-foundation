import { expect, test } from "@playwright/test";

for (const theme of ["classic", "dark"]) {
  for (const host of ["workflow", "activity", "instance", "executable"]) {
    test(`${host} loads its shared canvas and node styles (${theme})`, async ({ page }) => {
      await page.setViewportSize({ width: 1500, height: 950 });
      await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=unique&adaptive-ports=vertical&theme=${theme}`);
      const renderer = page.locator(".react-flow__renderer").first();
      const node = page.locator(".react-flow__node .wf-node").first();
      await expect(renderer).toBeVisible();
      await expect(renderer).toHaveCSS("position", "absolute");
      await expect(node).toHaveCSS("width", "246px");
      await expect(node).toHaveCSS("border-top-width", "1px");
      await expect(node.locator(".wf-node-content")).toHaveCSS("display", "grid");
      await expect(node.locator(".react-flow__handle").first()).toHaveCSS("width", "10px");
      if (host === "instance" || host === "executable") {
        await expect(page.locator(".react-flow__node").first()).not.toHaveClass(/draggable/);
      }
    });
  }
  for (const host of ["workflow", "activity"]) {
    test(`BPMN ${host} loads the canvas stylesheet (${theme})`, async ({ page }) => {
      await page.setViewportSize({ width: 1500, height: 950 });
      await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&adaptive-ports=vertical&theme=${theme}`);
      await expect(page.locator(".react-flow__renderer").first()).toHaveCSS("position", "absolute");
      await expect(page.locator(".wf-bpmn-task").first()).toBeVisible();
      // A straight vertical SVG path has a zero-width bounding box; inspect its
      // painted geometry rather than Playwright's rectangular visibility test.
      const edge = page.locator(".react-flow__edge-path").first();
      await expect.poll(() => edge.evaluate(element => (element as SVGPathElement).getTotalLength())).toBeGreaterThan(0);
      expect(await edge.evaluate(element => getComputedStyle(element).stroke)).not.toBe("none");
    });
  }
  for (const composition of ["flowchart", "bpmn"]) {
    test(`source-owned ${composition} Activity graph keeps styles and read-only nodes (${theme})`, async ({ page }) => {
      await page.setViewportSize({ width: 1500, height: 950 });
      await page.goto(`/?mode=activity-duplicate-editor&host=activity&composition=${composition}&adaptive-ports=vertical&read-only&theme=${theme}`);
      await expect(page.locator(".react-flow__renderer").first()).toHaveCSS("position", "absolute");
      const node = page.locator(".react-flow__node").first();
      await expect(node).not.toHaveClass(/draggable/);
      await expect(node.locator(composition === "bpmn" ? ".wf-bpmn-task" : ".wf-node")).toBeVisible();
      if (composition === "flowchart") await expect(node.locator(".wf-node-content")).toHaveCSS("display", "grid");
    });
  }
}
