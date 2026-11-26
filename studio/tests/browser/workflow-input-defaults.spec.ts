import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`authors, reopens and clears a workflow input default (${theme}, ${width}px)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=activity-duplicate-editor&theme=${theme}`);
    const views = page.getByRole("tablist", { name: "Editor view tabs" });
    await views.getByRole("tab", { name: "Inputs", exact: true }).click();
    await page.getByRole("button", { name: "Add input", exact: true }).click();
    await page.getByRole("textbox", { name: "Input name", exact: true }).fill("Greeting");

    const enabled = page.getByRole("checkbox", { name: "Default for Greeting", exact: true });
    await expect(enabled).not.toBeChecked();
    await enabled.focus();
    await page.keyboard.press("Space");
    await expect(enabled).toBeChecked();
    const value = page.getByLabel("Input default value", { exact: true });
    await expect(value).toHaveValue("");
    await value.fill("Hello");

    await views.getByRole("tab", { name: "Properties", exact: true }).click();
    await views.getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(enabled).toBeChecked();
    await expect(value).toHaveValue("Hello");
    await page.screenshot({ path: testInfo.outputPath("workflow-input-default.png") });

    await enabled.uncheck();
    await expect(value).toHaveCount(0);
    await views.getByRole("tab", { name: "Properties", exact: true }).click();
    await views.getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(enabled).not.toBeChecked();
    await expect(value).toHaveCount(0);
  });
}