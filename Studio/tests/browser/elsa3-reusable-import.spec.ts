import { expect, test, type Route } from "@playwright/test";

const importRoot = "/migration/elsa3/reusable-activities";

test("Elsa 3 import pages exact rows, preserves a valid subset, expands closure, and reconciles an ambiguous Apply", async ({ page }) => {
  const state = {
    selectionBody: null as Record<string, unknown> | null,
    applyBody: null as Record<string, unknown> | null
  };
  await page.route(`**${importRoot}/**`, async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname.endsWith("/analysis")) {
      const offset = Number(url.searchParams.get("offset"));
      return json(route, offset === 0 ? firstAnalysisPage() : secondAnalysisPage());
    }
    if (url.pathname.endsWith("/selection")) {
      state.selectionBody = request.postDataJSON();
      return json(route, {
        collectionHandle: "collection",
        planId: "plan",
        requestedSourceVersionIds: ["owner-v1"],
        expandedSourceVersionIds: ["dependency-v1", "owner-v1"],
        addedDependencySourceVersionIds: ["dependency-v1"],
        isReady: true,
        diagnostics: []
      });
    }
    if (url.pathname.endsWith("/apply")) {
      state.applyBody = request.postDataJSON();
      return json(route, {
        title: "Elsa 3 import failed",
        detail: "The response outcome is unknown.",
        errorCode: "elsa3.import.unexpected"
      }, 500);
    }
    if (url.pathname.includes("/imports/")) return json(route, importReceipt("Applied"));
    return route.abort();
  });

  await page.goto("/workflows/activity-definitions/import-elsa3");
  await expect(page.getByRole("heading", { name: "Import from Elsa 3" })).toBeVisible();
  await expect(page.getByText("No Execute-Workflow fallback")).toBeVisible();
  await page.getByPlaceholder("Authorized collection handle").fill("collection");
  await page.getByRole("button", { name: "Analyze immutable collection" }).click();

  await expect(page.getByText("100 / 102 versions")).toBeVisible();
  await expect(page.getByText("ELS3-REUSABLE-MISSING-REFERENCE", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("ELS3-REUSABLE-AMBIGUOUS-REFERENCE", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("ELS3-REUSABLE-UNSUPPORTED-REFERENCE", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("ELS3-REUSABLE-UNSUPPORTED-TRIGGER", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("owner-v1 → invalid-cycle-v1 → owner-v1", { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/Node.*reuse-node.*root\.activities\[0\]/).first()).toBeVisible();
  await expect(page.getByRole("checkbox", { name: "Select invalid-cycle exact source version 1" })).toBeDisabled();

  await page.getByRole("button", { name: "Load next analysis page from 100" }).click();
  await expect(page.getByText("All 102 exact source versions are loaded.")).toBeVisible();
  await expect(page.getByText("Direct starts", { exact: true }).last()).toBeVisible();

  await page.getByRole("checkbox", { name: "Select owner exact source version 1" }).check();
  await expect(page.getByText("Required dependency")).toBeVisible();
  expect(state.selectionBody).toEqual({
    planId: "plan",
    selectedSourceVersionIds: ["owner-v1"]
  });

  await page.getByRole("button", { name: "Review exact import" }).click();
  const review = page.getByRole("heading", { name: "4. Final atomic review" }).locator("xpath=ancestor::section[1]");
  await expect(review).toContainText("Exact source versions2");
  await expect(review).toContainText("Activity Definitions2");
  await expect(review).toContainText("Activity Definition versions2");
  await expect(review).toContainText("Exact reusable rewrites1");

  await page.getByRole("button", { name: "Apply reviewed import" }).click();
  await expect(page.getByRole("heading", { name: "Durable import receipt" })).toBeVisible();
  expect(state.applyBody).toMatchObject({
    planId: "plan",
    selectedSourceVersionIds: ["dependency-v1", "owner-v1"]
  });
  expect(state.applyBody?.idempotencyKey).toEqual(expect.any(String));

  await page.getByRole("button", { name: "Open Activity Definition version" }).first().click();
  await expect(page).toHaveURL(/\/workflows\/activity-definitions\?definition=activity-owner&section=versions&version=activity-owner-v1/);
});

test("Elsa 3 import reports an idempotent Already imported receipt without a second mutation claim", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 900 });
  await page.route(`**${importRoot}/**`, async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname.endsWith("/analysis")) {
      return json(route, {
        collectionHandle: "collection",
        planId: "plan",
        offset: 0,
        limit: 100,
        processed: 1,
        total: 1,
        processedDiagnostics: 0,
        totalDiagnostics: 0,
        isComplete: true,
        nextOffset: null,
        items: [sourceItem("owner", "owner-v1", true)],
        diagnostics: []
      });
    }
    if (url.pathname.endsWith("/selection")) {
      return json(route, {
        collectionHandle: "collection",
        planId: "plan",
        requestedSourceVersionIds: ["owner-v1"],
        expandedSourceVersionIds: ["owner-v1"],
        addedDependencySourceVersionIds: [],
        isReady: true,
        diagnostics: []
      });
    }
    if (url.pathname.endsWith("/apply")) return json(route, importReceipt(1));
    return route.abort();
  });

  await page.goto("/workflows/activity-definitions/import-elsa3");
  await page.getByPlaceholder("Authorized collection handle").fill("collection");
  await page.getByRole("button", { name: "Analyze immutable collection" }).click();
  await page.getByRole("checkbox", { name: "Select owner exact source version 1" }).check();
  await page.getByRole("button", { name: "Review exact import" }).click();
  await page.getByRole("button", { name: "Apply reviewed import" }).click();

  await expect(page.getByText("Already imported. The durable receipt is identical and no second mutation was made.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Apply reviewed import" })).toBeDisabled();
  const overflowOffenders = await page.evaluate(() =>
    [...document.querySelectorAll("*")]
      .filter(element => element.getBoundingClientRect().right > document.documentElement.clientWidth)
      .slice(0, 8)
      .map(element => ({
        tag: element.tagName,
        className: element.className,
        text: element.textContent?.slice(0, 80),
        right: element.getBoundingClientRect().right
      }))
  );
  expect(overflowOffenders).toEqual([]);
});

test("Elsa 3 collection upload is raw, bounded, and cancellable without accepting a late handle", async ({ page }) => {
  let uploadBody = "";
  await page.route(`**${importRoot}/collections`, async route => {
    uploadBody = route.request().postData() ?? "";
    await new Promise(resolve => setTimeout(resolve, 500));
    await json(route, {
      collectionHandle: "late-handle",
      createdAt: "2026-07-19T00:00:00Z",
      expiresAt: "2026-07-19T01:00:00Z",
      sourceVersionCount: 1,
      contentLength: uploadBody.length
    }, 201).catch(() => undefined);
  });

  await page.goto("/workflows/activity-definitions/import-elsa3");
  const rawCollection = JSON.stringify([{ id: "source-v1", definitionId: "source" }]);
  await page.getByLabel("Elsa 3 collection file").setInputFiles({
    name: "elsa3.json",
    mimeType: "application/json",
    buffer: Buffer.from(rawCollection)
  });
  await page.getByRole("button", { name: "Upload once" }).click();
  await page.getByRole("button", { name: "Cancel upload" }).click();

  await expect(page.getByText("Upload cancelled. No collection handle was accepted by Studio.")).toBeVisible();
  expect(uploadBody).toBe(rawCollection);
  await page.waitForTimeout(600);
  await expect(page.getByText("Handle accepted")).toHaveCount(0);
});

test("Elsa 3 expired handles require a fresh collection without retaining an analysis plan", async ({ page }) => {
  await page.route(`**${importRoot}/collections/expired/analysis?*`, route => json(route, {
    title: "Elsa 3 import collection expired",
    detail: "Elsa 3 import collection 'expired' has expired.",
    errorCode: "elsa3.import.collection-expired"
  }, 410));

  await page.goto("/workflows/activity-definitions/import-elsa3");
  await page.getByPlaceholder("Authorized collection handle").fill("expired");
  await page.getByRole("button", { name: "Analyze immutable collection" }).click();

  await expect(page.getByText("The immutable Elsa 3 collection expired. Upload the source again to create a fresh handle and run a new side-effect-free analysis.")).toBeVisible();
  await expect(page.getByPlaceholder("Authorized collection handle")).toHaveValue("");
  await expect(page.getByText("Plan ID")).toHaveCount(0);
});

function firstAnalysisPage() {
  const owner = sourceItem("owner", "owner-v1", true, {
    dependencies: [{
      ownerSourceVersionId: "owner-v1",
      targetSourceDefinitionId: "dependency",
      targetSourceVersionId: "dependency-v1",
      targetActivityDefinitionVersionId: "activity-dependency-v1",
      nodeId: "reuse-node"
    }],
    rewrites: [{
      ownerSourceVersionId: "owner-v1",
      nodeId: "reuse-node",
      targetSourceVersionId: "dependency-v1",
      targetActivityDefinitionVersionId: "activity-dependency-v1"
    }]
  });
  const cycle = diagnostic("ELS3-REUSABLE-DEPENDENCY-CYCLE", {
    cycle: ["owner-v1", "invalid-cycle-v1"],
    pathSegments: [
      { kind: 0, identity: "invalid-cycle-v1", location: "$[1]" },
      { kind: 1, identity: "reuse-node", location: "$[1].root.activities[0]" }
    ]
  });
  const invalid = sourceItem("invalid-cycle", "invalid-cycle-v1", false, {
    canApply: false,
    diagnostics: [cycle]
  });
  const fillers = Array.from({ length: 98 }, (_, index) =>
    sourceItem(`filler-${String(index).padStart(3, "0")}`, `filler-v${index}`, false));
  const diagnostics = [
    cycle,
    diagnostic("ELS3-REUSABLE-MISSING-REFERENCE"),
    diagnostic("ELS3-REUSABLE-AMBIGUOUS-REFERENCE"),
    diagnostic("ELS3-REUSABLE-UNSUPPORTED-REFERENCE"),
    diagnostic("ELS3-REUSABLE-UNSUPPORTED-TRIGGER")
  ];
  return {
    collectionHandle: "collection",
    planId: "plan",
    offset: 0,
    limit: 100,
    processed: 100,
    total: 102,
    processedDiagnostics: diagnostics.length,
    totalDiagnostics: diagnostics.length,
    isComplete: false,
    nextOffset: 100,
    items: [owner, invalid, ...fillers],
    diagnostics
  };
}

function secondAnalysisPage() {
  return {
    collectionHandle: "collection",
    planId: "plan",
    offset: 100,
    limit: 100,
    processed: 102,
    total: 102,
    processedDiagnostics: 5,
    totalDiagnostics: 5,
    isComplete: true,
    nextOffset: null,
    items: [
      sourceItem("dependency", "dependency-v1", true),
      sourceItem("ordinary", "ordinary-v1", false, {
        directStarts: [{ nodeId: "trigger-node", activityType: "HttpEndpoint" }]
      })
    ],
    diagnostics: []
  };
}

function sourceItem(
  sourceDefinitionId: string,
  sourceVersionId: string,
  isReusable: boolean,
  overrides: Record<string, unknown> = {}
) {
  return {
    sourceDefinitionId,
    sourceVersionId,
    sourceVersion: 1,
    sourceFingerprint: `fingerprint-${sourceVersionId}`,
    isReusable,
    workflowDefinitionId: `workflow-${sourceVersionId}`,
    workflowVersionId: `workflow-version-${sourceVersionId}`,
    activityDefinitionId: isReusable ? `activity-${sourceDefinitionId}` : null,
    activityDefinitionVersionId: isReusable ? `activity-${sourceDefinitionId}-v1` : null,
    activityTypeKey: isReusable ? `elsa3.workflow.${sourceDefinitionId}` : null,
    dependencies: [],
    rewrites: [],
    directStarts: [],
    diagnostics: [],
    canApply: true,
    ...overrides
  };
}

function diagnostic(code: string, overrides: Record<string, unknown> = {}) {
  return {
    severity: 2,
    code,
    message: `${code} blocks only its affected source closure.`,
    path: "$[1].root.activities[0]",
    guidance: "Resolve the exact source reference and analyze again.",
    metadata: {},
    pathSegments: [
      { kind: 0, identity: "owner-v1", location: "$[1]" },
      { kind: 1, identity: "reuse-node", location: "$[1].root.activities[0]" }
    ],
    cycle: [],
    isError: true,
    ...overrides
  };
}

function importReceipt(status: "Applied" | number) {
  return {
    receiptId: "receipt",
    collectionHandle: "collection",
    planId: "plan",
    idempotencyKey: "operation",
    selectionFingerprint: "sha256:selection",
    accessScope: { tenantId: "tenant", userId: "user", tenantScope: "tenant:tenant" },
    status,
    completedAt: "2026-07-19T00:00:00Z",
    sources: [{
      sourceDefinitionId: "owner",
      sourceVersionId: "owner-v1",
      workflowDefinitionId: "wrapper-owner",
      workflowVersionId: "wrapper-owner-v1",
      workflowDisposition: 0,
      workflowNavigationIdentity: "/design/workflows/definitions/wrapper-owner/versions/wrapper-owner-v1",
      activityDefinitionId: "activity-owner",
      activityDefinitionVersionId: "activity-owner-v1",
      activityDefinitionDisposition: 0,
      activityVersionDisposition: 0,
      activityDefinitionNavigationIdentity: "/design/activities/definitions/activity-owner",
      activityVersionNavigationIdentity: "/design/activities/versions/activity-owner-v1"
    }]
  };
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({
    status,
    contentType: status >= 400 ? "application/problem+json" : "application/json",
    body: JSON.stringify(body)
  });
}
