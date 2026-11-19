import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) for (const mode of ["Durable", "Balanced", "Ephemeral"]) {
  test(`shows the configured ${mode} persistence default (${theme})`, async ({ page }, testInfo) => {
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&host-persistence-mode=${mode}&theme=${theme}`);
    await page.getByRole("tab", { name: "Properties", exact: true }).click();
    const select = page.getByRole("combobox", { name: "Persistence mode", exact: true });
    await expect(select).toHaveValue("");
    await expect(select.locator("option:checked")).toHaveText(`Use host default (${mode})`);
    await select.selectOption(mode === "Durable" ? "Balanced" : "Durable");
    await select.selectOption("");
    await expect(select.locator("option:checked")).toHaveText(`Use host default (${mode})`);
    await page.screenshot({ path: testInfo.outputPath("persistence-default.png") });
  });
}
