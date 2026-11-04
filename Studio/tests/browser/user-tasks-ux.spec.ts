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
    await page.goto(`/user-tasks-ux.html?theme=${theme}`);
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
    await page.getByRole("button", { name: "Create form version", exact: true }).first().press("Enter");
    await expect(page.getByRole("textbox", { name: "Form ID", exact: true })).toBeFocused();
    await expect(page.getByRole("region", { name: "Reusable form versions" })).toHaveCount(0);
    await page.getByRole("button", { name: "Add field", exact: true }).click();
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
