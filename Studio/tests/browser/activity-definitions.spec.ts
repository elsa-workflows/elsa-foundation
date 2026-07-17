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

test("Activity Definition create, graph autosave, reload, conflict preservation, and recovery", async ({ page }) => {
  const state = await mockActivityDefinitionAuthoring(page);
  await page.goto("/?mode=activity-definitions");
  await expect(page.getByText("No Activity Definitions yet")).toBeVisible();

  await page.getByRole("button", { name: "Create Activity Definition" }).click();
  await expect(page.getByRole("combobox", { name: "Implementation provider" })).toHaveValue("elsa.activity-graph|1");
  await page.getByRole("textbox", { name: "Display name" }).fill("Browser graph activity");
  await page.getByRole("textbox", { name: "Category" }).fill("Browser tests");
  await page.getByRole("button", { name: "Create definition" }).click();

  await expect(page).toHaveURL(/definition=activity-def-browser.*section=editor.*draft=activity-draft-browser/);
  await expect(page.getByText("Saved revision 1")).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Root activity" }).locator("option", { hasText: "Flowchart" })).toHaveCount(0);
  await page.getByRole("combobox", { name: "Root activity" }).selectOption("sequence-v1");
  await expect(page.getByText("Saved revision 2")).toBeVisible();
  await page.getByRole("combobox", { name: "Activity for Activities" }).selectOption("write-line-v1");
  await page.getByRole("button", { name: "Add activity" }).click();
  await expect(page.getByText("Saved revision 3")).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "Activity for Activities" }).selectOption("delay-v1");
  await page.getByRole("button", { name: "Add activity" }).click();
  await expect(page.getByText("Saved revision 4")).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();

  await page.reload();
  await expect(page.getByText("Saved revision 4")).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();
  page.once("dialog", dialog => dialog.dismiss());
  await page.getByRole("combobox", { name: "Root activity" }).selectOption("delay-v1");
  await expect(page.getByRole("combobox", { name: "Root activity" })).toHaveValue("sequence-v1");
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();
  await page.locator(".ad-graph-node").filter({ hasText: "Write line" }).click();
  page.once("dialog", dialog => dialog.dismiss());
  await page.getByRole("button", { name: "Remove" }).click();
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();

  state.conflictNextSave = true;
  await page.locator(".ad-graph-node").filter({ hasText: "Delay" }).click();
  await page.getByRole("textbox", { name: "Activity inputs JSON" }).fill('[{"name":"Duration","value":"00:00:05"}]');
  await page.getByRole("button", { name: "Apply inputs" }).click();
  await expect(page.getByText("Local work preserved")).toBeVisible();
  await expect(page.getByText(/server draft advanced to revision 5/i)).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Create parallel recovery draft" }).click();

  await expect(page).toHaveURL(/draft=activity-draft-recovery/);
  await expect(page.getByText("Saved revision 1")).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();
  expect(state.conflictCopyPayload).toMatchObject({ rootActivity: { activityVersionId: "sequence-v1", structure: { payload: { activities: [
    { activityVersionId: "write-line-v1" },
    { activityVersionId: "delay-v1", inputs: [{ name: "Duration", value: "00:00:05" }] }
  ] } } } });
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

async function mockActivityDefinitionAuthoring(page: Page) {
  const state = {
    conflictNextSave: false,
    conflictCopyPayload: null as unknown,
    draft: authoringDraft("activity-draft-browser", 1, initialGraphPayload())
  };
  await page.route("**/capabilities", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(authoringApiCapabilities()) }));
  await page.route(/\/design\/activities\/definitions\?.*/, route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(pageOf([])) }));
  await page.route("**/design/activities/authoring-capabilities", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(authoringCapabilities()) }));
  await page.route("**/design/activities/catalog", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ activities: [catalogActivity("sequence-v1", "Sequence"), catalogActivity("flowchart-v1", "Flowchart"), catalogActivity("write-line-v1", "Write line"), catalogActivity("delay-v1", "Delay")] }) }));
  await page.route("**/design/activities/definitions", async route => {
    if (route.request().method() !== "POST") return route.fallback();
    const body = route.request().postDataJSON() as { provider: { payload: unknown } };
    state.draft = authoringDraft("activity-draft-browser", 1, body.provider.payload);
    await route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify({ definition: authoringDefinition(), draft: authoringDraftSummary(state.draft) }) });
  });
  await page.route("**/design/activities/drafts/activity-draft-browser", async route => {
    if (route.request().method() === "GET") return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(state.draft) });
    if (route.request().method() === "PUT") {
      const body = route.request().postDataJSON() as { expectedRevision: number; provider: { payload: unknown }; layout: unknown[]; contract: unknown; presentationLabel?: string | null };
      if (state.conflictNextSave) {
        state.conflictNextSave = false;
        return route.fulfill({ status: 409, contentType: "application/problem+json", body: JSON.stringify({ title: "Stale revision", status: 409, errorCode: "activity.draft.stale-revision", recovery: { currentRevision: 5, relation: "activity-draft-conflict-copies" } }) });
      }
      state.draft = { ...state.draft, revision: body.expectedRevision + 1, provider: { ...state.draft.provider, payload: body.provider.payload }, layout: body.layout, contract: body.contract, presentationLabel: body.presentationLabel ?? null, updatedAt: new Date().toISOString() };
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(state.draft) });
    }
    return route.fallback();
  });
  await page.route("**/design/activities/drafts/activity-draft-browser/conflict-copies", async route => {
    const body = route.request().postDataJSON() as { provider: { payload: unknown }; layout: unknown[]; contract: unknown; presentationLabel?: string | null };
    state.conflictCopyPayload = body.provider.payload;
    state.draft = { ...authoringDraft("activity-draft-recovery", 1, body.provider.payload), layout: body.layout, contract: body.contract, presentationLabel: body.presentationLabel ?? null };
    await route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify(state.draft) });
  });
  await page.route("**/design/activities/drafts/activity-draft-recovery", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(state.draft) }));
  return state;
}

function authoringApiCapabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-draft-validation", href: "design/activities/drafts/{draftId}/validate", templated: true },
    { rel: "activity-draft-conflict-copies", href: "design/activities/drafts/{draftId}/conflict-copies", templated: true },
    { rel: "activity-catalog", href: "design/activities/catalog" }
  ] }] };
}

function authoringCapabilities() {
  return {
    contractSchemaVersions: ["1"],
    activityTypeKeyRules: { serverGenerated: true, allowsPreCreationOverride: true, immutable: true, prefix: "elsa.user", pattern: "^elsa\\.user\\..+$", maximumLength: 160, collisionScope: "tenantId + activityTypeKey" },
    providers: [{ providerKey: "elsa.activity-graph", displayName: "Activity Graph", manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: ["1"] }], requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }],
    types: [], storageDriverKeys: [], snapshotFingerprint: "sha256:browser"
  };
}

function authoringDefinition() {
  return { definitionId: "activity-def-browser", activityTypeKey: "elsa.user.browser-graph-activity.activity-def-browser", tenantId: "browser-tenant", category: "Browser tests", displayName: "Browser graph activity", description: null, contentAuthority: { kind: "design", authorityKey: "elsa.activity-design", sourceId: null }, forkedFrom: null, headVersionId: null, recommendedVersionId: null };
}

function authoringDraftSummary(draft: ReturnType<typeof authoringDraft>) {
  return { draftId: draft.draftId, definitionId: draft.definitionId, revision: draft.revision, sourceVersionId: null, status: "active", providerKey: draft.provider.providerKey, providerSchemaVersion: draft.provider.schemaVersion, updatedAt: draft.updatedAt, presentationLabel: draft.presentationLabel };
}

function authoringDraft(draftId: string, revision: number, payload: unknown) {
  return { draftId, definitionId: "activity-def-browser", tenantId: "browser-tenant", revision, sourceVersionId: null, status: "active", contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }, provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", manifestFingerprint: `sha256:${revision}`, payload }, layout: [], validation: null, createdAt: "2026-07-17T10:00:00Z", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null };
}

function initialGraphPayload() { return { rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null }, variables: [], outputMappings: [] }; }
function catalogActivity(activityVersionId: string, displayName: string) { return { activityVersionId, activityTypeKey: `Elsa.${displayName.replaceAll(" ", "")}`, version: "1.0.0", category: "Primitives", displayName, description: null, executionType: "sync", inputs: [], outputs: [], designFacets: [], available: true, authoringTemplate: { nodeId: "template", activityVersionId, inputs: [], outputs: [], structure: null } }; }
