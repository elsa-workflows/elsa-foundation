import { expect, test, type Page } from "@playwright/test";

test("Activity Definitions supports keyboard collection-to-workbench navigation and exact draft URLs", async ({ page }) => {
  await mockActivityDefinitions(page);
  await page.goto("/?mode=activity-definitions");

  const row = page.getByRole("row", { name: "Open Activity Definition Invoice evaluator" });
  await expect(row).toBeVisible();
  await row.focus();
  await page.keyboard.press("Enter");

  await expect(page.getByText("Stable Activity Definition")).toBeVisible();
  await expect(page.getByText("Contoso.InvoiceEvaluator")).toBeVisible();
  const overview = page.getByRole("tab", { name: "Overview" });
  await overview.focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("tab", { name: "Drafts" })).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button").filter({ hasText: "Revision 3" }).click();
  await expect(page).toHaveURL(/definition=definition-1.*section=drafts.*draft=draft-1/);
});

test("Activity Definitions stays usable without viewport overflow at 360, 768, and 1280 pixels", async ({ page }) => {
  await mockActivityDefinitions(page);
  for (const width of [360, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/?mode=activity-definitions");
    await expect(page.getByRole("heading", { name: "Activity Definitions" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Refresh" })).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  }
});

test("Activity Definitions retains confirmed rows when refresh fails and keeps errors private", async ({ page }) => {
  let collectionReads = 0;
  await mockActivityDefinitions(page, async route => {
    collectionReads += 1;
    if (collectionReads === 1) return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(collectionPage()) });
    return route.fulfill({ status: 500, contentType: "application/problem+json", body: JSON.stringify({ title: "hidden definition-99 secret failure" }) });
  });
  await page.goto("/?mode=activity-definitions");
  await expect(page.getByText("Invoice evaluator")).toBeVisible();

  await page.getByRole("button", { name: "Refresh" }).click();
  await expect(page.getByText("Refresh failed; showing retained data")).toBeVisible();
  await expect(page.getByText("Invoice evaluator")).toBeVisible();
  await expect(page.getByText(/hidden definition-99 secret failure/)).toHaveCount(0);
});

async function mockActivityDefinitions(page: Page, collectionHandler?: Parameters<Page["route"]>[1]) {
  await page.route("**/capabilities", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(capabilities()) }));
  await page.route(/\/design\/activities\/definitions\?.*/, collectionHandler ?? (route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(collectionPage()) })));
  await page.route("**/design/activities/definitions/definition-1", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(definition()) }));
  await page.route(/\/design\/activities\/definitions\/definition-1\/drafts\?.*/, route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(pageOf([draft()])) }));
  await page.route(/\/design\/activities\/definitions\/definition-1\/versions\?.*/, route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(pageOf([version()])) }));
  await page.route("**/design/activities/drafts/draft-1", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(draftDetail()) }));
}

function capabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
    { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true }
  ] }] };
}

function collectionPage() { return pageOf([definition()]); }
function pageOf(items: unknown[]) { return { items, count: items.length, totalCount: items.length, hasMore: false, continuation: null, snapshot: { snapshotId: "snapshot-browser", asOf: "2026-07-17T10:00:00Z" } }; }
function definition() { return { definition: { definitionId: "definition-1", activityTypeKey: "Contoso.InvoiceEvaluator", tenantId: null, category: "Finance", displayName: "Invoice evaluator", description: "Evaluates invoice policy.", contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design", sourceId: null }, forkedFrom: null, headVersionId: "version-1", recommendedVersionId: "version-1" }, lifecycle: { draftCount: 1, versionCount: 1, head: { versionId: "version-1", version: "1.0.0", lifecycle: "Active", providerKey: "visual-graph", providerSchemaVersion: "1" }, recommendation: { versionId: "version-1", version: "1.0.0", lifecycle: "Active", providerKey: "visual-graph", providerSchemaVersion: "1" } }, actions: [], updatedAt: "2026-07-17T10:00:00Z" }; }
function draft() { return { draft: { draftId: "draft-1", definitionId: "definition-1", revision: 3, sourceVersionId: "version-1", status: "Active", providerKey: "visual-graph", providerSchemaVersion: "1", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null }, actions: [] }; }
function draftDetail() { return { draftId: "draft-1", definitionId: "definition-1", tenantId: null, revision: 3, sourceVersionId: "version-1", status: "Active", contract: {}, provider: { providerKey: "visual-graph", schemaVersion: "1", manifestFingerprint: "fingerprint", payload: {} }, layout: [], validation: null, createdAt: "2026-07-17T09:00:00Z", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null }; }
function version() { return { version: { versionId: "version-1", definitionId: "definition-1", version: "1.0.0", lifecycle: "Active", publishedAt: "2026-07-17T09:00:00Z" }, providerKey: "visual-graph", providerSchemaVersion: "1", isRecommended: true, actions: [] }; }
