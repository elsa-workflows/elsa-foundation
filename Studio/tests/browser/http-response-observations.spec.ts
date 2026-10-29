import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  for (const delivery of ["warning", "written", "unknown"]) {
    test(`inspects retained HTTP delivery evidence without changing completion (${theme}, ${delivery})`, async ({ page }) => {
      await page.setViewportSize({ width: 1550, height: 1000 });
      await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&http-delivery=${delivery}&theme=${theme}`);
      await page.getByRole("button", { name: "Test Run", exact: true }).click();
      await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
      const node = page.getByLabel("Workflow canvas", { exact: true }).getByRole("button", { name: /^Write HTTP Response\. Node ID:/ });
      await expect(node).toHaveAccessibleName(/Run state: Completed/);
      await expect(node.getByRole("img", { name: /1 warning:/ })).toHaveCount(delivery === "warning" ? 1 : 0);
      await node.click();
      const panel = page.getByRole("region", { name: "HTTP response delivery", exact: true });
      const status = delivery === "warning" ? "Not sent to originating request" : delivery === "written" ? "Written to HTTP response" : "Unknown";
      await expect(panel.getByText(status, { exact: true })).toBeVisible();
      if (delivery === "warning") await expect(panel.getByText("Response prepared but not sent: the original HTTP exchange is closed.", { exact: true })).toBeVisible();
      await page.screenshot({ path: `/tmp/http-delivery-${theme}-${delivery}.png` });
      await page.reload();
      await page.getByRole("button", { name: "Restore Test Run", exact: true }).click();
      await node.click();
      await expect(panel.getByText(status, { exact: true })).toBeVisible();
      await expect(node).toHaveAccessibleName(/Run state: Completed/);
    });
  }
}
