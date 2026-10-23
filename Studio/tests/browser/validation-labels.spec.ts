import { expect, test } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";

const screenshotDir = "/opt/cursor/artifacts/screenshots";

test("validation and rejection copy names activities by display name, not node id", async ({ page }) => {
  await page.setViewportSize({ width: 960, height: 720 });
  await page.goto("/?mode=validation-labels&theme=light");

  const fixture = page.getByTestId("validation-labels-fixture");
  await expect(fixture).toBeVisible();
  await expect(fixture).toContainText("Required input \"Text\" on \"Say hello\" (Write Line) is empty.");
  await expect(fixture).not.toContainText("writeline-22ab948a");
  await expect(fixture).not.toContainText("pinned default");
  await expect(page.locator(".wf-alert-code")).toHaveText("VF-ACT-003");
  await expect(page.locator(".wf-validation-message").first()).toHaveAttribute("title", "writeline-22ab948a");
  await expect(page.locator(".wf-runtime-reason")).toHaveAttribute("title", /writeline-22ab948a/);
});

test("captures Classic light before/after screenshots of named validation copy", async ({ page }) => {
  await mkdir(screenshotDir, { recursive: true });
  await page.setViewportSize({ width: 960, height: 720 });

  await page.goto("/?mode=validation-labels-before&theme=light");
  await expect(page.getByTestId("validation-labels-fixture")).toBeVisible();
  await expect(page.getByTestId("validation-labels-fixture")).toContainText("writeline-22ab948a");
  await page.screenshot({
    path: resolve(screenshotDir, "2698-before-classic-light.png"),
    animations: "disabled"
  });

  await page.goto("/?mode=validation-labels&theme=light");
  await expect(page.getByTestId("validation-labels-fixture")).toContainText("\"Say hello\" (Write Line)");
  await expect(page.getByTestId("validation-labels-fixture")).not.toContainText("writeline-22ab948a");
  await page.screenshot({
    path: resolve(screenshotDir, "2698-after-classic-light.png"),
    animations: "disabled"
  });
});
