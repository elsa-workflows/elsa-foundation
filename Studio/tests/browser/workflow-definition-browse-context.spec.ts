import { expect, test } from "@playwright/test";

test("narrow keyboard users preserve workflow browse context through global search, history, and reload", async ({ page }) => {
  await page.setViewportSize({ width: 430, height: 760 });
  await page.goto("/?theme=light&mode=folder-restructure&folderId=folder-operations&state=all&search=workflow");

  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search selected folder" })).toHaveValue("workflow");
  await expect(page.getByRole("button", { name: "All states" })).toHaveAttribute("aria-selected", "true");

  await page.getByRole("button", { name: "Search all workflows" }).click();
  await expect(page.getByText("All workflow", { exact: true })).toBeVisible();
  await expect(page).not.toHaveURL(/folderId=/);
  await expect(page).toHaveURL(/state=all/);
  await expect(page).toHaveURL(/search=workflow/);

  const resultBreadcrumb = page.getByRole("navigation", { name: "Folder for All workflow" });
  const operations = resultBreadcrumb.getByRole("button", { name: "Operations" });
  await operations.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page).toHaveURL(/folderId=folder-operations/);
  await expect(page).not.toHaveURL(/definition=/);

  await page.goBack();
  await expect(page.getByText("All workflow", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search all workflows" })).toHaveValue("workflow");
  await page.goBack();
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search selected folder" })).toHaveValue("workflow");

  await page.goForward();
  await expect(page.getByText("All workflow", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search all workflows" })).toHaveValue("workflow");
  await page.goBack();
  await expect(page.getByText("Operations workflow")).toBeVisible();

  const requests = await page.evaluate(() =>
    (window as Window & { browseRequests?: string[] }).browseRequests ?? []);
  expect(requests.some(url => url.includes("folderId=folder-operations") && url.includes("state=all") && url.includes("search=workflow"))).toBe(true);
  expect(requests.some(url => !url.includes("folderId=") && url.includes("state=all") && url.includes("search=workflow"))).toBe(true);

  await page.reload();
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.getByRole("button", { name: "Folders" })).toBeVisible();
});
