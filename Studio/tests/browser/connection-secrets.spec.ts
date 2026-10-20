import { expect, test } from "@playwright/test";
import axe from "axe-core";

async function openCreate(page: import("@playwright/test").Page, scenario = "default") {
  await page.goto(`/connection-secrets.html?scenario=${scenario}`);
  const opener = page.getByRole("button", { name: "New connection", exact: true });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  await expect(dialog).toBeVisible();
  return { dialog, opener };
}

for (const viewport of [
  { width: 1440, height: 900 },
  { width: 1280, height: 800 },
  { width: 360, height: 640 }
]) {
  test(`connection create keeps actions visible, body scrollable, and values on errors at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const { dialog, opener } = await openCreate(page, "tall-error");
    const content = dialog.locator(".connections-create-content");
    const cancel = dialog.getByRole("button", { name: "Cancel", exact: true });
    const create = dialog.getByRole("button", { name: "Create", exact: true });
    await expect.poll(() => content.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();

    await dialog.getByLabel("Name", { exact: true }).fill("preserved.connection");
    await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
    await dialog.getByLabel("Extra setting 12", { exact: true }).fill("preserved setting");
    await dialog.getByLabel("Search Secrets for Token", { exact: true }).fill("customer");
    const select = dialog.getByLabel("Existing Secret for Token", { exact: true });
    const option = select.getByRole("option", { name: /Customer API token/ });
    await expect(option).toHaveCount(1);
    await select.selectOption((await option.getAttribute("value"))!);

    await dialog.getByLabel("Extra setting 12", { exact: true }).scrollIntoViewIfNeeded();
    await expect.poll(() => content.evaluate(element => element.scrollTop > 0)).toBe(true);
    expect(await dialog.evaluate(element => element.scrollTop)).toBe(0);
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();

    await page.setViewportSize({ ...viewport, height: viewport.height - 24 });
    await expect(dialog.getByLabel("Name", { exact: true })).toHaveValue("preserved.connection");
    await expect(dialog.getByLabel("Base URL", { exact: true })).toHaveValue("https://api.example");
    await expect(dialog.getByLabel("Extra setting 12", { exact: true })).toHaveValue("preserved setting");
    await expect(create).toBeInViewport();
    await create.click();

    const alert = dialog.getByRole("alert");
    await expect(alert).toContainText("Fixture connection create failed.");
    await expect(alert).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect.poll(() => dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    await page.keyboard.press("Tab");
    const name = dialog.getByLabel("Name", { exact: true });
    await expect(name).toBeFocused();
    await name.scrollIntoViewIfNeeded();
    await expect(name).toBeInViewport();
    await expect(name).toHaveValue("preserved.connection");
    await expect(dialog.getByLabel("Extra setting 12", { exact: true })).toHaveValue("preserved setting");
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();
    await cancel.click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
  });
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
    await select.scrollIntoViewIfNeeded();
    await select.selectOption((await option.getAttribute("value"))!);
    await expect(select).toBeInViewport({ ratio: 1 });
    await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeInViewport();
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
