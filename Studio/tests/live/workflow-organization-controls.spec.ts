import { expect, test, type Page } from "@playwright/test";
import { readFileSync, writeFileSync } from "node:fs";
import { signIn } from "./support/journey";

// Uses actual independently configured hosts, never page.route API mocks. Run phase "disable",
// restart the same owned hosts, then phase "restore" against their retained databases.
test.skip(process.env.ELSA_ORGANIZATION_CONTROLS_LIVE !== "1", "Requires the owned organization-controls stack.");
test.setTimeout(300_000);
const phase = process.env.ELSA_ORGANIZATION_PHASE ?? "disable";
const primaryFile = process.env.ELSA_ORGANIZATION_PRIMARY_SHELLS!;
const secondaryFile = process.env.ELSA_ORGANIZATION_SECONDARY_SHELLS!;
const proofFile = process.env.ELSA_ORGANIZATION_PROOF!;
const apiBase = process.env.ELSA_ORGANIZATION_API ?? "http://api-a.studio.localhost:56951/default/";

interface Definition { id: string; name: string; folderId?: string; tags?: unknown[]; favorite: boolean; publishedVersionId?: string }
interface Folder { id: string; parentId: string | null; name: string }
interface View { id: string; name: string; shared: boolean; ownerId: string; query: unknown; columns: string[]; revision: string }
interface Snapshot { definition: Definition; organization: unknown; folders: Folder[]; tags: unknown; views: View[]; executable: { artifactId: string; liveSourceReferenceCount: number; references: { id: string }[] }; url: string; secondary: string; configuration: unknown }
interface AuthoredNode { inputs?: { referenceKey: string; value?: { expressionType: string; value?: unknown } }[]; structure?: { payload?: { activities?: AuthoredNode[] } } }
interface DraftDocument { id: string; state: { rootActivity?: AuthoredNode }; layout: unknown[]; activityPresentation?: unknown[] }

function editLiteralText(node: AuthoredNode | undefined): boolean {
  const text = node?.inputs?.find(input => input.referenceKey === "text" && input.value?.expressionType === "Literal" && typeof input.value.value === "string");
  if (text?.value) { text.value.value = `Organization controls publishing proof ${Date.now()}`; return true; }
  return node?.structure?.payload?.activities?.some(editLiteralText) ?? false;
}

async function request<T>(page: Page, bearer: string, path: string, method = "GET", body?: unknown) {
  return page.evaluate(async ({ base, bearer, path, method, body }) => {
    const response = await fetch(new URL(path, base), { method, cache: "no-store", credentials: "include", headers: { Authorization: bearer, ...(body === undefined ? {} : { "Content-Type": "application/json" }) }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
    const text = await response.text();
    return { status: response.status, body: text ? JSON.parse(text) : null };
  }, { base: apiBase, bearer, path, method, body }) as Promise<{ status: number; body: T }>;
}

async function read<T>(page: Page, bearer: string, path: string) {
  const result = await request<T>(page, bearer, path);
  expect(result.status, path).toBe(200);
  return result.body;
}

async function snapshot(page: Page, bearer: string, definitionId: string, secondary: string): Promise<Snapshot> {
  const definition = (await read<{ items: Definition[] }>(page, bearer, "design/workflows/definitions/page?pageSize=100")).items.find(item => item.id === definitionId)!;
  expect(definition).toBeTruthy();
  const organization = await read(page, bearer, `design/workflows/definitions/${definitionId}/organization`);
  const roots = await read<{ items: Folder[] }>(page, bearer, "design/workflows/folders?pageSize=100");
  const children = await Promise.all(roots.items.map(folder => read<{ items: Folder[] }>(page, bearer, `design/workflows/folders?parentId=${encodeURIComponent(folder.id)}&pageSize=100`)));
  const folders = [...roots.items, ...children.flatMap(page => page.items)].sort((a, b) => a.id.localeCompare(b.id));
  const tags = await read(page, bearer, "design/workflows/tags?pageSize=100");
  const views = (await read<{ items: View[] }>(page, bearer, "design/workflows/views")).items.sort((a, b) => a.id.localeCompare(b.id));
  const executable = (await read<{ items: (Snapshot["executable"] & { definitionId: string })[] }>(page, bearer, "runtime/workflows/executables")).items.find(item => item.definitionId === definitionId && item.liveSourceReferenceCount > 0)!;
  expect(executable).toBeTruthy();
  return { definition, organization, folders, tags, views, executable, secondary,
    url: `/workflows/definitions?backend=authoring&folderId=${definition.folderId}&columns=tags,location`,
    configuration: JSON.parse(readFileSync(primaryFile, "utf8")) };
}

async function applyReview(page: Page, enabled: boolean, cancelFirst: boolean) {
  const before = readFileSync(primaryFile, "utf8");
  const dialog = await openSettings(page);
  const toggle = dialog.locator("input[type='checkbox']");
  if (enabled) await toggle.check(); else await toggle.uncheck();
  await dialog.getByRole("button", { name: "Save draft", exact: true }).click();
  await page.getByRole("button", { name: "Review changes", exact: true }).click();
  const review = page.getByRole("dialog", { name: "Review changes", exact: true });
  await expect(review).toContainText("Workflow organization");
  await expect(review.locator("article[data-impact-feature-id]")).toHaveCount(1);
  if (cancelFirst) {
    await review.getByRole("button", { name: "Cancel", exact: true }).click();
    expect(readFileSync(primaryFile, "utf8")).toBe(before);
    await page.getByRole("button", { name: "Review changes", exact: true }).click();
  }
  const applied = page.waitForResponse(response => response.url().includes("/modularity/features/apply") && response.request().method() === "POST");
  await review.getByRole("button", { name: "Apply", exact: true }).click();
  expect((await applied).status()).toBe(200);
  await expect.poll(() => JSON.parse(readFileSync(primaryFile, "utf8")).CShells.Shells.default.Features.WorkflowsDesignApi.WorkflowOrganizationEnabled).toBe(enabled);
  await expect(page.getByText(/Applied \d+ descriptor/)).toBeVisible();
  const expected = JSON.parse(before);
  expected.CShells.Shells.default.Features.WorkflowsDesignApi.WorkflowOrganizationEnabled = enabled;
  expect(JSON.parse(readFileSync(primaryFile, "utf8"))).toEqual(expected);
}

async function openSettings(page: Page) {
  await page.goto("/features?target=server&backend=authoring");
  await page.locator(".feature-management-feature-button").filter({ has: page.locator("code").filter({ hasText: /^WorkflowsDesignApi$/ }) }).click();
  await page.getByRole("button", { name: "Edit settings", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("Workflow organization");
  return dialog;
}

test("reviewed organization availability preserves the actual library across disable and restart", async ({ page }, testInfo) => {
  expect(Boolean(primaryFile && secondaryFile && proofFile)).toBe(true);
  expect(["disable", "restore"]).toContain(phase);
  page.setDefaultTimeout(30_000);
  const secondaryBefore = readFileSync(secondaryFile, "utf8");
  let bearer = "";
  page.on("request", request => { if (request.url().startsWith(apiBase) && request.headers().authorization) bearer = request.headers().authorization; });
  await signIn(page);
  await expect(page.getByRole("heading", { name: "Home", level: 2, exact: true })).toBeVisible();
  const dialog = await openSettings(page);
  await expect(dialog.locator("input[type='checkbox']")).toBeChecked({ checked: phase === "disable" });
  await testInfo.attach("organization-setting", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  expect(bearer).toMatch(/^Bearer /);

  if (phase === "restore") {
    const before = JSON.parse(readFileSync(proofFile, "utf8")) as Snapshot;
    expect(readFileSync(secondaryFile, "utf8")).toBe(before.secondary);
    expect((await request(page, bearer, "design/workflows/folders")).status).toBe(501);
    await applyReview(page, true, false);
    await page.goto(before.url);
    await expect(page.getByRole("link", { name: before.definition.name, exact: true })).toBeVisible();
    await expect(page.getByRole("columnheader", { name: "Tags", exact: true })).toBeVisible();
    await expect(page.getByRole("columnheader", { name: "Location", exact: true })).toBeVisible();
    const after = await snapshot(page, bearer, before.definition.id, secondaryBefore);
    expect(after.organization).toEqual(before.organization);
    expect(after.folders).toEqual(before.folders);
    expect(after.tags).toEqual(before.tags);
    expect(after.views).toEqual(before.views);
    expect(after.definition.favorite).toBe(before.definition.favorite);
    expect(after.definition.folderId).toBe(before.definition.folderId);
    expect(after.definition.tags).toEqual(before.definition.tags);
    writeFileSync(`${proofFile}.restored.json`, JSON.stringify(after, null, 2));
    await testInfo.attach("restored-library", { body: JSON.stringify(after, null, 2), contentType: "application/json" });
  } else {
    const definitions = await read<{ items: Definition[] }>(page, bearer, "design/workflows/definitions/page?pageSize=100");
    const definition = definitions.items.find(item => item.publishedVersionId && item.folderId && item.tags?.length)!;
    expect(definition, "Prepare a published, foldered, tagged workflow on the owned primary host").toBeTruthy();
    const folders = await read<{ items: Folder[] }>(page, bearer, "design/workflows/folders?pageSize=100");
    const existingChildren = await read<{ items: Folder[] }>(page, bearer, `design/workflows/folders?parentId=${folders.items[0].id}&pageSize=100`);
    if (!existingChildren.items.some(folder => folder.name === "Organization controls retained child"))
      expect((await request(page, bearer, "design/workflows/folders", "POST", { name: "Organization controls retained child", parentId: folders.items[0].id })).status).toBe(200);
    const existingViews = await read<{ items: View[] }>(page, bearer, "design/workflows/views");
    for (const shared of [false, true]) {
      if (existingViews.items.some(view => view.name === `Organization controls ${shared ? "shared" : "private"}`)) continue;
      const view = await request(page, bearer, "design/workflows/views", "POST", { name: `Organization controls ${shared ? "shared" : "private"}`, shared, query: { folderId: definition.folderId, favoritesOnly: true }, columns: ["tags", "location"] });
      expect(view.status).toBe(200);
    }
    expect((await request(page, bearer, `design/workflows/definitions/${definition.id}/favorite`, "PUT", {})).status).toBe(204);
    const before = await snapshot(page, bearer, definition.id, secondaryBefore);
    writeFileSync(proofFile, JSON.stringify(before, null, 2));
    await applyReview(page, false, true);
    const capabilities = await read<{ capabilities: { links: { rel: string; href: string }[] }[] }>(page, bearer, "capabilities");
    const links = capabilities.capabilities.flatMap(capability => capability.links);
    expect(links.some(link => link.rel === "workflow-definitions-page")).toBe(true);
    expect(links.some(link => link.rel === "workflow-folders" || link.rel === "workflow-tags" || link.rel === "workflow-organization" || link.rel === "workflow-views" || link.rel === "workflow-favorites")).toBe(false);
    expect((await request(page, bearer, "design/workflows/folders", "POST", { name: "Must not be written" })).status).toBe(501);
    expect((await request(page, bearer, `design/workflows/definitions/page?folderId=${definition.folderId}`)).status).toBe(400);
    await page.goto(before.url);
    await expect(page.getByRole("button", { name: "Clear unavailable organization filters", exact: true })).toBeVisible();
    await expect(page.getByRole("table", { name: "Workflow definitions", exact: true })).toHaveCount(0);
    await page.getByRole("button", { name: "Clear unavailable organization filters", exact: true }).press("Enter");
    await expect(page.getByRole("link", { name: definition.name, exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Favorites", exact: true })).toHaveCount(0);
    await expect(page.getByRole("columnheader", { name: "Tags", exact: true })).toHaveCount(0);
    const executeLink = links.find(link => link.rel === "workflow-execute")!;
    expect(executeLink).toBeTruthy();
    const execution = await request<{ workflowExecutionId: string }>(page, bearer, executeLink.href.replace("{artifactId}", before.executable.artifactId), "POST", {});
    expect(execution.status).toBe(200);
    await expect.poll(async () => (await read<{ instance: { status: string } }>(page, bearer, `runtime/workflows/instances/${execution.body.workflowExecutionId}`)).instance.status, { timeout: 60_000 }).toBe("Completed");
    const run = await read<{ incidents: unknown[] }>(page, bearer, `runtime/workflows/instances/${execution.body.workflowExecutionId}`);
    expect(run.incidents).toEqual([]);
    expect((await request(page, bearer, `design/workflows/definitions/${definition.id}`, "PATCH", { description: "Edited while organization is disabled." })).status).toBe(200);
    const authoring = await read<{ draft: DraftDocument }>(page, bearer, `design/workflows/definitions/${definition.id}`);
    expect(editLiteralText(authoring.draft.state.rootActivity), "The owned fixture includes a literal Text input, such as Write Line").toBe(true);
    expect((await request(page, bearer, `design/workflows/drafts/${authoring.draft.id}`, "PUT", {
      state: authoring.draft.state, layout: authoring.draft.layout, activityPresentation: authoring.draft.activityPresentation
    })).status).toBe(200);
    await page.getByRole("link", { name: definition.name, exact: true }).click();
    await page.getByRole("button", { name: "Publish options", exact: true }).click();
    await page.getByRole("menuitemradio", { name: "Review & publish", exact: true }).click();
    const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
    await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
    await review.getByRole("button", { name: "Publish", exact: true }).click();
    await expect(page.getByRole("dialog", { name: "Published", exact: true })).toBeVisible({ timeout: 60_000 });
    await expect.poll(async () => (await read<{ definition: { publishedVersionId?: string } }>(page, bearer, `design/workflows/definitions/${definition.id}`)).definition.publishedVersionId, { timeout: 30_000 }).not.toBe(definition.publishedVersionId);
    const published = await read<{ definition: { publishedVersionId?: string }; versions: unknown[] }>(page, bearer, `design/workflows/definitions/${definition.id}`);
    expect(published.definition.publishedVersionId).toBeTruthy();
    expect(published.definition.publishedVersionId).not.toBe(definition.publishedVersionId);
    writeFileSync(`${proofFile}.disabled.json`, JSON.stringify({ before, execution: execution.body, run, published }, null, 2));
    await testInfo.attach("disabled-organization-published-run", { body: JSON.stringify({ execution: execution.body, run }, null, 2), contentType: "application/json" });
    await testInfo.attach("disabled-organization-new-publication", { body: JSON.stringify(published, null, 2), contentType: "application/json" });
    await testInfo.attach("retained-library-before", { body: JSON.stringify(before, null, 2), contentType: "application/json" });
  }
  expect(readFileSync(secondaryFile, "utf8")).toBe(secondaryBefore);
  await testInfo.attach(`organization-${phase}`, { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
});
