import { expect, test, type Page } from "@playwright/test";

function registry() {
  return { feeds: [{ name: "studio-local-packages", directoryPath: "packages", includePatterns: ["*"], includeAll: true, directory: { watch: false } }],
    retentionPolicy: { status: "Supported", keepNewestInactiveVersionsPerPackage: null as number | null },
    capabilities: { canManageFeeds: true, canPrunePackages: true, canReconcile: true, canManageRetentionPolicy: true, feedChangesRequireRestart: true } };
}
function preview() {
  return { mode: "Preview", status: "Preview", rootLabel: "default", rootIdentity: { volume: "owned-volume", file: "owned-root" }, enrollmentEpoch: 7,
    retention: null, protectionKnowledge: "Known", inventoryComplete: true, diagnostics: [], inventoryIssues: [],
    candidates: [{ install: { packageId: "Acme.Widget", version: "1.0.0" }, classification: "Eligible", reasons: [], attempt: "NotAttempted", physicalOutcome: null, journalPhase: null }] };
}
async function setup(page: Page) {
  const state = registry();
  await page.route("**/_elsa/module-management/registry", route => route.fulfill({ json: state }));
  await page.route("**/_elsa/studio/backend-management/registry", route => route.fulfill({ json: { status: "available", registry: {
    ...registry(), retentionPolicy: { retainLastNVersions: 3, retainYoungerThanDays: 30, mode: "Automatic" }
  } } }));
  return state;
}

test("preview, cancel and explicit execution show actual partial physical outcomes", async ({ page }) => {
  await setup(page);
  const requests: unknown[] = [];
  await page.route("**/_elsa/module-management/prune", route => {
    const request = route.request().postDataJSON(); requests.push(request);
    return route.fulfill({ json: request.dryRun ? preview() : { ...preview(), mode: "Execute", status: "Failed",
      candidates: [{ ...preview().candidates[0], attempt: "Completed", physicalOutcome: "Deleted", journalPhase: "Complete" }],
      diagnostics: [{ code: "StoppedAfterPartialWork", message: "Later work failed; preview again." }] } });
  });
  await page.goto("/package-maintenance.html");
  await expect(page.getByRole("button", { name: "Execute cleanup" })).toHaveCount(0);
  await expect(page.getByLabel("Inactive versions to keep per package")).toHaveValue("");
  await page.getByRole("button", { name: "Preview cleanup" }).click();
  await expect(page.getByLabel("Package maintenance report")).toContainText("No files were deleted by this preview");
  expect(requests).toEqual([{ dryRun: true, rootLabel: "default" }]);
  await page.screenshot({ path: "/tmp/studio3359-maintenance-preview.png", fullPage: true });
  page.once("dialog", dialog => dialog.dismiss());
  await page.getByRole("button", { name: "Execute cleanup" }).click();
  expect(requests).toHaveLength(1);
  page.once("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Execute cleanup" }).click();
  await expect(page.getByLabel("Package maintenance report")).toContainText("Execute: Failed");
  await expect(page.getByLabel("Package maintenance report")).toContainText("Observed deleted installs: 1");
  expect(requests[1]).toEqual({ dryRun: false, rootLabel: "default", confirmation: { rootLabel: "default", rootIdentity: preview().rootIdentity, enrollmentEpoch: 7, retention: null } });
  await expect(page.getByRole("button", { name: "Execute cleanup" })).toHaveCount(0);
});

test("a saved retention change clears the plan and requires another preview", async ({ page }) => {
  const state = await setup(page);
  await page.route("**/_elsa/module-management/prune", route => route.fulfill({ json: preview() }));
  await page.route("**/_elsa/module-management/retention-policy", route => {
    expect(route.request().postDataJSON()).toEqual({ keepNewestInactiveVersionsPerPackage: 0 });
    state.retentionPolicy.keepNewestInactiveVersionsPerPackage = 0;
    return route.fulfill({ json: { requiresRestart: false } });
  });
  await page.goto("/package-maintenance.html");
  await page.getByRole("button", { name: "Preview cleanup" }).click();
  await expect(page.getByRole("button", { name: "Execute cleanup" })).toBeEnabled();
  await page.getByLabel("Inactive versions to keep per package").fill("0");
  await expect(page.getByRole("button", { name: "Execute cleanup" })).toBeDisabled();
  await page.getByRole("button", { name: "Save retention" }).click();
  await expect(page.getByLabel("Package maintenance report")).toHaveCount(0);
  await expect(page.getByLabel("Inactive versions to keep per package")).toHaveValue("0");
});

test("older Server count/age policy is visible and read-only", async ({ page }) => {
  await setup(page); await page.goto("/package-maintenance.html");
  await page.getByRole("tab", { name: "Server", exact: true }).click();
  await expect(page.getByText(/Last versions: 3; younger than days: 30/)).toBeVisible();
  await expect(page.getByLabel("Inactive versions to keep per package")).toBeDisabled();
  await expect(page.getByRole("button", { name: "Save retention" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Preview cleanup" })).toBeDisabled();
});
