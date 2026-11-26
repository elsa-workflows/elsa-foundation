import { expect, test, type Page } from "@playwright/test";

test("reviews and applies one exact version while preserving stable and unresolved work", async ({ page }) => {
  const state = await mockVersionChange(page);
  await page.goto("/?mode=version-change");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  await expect(dialog).toContainText("Current version v1.0.0");
  const targetVersion = dialog.getByLabel("Target active version");
  await expect(targetVersion).toHaveValue("version-2");
  await expect(targetVersion.locator('option[value="version-0"]')).toHaveText("v0.9.0");
  await expect(dialog).not.toContainText("version-retired");
  const summary = dialog.getByRole("region", { name: "Compatibility and unresolved impact" });
  await expect(summary).toContainText("Breaking");
  const unresolvedBindings = summary.locator("dl > div").nth(2);
  await expect(unresolvedBindings.locator("dt")).toHaveText("Unresolved input bindings");
  await expect(unresolvedBindings.locator("dd")).toHaveText("1");
  const unresolvedConnections = summary.locator("dl > div").nth(3);
  await expect(unresolvedConnections.locator("dt")).toHaveText("Unresolved outcome connections");
  await expect(unresolvedConnections.locator("dd")).toHaveText("1");
  await expect(dialog.getByText("Amount", { exact: true })).not.toBeVisible();
  await expect(dialog.getByText("Legacy", { exact: true })).not.toBeVisible();
  await expect(dialog.getByText("Rejected", { exact: true })).not.toBeVisible();
  await dialog.getByText("Detailed technical evidence", { exact: true }).click();
  await expect(dialog.getByText("Amount", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Legacy", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Rejected", { exact: true })).toBeVisible();
  await expect(dialog).toContainText("version-1");

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
  await dialog.getByLabel("Target active version").selectOption("version-0");
  await expect(dialog).toContainText("Target version v0.9.0");
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

test("keeps review actions available on a narrow viewport when technical evidence is expanded", async ({ page }) => {
  await mockVersionChange(page);
  await page.setViewportSize({ width: 390, height: 600 });
  await page.goto("/?mode=version-change");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  await dialog.getByText("Detailed technical evidence", { exact: true }).click();

  await expect(dialog.getByRole("button", { name: "Cancel" })).toBeInViewport();
  await expect(dialog.getByRole("button", { name: "Apply to this occurrence" })).toBeInViewport();
});

test("allows choosing an exact version from a later page", async ({ page }) => {
  const state = await mockVersionChange(page, { paginated: true });
  await page.goto("/?mode=version-change");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  const targetVersion = dialog.getByLabel("Target active version");
  await dialog.getByRole("button", { name: "More versions" }).click();
  await expect(dialog.getByRole("status")).toContainText("No other active version is available.");
  await expect(targetVersion).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Apply to this occurrence" })).toBeDisabled();
  await dialog.getByRole("button", { name: "More versions" }).click();
  await expect(targetVersion).toHaveValue("version-0");
  await expect(dialog.getByRole("button", { name: "Previous versions" })).toBeEnabled();
  await expect(dialog.getByRole("button", { name: "More versions" })).toHaveCount(0);

  await targetVersion.selectOption("version-3");
  await expect(dialog).toContainText("Target version v3.0.0");
  await dialog.getByRole("button", { name: "Apply to this occurrence" }).click();

  await expect(page.getByTestId("invoice-one")).toContainText("version-3");
  expect(state.draftWrites).toBe(1);
});

test("keeps apply disabled when active versions cannot be loaded", async ({ page }) => {
  await mockVersionChange(page, { versionRequestFails: true });
  await page.goto("/?mode=version-change");

  await page.getByRole("button", { name: "Change exact version" }).click();
  const dialog = page.getByRole("dialog", { name: "Review exact Activity Definition Version change" });
  await expect(dialog.getByRole("alert")).toContainText("Authorized exact-version evidence could not be loaded");
  await expect(dialog.getByLabel("Target active version")).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Apply to this occurrence" })).toBeDisabled();
});

async function mockVersionChange(page: Page, options: { paginated?: boolean; versionRequestFails?: boolean } = {}) {
  const { paginated = false, versionRequestFails = false } = options;
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
  await page.route(/\/design\/activities\/definitions\/activity-def-browser\/versions\?.*/, route => {
    if (versionRequestFails) {
      return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "Unavailable" }) });
    }
    const cursor = new URL(route.request().url()).searchParams.get("cursor");
    const items = paginated && cursor === "next-versions"
      ? [versionSummary("version-retired", "3.0.0", "Retired", false)]
      : paginated && cursor === "later-active"
        ? [versionSummary("version-0", "0.9.0", "Active", false), versionSummary("version-3", "3.0.0", "Active", false)]
        : [
        versionSummary("version-2", "2.0.0", "Active", true),
        ...(!paginated ? [versionSummary("version-0", "0.9.0", "Active", false), versionSummary("version-retired", "3.0.0", "Retired", false)] : [])
      ];
    const continuation = paginated
      ? cursor === "next-versions" ? "later-active" : cursor ? null : "next-versions"
      : null;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items,
        totalCount: paginated ? 4 : 3,
        hasMore: Boolean(continuation),
        continuation,
        snapshot: { snapshotId: "versions-snapshot", asOf: "2026-07-19T00:00:00Z" }
      })
    });
  });
  await page.route(/\/design\/activities\/versions\/(version-2|version-0|version-3)$/, route => {
    const versionId = route.request().url().match(/\/(version-2|version-0|version-3)$/)?.[1] ?? "version-2";
    const version = versionId === "version-0" ? "0.9.0" : "2.0.0";
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(versionDetail(versionId, versionId === "version-3" ? "3.0.0" : version))
    });
  });
  await page.route(/\/design\/activities\/versions\/version-1\/diff\/(version-2|version-0|version-3)$/, route => {
    const toVersionId = route.request().url().match(/\/(version-2|version-0|version-3)$/)?.[1] ?? "version-2";
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
  const version = toVersionId === "version-0" ? "0.9.0" : toVersionId === "version-3" ? "3.0.0" : "2.0.0";
  return {
    from: { kind: "Version", definitionId: "activity-def-browser", versionId: "version-1", version: "1.0.0" },
    to: { kind: "Version", definitionId: "activity-def-browser", versionId: toVersionId, version },
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
