import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`contract tabs isolate collections and retain changes at ${width}px ${theme}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-authoring&provider=agent&theme=${theme}`);
    const tabs = page.getByRole("tablist", { name: "Activity Definition authoring views" });
    const contractEditor = page.locator(".ad-contract-editor");
    await expect(tabs.getByRole("tab")).toHaveText(["Designer", "Inputs (0)", "Outputs (0)", "Outcomes (1)"]);
    for (const kind of ["input", "output"] as const) {
      await tabs.getByRole("tab", { name: new RegExp(`^${kind === "input" ? "Inputs" : "Outputs"} \\(0\\)$`) }).click();
      await expect(contractEditor.locator(`.wf-properties-empty-row`)).toContainText(`No ${kind}s defined.`);
      const [empty, table] = await Promise.all([
        contractEditor.locator(".wf-properties-empty-row").boundingBox(),
        contractEditor.locator(".wf-properties-table").boundingBox()
      ]);
      expect(empty).not.toBeNull();
      expect(table).not.toBeNull();
      expect(empty!.width).toBeGreaterThanOrEqual(table!.width - 2);
      await page.addScriptTag({ content: axe.source });
      const violations = await page.evaluate(async () => {
        const engine = (window as unknown as { axe: typeof axe }).axe;
        return (await engine.run(document.querySelector(".ad-public-interface-view")!)).violations.map(item => item.id);
      });
      expect(violations).toEqual([]);
      await page.getByRole("button", { name: `Add ${kind}`, exact: true }).click();
      const name = page.getByRole("textbox", { name: `${kind === "input" ? "Input" : "Output"} name`, exact: true });
      await expect(name).toBeFocused();
      await name.fill(kind === "input" ? "Text" : "Suggestions");
      await name.press("Enter");
      const referenceKey = kind === "input" ? "text" : "suggestions";
      const row = contractEditor.locator(`tr[data-contract-row-kind="${kind}"][data-contract-reference-key="${referenceKey}"]`);
      await expect(row.locator("input[data-contract-field='name']")).toHaveValue(kind === "input" ? "Text" : "Suggestions");
      const toggle = row.locator(".ad-contract-details-toggle");
      if (await toggle.getAttribute("aria-expanded") !== "true") await toggle.click();
      const pane = contractEditor.locator(`.ad-contract-workbench-member[data-contract-kind="${kind}"][data-contract-reference-key="${referenceKey}"]`);
      await expect(pane).toBeVisible();
      expect(await contractEditor.locator(".ad-contract-detail-row[hidden]").evaluateAll(rows => rows.every(row => getComputedStyle(row).display === "none"))).toBe(true);
      await pane.getByRole("textbox", { name: /^Description/ }).fill(`Saved ${kind} description.`);
      await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`${kind}s.png`), fullPage: true });
    }
    await tabs.getByRole("tab", { name: /^Outcomes/ }).click();
    const done = contractEditor.locator("tr[data-contract-row-kind='outcome'][data-contract-reference-key='done']");
    const emitted = done.getByRole("checkbox", { name: "Done emitted" });
    await expect(emitted).toBeChecked();
    await expect(emitted).toBeDisabled();
    await expect(done.locator("input[data-contract-field='name']")).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("outcomes.png"), fullPage: true });
    await page.reload();
    for (const [tab, kind] of [["Inputs", "input"], ["Outputs", "output"]] as const) {
      await tabs.getByRole("tab", { name: new RegExp(`^${tab} \\(1\\)$`) }).click();
      const referenceKey = kind === "input" ? "text" : "suggestions";
      const row = contractEditor.locator(`tr[data-contract-row-kind="${kind}"][data-contract-reference-key="${referenceKey}"]`);
      const toggle = row.locator(".ad-contract-details-toggle");
      if (await toggle.getAttribute("aria-expanded") !== "true") await toggle.click();
      const pane = contractEditor.locator(`.ad-contract-workbench-member[data-contract-kind="${kind}"][data-contract-reference-key="${referenceKey}"]`);
      await expect(pane.getByRole("textbox", { name: /^Description/ })).toHaveValue(`Saved ${kind} description.`);
    }
  });
}

test("invalid defaults survive empty tabs and keep revision-sensitive actions blocked", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&contract=outcome-only");
  const tabs = page.locator(".ad-graph-canvas .ad-authoring-view-tabs");
  await tabs.getByRole("tab", { name: /^Inputs/ }).click();
  await page.getByRole("button", { name: "Add input", exact: true }).click();
  await page.getByRole("textbox", { name: "Input name", exact: true }).fill("Quantity");
  await page.getByRole("textbox", { name: "Input name", exact: true }).press("Enter");
  const quantityRow = page.locator(".ad-contract-editor tr[data-contract-row-kind='input'][data-contract-reference-key='quantity']");
  const detailsToggle = quantityRow.locator(".ad-contract-details-toggle");
  if (await detailsToggle.getAttribute("aria-expanded") !== "true") await detailsToggle.click();
  const quantity = page.locator(".ad-contract-workbench-member[data-contract-kind='input'][data-contract-reference-key='quantity']");
  await quantityRow.getByRole("combobox", { name: "Type", exact: true }).selectOption("Decimal");
  await quantity.getByRole("combobox", { name: /^Default/ }).selectOption("literal");
  const value = quantity.getByRole("textbox", { name: "Value", exact: true });
  await value.fill("2x");
  await expect(value).toHaveAttribute("aria-invalid", "true");
  await tabs.getByRole("tab", { name: /^Outputs/ }).click();
  await expect(page.locator(".ad-contract-editor .wf-properties-empty-row")).toContainText("No outputs defined.");
  await expect(page.getByRole("button", { name: "Test Run", exact: true })).toBeDisabled();
  await tabs.getByRole("tab", { name: /^Outcomes/ }).click();
  await tabs.getByRole("tab", { name: /^Inputs/ }).click();
  await expect(value).toHaveValue("2x");
  await expect(value).toHaveAttribute("aria-invalid", "true");
  await value.fill("2");
  await quantity.getByRole("button", { name: "Apply default", exact: true }).click();
  await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved");
  await expect(page.getByRole("button", { name: "Test Run", exact: true })).toBeEnabled();
});

test("diagnostics select each matching collection and focus its exact field", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&contract-diagnostics");
  for (const section of ["inputs", "outputs", "outcomes"]) {
    const diagnostic = page.getByRole("button", { name: `Focus contract.${section}`, exact: true });
    await diagnostic.click();
    await expect(page.locator(`.ad-authoring-view-tabs [data-tab-id=${section}]`)).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(`[data-contract-field='name']:focus`)).toBeFocused();
    await page.getByRole("button", { name: "Return to diagnostic", exact: true }).click();
    await expect(diagnostic).toBeFocused();
  }
});

test("keyboard navigation follows the four tabs and their labelled panel", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&provider=agent");
  const tabs = page.getByRole("tablist", { name: "Activity Definition authoring views" });
  await tabs.getByRole("tab", { name: "Designer", exact: true }).focus();
  for (const section of ["inputs", "outputs", "outcomes"]) {
    await page.keyboard.press("ArrowRight");
    const tab = page.locator(`.ad-authoring-view-tabs [data-tab-id=${section}]`);
    await expect(tab).toBeFocused();
    await expect(tab).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(`#${await tab.getAttribute("aria-controls")}`)).toBeVisible();
  }
  await page.keyboard.press("Home");
  await expect(tabs.getByRole("tab", { name: "Designer", exact: true })).toBeFocused();
  await page.keyboard.press("End");
  await expect(page.locator(".ad-authoring-view-tabs [data-tab-id=outcomes]")).toBeFocused();
});
