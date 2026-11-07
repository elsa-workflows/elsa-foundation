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
  await capture(page, testInfo, "3979-unpublished-warning");
});
