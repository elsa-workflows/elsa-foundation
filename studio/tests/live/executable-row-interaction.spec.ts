import { expect, test } from "@playwright/test";
import { signIn } from "./support/journey";

test.skip(!process.env.ELSA_STUDIO_URL, "Requires a running studio/Workbench pair with a published executable.");
test.use({ ignoreHTTPSErrors: true });

for (const mode of ["light", "dark"] as const) {
  test(`executable rows give consistent feedback and keep their actions separate in ${mode}`, async ({ page }, info) => {
    await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), mode);
    await signIn(page);
    await page.locator(".sidebar").getByRole("link", { name: "Workflows", exact: true }).click();
    await page.getByRole("navigation", { name: "Workflows views", exact: true }).getByRole("link", { name: "Executables", exact: true }).click();
    const table = page.getByRole("table", { name: "Workflow executables", exact: true });
    const row = table.getByRole("row", { name: /^Inspect executable / }).first();
    const source = row.locator(".wf-executable-workflow-name").first();
    const identifiers = row.locator(".wf-executable-identifiers");
    await expect(source).toBeVisible();
    await expect(source).toHaveAttribute("title", /^Open source workflow: /);
    const listUrl = page.url();

    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: 900 });
      await page.mouse.move(0, 0);
      const resting = await row.evaluate(element => getComputedStyle(element).backgroundColor);
      await source.hover();
      const hovered = await row.evaluate(element => getComputedStyle(element).backgroundColor);
      expect(hovered).not.toBe(resting);
      await expect(source).toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
      expect((await source.boundingBox())!.width).toBeLessThan((await row.locator(".wf-executable-name").boundingBox())!.width);
      await page.screenshot({ path: info.outputPath(`source-hover-${width}.png`), animations: "disabled" });
      await identifiers.hover();
      expect(await row.evaluate(element => getComputedStyle(element).backgroundColor)).toBe(hovered);
      await page.screenshot({ path: info.outputPath(`identifiers-hover-${width}.png`), animations: "disabled" });
    }

    await page.setViewportSize({ width: 1440, height: 900 });
    await row.getByRole("button", { name: "Copy Executable ID", exact: true }).click();
    await expect(row.getByRole("status")).toHaveText("Executable ID copied");
    await expect(page).toHaveURL(listUrl);
    const identifierTrigger = row.getByRole("button", { name: /^Identifiers for / });
    await identifierTrigger.click();
    const identifierDialog = page.getByRole("dialog", { name: /^Workflow executable identifiers for / });
    await expect(identifierDialog).toBeVisible();
    await page.locator(".wf-definition-identifiers-popover").click({ position: { x: 2, y: 2 } });
    await expect(page).toHaveURL(listUrl);
    await expect(identifierDialog).toBeVisible();
    await page.screenshot({ path: info.outputPath("identifier-popover.png"), animations: "disabled" });
    const closeIdentifiers = identifierDialog.getByRole("button", { name: "Close identifiers", exact: true });
    await closeIdentifiers.click();
    await expect(identifierDialog).toBeHidden();

    await identifierTrigger.press("Enter");
    await expect(closeIdentifiers).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(identifierDialog).toBeHidden();
    await expect(identifierTrigger).toBeFocused();
    await expect(page).toHaveURL(listUrl);
    await row.getByRole("button", { name: /^Show references of / }).click();
    await expect(page.locator(".wf-executable-reference-row").first()).toBeVisible();
    await expect(page).toHaveURL(listUrl);
    await row.getByRole("button", { name: /^Hide references of / }).click();

    await identifiers.click();
    await expect(page).toHaveURL(/\/workflows\/executables\/[^/?]+/);
    await page.goBack();
    await source.click();
    await expect(page).toHaveURL(/\/workflows\/definitions\?definition=/);
    await expect(page.locator(".wf-editor")).toBeVisible();
    await page.goBack();
    for (const key of ["Enter", "Space"]) {
      await row.focus();
      await page.keyboard.press("Tab");
      await page.keyboard.press("Shift+Tab");
      await page.mouse.move(0, 0);
      await expect(row).toBeFocused();
      await expect(row).toHaveCSS("outline-style", "solid");
      await page.screenshot({ path: info.outputPath(`row-focus-${key}.png`), animations: "disabled" });
      await row.press(key);
      await expect(page).toHaveURL(/\/workflows\/executables\/[^/?]+/);
      await page.goBack();
    }
  });
}
