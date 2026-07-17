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
    validationMode: "invalid" as "invalid" | "valid" | "forbidden" | "not-found" | "unavailable" | "stale",
    catalogBlocked: false,
    releaseCatalog: () => {},
    draft: authoringDraft("activity-draft-browser", 1, initialGraphPayload())
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
  ] }, { id: "elsa.api.expressions", contractVersion: "1", links: [
    { rel: "expression-descriptors", href: "expressions/descriptors" }
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

function authoringDraftSummary(draft: ReturnType<typeof authoringDraft>) {
  return { draftId: draft.draftId, definitionId: draft.definitionId, revision: draft.revision, sourceVersionId: null, status: "active", providerKey: draft.provider.providerKey, providerSchemaVersion: draft.provider.schemaVersion, updatedAt: draft.updatedAt, presentationLabel: draft.presentationLabel };
}

function authoringDraft(draftId: string, revision: number, payload: unknown) {
  return { draftId, definitionId: "activity-def-browser", tenantId: "browser-tenant", revision, sourceVersionId: null, status: "active", contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] }, provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", manifestFingerprint: `sha256:${revision}`, payload }, layout: [], validation: null, createdAt: "2026-07-17T10:00:00Z", updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null };
}

function initialGraphPayload() { return { rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null }, variables: [], outputMappings: [] }; }
function catalogActivity(activityVersionId: string, displayName: string) { return { activityVersionId, activityTypeKey: `Elsa.${displayName.replaceAll(" ", "")}`, version: "1.0.0", category: "Primitives", displayName, description: null, executionType: "sync", inputs: [], outputs: [], designFacets: [], available: true, authoringTemplate: { nodeId: "template", activityVersionId, inputs: [], outputs: [], structure: null } }; }
