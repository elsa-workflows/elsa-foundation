import { expect, test } from "@playwright/test";

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

test("captures Classic light before/after screenshots of named validation copy", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 960, height: 720 });

  await page.goto("/?mode=validation-labels-before&theme=light");
  await expect(page.getByTestId("validation-labels-fixture")).toBeVisible();
  await expect(page.getByTestId("validation-labels-fixture")).toContainText("writeline-22ab948a");
  await page.screenshot({
    path: testInfo.outputPath("2698-before-classic-light.png"),
    animations: "disabled"
  });

  await page.goto("/?mode=validation-labels&theme=light");
  await expect(page.getByTestId("validation-labels-fixture")).toContainText("\"Say hello\" (Write Line)");
  await expect(page.getByTestId("validation-labels-fixture")).not.toContainText("writeline-22ab948a");
  await page.screenshot({
    path: testInfo.outputPath("2698-after-classic-light.png"),
    animations: "disabled"
  });
});

for (const theme of ["light", "dark"]) {
  test(`compilation rejection retains its reason without execution requests or controls (${theme})`, async ({ page }) => {
    const requests: string[] = [];
    await page.route("**/rejected-fixture-api/**", async route => {
      requests.push(route.request().url());
      await route.fulfill({ status: 404, contentType: "application/json", body: JSON.stringify({ message: "Workflow test run was not found." }) });
    });
    await page.clock.install();
    await page.goto(`/?mode=validation-labels&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "Rejected by the server" })).toBeVisible();
    await expect(page.locator(".wf-runtime-reason")).toContainText("Say hello");
    await expect(page.getByRole("region", { name: "Test Run controls" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Reconnect execution" })).toHaveCount(0);
    await page.clock.runFor(10000);
    await expect(page.getByTestId("validation-labels-fixture")).not.toContainText("Workflow test run was not found.");
    expect(requests).toEqual([]);
  });
}
