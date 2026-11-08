import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import { expect, test, type Page, type TestInfo } from "@playwright/test";

const evidenceDirectory = process.env.DECISION_INPUTS_EVIDENCE_DIR;

async function capture(page: Page, testInfo: TestInfo, name: string) {
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`), fullPage: true, animations: "disabled" });
  if (!evidenceDirectory) return;
  await mkdir(evidenceDirectory, { recursive: true });
  await page.screenshot({ path: resolve(evidenceDirectory, `${name}.png`), fullPage: true, animations: "disabled" });
}

async function openInspector(page: Page, query = "") {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`/?mode=decision-inputs&theme=light${query}`);
  return page.getByRole("complementary", { name: "Activity inspector" });
}

async function refreshForeground(page: Page) {
  await page.evaluate(() => {
    window.dispatchEvent(new Event("focus"));
    document.dispatchEvent(new Event("visibilitychange"));
  });
}

test("generic dictionary JSON remains the before surface without the dedicated editor", async ({ page }, testInfo) => {
  const inspector = await openInspector(page, "&before=1");
  await expect(inspector.getByRole("heading", { name: "Evaluate Decision" })).toBeVisible();
  await expect(inspector.locator(".wf-dictionary-editor")).toBeVisible();
  await expect(inspector.getByText("No entries")).toBeVisible();
  await inspector.getByRole("button", { name: /Open expanded editor/ }).click();
  const dialog = page.getByRole("dialog", { name: "Inputs" });
  await dialog.getByRole("tab", { name: "JSON" }).click();
  await expect(dialog.getByRole("textbox", { name: "Inputs dictionary JSON" })).toHaveValue(/^\{\s*\}\s*$/);
  await expect(dialog.getByRole("button", { name: "Close", exact: true })).toBeVisible();
  await expect(dialog.getByText("Changes update the draft immediately.")).toBeVisible();
  await capture(page, testInfo, "3979-before-inputs-json");
});

test("selecting a published decision seeds table inputs without tagged JSON", async ({ page }, testInfo) => {
  const inspector = await openInspector(page);
  await expect(inspector.getByLabel("Business decision", { exact: true })).toHaveValue("approval");
  await expect(inspector.getByLabel("amount", { exact: true })).toBeVisible();
  await expect(inspector.getByLabel("customerCategory", { exact: true })).toBeVisible();
  await expect(inspector.locator(".wf-dictionary-editor")).toHaveCount(0);
  await inspector.getByLabel("amount", { exact: true }).fill("950");
  await inspector.getByLabel("customerCategory", { exact: true }).fill("Standard");
  const state = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
  expect(state.Inputs.expression.value).toEqual({
    amount: { kind: "number", value: "950" },
    customerCategory: { kind: "string", value: "Standard" }
  });
  await capture(page, testInfo, "3979-after-seeded-fields");
  await inspector.getByText("Advanced JSON").click();
  await expect(inspector.getByRole("textbox", { name: "Decision inputs JSON" })).toContainText('"kind": "number"');
  await capture(page, testInfo, "3979-after-advanced-json");
});

test("an unpublished @latest pin explains that Review & publish cannot freeze the draft", async ({ page }, testInfo) => {
  const inspector = await openInspector(page, "&unpublished=1");
  await expect(inspector.getByRole("alert").first()).toContainText("no published version");
  await expect(inspector.getByText("Review & publish cannot pin a draft with @latest.")).toBeVisible();
  await expect(inspector.getByLabel("amount", { exact: true })).toBeVisible();
  await expect(page.locator(".wf-validation")).toContainText("Review & publish cannot pin a draft with @latest.");
  await expect(page.getByText("No validation errors")).toHaveCount(0);
  await capture(page, testInfo, "3979-unpublished-warning");
});

test("switching tables keeps extras until they are removed", async ({ page }, testInfo) => {
  const inspector = await openInspector(page);
  await inspector.getByLabel("amount", { exact: true }).fill("950");
  await inspector.getByLabel("customerCategory", { exact: true }).fill("Standard");
  await inspector.getByLabel("Business decision", { exact: true }).selectOption({ label: "Scoring" });
  await expect(inspector.getByText("InputMembership")).toBeVisible();
  await expect(page.locator(".wf-validation")).toContainText("InputMembership");
  await expect(page.getByText("No validation errors")).toHaveCount(0);
  await expect(inspector.getByLabel("score", { exact: true })).toBeVisible();
  await expect(inspector.getByLabel("amount (not in this table)", { exact: true })).toBeVisible();
  const state = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
  expect(state.Inputs.expression.value).toEqual({
    amount: { kind: "number", value: "950" },
    customerCategory: { kind: "string", value: "Standard" },
    score: { kind: "number", value: "0" }
  });
  await capture(page, testInfo, "3979-table-switch-extras");
  await inspector.getByRole("button", { name: "Remove inputs not in this table", exact: true }).click();
  const cleaned = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
  expect(cleaned.Inputs.expression.value).toEqual({ score: { kind: "number", value: "0" } });
  await expect(inspector.getByLabel("amount", { exact: true })).toHaveCount(0);
});

test("a type switch keeps the stored kind until an edit writes the displayed type", async ({ page }, testInfo) => {
  const inspector = await openInspector(page);
  await inspector.getByLabel("amount", { exact: true }).fill("950");
  await inspector.getByLabel("Business decision version", { exact: true }).selectOption({ label: "Version 2" });
  await expect(inspector.getByText("InputTypeMismatch")).toBeVisible();
  await expect(page.locator(".wf-validation")).toContainText("InputTypeMismatch");
  await expect(page.getByText("No validation errors")).toHaveCount(0);
  const mismatched = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
  expect(mismatched.Inputs.expression.value.amount).toEqual({ kind: "number", value: "950" });
  await capture(page, testInfo, "3979-type-switch-mismatch");
  await inspector.getByLabel("amount", { exact: true }).fill("951");
  const written = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
  expect(written.Inputs.expression.value.amount).toEqual({ kind: "string", value: "951" });
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByTestId("review-status")).toHaveText("ready");
});

test("cleared JSON reseeds and invalid JSON blur restores stored text", async ({ page }) => {
  const inspector = await openInspector(page);
  await inspector.getByLabel("amount", { exact: true }).fill("950");
  await inspector.getByText("Advanced JSON").click();
  const json = inspector.getByRole("textbox", { name: "Decision inputs JSON" });
  await json.fill("{}");
  await expect(inspector.getByLabel("amount", { exact: true })).toHaveValue("0");
  const reseeded = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
  expect(reseeded.Inputs.expression.value.amount).toEqual({ kind: "number", value: "0" });
  await inspector.getByLabel("amount", { exact: true }).fill("950");
  if (!await json.isVisible()) await inspector.getByText("Advanced JSON").click();
  await json.fill("{");
  await expect(inspector.getByText("JSON is not valid.")).toBeVisible();
  await expect(json).toHaveAttribute("aria-invalid", "true");
  await json.blur();
  await expect(inspector.getByText("JSON is not valid.")).toHaveCount(0);
  await expect(json).not.toHaveAttribute("aria-invalid", "true");
  await expect(json).toContainText('"950"');
});

test("an empty required number is not Ready on the validation surface", async ({ page }, testInfo) => {
  const inspector = await openInspector(page);
  await inspector.getByLabel("amount", { exact: true }).fill("");
  await expect(inspector.getByText("InvalidValue")).toBeVisible();
  await expect(page.locator(".wf-validation")).toContainText("InvalidValue");
  await expect(page.getByText("No validation errors")).toHaveCount(0);
  await capture(page, testInfo, "3979-empty-amount-invalid");
});

test("a version-fetch failure is not labeled unpublished", async ({ page }, testInfo) => {
  const inspector = await openInspector(page, "&fetchError=1");
  await expect(inspector.getByRole("alert").filter({ hasText: "versions unavailable" }).first()).toBeVisible();
  await expect(inspector.getByText("no published version")).toHaveCount(0);
  await capture(page, testInfo, "3979-version-fetch-error");
});

test("Review stays blocked until an unpublished @latest table is published in the same session", async ({ page }, testInfo) => {
  const inspector = await openInspector(page, "&unpublished=1");
  await expect(page.locator(".wf-validation")).toContainText("Review & publish cannot pin a draft with @latest.");
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  const review = page.getByRole("dialog", { name: "Review and publish" });
  await expect(review).toContainText("no published version");
  await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeDisabled();
  await review.getByRole("button", { name: "Cancel" }).click();
  await inspector.getByRole("button", { name: "Publish decision", exact: true }).click();
  await refreshForeground(page);
  await expect(inspector.getByText("no published version")).toHaveCount(0);
  await expect(inspector.getByLabel("Business decision version", { exact: true }).locator("option", { hasText: "Version 1" })).toHaveCount(1);
  await inspector.getByLabel("amount", { exact: true }).fill("950");
  await inspector.getByLabel("customerCategory", { exact: true }).fill("Standard");
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Review and publish" })).toContainText("review");
  await expect(page.getByTestId("review-status")).toHaveText("ready");
  await expect(page.getByRole("dialog", { name: "Review and publish" }).getByRole("button", { name: "Publish", exact: true })).toBeEnabled();
  await capture(page, testInfo, "3979-publish-after-unpublished-review");
});

test("Review re-resolves @latest after a new published table adds an input", async ({ page }, testInfo) => {
  const inspector = await openInspector(page);
  await inspector.getByLabel("amount", { exact: true }).fill("950");
  await inspector.getByLabel("customerCategory", { exact: true }).fill("Standard");
  await inspector.getByLabel("Business decision version", { exact: true }).selectOption({ value: "latest" });
  await expect(inspector.getByTestId("authored-state")).toContainText("approval@latest");
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByTestId("review-status")).toHaveText("ready");
  await page.getByRole("dialog", { name: "Review and publish" }).getByRole("button", { name: "Cancel" }).click();
  await inspector.getByRole("button", { name: "Publish table with region", exact: true }).click();
  await expect(inspector.getByTestId("backend-state")).toContainText('"latestAddsRegion":true');
  await refreshForeground(page);
  await expect(inspector.getByLabel("Business decision version", { exact: true }).locator("option", { hasText: "Version 3" })).toHaveCount(1);
  await expect(inspector.getByLabel("region", { exact: true })).toBeVisible();
  await expect(inspector.getByLabel("amount value kind", { exact: true })).toHaveValue("number");
  const afterRefresh = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
  expect(afterRefresh.Inputs.expression.value.region).toBeUndefined();
  expect(afterRefresh.Inputs.expression.value.amount).toEqual({ kind: "number", value: "950" });
  await expect(page.locator(".wf-validation")).toContainText("MissingInput");
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Review and publish" })).toContainText("MissingInput");
  await expect(page.getByTestId("review-status")).toHaveText("blocked");
  await expect(page.getByRole("dialog", { name: "Review and publish" }).getByRole("button", { name: "Publish", exact: true })).toBeDisabled();
  await capture(page, testInfo, "3979-latest-new-version-missing-input");
});

test("Review retries after a transient table lookup failure", async ({ page }, testInfo) => {
  const inspector = await openInspector(page, "&failOnce=1");
  await expect(page.locator(".wf-validation")).toContainText("temporary lookup failed");
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Review and publish" })).toContainText("temporary lookup failed");
  await expect(page.getByTestId("review-status")).toHaveText("blocked");
  await page.getByRole("dialog", { name: "Review and publish" }).getByRole("button", { name: "Cancel" }).click();
  await inspector.getByRole("button", { name: "Recover lookup", exact: true }).click();
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByTestId("review-status")).toHaveText("ready");
  await expect(page.getByRole("dialog", { name: "Review and publish" }).getByRole("button", { name: "Publish", exact: true })).toBeEnabled();
  await capture(page, testInfo, "3979-transient-retry-review");
});

test("an unrelated custom activity does not look up a decision table", async ({ page }, testInfo) => {
  const inspector = await openInspector(page, "&unrelated=1");
  await expect(inspector.getByRole("heading", { name: "Notify" })).toBeVisible();
  await expect(inspector.getByTestId("authored-state")).toContainText("ops@example.com");
  await expect(page.getByText("No validation errors")).toBeVisible();
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByTestId("review-status")).toHaveText("ready");
  await expect(page.getByRole("dialog", { name: "Review and publish" })).not.toContainText("InputMembership");
  await expect(page.getByRole("dialog", { name: "Review and publish" })).not.toContainText("Unavailable");
  await capture(page, testInfo, "3979-unrelated-custom-activity");
});

test("closing the expanded Inputs editor keeps the inline InvalidValue row error", async ({ page }, testInfo) => {
  const inspector = await openInspector(page);
  await inspector.getByLabel("amount", { exact: true }).fill("");
  await expect(inspector.getByText("InvalidValue")).toBeVisible();
  await inspector.getByRole("button", { name: "Open expanded Inputs editor" }).click();
  const dialog = page.getByRole("dialog", { name: "Inputs" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText("InvalidValue")).toBeVisible();
  await dialog.getByRole("button", { name: "Close Inputs editor" }).click();
  await expect(dialog).toHaveCount(0);
  await expect(inspector.getByText("InvalidValue")).toBeVisible();
  await expect(page.locator(".wf-validation")).toContainText("InvalidValue");
  await inspector.getByRole("button", { name: "Review & publish", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Review and publish" })).toContainText("InvalidValue");
  await expect(page.getByTestId("review-status")).toHaveText("blocked");
  await capture(page, testInfo, "3979-expanded-close-keeps-invalid");
});
