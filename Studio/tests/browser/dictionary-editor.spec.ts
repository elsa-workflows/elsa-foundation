import { expect, test } from "@playwright/test";

test("dictionary entries support responsive inline and expanded editing", async ({ page }, info) => {
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.setViewportSize({ width: 1100, height: 760 });
  await page.goto("/?theme=light&mode=dictionary");

  const inspector = page.getByRole("complementary", { name: "Activity inspector" });
  await expect(inspector.getByRole("table", { name: "Headers entries" })).toBeVisible();
  await expect(inspector.getByText("1 more entry")).toBeVisible();

  const add = inspector.getByRole("button", { name: /Add entry/ });
  await add.click();
  const keys = inspector.getByRole("textbox", { name: /Header name/ });
  await expect(keys).toHaveCount(6);
  await expect(keys.last()).toBeFocused();
  await keys.last().fill("X-New");
  await page.keyboard.press("Enter");
  await page.keyboard.type("new value");

  const expand = inspector.getByRole("button", { name: /Open expanded editor/ });
  await expand.click();
  const dialog = page.getByRole("dialog", { name: "Headers" });
  await expect(dialog).toBeVisible();
  const tableTab = dialog.getByRole("tab", { name: "Table" });
  await expect(tableTab).toHaveAttribute("aria-selected", "true");
  await expect(tableTab).toBeFocused();

  const filter = dialog.getByRole("searchbox", { name: "Filter Headers entries" });
  await filter.fill("correlation");
  await expect(dialog.getByRole("textbox", { name: "Header name 2" })).toHaveValue("X-Correlation-Id");

  await dialog.getByRole("tab", { name: "JSON" }).click();
  const json = dialog.getByRole("textbox", { name: "Headers dictionary JSON" });
  const jsonValue = await json.inputValue();
  expect(jsonValue).toContain("X-Correlation-Id");
  const copiedJson = jsonValue.replace("no-cache", "max-age=0");
  await json.fill(copiedJson);
  await dialog.getByRole("button", { name: /Copy JSON/ }).click();
  const confirmation = page.locator(".studio-toast").getByText("JSON copied", { exact: true });
  await expect(confirmation).toBeVisible();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(copiedJson);

  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
  await expect(expand).toBeFocused();
  await expect(confirmation).toBeVisible();
  await page.screenshot({ path: info.outputPath("dictionary-json-copied.png"), fullPage: true });
});

test("dictionary rows stack in a narrow inspector", async ({ page }) => {
  await page.setViewportSize({ width: 420, height: 760 });
  await page.goto("/?theme=dark&mode=dictionary");

  const row = page.locator(".wf-dictionary-row").first();
  const key = row.locator(".wf-dictionary-cell.key");
  const value = row.locator(".wf-dictionary-cell.value");
  const keyBox = await key.boundingBox();
  const valueBox = await value.boundingBox();
  expect(keyBox).not.toBeNull();
  expect(valueBox).not.toBeNull();
  expect(valueBox!.y).toBeGreaterThan(keyBox!.y);
  await expect(row.getByText("Header name", { exact: true })).toBeVisible();
  await expect(row.getByText("Header value", { exact: true })).toBeVisible();
});
