import { expect, test } from "@playwright/test";
import axe from "axe-core";

async function openCreate(page: import("@playwright/test").Page) {
  await page.goto("/connection-secrets.html");
  const opener = page.getByRole("button", { name: "New connection", exact: true });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  await expect(dialog).toBeVisible();
  return { dialog, opener };
}

for (const width of [320, 768, 1024, 1440]) {
  test(`existing Secret creation stays usable at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const { dialog, opener } = await openCreate(page);
    await dialog.getByLabel("Name", { exact: true }).fill("customer-api");
    await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
    await dialog.getByLabel("Search Secrets for Token", { exact: true }).fill("customer");
    const select = dialog.getByLabel("Existing Secret for Token", { exact: true });
    const option = select.getByRole("option", { name: /Customer API token/ });
    await expect(option).toHaveCount(1);
    await select.selectOption((await option.getAttribute("value"))!);
    await expect(select).toBeInViewport({ ratio: 1 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await dialog.getByRole("button", { name: "Create", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
    const payload = JSON.parse(await page.getByTestId("last-connection-payload").innerText());
    expect(payload.secretFields).toEqual({ token: { sourceKind: "secret", existingSecretName: "customer-api" } });
  });
}

test("dialog traps keyboard focus, restores opener, and passes automated accessibility checks", async ({ page }) => {
  const { dialog, opener } = await openCreate(page);
  await expect(dialog.getByLabel("Name", { exact: true })).toBeFocused();
  await dialog.getByRole("button", { name: "Cancel", exact: true }).focus();
  await page.keyboard.press("Tab");
  expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const root = document.querySelector('[role="dialog"]')!;
    const result = await (window as Window & { axe: typeof axe }).axe.run(root);
    return result.violations.filter(item => item.impact === "critical" || item.impact === "serious").map(item => ({ id: item.id, targets: item.nodes.map(node => node.target) }));
  });
  expect(violations).toEqual([]);
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(opener).toBeFocused();
});
