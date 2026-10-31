import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  for (const width of [1920, 390]) {
    test(`keeps interface editing and connections compact at ${width}px in ${theme}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 1100 });
      await page.goto(`/?mode=activity-authoring&theme=${theme}&contract=outcome-only`);
      await page.getByRole("tab", { name: "Inputs & outputs", exact: true }).click();
      const members = page.getByRole("navigation", { name: "Public interface members" });
      const outputs = page.getByRole("region", { name: "Output values", exact: true });
      const paths = page.getByRole("region", { name: "Completion paths", exact: true });
      await expect(page.getByRole("region", { name: "Done", exact: true })).toBeVisible();
      if (width > 1100) {
        expect((await members.boundingBox())!.width).toBeLessThanOrEqual(320);
        const outputBox = (await outputs.boundingBox())!;
        const pathBox = (await paths.boundingBox())!;
        expect(pathBox.x).toBeGreaterThan(outputBox.x + outputBox.width);
        expect(Math.abs(pathBox.y - outputBox.y)).toBeLessThan(2);
      }
      const connections = page.getByRole("region", { name: "Provider boundary mappings" });
      const suggestions = page.locator("details.ad-interface-suggestions");
      const connectionBox = (await connections.boundingBox())!;
      expect((await suggestions.boundingBox())!.y).toBeGreaterThanOrEqual(connectionBox.y + connectionBox.height);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(0);

      await paths.getByRole("button", { name: "Remove connection", exact: true }).click();
      await expect(paths.getByText("No completion paths connected yet.", { exact: true })).toBeVisible();
      await paths.getByRole("combobox", { name: "Designer outcome", exact: true }).selectOption("done");
      await paths.getByRole("combobox", { name: "Activity outcome", exact: true }).selectOption("done");
      const connect = paths.getByRole("button", { name: "Connect", exact: true });
      await connect.focus();
      await page.keyboard.press("Enter");
      await expect(paths.getByText("Done → Done", { exact: true })).toBeVisible();
      await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
      await page.reload();
      await page.getByRole("tab", { name: "Inputs & outputs", exact: true }).click();
      await expect(paths.getByText("Done → Done", { exact: true })).toBeVisible();
      await paths.scrollIntoViewIfNeeded();
      for (const control of await paths.getByRole("combobox").all()) {
        const box = (await control.boundingBox())!;
        expect(box.x).toBeGreaterThanOrEqual(0);
        expect(box.x + box.width).toBeLessThanOrEqual(width);
      }
      await page.screenshot({ path: testInfo.outputPath(`interface-${theme}-${width}.png`), fullPage: true });
    });
  }

  test(`keeps immutable interface controls read-only in ${theme}`, async ({ page }) => {
    await page.goto(`/?mode=activity-authoring&theme=${theme}&contract=outcome-only&read-only`);
    await page.getByRole("tab", { name: "Inputs & outputs", exact: true }).click();
    await expect(page.getByRole("button", { name: "Add outcome", exact: true })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Remove outcome", exact: true })).toBeDisabled();
    const paths = page.getByRole("region", { name: "Completion paths", exact: true });
    await expect(paths.getByRole("button", { name: "Remove connection", exact: true })).toBeDisabled();
    await expect(paths.getByRole("button", { name: "Connect", exact: true })).toBeDisabled();
    await expect(paths.getByRole("combobox", { name: "Designer outcome", exact: true })).toBeDisabled();
    await expect(paths.getByRole("combobox", { name: "Activity outcome", exact: true })).toBeDisabled();
  });
}
