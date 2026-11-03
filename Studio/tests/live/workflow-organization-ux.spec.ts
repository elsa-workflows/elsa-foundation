import { expect, test } from "@playwright/test";
import { signIn } from "./support/journey";

test.skip(!process.env.ELSA_STUDIO_URL, "Requires a local Studio/Workbench pair; creates a qualification workflow, folder and tag.");

test("separate folder and chip-tag flows persist without changing authored workflow state", async ({ page }, testInfo) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(30_000);
  page.setDefaultNavigationTimeout(30_000);
  const suffix = Date.now();
  const name = `Organization UX ${suffix}`;
  const folderName = `Organization review ${suffix}`;
  const tagName = `ZZZ Customer-facing ${suffix}`;
  let bearer = "";
  page.on("request", request => { if (request.headers().authorization?.startsWith("Bearer ")) bearer = request.headers().authorization; });
  await signIn(page);
  await page.goto("/workflows/definitions");
  await page.getByRole("button", { name: "Create folder", exact: true }).click();
  const folder = page.getByRole("dialog", { name: "Create folder", exact: true });
  await folder.getByRole("textbox").fill(folderName);
  const folderCreatedResponse = page.waitForResponse(response => response.request().method() === "POST" && /\/folders$/.test(new URL(response.url()).pathname));
  await folder.getByRole("button", { name: "Create", exact: true }).click();
  const folderCreated = await (await folderCreatedResponse).json() as { id: string };
  await expect(folder).toBeHidden();
  await page.getByRole("group", { name: "Workflow definition page actions", exact: true }).getByRole("button", { name: "Create workflow", exact: true }).click();
  const create = page.getByRole("dialog", { name: "Create Workflow", exact: true });
  await create.getByLabel("Display name").fill(name);
  await create.getByRole("radio", { name: "Sequence", exact: true }).focus();
  await page.keyboard.press("Space");
  const createdResponse = page.waitForResponse(response => response.request().method() === "POST" && /\/definitions$/.test(new URL(response.url()).pathname));
  await create.getByRole("button", { name: "Create", exact: true }).click();
  const created = await createdResponse;
  expect(created.ok()).toBe(true);
  const creation = await created.json() as { definition: { id: string } };
  await page.getByRole("button", { name: "Definitions", exact: true }).click();
  const readAuthoring = async () => {
    const response = await page.request.get(`${created.url()}/${creation.definition.id}`, { headers: { Authorization: bearer } });
    expect(response.ok()).toBe(true);
    const value = await response.json() as { draft: unknown; versions: unknown[] };
    return { draft: value.draft, versions: value.versions };
  };
  const before = await readAuthoring();
  const tagCatalogUrl = created.url().replace(/\/definitions$/, "/tags");
  // Keep the new label beyond the catalog's first 100 items, independently of UI search.
  for (let start = 0; start < 100; start += 8) {
    await Promise.all(Array.from({ length: Math.min(8, 100 - start) }, async (_, offset) => {
      const response = await page.request.post(tagCatalogUrl, {
        headers: { Authorization: bearer },
        data: { name: `AAA catalog ${suffix} ${start + offset}`, mode: "marker", cardinality: "single", values: [] }
      });
      expect(response.ok()).toBe(true);
    }));
  }
  const search = page.getByRole("searchbox", { name: "Search all workflows", exact: true });
  await search.fill(name);
  const row = page.getByRole("row", { name: `Open workflow definition ${name}`, exact: true });
  await row.getByRole("button", { name: `Actions for ${name}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const tags = page.getByRole("dialog", { name: `Tags for ${name}`, exact: true });
  await tags.getByRole("combobox", { name: "Tags", exact: true }).fill(tagName);
  const tagCreatedResponse = page.waitForResponse(response => response.request().method() === "POST" && /\/tags$/.test(new URL(response.url()).pathname));
  await tags.getByRole("option", { name: `Create “${tagName}”`, exact: true }).click();
  const createdTag = await (await tagCreatedResponse).json() as { id: string };
  const firstCatalogResponse = await page.request.get(`${tagCatalogUrl}?pageSize=100`, { headers: { Authorization: bearer } });
  expect(firstCatalogResponse.ok()).toBe(true);
  const firstCatalog = await firstCatalogResponse.json() as { items: { id: string }[]; nextContinuationToken?: string };
  expect(firstCatalog.nextContinuationToken).toBeTruthy();
  expect(firstCatalog.items.some(tag => tag.id === createdTag.id)).toBe(false);
  await expect(tags.getByRole("button", { name: `Remove ${tagName}`, exact: true })).toBeVisible();
  await expect(tags.getByText("Finding tags…", { exact: true })).toHaveCount(0);
  await expect(tags.getByRole("button", { name: "Save tags", exact: true })).toBeEnabled();
  await testInfo.attach("live-tag-chips", { body: await page.screenshot({ path: testInfo.outputPath("tag-chips.png") }), contentType: "image/png" });
  await tags.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(tags).toBeHidden();
  await row.getByRole("button", { name: `Actions for ${name}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Move to folder…", exact: true }).click();
  const move = page.getByRole("dialog", { name: "Move workflow definitions", exact: true });
  await move.getByRole("radio", { name: folderName, exact: true }).check();
  await testInfo.attach("live-folder-move", { body: await page.screenshot({ path: testInfo.outputPath("folder-move.png") }), contentType: "image/png" });
  await move.getByRole("button", { name: "Move", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.reload();
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: `Actions for ${name}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  await expect(tags.getByRole("button", { name: `Remove ${tagName}`, exact: true })).toBeVisible();
  await tags.getByRole("button", { name: "Close", exact: true }).click();
  expect(await readAuthoring()).toEqual(before);
  const organizationResponse = await page.request.get(`${created.url()}/${creation.definition.id}/organization`, { headers: { Authorization: bearer } });
  expect(organizationResponse.ok()).toBe(true);
  const organization = await organizationResponse.json() as { folderId: string; tags: unknown[] };
  expect(organization.folderId).toBe(folderCreated.id);
  expect(organization.tags).toHaveLength(1);
  await page.getByRole("treeitem", { name: new RegExp(folderName) }).click();
  await expect(row).toBeVisible();
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  const columns = page.getByRole("group", { name: "Optional columns", exact: true });
  await columns.getByRole("checkbox", { name: "Tags", exact: true }).check();
  await columns.getByRole("checkbox", { name: "Location", exact: true }).check();
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  await expect(row.getByRole("cell", { name: tagName, exact: true })).toBeVisible();
  await testInfo.attach("live-library", { body: await page.screenshot({ path: testInfo.outputPath("library.png") }), contentType: "image/png" });
});
