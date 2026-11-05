import { expect, test } from "@playwright/test";

for (const width of [360, 1440]) for (const theme of ["light", "dark"]) {
  test(`User Tasks collection and details ${width}px ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/user-tasks-ux.html?empty&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No tasks here", exact: true })).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", theme);
    await expect(page.locator("aside")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Next page", exact: true })).toHaveCount(0);
    const region = page.getByRole("region", { name: "Task inbox", exact: true });
    const frame = (await region.boundingBox())!;
    expect(frame.width).toBeGreaterThan(width - 70);
    expect(await region.evaluate(node => getComputedStyle(node).borderTopStyle)).toBe("solid");
    const search = page.getByRole("searchbox", { name: "Search tasks", exact: true });
    expect((await search.boundingBox())!.width).toBeGreaterThan(200);
    await page.screenshot({ path: info.outputPath("empty.png"), fullPage: true });
    await page.goto(`/user-tasks-ux.html?two&theme=${theme}`);
    const link = page.getByRole("link", { name: /^Review purchase request/ });
    await expect(link).toBeVisible();
    await page.getByRole("searchbox", { name: "Search tasks", exact: true }).fill("missing");
    await expect(page.getByRole("heading", { name: "No matching tasks", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Clear search", exact: true }).last().press("Enter");
    await expect(link).toBeVisible();
    await link.press("Enter");
    const detail = page.getByRole("complementary", { name: /^Review purchase request/ });
    await expect(detail.getByRole("heading")).toBeFocused();
    await detail.getByRole("textbox", { name: "Note" }).fill("Reviewed with keyboard");
    await expect(detail.getByRole("button", { name: "Approve", exact: true })).toBeEnabled();
    await page.getByRole("searchbox", { name: "Search tasks", exact: true }).fill("missing");
    await expect(page.getByRole("heading", { name: "No matching tasks", exact: true })).toBeVisible();
    await expect(detail.getByRole("textbox", { name: "Note" })).toHaveValue("Reviewed with keyboard");
    await page.getByRole("button", { name: "Clear search", exact: true }).last().click();
    page.once("dialog", dialog => dialog.dismiss());
    await page.getByRole("link", { name: "Approve travel request", exact: true }).click();
    await expect(detail.getByRole("textbox", { name: "Note" })).toHaveValue("Reviewed with keyboard");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("task-detail.png"), fullPage: true });
    page.once("dialog", dialog => dialog.dismiss());
    await detail.getByRole("button", { name: "Back to task list", exact: true }).press("Enter");
    await expect(detail.getByRole("textbox", { name: "Note" })).toHaveValue("Reviewed with keyboard");
    page.once("dialog", dialog => dialog.accept());
    await detail.getByRole("button", { name: "Back to task list", exact: true }).press("Enter");
    await expect(detail).toHaveCount(0); await expect(link).toBeFocused();
    await page.goto(`/user-tasks-ux.html?forms&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No reusable forms yet", exact: true })).toBeVisible();
    await expect(page.getByRole("textbox")).toHaveCount(0);
    await page.goto(`/user-tasks-ux.html?forms&populated-forms&theme=${theme}`);
    const nextVersion = page.getByRole("button", { name: "Create next version", exact: true });
    await expect(nextVersion).toBeVisible();
    expect((await nextVersion.boundingBox())!.width).toBeGreaterThan(120);
    expect((await nextVersion.boundingBox())!.height).toBeLessThan(48);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("forms-list.png"), fullPage: true });
    await nextVersion.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath("form-version-action.png"), fullPage: true });
    await page.getByRole("button", { name: "Create form version", exact: true }).first().press("Enter");
    await expect(page.getByRole("textbox", { name: "Form ID", exact: true })).toBeFocused();
    await expect(page.getByRole("region", { name: "Reusable form versions" })).toHaveCount(0);
    await page.getByRole("button", { name: "Add field", exact: true }).click();
    const checkbox = page.getByRole("checkbox", { name: "Required", exact: true });
    expect((await checkbox.boundingBox())!.width).toBeLessThanOrEqual(20);
    expect((await checkbox.boundingBox())!.height).toBeLessThanOrEqual(20);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("form-authoring.png"), fullPage: true });
    await page.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(page.getByRole("button", { name: "Create form version", exact: true }).first()).toBeFocused();
  });
}

test("User Tasks distinguish denied and failed requests from an empty inbox", async ({ page }) => {
  for (const [query, title] of [["denied", "Task access unavailable"], ["error", "Tasks unavailable"]]) {
    await page.goto(`/user-tasks-ux.html?${query}`);
    await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "No tasks here", exact: true })).toHaveCount(0);
    await expect(page.locator("aside")).toHaveCount(0);
  }
});

test("an unconfirmed response survives filtering and refuses task switches until resolved", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?two&ambiguous");
  await page.getByRole("link", { name: /^Review purchase request/ }).click();
  const detail = page.getByRole("complementary", { name: /^Review purchase request/ });
  await detail.getByRole("textbox", { name: /^Note/ }).fill("Keep this exact response");
  await detail.getByRole("button", { name: "Approve", exact: true }).click();
  await expect(detail.getByRole("button", { name: "Retry Approve", exact: true })).toBeVisible();
  await expect(detail.getByRole("button", { name: "Back to task list", exact: true })).toBeDisabled();
  await page.getByRole("searchbox", { name: "Search tasks", exact: true }).fill("missing");
  await page.getByRole("button", { name: "Clear search", exact: true }).last().click();
  await page.getByRole("link", { name: "Approve travel request", exact: true }).click();
  await expect(detail.getByRole("textbox", { name: /^Note/ })).toHaveValue("Keep this exact response");
  await detail.getByRole("button", { name: "Retry Approve", exact: true }).click();
  const sent = await page.evaluate(() => (window as unknown as { taskRequests: unknown[] }).taskRequests);
  expect(sent).toHaveLength(2); expect(sent[1]).toEqual(sent[0]);
});

 test("a failed form publication does not break the collection or its paging", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?forms&form-error");
  await expect(page.getByRole("button", { name: "More form versions", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Create form version", exact: true }).first().click();
  await page.getByRole("textbox", { name: "Form ID", exact: true }).fill("purchase");
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("Purchase response");
  await page.getByRole("button", { name: "Publish form version", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("This version already exists");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "More form versions", exact: true })).toBeVisible();
});
