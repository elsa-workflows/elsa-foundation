import { test, expect, type Page, type TestInfo } from "@playwright/test";
import { allThemeModes, builtInThemeDefinitions } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { workspaceStyles } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles";

for (const theme of builtInThemeDefinitions) for (const mode of allThemeModes) {
  test(`Outlined ${theme.id} ${mode}`, async ({ page }, info) => {
    await page.goto(`/outlined-fields.html?theme=${theme.id}&mode=${mode}`);
    await page.getByLabel("Focused", { exact: true }).focus();
    await expect(page.getByLabel("Focused", { exact: true })).toBeFocused();
    const frames = page.getByRole("region", { name: "Field states", exact: true }).locator(".studio-form-control");
    await expect(frames).toHaveCount(20);
    const ownedEditor = page.getByLabel("Host actions", { exact: true });
    await expect(ownedEditor).toHaveValue("Opaque editor value");
    const ownedFrame = frames.filter({ has: ownedEditor });
    await expect(ownedFrame.getByRole("button", { name: "Editor action", exact: true })).toBeVisible();
    await expect(ownedFrame.getByRole("button", { name: "Host action", exact: true })).toBeVisible();
    const geometry = await frames.evaluateAll(elements => elements.map(element => {
      const box = element.getBoundingClientRect(), legend = element.querySelector("legend")!.getBoundingClientRect(), style = getComputedStyle(element);
      return { border: style.borderLeftWidth, overflows: element.scrollWidth > element.clientWidth + 1, labelFits: legend.right <= box.right + 1 && legend.left >= box.left - 1 };
    }));
    expect(geometry.every(item => item.border === "1px" && !item.overflows && item.labelFits)).toBe(true);
    expect(await page.locator(".studio-form-control [data-studio-field-control]").evaluateAll(elements => elements.every(element => getComputedStyle(element).borderLeftWidth === "0px"))).toBe(true);
    const record = page.getByRole("region", { name: "Completed response", exact: true }).getByLabel("Note", { exact: true });
    await expect(record).toBeDisabled();
    await expect(record).toHaveValue("Reviewed purchase request");
    await expect(record).toHaveCSS("resize", "none");
    expect(await record.evaluate(element => { const style = getComputedStyle(element.closest("fieldset")!); return { border: style.borderTopWidth, opacity: style.opacity }; })).toEqual({ border: "0px", opacity: "1" });
    await expect(page.getByLabel("Disabled", { exact: true })).toBeDisabled();
    expect(await page.locator(".studio-form-control [data-studio-field-control]:disabled").evaluateAll(elements => elements.every(element => getComputedStyle(element).opacity === "1"))).toBe(true);
    await expect(page.getByLabel("Read only", { exact: true })).toHaveAttribute("readonly", "");
    await expect(page.getByLabel("Invalid", { exact: true })).toHaveAttribute("aria-invalid", "true");
    await capture(page, info, `${theme.id}-${mode}-states`);
    const draft = page.getByLabel("Invalid", { exact: true });
    await draft.fill("Recovered draft"); await expect(draft).not.toHaveAttribute("aria-invalid", "true");
    await page.getByRole("button", { name: "Show value" }).click();
    await expect(page.getByLabel("Secret value", { exact: true })).toHaveValue("protected draft");
    await expect(page.getByLabel("Secret value", { exact: true })).toHaveAttribute("type", "text");
    await page.getByLabel("Focused", { exact: true }).focus();
    await capture(page, info, `${theme.id}-${mode}`);
  });
}
for (const style of workspaceStyles) for (const mode of ["light", "dark"] as const) {
  test(`Outlined ${style.id} narrow ${mode}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto(`/outlined-fields.html?theme=classic&mode=${mode}&style=${style.id}&rtl=1`);
    await page.evaluate(() => { document.documentElement.style.setProperty("--studio-body-size", "28px"); document.documentElement.style.setProperty("--studio-small-size", "24px"); });
    await expect(page.getByLabel("Populated", { exact: true })).toHaveValue("Draft value");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await capture(page, info, `${style.id}-${mode}-narrow-rtl`);
  });
}
test("Outlined forced colours preserve field and keyboard focus", async ({ page }, info) => {
  await page.emulateMedia({ forcedColors: "active", reducedMotion: "reduce" });
  await page.goto("/outlined-fields.html");
  const field = page.getByLabel("Populated", { exact: true }); await field.focus();
  await expect(field).toBeFocused();
  expect(await field.evaluate(element => getComputedStyle(element.closest("fieldset")!).outlineStyle)).toBe("solid");
  await capture(page, info, "forced-colours");
});

async function capture(page: Page, info: TestInfo, name: string) {
  const path = info.outputPath(`${name.replace(/[^a-zA-Z0-9_-]/g, "-")}.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(name, { path, contentType: "image/png" });
}
