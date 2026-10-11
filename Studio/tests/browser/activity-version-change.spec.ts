import { expect, test, type Page } from "@playwright/test";

test("reviews and applies one exact version while preserving stable and unresolved work", async ({ page }) => {
  const state = await mockVersionChange(page);
  await page.goto("/?mode=version-change");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  await expect(dialog).toContainText("v1.0.0 · version-1");
  await expect(dialog.getByRole("radio", { name: /v2\.0\.0.*Recommended/ })).toBeChecked();
  await expect(dialog.getByRole("radio", { name: /v0\.9\.0.*Active/ })).toBeVisible();
  await expect(dialog).not.toContainText("version-retired");
  await expect(dialog).toContainText("Breaking");
  await expect(dialog).toContainText("Amount");
  await expect(dialog).toContainText("Legacy");
  await expect(dialog).toContainText("Rejected");

  await dialog.getByRole("button", { name: "Apply to this occurrence" }).click();
  await expect(page.getByText("Authoritative exact version change applied")).toBeVisible();
  await expect(page.getByTestId("invoice-one")).toContainText("version-2");
  await expect(page.getByTestId("invoice-two")).toContainText("version-1");
  expect(state.draftWrites).toBe(1);
  expect(state.lastDraft?.sourceVersionId).toBe("published-workflow-version");
});

test("explicitly applies to every matching occurrence in only the current draft", async ({ page }) => {
  const state = await mockVersionChange(page);
  await page.goto("/?mode=version-change");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  await dialog.getByRole("radio", { name: "All matching occurrences in this workflow draft" }).check();
  await dialog.getByRole("button", { name: "Apply to 2 occurrences" }).click();

  await expect(page.getByTestId("invoice-one")).toContainText("version-2");
  await expect(page.getByTestId("invoice-two")).toContainText("version-2");
  await expect(page.getByTestId("other")).toContainText("other-version");
  expect(state.draftWrites).toBe(1);
});

test("allows deliberate rollback to an older active version", async ({ page }) => {
  await mockVersionChange(page);
  await page.goto("/?mode=version-change");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  await dialog.getByRole("radio", { name: /v0\.9\.0.*Active/ }).check();
  await expect(dialog).toContainText("v0.9.0 · version-0");
  await dialog.getByRole("button", { name: "Apply to this occurrence" }).click();

  await expect(page.getByTestId("invoice-one")).toContainText("version-0");
});

test("stale review preserves local work and performs no authoritative write", async ({ page }) => {
  const state = await mockVersionChange(page);
  await page.goto("/?mode=version-change&stale=true");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  await dialog.getByRole("button", { name: "Apply to this occurrence" }).click();

  await expect(dialog.getByRole("alert")).toContainText("local work was kept");
  await expect(page.getByTestId("invoice-one")).toContainText("version-1");
  expect(state.draftWrites).toBe(0);
});

async function mockVersionChange(page: Page) {
  const state = {
    draftWrites: 0,
    lastDraft: null as Record<string, unknown> | null
  };
  await page.route("**/capabilities", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      capabilities: [
        {
          id: "elsa.api.activity-design",
          contractVersion: "1",
          links: [
            { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
            { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true },
            { rel: "activity-version-diff", href: "design/activities/versions/{fromVersionId}/diff/{toVersionId}", templated: true }
          ]
        },
        {
          id: "elsa.api.workflow-design",
          contractVersion: "1",
          links: [{ rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true }]
        }
      ]
    })
  }));
  await page.route(/\/design\/activities\/definitions\/activity-def-browser\/versions\?.*/, route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      items: [
        versionSummary("version-2", "2.0.0", "Active", true),
        versionSummary("version-0", "0.9.0", "Active", false),
        versionSummary("version-retired", "3.0.0", "Retired", false)
      ],
      totalCount: 3,
      hasMore: false,
      continuation: null,
      snapshot: { snapshotId: "versions-snapshot", asOf: "2026-07-19T00:00:00Z" }
    })
  }));
  await page.route(/\/design\/activities\/versions\/(version-2|version-0)$/, route => {
    const versionId = route.request().url().endsWith("version-0") ? "version-0" : "version-2";
    const version = versionId === "version-0" ? "0.9.0" : "2.0.0";
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(versionDetail(versionId, version))
    });
  });
  await page.route(/\/design\/activities\/versions\/version-1\/diff\/(version-2|version-0)$/, route => {
    const toVersionId = route.request().url().endsWith("version-0") ? "version-0" : "version-2";
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(versionDiff(toVersionId))
    });
  });
  await page.route("**/design/workflows/drafts/workflow-draft-version-change", async route => {
    if (route.request().method() !== "PUT") return route.fallback();
    state.draftWrites += 1;
    const body = route.request().postDataJSON() as { state: Record<string, unknown>; layout: unknown[] };
    state.lastDraft = {
      id: "workflow-draft-version-change",
      definitionId: "workflow-definition-version-change",
      sourceVersionId: "published-workflow-version",
      state: body.state,
      layout: body.layout,
      validationErrors: []
    };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(state.lastDraft) });
  });
  return state;
}

function versionSummary(versionId: string, version: string, lifecycle: string, isRecommended: boolean) {
  return {
    version: {
      versionId,
      definitionId: "activity-def-browser",
      version,
      lifecycle,
      publishedAt: "2026-07-19T00:00:00Z"
    },
    providerKey: "ActivityGraph",
    providerSchemaVersion: "1",
    isRecommended,
    actions: []
  };
}

function versionDetail(versionId: string, version: string) {
  return {
    definition: {
      definitionId: "activity-def-browser",
      activityTypeKey: "Contoso.Invoice",
      category: "Browser tests",
      displayName: "Invoice",
      contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design" }
    },
    versionId,
    version,
    contract: {
      contractSchemaVersion: "1",
      inputs: [contractInput("Amount")],
      outputs: [],
      outcomes: [{ referenceKey: "Done", name: "Done", isEmitted: true }]
    },
    provider: { providerKey: "ActivityGraph", schemaVersion: "1", manifestFingerprint: "browser" },
    lifecycle: "Active",
    publishedAt: "2026-07-19T00:00:00Z"
  };
}

function contractInput(referenceKey: string) {
  return {
    referenceKey,
    name: referenceKey,
    type: { alias: "String", collectionKind: "None" },
    isRequired: false,
    isNullable: true,
    default: null,
    storageDriverKey: "Workflow",
    durability: "Durable"
  };
}

function versionDiff(toVersionId: string) {
  return {
    from: { kind: "Version", definitionId: "activity-def-browser", versionId: "version-1", version: "1.0.0" },
    to: { kind: "Version", definitionId: "activity-def-browser", versionId: toVersionId, version: toVersionId === "version-0" ? "0.9.0" : "2.0.0" },
    compatibility: "Breaking",
    requiredBump: "Major",
    behaviorChanged: true,
    provider: { fromKey: "ActivityGraph", toKey: "ActivityGraph", changed: false },
    summary: { breaking: 2, additive: 0, nonBehavioral: 0, warnings: 0 },
    changes: [
      {
        changeId: "legacy-removed",
        area: "Contract",
        kind: "Removed",
        subject: { memberKind: "Input", referenceKey: "Legacy" },
        impact: "Breaking",
        requiredBump: "Major",
        message: "Legacy input was removed."
      },
      {
        changeId: "rejected-removed",
        area: "Contract",
        kind: "Removed",
        subject: { memberKind: "Outcome", referenceKey: "Rejected" },
        impact: "Breaking",
        requiredBump: "Major",
        message: "Rejected outcome was removed."
      }
    ],
    diagnostics: []
  };
}
