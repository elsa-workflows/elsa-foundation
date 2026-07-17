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

test("Activity Definition draft management keeps parallel labels and supports blank plus exact clone creation", async ({ page }) => {
  const state = await mockActivityDraftManagement(page);
  await page.goto("/workflows/activity-definitions?definition=definition-1&section=drafts");

  await expect(page.getByText("Review", { exact: true })).toHaveCount(2);
  await expect(page.getByText("Draft draft-unlabeled", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Create parallel draft" }).click();
  await expect(page.getByRole("dialog", { name: "Create parallel draft" })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Implementation provider" })).toHaveValue("elsa.activity-graph|1");
  await page.getByRole("button", { name: "Create blank draft" }).click();

  await expect(page).toHaveURL(/definition=definition-1.*section=editor.*draft=draft-blank/);
  await expect(page.getByText("Saved revision 1")).toBeVisible();
  expect(state.draftRequests[0]).toMatchObject({
    sourceVersionId: null,
    presentationLabel: null,
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "1" },
    contract: { contractSchemaVersion: "1", inputs: [], outputs: [] }
  });

  await page.goto("/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1");
  await page.getByRole("button", { name: "Create draft from this exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Create parallel draft" });
  await expect(dialog.getByRole("radio", { name: /Clone exact version/ })).toBeChecked();
  await dialog.getByRole("textbox", { name: /Draft label/ }).fill("Review");
  await dialog.getByRole("button", { name: "Clone exact version" }).click();

  await expect(page).toHaveURL(/definition=definition-1.*section=editor.*draft=draft-clone/);
  await expect(page.getByText("Saved revision 1")).toBeVisible();
  expect(state.draftRequests[1]).toEqual({ sourceVersionId: "version-1", presentationLabel: "Review" });
  expect(state.sourceVersion).toEqual(sourceVersionDetail());
});

test("source-owned Activity Definition history stays exact and offers no denied mutable controls", async ({ page }) => {
  await mockActivityDraftManagement(page, { sourceOwned: true });
  await page.goto("/workflows/activity-definitions?definition=definition-1&section=versions&version=version-1");

  await expect(page.getByText("Source-owned and read-only")).toBeVisible();
  await expect(page.getByText("contoso.catalog")).toBeVisible();
  await expect(page.getByText("invoice-source")).toBeVisible();
  await expect(page.getByLabel("Selected exact version")).toContainText("elsa.activity-graph");
  await expect(page.getByText("Immutable public contract")).toBeVisible();
  await expect(page.getByRole("button", { name: /Fork/ })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Create draft from/ })).toHaveCount(0);
});

test("draft management dialog traps and restores focus, supports keyboard clone submission, and fits target layouts", async ({ page }) => {
  const state = await mockActivityDraftManagement(page);
  for (const width of [320, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/workflows/activity-definitions?definition=definition-1&section=drafts");
    const opener = page.getByRole("button", { name: "Create parallel draft" });
    await opener.focus();
    await opener.press("Enter");
    const dialog = page.getByRole("dialog", { name: "Create parallel draft" });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Close" })).toBeFocused();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
  }

  const opener = page.getByRole("button", { name: "Create parallel draft" });
  await opener.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Create parallel draft" });
  const clone = dialog.getByRole("radio", { name: /Clone exact version/ });
  await clone.focus();
  await page.keyboard.press("Space");
  await expect(clone).toBeChecked();
  const version = dialog.getByRole("combobox", { name: "Exact immutable version" });
  await expect(version.locator("option")).toHaveCount(2);
  await version.selectOption("version-1");
  await expect(version).toHaveValue("version-1");
  await dialog.getByRole("textbox", { name: /Draft label/ }).fill("Keyboard clone");
  const submit = dialog.getByRole("button", { name: "Clone exact version" });
  await submit.focus();
  await page.keyboard.press("Enter");

  await expect(page).toHaveURL(/draft=draft-clone/);
  expect(state.draftRequests.at(-1)).toEqual({ sourceVersionId: "version-1", presentationLabel: "Keyboard clone" });
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
  await page.getByRole("textbox", { name: "Member name" }).fill("Customer note");
  await page.getByRole("button", { name: "Add input" }).click();
  const contractInput = page.getByRole("group", { name: "Input 1: Customer note" });
  await expect(contractInput).toBeVisible();
  await contractInput.getByRole("checkbox", { name: /Allows null/ }).check();
  await contractInput.getByRole("combobox", { name: "Default" }).selectOption("literal");
  await contractInput.getByRole("textbox", { name: "Literal JSON value" }).fill("null");
  await contractInput.getByRole("button", { name: "Apply default" }).click();
  await expect(page.getByText("Saved revision 2")).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Root activity" }).locator("option", { hasText: "Flowchart" })).toHaveCount(0);
  await page.getByRole("combobox", { name: "Root activity" }).selectOption("sequence-v1");
  await expect(page.getByText("Saved revision 3")).toBeVisible();
  await page.getByRole("combobox", { name: "Activity for Activities" }).selectOption("write-line-v1");
  await page.getByRole("button", { name: "Add activity" }).click();
  await expect(page.getByText("Saved revision 4")).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "Activity for Activities" }).selectOption("delay-v1");
  await page.getByRole("button", { name: "Add activity" }).click();
  await expect(page.getByText("Saved revision 5")).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();

  await page.reload();
  await expect(page.getByText("Saved revision 5")).toBeVisible();
  await expect(page.getByRole("group", { name: "Input 1: Customer note" }).getByRole("textbox", { name: "Literal JSON value" })).toHaveValue("null");
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();
  page.once("dialog", dialog => dialog.dismiss());
  await page.getByRole("combobox", { name: "Root activity" }).selectOption("delay-v1");
  await expect(page.getByRole("combobox", { name: "Root activity" })).toHaveValue("sequence-v1");
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();
  await page.locator(".ad-graph-node").filter({ hasText: "Write line" }).click();
  page.once("dialog", dialog => dialog.dismiss());
  await page.getByRole("button", { name: "Remove", exact: true }).click();
  await expect(page.locator(".ad-graph-node").getByText("Write line", { exact: true })).toBeVisible();

  state.conflictNextSave = true;
  await page.locator(".ad-graph-node").filter({ hasText: "Delay" }).click();
  await page.getByRole("textbox", { name: "Activity inputs JSON" }).fill('[{"name":"Duration","value":"00:00:05"}]');
  await page.getByRole("button", { name: "Apply inputs" }).click();
  await expect(page.getByText("Local work preserved")).toBeVisible();
  await expect(page.getByText(/server draft advanced to revision 6/i)).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Create parallel recovery draft" }).click();

  await expect(page).toHaveURL(/draft=activity-draft-recovery/);
  await expect(page.getByText("Saved revision 1")).toBeVisible();
  await expect(page.locator(".ad-graph-node").getByText("Delay", { exact: true })).toBeVisible();
  expect(state.conflictCopyPayload).toMatchObject({ rootActivity: { activityVersionId: "sequence-v1", structure: { payload: { activities: [
    { activityVersionId: "write-line-v1" },
    { activityVersionId: "delay-v1", inputs: [{ name: "Duration", value: "00:00:05" }] }
  ] } } } });
  expect(state.draft.contract).toMatchObject({ inputs: [{ referenceKey: "customer-note", isRequired: false, isNullable: true, default: { syntax: "Literal", value: null }, storageDriverKey: "elsa.json", durability: "Required" }] });
});

test("Activity Definition validation distinguishes valid, invalid, unavailable, forbidden, missing, unknown, and stale outcomes", async ({ page }) => {
  const state = await mockActivityDefinitionAuthoring(page);
  await page.goto("/?mode=activity-definitions");
  await page.getByRole("button", { name: "Create Activity Definition" }).click();
  await page.getByRole("textbox", { name: "Display name" }).fill("Diagnostic browser activity");
  await page.getByRole("button", { name: "Create definition" }).click();
  await expect(page.getByText("Saved revision 1")).toBeVisible();

  state.validationMode = "invalid";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByText("Draft rejected")).toBeVisible();
  await expect(page.getByLabel("Diagnostic severity counts")).toContainText("2 errors");
  await expect(page.getByLabel("Diagnostic severity counts")).toContainText("2 warnings");

  const contractDiagnostic = page.getByRole("button", { name: "Focus activity.contract.outcome-required" });
  await contractDiagnostic.click();
  await expect(page.locator("[data-contract-field='referenceKey']").locator("..")).toBeFocused();
  await page.getByRole("button", { name: "Return to diagnostic" }).click();
  await expect(contractDiagnostic).toBeFocused();

  await page.getByRole("button", { name: "Focus activity.graph.root-required" }).click();
  await expect(page.getByRole("combobox", { name: "Root activity" })).toBeFocused();
  await page.getByRole("button", { name: "Return to diagnostic" }).click();

  await page.getByRole("button", { name: "Focus activity.future-location" }).click();
  await expect(page.getByText(/exact diagnostic location is unavailable/i)).toBeVisible();

  const missingProviderDiagnostic = page.getByRole("button", { name: "Focus activity.provider.editor-unavailable" });
  await missingProviderDiagnostic.click();
  await expect(page.getByText(/exact diagnostic location is unavailable/i)).toBeVisible();
  await expect(missingProviderDiagnostic).toBeFocused();
  await expect(missingProviderDiagnostic).toBeVisible();
  await expect(page.getByRole("button", { name: "Return to diagnostic" })).toHaveCount(0);
  await expect(page.getByText("hidden.provider")).toHaveCount(0);
  await expect(page.getByText("hidden-provider-subject-id")).toHaveCount(0);

  state.validationMode = "valid";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByText("Valid draft")).toBeVisible();
  await expect(page.getByText(/Revision 1 passed validation/)).toBeVisible();

  state.validationMode = "forbidden";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByText(/Draft validation is not authorized/)).toBeVisible();
  await expect(page.getByText("hidden validation identity")).toHaveCount(0);

  state.validationMode = "not-found";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByText(/exact authorized draft could not be confirmed/)).toBeVisible();
  await expect(page.getByText("hidden validation identity")).toHaveCount(0);

  state.validationMode = "unavailable";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByText(/Draft validation is unavailable/)).toBeVisible();
  await expect(page.getByText("hidden validation identity")).toHaveCount(0);

  state.validationMode = "stale";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByText(/backend rejected validation/)).toBeVisible();
  await expect(page.getByText(/server draft advanced to revision 2/i)).toBeVisible();
  await expect(page.getByText(/Runtime rejection is reported by the Test Run experience/)).toBeVisible();
});

test("Activity Graph diagnostics focus accessible root context while the exact control is pending", async ({ page }) => {
  const state = await mockActivityDefinitionAuthoring(page);
  state.catalogBlocked = true;
  await page.goto("/?mode=activity-definitions");
  await page.getByRole("button", { name: "Create Activity Definition" }).click();
  await page.getByRole("textbox", { name: "Display name" }).fill("Pending catalog activity");
  await page.getByRole("button", { name: "Create definition" }).click();
  await expect(page.getByText("Saved revision 1")).toBeVisible();

  state.validationMode = "invalid";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await page.getByRole("button", { name: "Focus activity.graph.root-required" }).click();
  await expect(page.locator("[data-graph-root-location]")).toBeFocused();
  await expect(page.getByText(/Focused the provider-owned Activity Graph location/)).toBeVisible();
  state.releaseCatalog();
});

test("Activity Definition publication keeps first and later recommendation semantics while preserving exact versions", async ({ page }) => {
  const state = await mockActivityDefinitionAuthoring(page);
  await createBrowserActivity(page);

  await page.getByRole("button", { name: "Prepare publication" }).click();
  await expect(page.getByText("First publication · no published baseline exists")).toBeVisible();
  await expect(page.getByText("Protected value redacted")).toBeVisible();
  await expect(page.getByText("raw-protected-provider-value")).toHaveCount(0);
  await page.getByRole("textbox", { name: "Publication version" }).fill("2.0.0");
  await page.getByRole("button", { name: "Publish 2.0.0" }).click();
  await expect(page.getByText("Published immutable version 2.0.0")).toBeVisible();
  await expect(page.getByText("first published version became recommended automatically", { exact: false })).toBeVisible();
  expect(state.recommendedVersionId).toBe("published-version-1");

  await page.getByRole("button", { name: "Reopen preflight" }).click();
  await expect(page.getByText("Compared with published head")).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Publication version" })).toHaveValue("2.1.0");
  await page.getByRole("textbox", { name: "Publication version" }).fill("3.4.5");
  await page.getByRole("button", { name: "Publish 3.4.5" }).click();
  await expect(page.getByText("Published immutable version 3.4.5")).toBeVisible();
  await expect(page.getByText("existing recommended version was not moved", { exact: false })).toBeVisible();
  expect(state.recommendedVersionId).toBe("published-version-1");
  expect(state.requestedVersions).toEqual(["2.0.0", "3.4.5"]);
  expect(state.publicationWrites).toBe(2);
});

test("published recommendation is placed exactly, dispatched once, and inspected as one reusable boundary", async ({ page }) => {
  const state = await mockActivityDefinitionAuthoring(page);
  await createBrowserActivity(page);

  await page.getByRole("combobox", { name: "Root activity" }).selectOption("sequence-v1");
  await expect(page.getByText("Saved revision 2")).toBeVisible();
  state.validationMode = "valid";
  await page.getByRole("button", { name: "Validate saved revision" }).click();
  await expect(page.getByText("Valid draft")).toBeVisible();
  await page.getByRole("button", { name: "Prepare publication" }).click();
  await page.getByRole("textbox", { name: "Publication version" }).fill("2.0.0");
  await page.getByRole("button", { name: "Publish 2.0.0" }).click();
  await expect(page.getByText("Published immutable version 2.0.0")).toBeVisible();
  expect(state.recommendedVersionId).toBe("published-version-1");

  const journey = await mockReusableBoundaryJourney(page);
  await page.setViewportSize({ width: 360, height: 900 });
  await page.goto("/?mode=reusable-boundary");

  const recommended = page.getByRole("treeitem", { name: /Published browser activity.*2\.0\.0/ });
  await expect(recommended).toBeVisible();
  await expect(page.getByText("v10.0.0")).toHaveCount(0);
  await expect(page.getByRole("combobox", { name: /version/i })).toHaveCount(0);
  await recommended.focus();
  await page.keyboard.press("Enter");

  await expect(page.getByText("Draft saved and reloaded")).toBeVisible();
  expect(journey.draftWrites).toBe(1);
  expect(journey.draftReads).toBe(1);
  expect(journey.submittedRoot).toEqual({
    nodeId: "invoice-boundary",
    activityVersionId: "published-version-1",
    inputs: [],
    outputs: [],
    structure: null
  });
  expect(JSON.stringify(journey.submittedRoot)).not.toContain("activityDefinition");
  await expect(page.getByRole("button", { name: /Published browser activity exact version 2\.0\.0/ })).toBeVisible();
  const inspector = page.getByRole("complementary", { name: "Activity inspector" });
  await expect(inspector).toContainText("activity-def-browser");
  await expect(inspector).toContainText("published-version-1");
  await expect(inspector).toContainText("elsa.activity-graph");
  await expect(inspector).toContainText("Active");

  await page.getByRole("button", { name: "Dispatch workflow" }).click();
  await expect(page.getByText("One Run · workflow-execution-1")).toBeVisible();
  expect(journey.dispatchWrites).toBe(1);
  const runDetails = page.getByRole("complementary", { name: "Run details" });
  await expect(runDetails).toContainText("Boundary lifecycle");
  await expect(runDetails).toContainText("Descendant aggregate");
  await expect(runDetails).toContainText("Pinned historical layout");
  await expect(runDetails).toContainText("workflow-source-1");
  await expect(runDetails).toContainText("invoice-write");
  await expect(runDetails.getByText("Boundary lifecycle")).toHaveCount(1);

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test("Activity Definition publication reconciles an ambiguous response without duplicate writes", async ({ page }) => {
  const state = await mockActivityDefinitionAuthoring(page);
  state.publishMode = "ambiguous";
  await createBrowserActivity(page);

  await page.getByRole("button", { name: "Prepare publication" }).click();
  await expect(page.getByText("Minimum valid version: 1.0.0")).toBeVisible();
  await page.getByRole("button", { name: "Publish 1.0.0" }).click();
  await expect(page.getByText("Published immutable version 1.0.0")).toBeVisible();
  expect(state.publicationWrites).toBe(1);
  expect(state.receiptLookups).toBe(1);
});

test("Activity Definition publication blocks invalid preflight and reopens stale head review without writes", async ({ page }) => {
  const state = await mockActivityDefinitionAuthoring(page);
  await createBrowserActivity(page);
  state.preflightMode = "invalid";

  await page.getByRole("button", { name: "Prepare publication" }).click();
  await expect(page.getByText("Publication blocked")).toBeVisible();
  await expect(page.getByRole("button", { name: "Publish 1.0.0" })).toBeDisabled();
  expect(state.publicationWrites).toBe(0);

  state.preflightMode = "valid";
  state.publishMode = "stale";
  await page.getByRole("button", { name: "Reopen preflight" }).click();
  await expect(page.getByText("Ready to publish")).toBeVisible();
  await page.getByRole("button", { name: "Publish 1.0.0" }).click();
  await expect(page.getByText("reopened preflight without publishing", { exact: false })).toBeVisible();
  expect(state.publicationWrites).toBe(1);
  expect(state.appliedPublications).toBe(0);
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

async function mockActivityDraftManagement(page: Page, options: { sourceOwned?: boolean } = {}) {
  const draftRequests: unknown[] = [];
  const sourceVersion = sourceVersionDetail(options.sourceOwned);
  const managedDefinition = {
    definition: {
      ...definition().definition,
      contentAuthority: options.sourceOwned
        ? { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
        : { kind: "Design", authorityKey: "elsa.activity-design", sourceId: null }
    },
    lifecycle: {
      ...definition().lifecycle,
      draftCount: options.sourceOwned ? 0 : 3,
      head: { ...definition().lifecycle.head, providerKey: "elsa.activity-graph" },
      recommendation: { ...definition().lifecycle.recommendation, providerKey: "elsa.activity-graph" }
    },
    actions: options.sourceOwned
      ? [{ action: "fork-definition", allowed: false, unavailableCode: "activity.action.forbidden" }]
      : [{ action: "create-draft", allowed: true }],
    updatedAt: definition().updatedAt
  };
  const managedVersion = {
    version: { versionId: "version-1", definitionId: "definition-1", version: "1.0.0", lifecycle: "Active", publishedAt: "2026-07-17T09:00:00Z" },
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "1",
    isRecommended: true,
    actions: [{
      action: options.sourceOwned ? "fork-definition" : "clone-draft",
      allowed: !options.sourceOwned,
      unavailableCode: options.sourceOwned ? "activity.action.forbidden" : null
    }]
  };

  await page.route("**/capabilities", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({
    capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
      { rel: "activity-definitions", href: "design/activities/definitions" },
      { rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" },
      { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
      { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
      { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
      { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
      { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true }
    ] }]
  }) }));
  await page.route("**/design/activities/definitions/definition-1", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(managedDefinition) }));
  await page.route(/\/design\/activities\/definitions\/definition-1\/drafts\?.*/, route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(pageOf(options.sourceOwned ? [] : [
      managedDraft("draft-review-a", "Review"),
      managedDraft("draft-review-b", "Review"),
      managedDraft("draft-unlabeled", null)
    ]))
  }));
  await page.route(/\/design\/activities\/definitions\/definition-1\/versions\?.*/, route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(pageOf([managedVersion])) }));
  await page.route("**/design/activities/authoring-capabilities", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(browserAuthoringCapabilities()) }));
  await page.route("**/design/activities/versions/version-1", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(sourceVersion) }));
  await page.route("**/design/activities/drafts/draft-blank", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(managementDraftDetail("draft-blank", null, null) ) }));
  await page.route("**/design/activities/drafts/draft-clone", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(managementDraftDetail("draft-clone", "Review", "version-1")) }));
  await page.route("**/design/activities/definitions/definition-1/drafts", async route => {
    const request = route.request().postDataJSON();
    draftRequests.push(request);
    const clone = request.sourceVersionId === "version-1";
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(managementDraftDetail(clone ? "draft-clone" : "draft-blank", clone ? "Review" : null, clone ? "version-1" : null)) });
  });
  return { draftRequests, sourceVersion };
}

function managedDraft(draftId: string, presentationLabel: string | null) {
  return { draft: { draftId, definitionId: "definition-1", revision: 1, sourceVersionId: null, status: "Active", providerKey: "elsa.activity-graph", providerSchemaVersion: "1", updatedAt: "2026-07-17T10:00:00Z", presentationLabel }, actions: [] };
}

function managementDraftDetail(draftId: string, presentationLabel: string | null, sourceVersionId: string | null) {
  return {
    draftId,
    definitionId: "definition-1",
    tenantId: null,
    revision: 1,
    sourceVersionId,
    status: "Active",
    contract: browserContract(),
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", manifestFingerprint: "sha256:graph", payload: { rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null }, variables: [], outputMappings: [] } },
    layout: [],
    validation: null,
    createdAt: "2026-07-17T10:00:00Z",
    updatedAt: "2026-07-17T10:00:00Z",
    presentationLabel
  };
}

function sourceVersionDetail(sourceOwned = false) {
  return {
    definition: {
      ...definition().definition,
      contentAuthority: sourceOwned
        ? { kind: "ProviderSource", authorityKey: "contoso.catalog", sourceId: "invoice-source" }
        : { kind: "Design", authorityKey: "elsa.activity-design", sourceId: null }
    },
    versionId: "version-1",
    version: "1.0.0",
    sourceDraftId: "draft-source",
    sourceVersionId: null,
    contract: browserContract(),
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", manifestFingerprint: "sha256:source" },
    lifecycle: "Active",
    publishedAt: "2026-07-17T09:00:00Z"
  };
}

function browserContract() {
  return { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] };
}

function browserAuthoringCapabilities() {
  return {
    contractSchemaVersions: ["1"],
    activityTypeKeyRules: { serverGenerated: true, allowsPreCreationOverride: false, immutable: true, prefix: "elsa.user", pattern: "^elsa\\\\.user\\\\..+$", maximumLength: 160, collisionScope: "tenant" },
    providers: [{ providerKey: "elsa.activity-graph", displayName: "Activity Graph", manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: ["1"] }], requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }],
    types: [],
    storageDriverKeys: [],
    snapshotFingerprint: "sha256:browser"
  };
}

async function mockActivityDefinitionAuthoring(page: Page) {
  const state = {
    conflictNextSave: false,
    conflictCopyPayload: null as unknown,
    validationMode: "invalid" as "invalid" | "valid" | "forbidden" | "not-found" | "unavailable" | "stale",
    catalogBlocked: false,
    releaseCatalog: () => {},
    draft: authoringDraft("activity-draft-browser", 1, initialGraphPayload()),
    headVersionId: null as string | null,
    recommendedVersionId: null as string | null,
    publicationWrites: 0,
    appliedPublications: 0,
    receiptLookups: 0,
    requestedVersions: [] as string[],
    publishMode: "normal" as "normal" | "ambiguous" | "stale",
    preflightMode: "valid" as "valid" | "invalid",
    latestReceipt: null as ReturnType<typeof browserPublicationReceipt> | null
  };
  let releaseCatalog!: () => void;
  const catalogGate = new Promise<void>(resolve => { releaseCatalog = resolve; });
  state.releaseCatalog = releaseCatalog;
  await page.route("**/capabilities", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(authoringApiCapabilities()) }));
  await page.route(/\/design\/activities\/definitions\?.*/, route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(pageOf([])) }));
  await page.route("**/design/activities/authoring-capabilities", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(authoringCapabilities()) }));
  await page.route("**/expressions/descriptors", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ items: [{ type: "Python", displayName: "Python", editingMode: "text" }] }) }));
  await page.route("**/design/activities/catalog", async route => {
    if (state.catalogBlocked) await catalogGate;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ activities: [catalogActivity("sequence-v1", "Sequence"), catalogActivity("flowchart-v1", "Flowchart"), catalogActivity("write-line-v1", "Write line"), catalogActivity("delay-v1", "Delay")] }) });
  });
  await page.route("**/design/activities/definitions", async route => {
    if (route.request().method() !== "POST") return route.fallback();
    const body = route.request().postDataJSON() as { provider: { payload: unknown } };
    state.draft = authoringDraft("activity-draft-browser", 1, body.provider.payload);
    await route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify({ definition: authoringDefinition(), draft: authoringDraftSummary(state.draft) }) });
  });
  await page.route("**/design/activities/definitions/activity-def-browser", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(authoringManagementDefinition(state))
  }));
  await page.route("**/design/activities/drafts/activity-draft-browser", async route => {
    if (route.request().method() === "GET") return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(state.draft) });
    if (route.request().method() === "PUT") {
      const body = route.request().postDataJSON() as { expectedRevision: number; provider: { payload: unknown }; layout: unknown[]; contract: unknown; presentationLabel?: string | null };
      if (state.conflictNextSave) {
        state.conflictNextSave = false;
        return route.fulfill({ status: 409, contentType: "application/problem+json", body: JSON.stringify({ title: "Stale revision", status: 409, errorCode: "activity.draft.stale-revision", recovery: { currentRevision: state.draft.revision + 1, relation: "activity-draft-conflict-copies" } }) });
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
  await page.route("**/design/activities/drafts/activity-draft-browser/validate", async route => {
    const body = route.request().postDataJSON() as { expectedRevision: number };
    if (state.validationMode === "forbidden") {
      return route.fulfill({ status: 403, contentType: "application/problem+json", body: JSON.stringify({ title: "hidden validation identity" }) });
    }
    if (state.validationMode === "not-found") {
      return route.fulfill({ status: 404, contentType: "application/problem+json", body: JSON.stringify({ title: "hidden validation identity" }) });
    }
    if (state.validationMode === "unavailable") {
      return route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ title: "hidden validation identity" }) });
    }
    if (state.validationMode === "stale") {
      return route.fulfill({
        status: 409,
        contentType: "application/problem+json",
        body: JSON.stringify({
          title: "Stale revision",
          errorCode: "activity.draft.stale-revision",
          recovery: { currentRevision: state.draft.revision + 1 }
        })
      });
    }
    const diagnostics = state.validationMode === "valid" ? [] : [
      {
        code: "activity.contract.outcome-required",
        severity: "Error",
        message: "The required outcome must be retained.",
        subject: { kind: "ActivityDraft", id: state.draft.draftId, revision: body.expectedRevision },
        location: { jsonPointer: "/contract/outcomes/0/referenceKey", referenceKey: "done" },
        remediation: "Retain the provider-required outcome.",
        metadata: {}
      },
      {
        code: "activity.graph.root-required",
        severity: "Error",
        message: "Choose a root activity.",
        subject: { kind: "ActivityDraft", id: state.draft.draftId, revision: body.expectedRevision },
        location: { providerKey: "elsa.activity-graph", jsonPointer: "/rootActivity/activityVersionId", referenceKey: "root" },
        remediation: "Choose an exact root activity version.",
        metadata: {}
      },
      {
        code: "activity.future-location",
        severity: "Warning",
        message: "A future location needs attention.",
        subject: { kind: "ActivityDraft", id: state.draft.draftId, revision: body.expectedRevision },
        location: { jsonPointer: "/future/location" },
        remediation: "Use an authorized editor that supports this location.",
        metadata: {}
      },
      {
        code: "activity.provider.editor-unavailable",
        severity: "Warning",
        message: "The provider-owned location needs attention.",
        subject: { kind: "ActivityDraft", id: "hidden-provider-subject-id", revision: body.expectedRevision },
        location: { providerKey: "hidden.provider", jsonPointer: "/hidden/provider-location", referenceKey: "hidden-reference" },
        remediation: "Use an authorized provider editor when one is available.",
        metadata: {}
      }
    ];
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        draftId: state.draft.draftId,
        revision: body.expectedRevision,
        isValid: diagnostics.length === 0,
        validatedAt: new Date().toISOString(),
        diagnostics
      })
    });
  });
  await page.route("**/design/activities/drafts/activity-draft-browser/publication-preflight", async route => {
    const body = route.request().postDataJSON() as { expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null };
    const invalid = state.preflightMode === "invalid";
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(browserPublicationPreflight(
        body.expectedDraftRevision,
        state.headVersionId,
        invalid
      ))
    });
  });
  await page.route("**/design/activities/drafts/activity-draft-browser/publish", async route => {
    const body = route.request().postDataJSON() as { version: string; idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string };
    state.publicationWrites += 1;
    state.requestedVersions.push(body.version);
    if (state.publishMode === "stale") {
      return route.fulfill({
        status: 409,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Stale review", errorCode: "activity.publication.review-stale" })
      });
    }
    const versionId = `published-version-${state.appliedPublications + 1}`;
    state.appliedPublications += 1;
    state.headVersionId = versionId;
    state.recommendedVersionId ??= versionId;
    state.latestReceipt = browserPublicationReceipt(
      body.idempotencyKey,
      body.version,
      body.expectedDraftRevision,
      body.expectedDefinitionHeadVersionId ?? null,
      body.reviewToken,
      versionId
    );
    if (state.publishMode === "ambiguous") {
      state.publishMode = "normal";
      return route.abort("connectionreset");
    }
    return route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify(state.latestReceipt) });
  });
  await page.route(/\/design\/activities\/publications\/[^/]+$/, async route => {
    state.receiptLookups += 1;
    await route.fulfill({
      status: state.latestReceipt ? 200 : 404,
      contentType: state.latestReceipt ? "application/json" : "application/problem+json",
      body: JSON.stringify(state.latestReceipt ?? { title: "Receipt unavailable" })
    });
  });
  await page.route("**/design/activities/drafts/activity-draft-recovery", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(state.draft) }));
  return state;
}

async function mockReusableBoundaryJourney(page: Page) {
  const state: {
    dispatchWrites: number;
    draftWrites: number;
    draftReads: number;
    submittedRoot: unknown;
  } = {
    dispatchWrites: 0,
    draftWrites: 0,
    draftReads: 0,
    submittedRoot: null
  };
  let workflowDraft = {
    id: "workflow-draft-1",
    definitionId: "workflow-definition-1",
    state: { rootActivity: null as Record<string, unknown> | null },
    layout: [] as unknown[],
    validationErrors: [] as unknown[]
  };
  await page.unroute("**/capabilities");
  await page.unroute("**/design/activities/catalog");
  await page.route("**/capabilities", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({ capabilities: [
      {
        id: "elsa.api.activity-design",
        contractVersion: "1",
        links: [
          { rel: "activity-catalog", href: "design/activities/catalog" },
          { rel: "recommended-activity-definitions", href: "design/activities/definitions/picker" },
          { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true }
        ]
      },
      {
        id: "elsa.api.workflow-design",
        contractVersion: "1",
        links: [
          { rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true }
        ]
      },
      {
        id: "elsa.api.runtime",
        contractVersion: "1",
        links: [
          { rel: "workflow-execute", href: "runtime/executables/{artifactId}/execute", templated: true },
          { rel: "activity-execution", href: "runtime/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true },
          { rel: "activity-execution-descendants", href: "runtime/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants", templated: true },
          { rel: "activity-execution-layout", href: "runtime/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout", templated: true }
        ]
      }
    ] })
  }));
  await page.route("**/design/activities/catalog", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({ activities: [
      reusableCatalogActivity("published-version-10", "10.0.0"),
      reusableCatalogActivity("published-version-1", "2.0.0")
    ] })
  }));
  await page.route(/\/design\/activities\/definitions\/picker\?.*/, route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      items: [{
        definitionId: "activity-def-browser",
        activityTypeKey: "elsa.user.browser-graph-activity.activity-def-browser",
        tenantId: "browser-tenant",
        category: "Browser tests",
        displayName: "Published browser activity",
        description: "A reusable activity boundary.",
        versionId: "published-version-1",
        version: "2.0.0",
        isAvailable: true,
        unavailableReason: null
      }],
      nextOffset: null
    })
  }));
  await page.route("**/design/activities/versions/published-version-1", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      definition: {
        definitionId: "activity-def-browser",
        recommendedVersionId: "published-version-1"
      },
      versionId: "published-version-1",
      version: "2.0.0",
      lifecycle: "Active",
      provider: {
        providerKey: "elsa.activity-graph",
        schemaVersion: "1"
      },
      contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] },
      publishedAt: "2026-07-17T10:00:00Z"
    })
  }));
  await page.route("**/design/workflows/drafts/workflow-draft-1", async route => {
    if (route.request().method() === "PUT") {
      state.draftWrites += 1;
      const submitted = route.request().postDataJSON() as {
        state: { rootActivity?: Record<string, unknown> | null };
        layout: unknown[];
      };
      state.submittedRoot = submitted.state.rootActivity ?? null;
      const root = submitted.state.rootActivity;
      workflowDraft = {
        ...workflowDraft,
        state: {
          rootActivity: root ? {
            nodeId: root.nodeId,
            activityVersionId: root.activityVersionId,
            inputs: root.inputs,
            outputs: root.outputs,
            structure: root.structure
          } : null
        },
        layout: submitted.layout
      };
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(workflowDraft) });
    }

    state.draftReads += 1;
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(workflowDraft) });
  });
  await page.route("**/runtime/executables/workflow-artifact-1/execute", route => {
    state.dispatchWrites += 1;
    return route.fulfill({
      status: 202,
      contentType: "application/json",
      body: JSON.stringify({ workflowExecutionId: "workflow-execution-1" })
    });
  });
  await page.route("**/runtime/instances/workflow-execution-1/activity-executions/boundary-execution-1", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(boundaryInspection())
  }));
  await page.route(/\/runtime\/instances\/workflow-execution-1\/activity-executions\/boundary-execution-1\/descendants\?limit=100$/, route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      root: {
        workflowExecutionId: "workflow-execution-1",
        activityExecutionId: "boundary-execution-1",
        executionScopeId: "scope-invoice",
        definitionVersionId: "published-version-1",
        templateHash: "sha256:invoice-v2"
      },
      committedThroughSequence: 3,
      effectiveLimit: 100,
      items: [{
        activityExecutionId: "descendant-execution-1",
        workflowExecutionId: "workflow-execution-1",
        executableNodeId: "invoice-write",
        authoredActivityId: "write",
        activityType: "Elsa.WriteLine",
        activityTypeVersion: "1",
        status: "Completed",
        executionSequence: 2,
        scheduledAt: "2026-07-17T10:00:01Z",
        relativeDepth: 1,
        outcomeNames: [],
        bookmarkCount: 0,
        incidentCount: 0,
        blockingIncidentCount: 0,
        metadata: {}
      }],
      nextCursor: null
    })
  }));
  await page.route("**/runtime/instances/workflow-execution-1/activity-executions/boundary-execution-1/layout", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      workflowExecutionId: "workflow-execution-1",
      activityExecutionId: "boundary-execution-1",
      artifactId: "workflow-artifact-1",
      sourceReferenceId: "workflow-source-1",
      selection: "ExecutedReference",
      boundaryOrigin: [{ kind: "TemplateBoundary", id: "published-version-1" }],
      templateHash: "sha256:invoice-v2",
      nodes: [{
        templateNodeId: "template-write",
        authoredActivityId: "write",
        executableNodeId: "invoice-write",
        x: 120,
        y: 80,
        hasPinnedGeometry: true
      }],
      connections: [],
      nestedBoundaries: []
    })
  }));
  return state;
}

function reusableCatalogActivity(activityVersionId: string, version: string) {
  return {
    activityVersionId,
    activityTypeKey: "elsa.user.browser-graph-activity.activity-def-browser",
    version,
    category: "Browser tests",
    displayName: "Published browser activity",
    description: "A reusable activity boundary.",
    executionType: "sync",
    inputs: [],
    outputs: [],
    designFacets: [],
    available: true,
    authoringTemplate: {
      nodeId: "template-boundary",
      activityVersionId,
      inputs: [],
      outputs: [],
      structure: null
    }
  };
}

function boundaryInspection() {
  return {
    activityExecutionId: "boundary-execution-1",
    workflowExecutionId: "workflow-execution-1",
    executableNodeId: "invoice-boundary",
    authoredActivityId: "invoice-boundary",
    activityType: "Contoso.InvoiceEvaluator",
    activityTypeVersion: "2.0.0",
    status: "Completed",
    subStatus: null,
    executionSequence: 1,
    scheduledAt: "2026-07-17T10:00:00Z",
    startedAt: "2026-07-17T10:00:01Z",
    completedAt: "2026-07-17T10:00:02Z",
    firstCheckpointId: "checkpoint:start",
    lastCheckpointId: "checkpoint:complete",
    lastCommittedAt: "2026-07-17T10:00:02Z",
    provenance: {
      parentActivityExecutionId: null,
      schedulingActivityExecutionId: null,
      schedulingWorkflowExecutionId: "workflow-execution-1",
      branchId: null,
      iterationId: null,
      executionPathId: null,
      executionScopeId: "scope-invoice",
      schedulingCause: null,
      metadata: {}
    },
    outcomeNames: [],
    bookmarks: [],
    incidents: [],
    valueSnapshots: [],
    metadata: {},
    boundary: {
      kind: "ReusableActivity",
      definitionId: "activity-def-browser",
      definitionVersionId: "published-version-1",
      version: "2.0.0",
      templateHash: "sha256:invoice-v2",
      invocationOrigin: [{ kind: "TemplateBoundary", id: "published-version-1" }],
      executionScopeId: "scope-invoice",
      hasChildren: true,
      directChildCount: 1,
      committedDescendantCount: 1,
      aggregate: {
        status: "Completed",
        total: 1,
        scheduled: 0,
        running: 0,
        suspended: 0,
        completed: 1,
        faulted: 0,
        cancelled: 0,
        blockingIncidentCount: 0,
        retryCount: 0,
        lastExecutionSequence: 3
      },
      layoutAvailable: true
    }
  };
}

function authoringApiCapabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" },
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-draft-validation", href: "design/activities/drafts/{draftId}/validate", templated: true },
    { rel: "activity-draft-conflict-copies", href: "design/activities/drafts/{draftId}/conflict-copies", templated: true },
    { rel: "activity-catalog", href: "design/activities/catalog" }
  ] }, { id: "elsa.api.expressions", contractVersion: "1", links: [
    { rel: "expression-descriptors", href: "expressions/descriptors" }
  ] }, { id: "elsa.api.publishing", contractVersion: "1", links: [
    { rel: "activity-publication-preflight", href: "design/activities/drafts/{draftId}/publication-preflight", templated: true },
    { rel: "activity-publication", href: "design/activities/drafts/{draftId}/publish", templated: true },
    { rel: "activity-publication-receipt", href: "design/activities/publications/{idempotencyKey}", templated: true }
  ] }] };
}

function authoringCapabilities() {
  return {
    contractSchemaVersions: ["1"],
    activityTypeKeyRules: { serverGenerated: true, allowsPreCreationOverride: true, immutable: true, prefix: "elsa.user", pattern: "^elsa\\.user\\..+$", maximumLength: 160, collisionScope: "tenantId + activityTypeKey" },
    providers: [{ providerKey: "elsa.activity-graph", displayName: "Activity Graph", manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: ["1"] }], requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }],
    types: [{
      alias: "String",
      displayName: "Text",
      category: "Primitives",
      defaultEditor: "text",
      supportedCollectionKinds: ["Single"],
      supportsNull: true,
      supportsDurability: true,
      compatibleStorageDriverKeys: ["elsa.json"]
    }], storageDriverKeys: ["elsa.json"], snapshotFingerprint: "sha256:browser"
  };
}

function authoringDefinition() {
  return { definitionId: "activity-def-browser", activityTypeKey: "elsa.user.browser-graph-activity.activity-def-browser", tenantId: "browser-tenant", category: "Browser tests", displayName: "Browser graph activity", description: null, contentAuthority: { kind: "design", authorityKey: "elsa.activity-design", sourceId: null }, forkedFrom: null, headVersionId: null, recommendedVersionId: null };
}

function authoringManagementDefinition(state: { headVersionId: string | null; recommendedVersionId: string | null; appliedPublications: number }) {
  const definition = { ...authoringDefinition(), headVersionId: state.headVersionId, recommendedVersionId: state.recommendedVersionId };
  const reference = state.headVersionId ? { versionId: state.headVersionId, version: state.appliedPublications === 1 ? "2.0.0" : "3.4.5", lifecycle: "Active", providerKey: "elsa.activity-graph", providerSchemaVersion: "1" } : null;
  const recommendation = state.recommendedVersionId ? { versionId: state.recommendedVersionId, version: "2.0.0", lifecycle: "Active", providerKey: "elsa.activity-graph", providerSchemaVersion: "1" } : null;
  return { definition, lifecycle: { draftCount: 1, versionCount: state.appliedPublications, head: reference, recommendation }, actions: [], updatedAt: "2026-07-17T10:00:00Z" };
}

function browserPublicationPreflight(revision: number, headVersionId: string | null, invalid: boolean) {
  const first = headVersionId === null;
  return {
    draftId: "activity-draft-browser",
    draftRevision: revision,
    definitionId: "activity-def-browser",
    definitionHeadVersionId: headVersionId,
    hasBaseline: !first,
    reviewToken: `sha256:review-${revision}-${headVersionId ?? "first"}`,
    isPublishable: !invalid,
    minimumVersion: first ? "1.0.0" : "2.1.0",
    validVersions: [first ? "1.0.0" : "2.1.0"],
    diff: { compatibility: "Compatible", requiredBump: first ? "Minor" : "Patch", behaviorChanged: true, summary: { breaking: 0, additive: 1, nonBehavioral: 0, warnings: 0 } },
    impactFirstChanges: [{
      changeId: "provider-protected",
      area: "Provider",
      kind: "provider.secret-rotated",
      subject: { memberKind: null, referenceKey: null, dependencyVersionId: null, occurrenceId: null },
      before: { redacted: true, value: "raw-protected-provider-value" },
      after: { kind: "Redacted", value: "raw-protected-provider-value" },
      impact: "Additive",
      requiredBump: "Minor",
      message: "Protected provider configuration changed."
    }],
    dependencies: [],
    provider: { kind: "Provider", key: "elsa.activity-graph", schemaVersion: "1", status: invalid ? "Unavailable" : "Available", supportedSchemaVersions: invalid ? [] : ["1"] },
    storage: [],
    runtime: [{ kind: "Runtime", key: "elsa.activity-graph", schemaVersion: "1", status: invalid ? "Missing" : "Available", supportedSchemaVersions: invalid ? [] : ["1"] }],
    diagnostics: invalid ? [{ code: "activity.runtime.consumer-missing", severity: "Error", message: "Required Runtime consumer is missing.", subject: { kind: "ActivityDraft", id: "activity-draft-browser", revision }, location: null, remediation: "Install Runtime support.", metadata: {} }] : []
  };
}

function browserPublicationReceipt(idempotencyKey: string, version: string, revision: number, expectedHeadVersionId: string | null, reviewToken: string, versionId: string) {
  return {
    idempotencyKey,
    status: "Applied",
    draftId: "activity-draft-browser",
    expectedDraftRevision: revision,
    expectedDefinitionHeadVersionId: expectedHeadVersionId,
    reviewToken,
    requestedVersion: version,
    outcome: {
      definitionId: "activity-def-browser",
      definitionVersionId: versionId,
      draftId: "activity-draft-browser",
      version,
      templateId: `template-${versionId}`,
      templateHash: `sha256:${versionId}`,
      sourceReferenceId: `source-${versionId}`,
      publishedAt: "2026-07-17T11:00:00Z"
    },
    errorCode: null,
    diagnostics: [],
    updatedAt: "2026-07-17T11:00:00Z"
  };
}

async function createBrowserActivity(page: Page) {
  await page.goto("/?mode=activity-definitions");
  await page.getByRole("button", { name: "Create Activity Definition" }).click();
  await page.getByRole("textbox", { name: "Display name" }).fill("Published browser activity");
  await page.getByRole("button", { name: "Create definition" }).click();
  await expect(page.getByText("Saved revision 1")).toBeVisible();
}

function authoringDraftSummary(draft: ReturnType<typeof authoringDraft>) {
  return { draftId: draft.draftId, definitionId: draft.definitionId, revision: draft.revision, sourceVersionId: null, status: "active", providerKey: draft.provider.providerKey, providerSchemaVersion: draft.provider.schemaVersion, updatedAt: draft.updatedAt, presentationLabel: draft.presentationLabel };
}

function authoringDraft(draftId: string, revision: number, payload: unknown) {
  return { draftId, definitionId: "activity-def-browser", tenantId: "browser-tenant", revision, sourceVersionId: null, status: "active", contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }, provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", manifestFingerprint: `sha256:${revision}`, payload }, layout: [], validation: null, createdAt: "2026-07-17T10:00:00Z", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null };
}

function initialGraphPayload() { return { rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null }, variables: [], outputMappings: [] }; }
function catalogActivity(activityVersionId: string, displayName: string) { return { activityVersionId, activityTypeKey: `Elsa.${displayName.replaceAll(" ", "")}`, version: "1.0.0", category: "Primitives", displayName, description: null, executionType: "sync", inputs: [], outputs: [], designFacets: [], available: true, authoringTemplate: { nodeId: "template", activityVersionId, inputs: [], outputs: [], structure: null } }; }
