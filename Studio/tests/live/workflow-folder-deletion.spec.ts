import { expect, test } from "@playwright/test";
import { readFileSync, writeFileSync } from "node:fs";
import { signIn } from "./support/journey";

test.skip(!process.env.ELSA_STUDIO_URL || !process.env.ELSA_FOLDER_DELETION_API_URL,
  "Requires an owned normal Workbench/Studio pair.");
test.setTimeout(300_000);
const apiBase = process.env.ELSA_FOLDER_DELETION_API_URL ?? "";
const receiptPath = process.env.ELSA_FOLDER_DELETION_RECEIPT;

for (const deleteWorkflows of [false, true]) {
  test(`deletes a real folder with workflow deletion ${deleteWorkflows ? "selected" : "unchecked"}`, async ({ page }, testInfo) => {
    let bearer = "";
    page.on("request", request => {
      if (request.url().startsWith(apiBase) && request.headers().authorization) bearer = request.headers().authorization;
    });
    page.setDefaultTimeout(30_000);
    await signIn(page);
    await page.goto("/workflows/definitions");
    await expect(page.getByRole("button", { name: "Create workflow", exact: true }).first()).toBeVisible();
    await expect.poll(() => !!bearer).toBe(true);
    const api = async (path: string, method = "GET", data?: unknown) => {
      const response = await page.request.fetch(`${apiBase}/design/workflows/${path}`, {
        method, headers: { Authorization: bearer }, data
      });
      expect(response.ok(), `${method} ${path}: ${response.status()}`).toBe(true);
      return response.status() === 204 ? null : response.json();
    };
    const name = `Folder deletion ${deleteWorkflows ? "chosen" : "preserve"} ${Date.now()}`;
    const folder = await api("folders", "POST", { name });
    const child = await api("folders", "POST", { name: `${name} child`, parentId: folder.id });
    const grandchild = await api("folders", "POST", { name: "Grandchild", parentId: child.id });
    await page.goto(`/workflows/definitions?folderId=${folder.id}`);
    await page.getByRole("button", { name: "Create workflow", exact: true }).first().click();
    const create = page.getByRole("dialog", { name: "Create Workflow", exact: true });
    await create.getByLabel("Display name").fill(`${name} primary`);
    const sequence = create.getByRole("radio", { name: "Sequence", exact: true });
    await sequence.focus();
    await sequence.press("Space");
    await expect(sequence).toBeChecked();
    const creationResponse = page.waitForResponse(response => response.request().method() === "POST" &&
      /\/definitions$/.test(new URL(response.url()).pathname));
    await create.getByRole("button", { name: "Create", exact: true }).click();
    const creation = await creationResponse;
    const initialState = creation.request().postDataJSON().initialState;
    const primary = (await creation.json()).definition;
    await page.getByRole("button", { name: "Publish options", exact: true }).click();
    await page.getByRole("menuitemradio", { name: "Review & publish", exact: true }).click();
    const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
    await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
    await review.getByRole("button", { name: "Publish", exact: true }).click();
    const published = page.getByRole("dialog", { name: "Published", exact: true });
    await expect(published).toBeVisible({ timeout: 60_000 });
    await published.getByRole("button", { name: "Close", exact: true }).click();
    const second = (await api("definitions", "POST", { name: `${name} second`, folderId: folder.id, initialState })).definition;
    const deleted = (await api("definitions", "POST", { name: `${name} already deleted`, folderId: folder.id, initialState })).definition;
    const inside = (await api("definitions", "POST", { name: `${name} child workflow`, folderId: grandchild.id, initialState })).definition;
    await api(`definitions/${deleted.id}`, "DELETE");
    const before = await api(`definitions/${primary.id}`);
    const originalDeleted = await api(`definitions/${deleted.id}`);
    await page.goto(`/workflows/definitions?folderId=${folder.id}&search=${encodeURIComponent(`${name} primary`)}`);
    await expect(page.getByRole("link", { name: `${name} primary`, exact: true })).toBeVisible();
    await page.getByRole("button", { name: `Expand ${name}`, exact: true }).click();
    await page.getByRole("button", { name: `Expand ${name} child`, exact: true }).click();
    await expect(page.locator(`[data-folder-id="${grandchild.id}"]`)).toBeVisible();
    await page.getByRole("group", { name: `Actions for ${name}`, exact: true }).getByRole("button", { name: "Delete", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: `Delete folder “${name}”?`, exact: true });
    await expect(dialog).toContainText("2 active workflows");
    await expect(dialog).toContainText("1 already deleted workflow");
    await expect(dialog).toContainText("1 subfolder will move to the top level");
    const checkbox = dialog.getByRole("checkbox");
    await expect(checkbox).not.toBeChecked();
    if (deleteWorkflows) await checkbox.check();
    await expect(dialog).toContainText("Unfiled");
    await page.setViewportSize({ width: 360, height: 800 });
    await expect(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeInViewport();
    const narrowScreenshot = testInfo.outputPath("folder-deletion-narrow.png");
    await page.screenshot({ path: narrowScreenshot });
    await testInfo.attach("folder-deletion-narrow", { path: narrowScreenshot, contentType: "image/png" });
    await page.setViewportSize({ width: 1440, height: 900 });
    const screenshot = testInfo.outputPath("folder-deletion.png");
    await page.screenshot({ path: screenshot });
    await testInfo.attach("folder-deletion", { path: screenshot, contentType: "image/png" });
    await dialog.getByRole("button", { name: deleteWorkflows ? "Delete folder and 2 workflows" : "Delete folder", exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator(`[data-folder-id="${grandchild.id}"]`)).toBeVisible();
    const after = await api(`definitions/${primary.id}`);
    expect(after.versions).toEqual(before.versions);
    expect(after.draft).toEqual(before.draft);
    expect(after.definition.publicationState).toBe(before.definition.publicationState);
    expect(!!after.definition.deletedAt).toBe(deleteWorkflows);
    expect((await api(`definitions/${second.id}`)).definition.deletedAt != null).toBe(deleteWorkflows);
    expect((await api(`definitions/${inside.id}`)).definition.deletedAt ?? null).toBeNull();
    expect((await api(`definitions/${deleted.id}`)).definition.deletedAt).toBe(originalDeleted.definition.deletedAt);
    expect((await api(`definitions/${primary.id}/organization`)).folderId ?? null).toBeNull();
    expect((await api(`folders/${child.id}`)).folder.parentId ?? null).toBeNull();
    if (deleteWorkflows) {
      await api(`definitions/${primary.id}/restore`, "POST", { operationKey: crypto.randomUUID() });
      expect((await api(`definitions/${primary.id}`)).definition.deletedAt ?? null).toBeNull();
    }
    if (receiptPath) {
      let receipts: unknown[] = [];
      try { receipts = JSON.parse(readFileSync(receiptPath, "utf8")); } catch { /* First phase. */ }
      receipts.push({ name, folderId: folder.id, childId: child.id, grandchildId: grandchild.id,
        primaryId: primary.id, secondId: second.id, insideId: inside.id, deletedId: deleted.id,
        deleteWorkflows, versions: before.versions, draft: before.draft, publicationState: before.definition.publicationState });
      writeFileSync(receiptPath, JSON.stringify(receipts, null, 2));
    }
  });
}

test("reads the same workflow identities and hierarchy after the owned hosts restart", async ({ page }) => {
  test.skip(process.env.ELSA_FOLDER_DELETION_PHASE !== "readback" || !receiptPath, "Run after restarting the owned pair.");
  const receipts = JSON.parse(readFileSync(receiptPath!, "utf8"));
  let bearer = "";
  page.on("request", request => { if (request.url().startsWith(apiBase) && request.headers().authorization) bearer = request.headers().authorization; });
  await signIn(page); await page.goto("/workflows/definitions");
  await expect.poll(() => !!bearer).toBe(true);
  for (const receipt of receipts) {
    const get = async (path: string) => {
      const response = await page.request.get(`${apiBase}/design/workflows/${path}`, { headers: { Authorization: bearer } });
      expect(response.ok()).toBe(true); return response.json();
    };
    const primary = await get(`definitions/${receipt.primaryId}`);
    expect(primary.definition.deletedAt ?? null).toBeNull();
    expect(primary.versions).toEqual(receipt.versions);
    expect(primary.draft).toEqual(receipt.draft);
    expect(primary.definition.publicationState).toBe(receipt.publicationState);
    expect((await get(`folders/${receipt.childId}`)).folder.parentId ?? null).toBeNull();
    expect((await get(`folders/${receipt.grandchildId}`)).folder.parentId).toBe(receipt.childId);
    expect((await get(`definitions/${receipt.primaryId}/organization`)).folderId ?? null).toBeNull();
    expect((await get(`definitions/${receipt.secondId}`)).definition.deletedAt != null).toBe(receipt.deleteWorkflows);
  }
});
