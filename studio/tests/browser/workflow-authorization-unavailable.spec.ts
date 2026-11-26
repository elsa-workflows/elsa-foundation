import { expect, test } from "@playwright/test";

// Spec 1193, story 5: the designer's definition load shows an owner that cannot decide as unavailable, with its stable
// code, distinct from a denial. The fixture serves the design API's exact bodies through the real client and banner.

test("an owner that cannot decide shows the unavailable state with its stable code", async ({ page }) => {
  await page.goto("/?mode=workflow-authorization-unavailable&outcome=unavailable");

  const alert = page.getByRole("alert");
  await expect(alert).toBeVisible();
  await expect(alert.locator(".wf-alert-message")).toHaveText("Workflow definition authorization could not be completed.");
  await expect(alert.locator(".wf-alert-code")).toHaveText("workflow.authorization.unavailable");
  await expect(alert.locator(".wf-alert-status")).toHaveText("HTTP 503");
  await expect(alert).not.toContainText("not readable");
});

test("a denying owner keeps the denied state, without the unavailable code", async ({ page }) => {
  await page.goto("/?mode=workflow-authorization-unavailable&outcome=denied");

  const alert = page.getByRole("alert");
  await expect(alert).toBeVisible();
  await expect(alert.locator(".wf-alert-message")).toHaveText("The requested workflow definition is not readable in the current application context.");
  await expect(alert.locator(".wf-alert-status")).toHaveText("HTTP 403");
  await expect(alert.locator(".wf-alert-code")).toHaveCount(0);
  await expect(alert).not.toContainText("workflow.authorization.unavailable");
});
