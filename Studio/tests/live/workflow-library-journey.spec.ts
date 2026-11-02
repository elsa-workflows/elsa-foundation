import { expect, test, type Page } from "@playwright/test";
import { signIn } from "./support/journey";

// Run the first journey, restart the same owned Workbench/Studio pair, then run the
// second journey to establish actual durable readback. No HTTP routes are mocked.
test.skip(!process.env.ELSA_STUDIO_URL, "Requires an owned normal Workbench/Studio pair.");
test.setTimeout(300_000);

const prefix = process.env.ELSA_LIBRARY_NAME ?? "Workflow library delivery";
const folderName = `${prefix} finance`;
const workflowName = `${prefix} payment`;
const tagName = `${prefix} critical`;
const teamName = `${prefix} owning team`;
const viewName = `${prefix} published finance`;

async function openLibrary(page: Page) {
  await signIn(page);
  await page.goto("/workflows/definitions");
  await expect(page.getByRole("region", { name: "Workflow library organization" }))
    .toBeVisible({ timeout: 60_000 });
}

function workflowRow(page: Page) {
  return page.getByRole("row").filter({ has: page.getByText(workflowName, { exact: true }) });
}

test("organizes and queries a real workflow through folders, tags, publication, views and favorites", async ({ page }, testInfo) => {
  await openLibrary(page);
  await page.getByRole("button", { name: "Create folder", exact: true }).click();
  const folderDialog = page.getByRole("dialog", { name: "Create folder", exact: true });
  await folderDialog.getByRole("textbox").fill(folderName);
  await folderDialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(folderDialog).toBeHidden();
  await page.getByRole("treeitem", { name: new RegExp(folderName) }).click();

  await page.getByRole("button", { name: /^(Create|Create workflow)$/ }).click();
  const create = page.getByRole("dialog", { name: "Create Workflow", exact: true });
  await create.getByLabel("Display name").fill(workflowName);
  const sequence = create.getByRole("radio", { name: "Sequence" });
  await expect(sequence).toBeEnabled();
  await sequence.focus();
  await sequence.press("Space");
  await expect(sequence).toBeChecked();
  const creationResponse = page.waitForResponse(response => response.request().method() === "POST" &&
    /\/definitions$/.test(new URL(response.url()).pathname));
  await create.getByRole("button", { name: "Create", exact: true }).click();
  const creation = await (await creationResponse).json() as { definition: { id: string }; draft: { id: string } };
  const editorUrl = page.url();
  await expect(page.getByRole("button", { name: "Definitions", exact: true })).toBeVisible();

  // An empty Sequence is deliberately triggerless. Its publication must still enter
  // the authority-backed Published query without manufacturing a trigger or tag.
  await page.getByRole("button", { name: "Publish options", exact: true }).click();
  await page.getByRole("menuitemradio", { name: "Review & publish", exact: true }).click();
  const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
  await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
  await review.getByRole("button", { name: "Publish", exact: true }).click();
  const published = page.getByRole("dialog", { name: "Published", exact: true });
  await expect(published).toBeVisible({ timeout: 60_000 });
  await published.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Definitions", exact: true }).click();
  await expect(workflowRow(page)).toBeVisible();

  await page.getByRole("button", { name: "Manage tags", exact: true }).click();
  const catalog = page.getByRole("dialog", { name: "Manage workflow tags", exact: true });
  await catalog.getByLabel("Tag name", { exact: true }).fill(tagName);
  await catalog.getByRole("button", { name: "Create tag", exact: true }).click();
  await expect(catalog.getByRole("status")).toContainText(`Saved tag ${tagName}`);
  await catalog.getByLabel("Tag to manage", { exact: true }).selectOption("");
  await catalog.getByLabel("Tag name", { exact: true }).fill(teamName);
  await catalog.getByLabel("Tag kind", { exact: true }).selectOption("controlled");
  await catalog.getByRole("button", { name: "Add controlled value", exact: true }).click();
  await catalog.getByLabel("Value name 1", { exact: true }).fill("Finance");
  await catalog.getByRole("button", { name: "Add controlled value", exact: true }).click();
  await catalog.getByLabel("Value name 2", { exact: true }).fill("Operations");
  const controlledResponse = page.waitForResponse(response => response.request().method() === "POST" &&
    new URL(response.url()).pathname.endsWith("/tags"));
  await catalog.getByRole("button", { name: "Create tag", exact: true }).click();
  const controlledTagReceipt = await (await controlledResponse).json() as { values: { id: string; name: string }[] };
  await expect(catalog.getByRole("status")).toContainText(`Saved tag ${teamName}`);
  expect(controlledTagReceipt.values).toHaveLength(2);
  expect(controlledTagReceipt.values.every(value => value.id.startsWith("value-"))).toBe(true);
  await catalog.getByRole("button", { name: "Close", exact: true }).click();

  await workflowRow(page).getByRole("button", { name: `Actions for ${workflowName}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const organization = page.getByRole("dialog", { name: `Organize ${workflowName}`, exact: true });
  await expect(organization).toContainText(folderName);
  await organization.getByRole("checkbox", { name: tagName, exact: true }).check();
  await organization.getByRole("group", { name: `${teamName} · choose one`, exact: true }).getByRole("checkbox", { name: "Finance", exact: true }).check();
  await organization.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(organization).toBeHidden();

  await workflowRow(page).getByRole("checkbox").check();
  await page.getByRole("button", { name: "Edit tags", exact: true }).click();
  const bulk = page.getByRole("dialog", { name: "Edit tags on 1 workflows", exact: true });
  await bulk.getByLabel("Bulk tag change", { exact: true }).selectOption("remove");
  await bulk.getByRole("checkbox", { name: tagName, exact: true }).check();
  await bulk.getByRole("button", { name: "Preview changes", exact: true }).click();
  await expect(bulk.getByRole("region", { name: "Bulk tag preview", exact: true })).toBeVisible();
  const bulkResponse = page.waitForResponse(response => response.request().method() === "POST" &&
    new URL(response.url()).pathname.endsWith("/tags/bulk"));
  await bulk.getByRole("button", { name: "Apply tag changes", exact: true }).click();
  const bulkReceipt = await (await bulkResponse).json();
  await expect(bulk.getByRole("status", { name: "Bulk tag results", exact: true })).toContainText("success");
  await bulk.getByRole("button", { name: "Close", exact: true }).click();
  await workflowRow(page).getByRole("button", { name: `Actions for ${workflowName}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  await expect(organization.getByRole("checkbox", { name: tagName, exact: true })).not.toBeChecked();
  await expect(organization.getByRole("group", { name: `${teamName} · choose one`, exact: true }).getByRole("checkbox", { name: "Finance", exact: true })).toBeChecked();
  await organization.getByRole("checkbox", { name: tagName, exact: true }).check();
  await organization.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(organization).toBeHidden();

  await workflowRow(page).getByRole("button", { name: `Actions for ${workflowName}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Add to favorites", exact: true }).click();
  await page.getByRole("button", { name: "Favorites", exact: true }).click();
  await expect(workflowRow(page)).toBeVisible();
  await page.getByRole("button", { name: /^Filters/ }).click();
  await page.getByLabel("Publication filter", { exact: true }).selectOption("published");
  await page.getByRole("button", { name: "Add tag filter", exact: true }).click();
  const filter = page.getByLabel("Tag filter 1", { exact: true });
  await filter.selectOption({ label: tagName });
  await expect(workflowRow(page)).toBeVisible();

  await page.getByRole("button", { name: "Save view", exact: true }).click();
  const saveView = page.getByRole("dialog", { name: "Save workflow view", exact: true });
  await saveView.getByLabel("View name", { exact: true }).fill(viewName);
  await saveView.getByRole("checkbox", { name: "Share within this tenant" }).check();
  await saveView.getByRole("button", { name: "Save as new view", exact: true }).click();
  await expect(saveView).toBeHidden();
  await page.getByLabel("Saved workflow view", { exact: true }).selectOption({ label: `${viewName} · Shared` });
  await expect(workflowRow(page)).toBeVisible();
  const savedUrl = page.url();

  // Organization changes must not create another authored draft/version. Reopen the
  // definition through the normal UI and retain its actual server response as evidence.
  const detailResponse = page.waitForResponse(response => response.request().method() === "GET" &&
    new URL(response.url()).pathname.endsWith(`/definitions/${creation.definition.id}`));
  await workflowRow(page).getByText(workflowName, { exact: true }).click();
  const details = await (await detailResponse).json() as { definition: { id: string; versionCount: number; folderId: string | null }; draft?: { id: string }; versions: { id: string }[] };
  expect(details.definition.id).toBe(creation.definition.id);
  expect(details.definition.folderId).toBeTruthy();
  expect(details.definition.versionCount).toBe(1);
  expect(details.versions).toHaveLength(1);
  if (details.draft) expect(details.draft.id).toBe(creation.draft.id);
  await page.getByRole("button", { name: "Definitions", exact: true }).click();
  await expect(page).toHaveURL(savedUrl);
  await expect(workflowRow(page)).toBeVisible();

  await testInfo.attach("real-library-identities", { body: JSON.stringify({ creation, details, editorUrl, savedUrl, bulkReceipt, controlledTagReceipt }, null, 2), contentType: "application/json" });
  await testInfo.attach("real-library-desktop", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
});

test("reopens persisted library organization after the owned host restart", async ({ page }, testInfo) => {
  await openLibrary(page);
  await page.getByLabel("Saved workflow view", { exact: true }).selectOption({ label: `${viewName} · Shared` });
  await expect(workflowRow(page)).toBeVisible({ timeout: 60_000 });
  await expect(page.getByRole("button", { name: "Favorites", exact: true })).toHaveAttribute("aria-pressed", "true");
  await workflowRow(page).getByRole("button", { name: `Actions for ${workflowName}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const organization = page.getByRole("dialog", { name: `Organize ${workflowName}`, exact: true });
  await expect(organization).toContainText(folderName);
  await expect(organization.getByRole("checkbox", { name: tagName, exact: true })).toBeChecked();
  await expect(organization.getByRole("group", { name: `${teamName} · choose one`, exact: true }).getByRole("checkbox", { name: "Finance", exact: true })).toBeChecked();
  await organization.getByRole("button", { name: "Close", exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByLabel("Saved workflow view", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Folders", exact: true })).toBeVisible();
  await testInfo.attach("real-library-restarted-narrow", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
});
