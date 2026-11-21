import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`contract tabs isolate collections and retain changes at ${width}px ${theme}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-authoring&provider=agent&theme=${theme}`);
    const tabs = page.getByRole("tablist", { name: "Activity Definition authoring views" });
    await expect(tabs.getByRole("tab")).toHaveText(["Designer", "Inputs (0)", "Outputs (0)", "Outcomes (1)"]);
    for (const kind of ["input", "output"] as const) {
      await tabs.getByRole("tab", { name: new RegExp(`^${kind === "input" ? "Inputs" : "Outputs"} \\(0\\)$`) }).click();
      await expect(page.getByRole("region", { name: "Done", exact: true })).toHaveCount(0);
      await expect(page.getByText(`No ${kind}s yet`, { exact: true })).toBeVisible();
      const [empty, workbench] = await page.locator(".ad-contract-workbench-empty, .ad-contract-workbench").evaluateAll(elements => {
        const empty = elements.find(element => element.classList.contains("ad-contract-workbench-empty"))!;
        const workbench = elements.find(element => element.classList.contains("ad-contract-workbench"))!;
        return [empty.getBoundingClientRect().width, workbench.getBoundingClientRect().width];
      });
      expect(empty).toBeGreaterThanOrEqual(workbench - 2);
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
      const pane = page.getByRole("region", { name: kind === "input" ? "Text" : "Suggestions", exact: true });
      await expect(pane.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
      await pane.getByRole("textbox", { name: /^Description/ }).fill(`Saved ${kind} description.`);
      await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`${kind}s.png`), fullPage: true });
    }
    await tabs.getByRole("tab", { name: /^Outcomes/ }).click();
    await expect(page.getByRole("region", { name: "Text", exact: true })).toHaveCount(0);
    const done = page.getByRole("region", { name: "Done", exact: true });
    await expect(done.getByText("Emitted", { exact: true })).toBeVisible();
    await expect(done.getByRole("textbox", { name: "Name", exact: true })).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("outcomes.png"), fullPage: true });
    await page.reload();
    for (const [tab, name, kind] of [["Inputs", "Text", "input"], ["Outputs", "Suggestions", "output"]]) {
      await tabs.getByRole("tab", { name: new RegExp(`^${tab} \\(1\\)$`) }).click();
      await expect(page.getByRole("region", { name, exact: true }).getByRole("textbox", { name: /^Description/ })).toHaveValue(`Saved ${kind} description.`);
    }
  });
}

test("invalid defaults survive empty tabs and keep revision-sensitive actions blocked", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&contract=outcome-only");
  await page.getByRole("tab", { name: /^Inputs/ }).click();
  await page.getByRole("button", { name: "Add input", exact: true }).click();
  await page.getByRole("textbox", { name: "Input name", exact: true }).fill("Quantity");
  await page.getByRole("textbox", { name: "Input name", exact: true }).press("Enter");
  const quantity = page.getByRole("region", { name: "Quantity", exact: true });
  await quantity.getByRole("combobox", { name: "Type", exact: true }).selectOption("Decimal");
  await quantity.getByRole("combobox", { name: /^Default/ }).selectOption("literal");
  const value = quantity.getByRole("textbox", { name: "Value", exact: true });
  await value.fill("2x");
  await expect(value).toHaveAttribute("aria-invalid", "true");
  await page.getByRole("tab", { name: /^Outputs/ }).click();
  await expect(page.getByText("No outputs yet", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Test Run", exact: true })).toBeDisabled();
  await page.getByRole("tab", { name: /^Outcomes/ }).click();
  await page.getByRole("tab", { name: /^Inputs/ }).click();
  await expect(value).toHaveValue("2x");
  await expect(value).toHaveAttribute("aria-invalid", "true");
  await value.fill("2");
  await quantity.getByRole("button", { name: "Apply default", exact: true }).click();
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await expect(page.getByRole("button", { name: "Test Run", exact: true })).toBeEnabled();
});

test("diagnostics select each matching collection and focus its exact field", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&contract-diagnostics");
  for (const section of ["inputs", "outputs", "outcomes"]) {
    const diagnostic = page.getByRole("button", { name: `Focus contract.${section}`, exact: true });
    await diagnostic.click();
    await expect(page.locator(`.ad-authoring-view-tabs [data-tab-id=${section}]`)).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(`[data-contract-kind=${section.slice(0, -1)}][data-contract-selected=true] [data-contract-field=name]`)).toBeFocused();
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
