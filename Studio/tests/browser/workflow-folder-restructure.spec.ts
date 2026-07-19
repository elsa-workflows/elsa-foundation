import { expect, test } from "@playwright/test";

test("keyboard users can rename, restructure, and empty-delete folders in a narrow public-capability view", async ({ page }) => {
  await page.setViewportSize({ width: 430, height: 760 });
  await page.goto("/?theme=light&mode=folder-restructure");

  const folderPicker = page.getByRole("button", { name: "Folders" });
  await folderPicker.focus();
  await page.keyboard.press("Enter");
  const platform = page.getByRole("treeitem", { name: /Platform/ });
  await platform.focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("treeitem", { name: /Operations/ })).toBeVisible();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("treeitem", { name: /Operations/ })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.getByText("Operations workflow")).toBeVisible();

  const rename = page.getByRole("button", { name: "Rename", exact: true });
  await rename.focus();
  await page.keyboard.press("Enter");
  const renameDialog = page.getByRole("dialog", { name: "Rename folder" });
  await renameDialog.getByRole("textbox", { name: "Folder name" }).fill("Renamed operations");
  await renameDialog.getByRole("button", { name: "Rename", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(renameDialog).toBeHidden();
  await expect(page.getByText("Folder renamed", { exact: true })).toBeVisible();
  await expect(page.locator(".wf-folder-breadcrumb")).toContainText("Renamed operations");
  await expect(rename).toBeFocused();

  const move = page.getByRole("button", { name: "Move", exact: true });
  await move.focus();
  await page.keyboard.press("Enter");
  const moveDialog = page.getByRole("dialog", { name: "Move folder" });
  await expect(moveDialog.getByRole("radio", { name: "Archive" })).toBeVisible();
  await moveDialog.getByRole("button", { name: "Expand Platform" }).click();
  await expect(moveDialog.getByRole("radio", { name: "Renamed operations" })).toHaveCount(0);
  await expect(moveDialog.getByText("Private descendant")).toHaveCount(0);
  await moveDialog.getByRole("radio", { name: "Archive" }).check();
  await moveDialog.getByRole("button", { name: "Move", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(moveDialog).toBeHidden();
  await expect(page.locator(".wf-folder-breadcrumb")).toContainText("Archive");
  await expect(page.locator(".wf-folder-breadcrumb")).toContainText("Renamed operations");
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(move).toBeFocused();

  await folderPicker.click();
  const archive = page.getByRole("treeitem", { name: /Archive/ });
  await archive.focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("treeitem", { name: /Empty folder/ })).toBeVisible();
  await page.getByRole("treeitem", { name: /Empty folder/ }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByText("No active workflow definitions")).toBeVisible();
  const remove = page.getByRole("button", { name: "Delete", exact: true });
  await remove.focus();
  await page.keyboard.press("Enter");

  await expect(page.getByText("Deleted folder Empty folder", { exact: true })).toBeVisible();
  await expect(page.getByText("Archive workflow")).toBeVisible();
  await expect(folderPicker).toBeFocused();
  await expect.poll(() => page.evaluate(() => (window as Window & { folderConfirmations?: unknown[] }).folderConfirmations?.length)).toBe(1);
  await expect.poll(() => page.evaluate(() => (window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests)).toEqual([
    { method: "POST", url: "/browser/restructure/folders/folder-operations/rename", body: { name: "Renamed operations" } },
    { method: "POST", url: "/browser/restructure/folders/folder-operations/move", body: { parentId: "folder-archive" } },
    { method: "DELETE", url: "/browser/restructure/folders/folder-empty" }
  ]);
});

test("rejected move keeps the destination, selected folder, direct members, and reachable short dialog", async ({ page }) => {
  await page.setViewportSize({ width: 760, height: 430 });
  await page.goto("/?theme=light&mode=folder-restructure&failure=move");

  await page.getByRole("treeitem", { name: /Platform/ }).focus();
  await page.keyboard.press("ArrowRight");
  await page.getByRole("treeitem", { name: /Operations/ }).click();
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await page.getByRole("button", { name: "Move", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Move folder" });
  const archive = dialog.getByRole("radio", { name: "Archive" });
  await archive.check();
  await dialog.getByRole("button", { name: "Move", exact: true }).click();

  await expect(dialog.getByRole("alert")).toContainText("Move rejected by the server");
  await expect(archive).toBeChecked();
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.locator("[data-folder-id='folder-operations']")).toHaveAttribute("aria-selected", "true");
  const box = await dialog.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.y).toBeGreaterThanOrEqual(0);
  expect(box!.y + box!.height).toBeLessThanOrEqual(430);
});

test("rejected delete retains tree, table, selection, and action focus", async ({ page }) => {
  await page.setViewportSize({ width: 980, height: 720 });
  await page.goto("/?theme=light&mode=folder-restructure&failure=delete");

  const archive = page.getByRole("treeitem", { name: /Archive/ });
  await archive.focus();
  await page.keyboard.press("ArrowRight");
  const empty = page.getByRole("treeitem", { name: /Empty folder/ });
  await empty.click();
  await expect(page.getByText("No active workflow definitions")).toBeVisible();
  const remove = page.getByRole("button", { name: "Delete", exact: true });
  await remove.focus();
  await page.keyboard.press("Enter");

  await expect(page.getByRole("alert")).toContainText("Folder is not empty");
  await expect(empty).toHaveAttribute("aria-selected", "true");
  await expect(archive).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByText("No active workflow definitions")).toBeVisible();
  await expect(remove).toBeFocused();
});

test("folder restructuring controls stay absent when mutation relations are not advertised", async ({ page }) => {
  await page.setViewportSize({ width: 980, height: 720 });
  await page.goto("/?theme=light&mode=folder-restructure&capabilities=absent");

  const platform = page.getByRole("treeitem", { name: /Platform/ });
  await platform.focus();
  await page.keyboard.press("ArrowRight");
  await page.getByRole("treeitem", { name: /Operations/ }).click();
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.locator(".wf-folder-actions")).toHaveCount(0);
  expect(await page.evaluate(() => (window as Window & { folderMutationRequests?: unknown[] }).folderMutationRequests)).toBeUndefined();
});

test("renaming a continuation-page folder retains its selected and expanded tree context", async ({ page }) => {
  await page.setViewportSize({ width: 980, height: 720 });
  await page.goto("/?theme=light&mode=folder-restructure&paging=continuation");

  await page.getByRole("treeitem", { name: "Load more folders" }).click();
  await expect(page.getByRole("treeitem", { name: "Load more folders" })).toHaveCount(0);
  const archive = page.getByRole("treeitem", { name: /Archive/ });
  await expect(archive).toBeVisible();
  await archive.focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("treeitem", { name: /Empty folder/ })).toBeVisible();
  await archive.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByText("Archive workflow")).toBeVisible();
  await page.getByRole("button", { name: "Rename", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Rename folder" });
  await dialog.getByRole("textbox", { name: "Folder name" }).fill("Long-term archive");
  await dialog.getByRole("button", { name: "Rename", exact: true }).click();

  const renamed = page.getByRole("treeitem", { name: /Long-term archive/ });
  await expect(renamed).toBeVisible();
  await expect(renamed).toHaveAttribute("aria-selected", "true");
  await expect(renamed).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("treeitem", { name: /Empty folder/ })).toBeVisible();
  await expect(page.locator(".wf-folder-breadcrumb")).toContainText("Long-term archive");
  await expect(page.getByText("Archive workflow")).toBeVisible();
});
