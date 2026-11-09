import { expect, test, type Locator } from "@playwright/test";

async function geometry(elements: Locator[]) {
  return Promise.all(elements.map(element => element.boundingBox()));
}

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`keeps execution controls and canvas still during polling (${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&execution-motion=causal&evidence-latency&theme=${theme}`);
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();

    const runtime = page.getByRole("region", { name: "Test run summary" });
    const toolbar = runtime.getByRole("group", { name: "Execution controls", exact: true });
    const refresh = toolbar.getByRole("button", { name: "Refresh execution", exact: true });
    const elements = [
      toolbar.getByRole("button", { name: "Pause following", exact: true }), refresh,
      toolbar.getByRole("button", { name: "Return to editing", exact: true }),
      runtime.getByRole("button", { name: "Open run", exact: true }),
      toolbar.getByText("Activity state is recorded at commit boundaries.", { exact: true }),
      page.getByLabel("Workflow canvas", { exact: true })
    ];
    await expect(runtime.locator(".wf-status-badge")).toHaveText("Running");
    await expect(elements[4]).toBeVisible();
    await expect(refresh).toBeEnabled();
    const settled = await geometry(elements);

    // Wait for an automatic poll to begin a deliberately slow evidence read.
    await expect(refresh).toBeDisabled({ timeout: 10000 });
    expect(await geometry(elements)).toEqual(settled);
    await expect(toolbar.getByText("Loading execution evidence…", { exact: true })).toBeHidden();
    await expect(refresh).toBeEnabled();
    expect(await geometry(elements)).toEqual(settled);

    await refresh.click();
    await expect(refresh).toBeDisabled();
    expect(await geometry(elements)).toEqual(settled);
    await expect(refresh).toBeEnabled();
    expect(await geometry(elements)).toEqual(settled);

    const statusSlot = runtime.locator(".wf-runtime-heading");
    const confirmedSlot = await statusSlot.boundingBox();
    await toolbar.getByRole("button", { name: "Pause following", exact: true }).click();
    await expect(runtime.locator(".wf-runtime-outcome")).toContainText("observation paused");
    await expect(runtime.locator(".wf-status-badge")).toHaveText("Running");
    expect(await statusSlot.boundingBox()).toEqual(confirmedSlot);
    await toolbar.getByRole("button", { name: "Follow execution", exact: true }).press("Enter");
    await expect(runtime.locator(".wf-status-badge")).toHaveText("Running");
    await expect(refresh).toBeEnabled();
    expect(await statusSlot.boundingBox()).toEqual(confirmedSlot);
    expect(await geometry(elements)).toEqual(settled);

    await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
    await expect(runtime.locator(".wf-status-badge")).toHaveText("Completed", { timeout: 10000 });
    await expect(refresh).toBeEnabled();
    // A changed summary can wrap within the Runtime panel; the authoring canvas stays fixed.
    expect(await elements[5].boundingBox()).toEqual(settled[5]);
    for (const element of elements.slice(0, 5)) await expect(element).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
    await page.screenshot({ path: test.info().outputPath("execution-toolbar.png"), fullPage: true });
  });
}
