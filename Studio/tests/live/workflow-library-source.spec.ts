import { readFileSync, writeFileSync } from "node:fs";
import { expect, test, type Page } from "@playwright/test";
import { signIn } from "./support/journey";

// The caller stages a real JsonWorkflowReconciliation catalog and restarts the
// owned host between phases. This journey uses normal authenticated Studio APIs.
const phase = process.env.ELSA_LIBRARY_SOURCE_PHASE;
const receiptPath = process.env.ELSA_LIBRARY_SOURCE_RECEIPT;
const prefix = process.env.ELSA_LIBRARY_NAME ?? "Workflow library delivery";
const definitionId = "workflow-library-source-3500";
test.skip(!process.env.ELSA_STUDIO_URL || !phase || !receiptPath, "Requires an owned source-reconciliation host and receipt path.");
test.setTimeout(180_000);

type Details = { definition: { id: string; versionCount: number; latestVersion: string }; draft: unknown; versions: { id: string; version: string }[] };
type Organization = { folderId: string; tags: { tagId: string; valueId?: string | null }[]; revision: string };
type Receipt = { details: Details; organization: Organization };

async function inspectDetails(page: Page, name: string) {
  const response = page.waitForResponse(value => value.request().method() === "GET" &&
    new URL(value.url()).pathname.endsWith(`/definitions/${definitionId}`));
  await page.getByRole("row").filter({ has: page.getByText(name, { exact: true }) }).getByText(name, { exact: true }).click();
  const result = await (await response).json() as Details;
  // Source-owned definitions have no draft and retain the existing read-only
  // empty-editor surface, whose return action is Browse workflows.
  await page.getByRole("button", { name: "Browse workflows", exact: true }).click();
  return result;
}

test("source reconciliation preserves manual library organization", async ({ page }, testInfo) => {
  await signIn(page);
  await page.goto("/workflows/definitions");
  const name = phase === "initial" ? "Library3500 source payment" : "Library3500 source payment reconciled";
  const row = page.getByRole("row").filter({ has: page.getByText(name, { exact: true }) });
  await expect(row).toBeVisible({ timeout: 60_000 });
  const details = await inspectDetails(page, name);
  expect(details.definition.id).toBe(definitionId);
  expect(details.draft).toBeFalsy();
  expect(details.definition.versionCount).toBe(phase === "initial" ? 1 : 2);
  expect(details.versions).toHaveLength(phase === "initial" ? 1 : 2);
  expect(details.definition.latestVersion).toBe(phase === "initial" ? "1.0.0" : "1.1.0");

  const organizationResponse = page.waitForResponse(value => value.request().method() === "GET" &&
    new URL(value.url()).pathname.endsWith(`/definitions/${definitionId}/organization`));
  await row.getByRole("button", { name: `Actions for ${name}`, exact: true }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const response = await organizationResponse;
  let organization = await response.json() as Organization;
  const dialog = page.getByRole("dialog", { name: `Organize ${name}`, exact: true });
  if (phase === "initial") {
    await dialog.getByRole("button", { name: "Move to folder", exact: true }).click();
    const move = page.getByRole("dialog", { name: "Move workflow definitions", exact: true });
    await move.getByRole("radio", { name: `${prefix} finance`, exact: true }).check();
    await move.getByRole("button", { name: "Move", exact: true }).click();
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText(`${prefix} finance`);
    await dialog.getByRole("checkbox", { name: `${prefix} critical`, exact: true }).check();
    await dialog.getByRole("group", { name: `${prefix} owning team · choose one`, exact: true })
      .getByRole("checkbox", { name: "Finance", exact: true }).check();
    const saved = page.waitForResponse(value => value.request().method() === "PUT" &&
      new URL(value.url()).pathname.endsWith(`/definitions/${definitionId}/organization`));
    await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
    organization = await (await saved).json() as Organization;
    expect(organization.folderId).toBeTruthy();
    expect(organization.tags).toHaveLength(2);
    writeFileSync(receiptPath!, JSON.stringify({ details, organization }, null, 2));
  } else {
    const baseline = JSON.parse(readFileSync(receiptPath!, "utf8")) as Receipt;
    expect(organization.folderId).toBe(baseline.organization.folderId);
    expect(organization.tags).toEqual(baseline.organization.tags);
    expect(organization.revision).toBe(baseline.organization.revision);
    for (const original of baseline.details.versions) expect(details.versions).toContainEqual(original);
    await expect(dialog.getByRole("checkbox", { name: `${prefix} critical`, exact: true })).toBeChecked();
    await expect(dialog.getByRole("group", { name: `${prefix} owning team · choose one`, exact: true })
      .getByRole("checkbox", { name: "Finance", exact: true })).toBeChecked();
  }
  await testInfo.attach(`source-${phase}`, { body: JSON.stringify({ details, organization }, null, 2), contentType: "application/json" });
});
