import { expect, test } from "@playwright/test";

test("keeps the publication decision calm and actions visible at constrained height", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 620 });
  await page.goto("/?mode=publication-review&theme=black-glass");

  const dialog = page.getByRole("dialog", { name: "Review and publish" });
  await expect(dialog).toContainText("Publication channel");
  await expect(dialog).toContainText("Replace the current publication in default");
  await expect(dialog).toContainText("2.0.0 · assigned automatically by version policy");
  await expect(dialog).toContainText("Ready to publish");
  await expect(dialog.getByText("Advanced details")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeVisible();

  const dialogBox = await dialog.boundingBox();
  const footerBox = await dialog.locator(".wf-publication-footer").boundingBox();
  expect(dialogBox).not.toBeNull();
  expect(footerBox).not.toBeNull();
  expect(footerBox!.y + footerBox!.height).toBeLessThanOrEqual(620);
  expect(footerBox!.y + footerBox!.height).toBeLessThanOrEqual(dialogBox!.y + dialogBox!.height + 1);
});

test("creates a named Publication channel and refreshes review automatically", async ({ page }) => {
  await page.goto("/?mode=publication-review");
  const dialog = page.getByRole("dialog", { name: "Review and publish" });

  await dialog.getByLabel("Publication channel").selectOption("__create-publication-channel__");
  await dialog.getByLabel("New publication channel").fill("canary");

  await expect(dialog).toContainText("Create a separate publication channel named canary");
  await expect(dialog).toContainText("Ready to publish");
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeEnabled();
});

test("offers exact version editing only when the server advertises support", async ({ page }) => {
  await page.goto("/?mode=publication-review");
  const dialog = page.getByRole("dialog", { name: "Review and publish" });

  await dialog.getByText("Edit version").click();
  await dialog.getByRole("radio", { name: /Exact semantic version/ }).check();
  await dialog.getByRole("textbox", { name: "Exact semantic version" }).fill("2.3.0-rc.1");

  await expect(dialog).toContainText("2.3.0-rc.1");
  await expect(dialog).toContainText("Ready to publish");
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeEnabled();

  await page.goto("/?mode=publication-review&exact=unsupported");
  await expect(page.getByRole("dialog", { name: "Review and publish" }).getByText("Edit version")).toHaveCount(0);
});

test("success and retained-promotion recovery keep their actions in the fixed footer", async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 520 });
  await page.goto("/?mode=publication-review&theme=black-glass");
  await page.getByRole("button", { name: "Publish" }).click();

  const success = page.getByRole("dialog", { name: "Publication complete" });
  await expect(success).toContainText("Workflow is published");
  await expect(success.getByRole("button", { name: "Close" })).toBeVisible();
  await expect(success.getByRole("button", { name: "Open published executable" })).toBeVisible();

  await page.goto("/?mode=publication-review&outcome=recovery&theme=black-glass");
  await page.getByRole("button", { name: "Publish" }).click();
  const recovery = page.getByRole("dialog", { name: "Publication needs attention" });
  await expect(recovery).toContainText("version was retained, but the channel was not activated");
  await expect(recovery.getByRole("button", { name: "Close" })).toBeVisible();
  await expect(recovery.getByRole("button", { name: "Retry publication" })).toBeVisible();
});
