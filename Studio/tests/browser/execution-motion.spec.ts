import { expect, test } from "@playwright/test";

for (const host of ["editor", "instance"]) {
  for (const theme of ["light", "dark"]) {
    test(`pulses a newly confirmed exact connection in the ${host} (${theme})`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: 1440, height: 1000 });
      await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=unique&execution-motion=causal&theme=${theme}`);
      const canvas = host === "instance" ? page.getByRole("region", { name: "Workflow run canvas", exact: true }) : page.getByLabel("Workflow canvas", { exact: true });
      if (host === "editor") {
        await page.getByRole("button", { name: "Test Run", exact: true }).click();
        await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
      }
      const source = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
      await expect(source).toHaveAccessibleName(/Run state: Completed/);
      await source.click();
      await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
      const connection = canvas.locator('.wf-execution-edge-pulse[data-execution-pulse="connection"]');
      await expect(connection).toHaveCount(0);
      const location = page.url();
      await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
      // Horizontal SVG strokes have a zero-height geometric box; verify their paint styles directly.
      await expect(connection).toBeAttached();
      await expect(connection).not.toHaveCSS("display", "none");
      await expect(connection).toHaveCSS("visibility", "visible");
      await expect(connection).toHaveCSS("animation-name", "wf-recorded-connection");
      await expect(connection).toHaveAttribute("aria-hidden", "true");
      await page.screenshot({ path: testInfo.outputPath(`${host}-${theme}-confirmed-pulse.png`), fullPage: true });
      await expect(source).toHaveAccessibleName(/Selected/);
      expect(page.url()).toBe(location);
      await expect(connection).toHaveCount(0);
    });
  }

  test(`honors reduced motion while retaining execution status in the ${host}`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=unique&execution-motion=causal`);
    const canvas = host === "instance" ? page.getByRole("region", { name: "Workflow run canvas", exact: true }) : page.getByLabel("Workflow canvas", { exact: true });
    if (host === "editor") {
      await page.getByRole("button", { name: "Test Run", exact: true }).click();
      await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    }
    await expect(canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ })).toHaveAccessibleName(/Run state: Completed/);
    await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
    const connection = canvas.locator('.wf-execution-edge-pulse[data-execution-pulse="connection"]');
    await expect(connection).toHaveCSS("animation-name", "none");
    await expect(connection).toHaveCSS("display", "none");
    await expect(canvas.getByRole("button", { name: /^Write Line\. Node ID: write-two/ })).toHaveAccessibleName(/Run state: Completed/);
  });
}

for (const host of ["editor", "instance"]) for (const reduced of [false, true]) {
  test(`shows a confirmed Sequence batch in the ${host}, reduced motion ${reduced}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.emulateMedia({ reducedMotion: reduced ? "reduce" : "no-preference" });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=unique&composition=sequence&execution-motion=causal`);
    const canvas = host === "instance" ? page.getByRole("region", { name: "Workflow run canvas", exact: true }) : page.getByLabel("Sequence canvas", { exact: true });
    if (host === "editor") {
      await page.getByRole("button", { name: "Test Run", exact: true }).click();
      await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    }
    const source = canvas.getByRole("button", { name: "1. Notify buyer", exact: true });
    await expect(source.locator("..")).toContainText("Completed");
    await source.click();
    await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
    const pulses = canvas.locator('.wf-execution-sequence-pulse[data-execution-pulse="connection"]');
    await expect(pulses).toHaveCount(0);
    await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
    await expect(pulses).toHaveCount(2);
    await expect(pulses.first()).toHaveCSS("animation-name", reduced ? "none" : "wf-recorded-sequence");
    if (reduced) await expect(pulses.first()).toHaveCSS("display", "none");
    await expect(source).toHaveAttribute("aria-pressed", "true");
    await page.screenshot({ path: testInfo.outputPath(`${host}-sequence-${reduced ? "reduced" : "animated"}.png`), fullPage: true });
    await expect(pulses).toHaveCount(0);
  });
}
