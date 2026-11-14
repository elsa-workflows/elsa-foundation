import { expect, test } from "@playwright/test";

// Goal/reference: browse the run timeline shown in the owner's screenshot without losing it to Activity.
// Cover repeated selection, keyboard focus, explicit details navigation, and narrow/light/dark layouts.
// These fixture-backed checks support interaction acceptance; composed-host acceptance is separate.
for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`timeline selection retains its tab and exact occurrence (${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=instance&run-canvas=repeated&timeline-entries&theme=${theme}`);
    if (width < 600) await page.getByRole("region", { name: "Workflow run canvas", exact: true })
      .getByRole("button", { name: /^Notify buyer\. Node ID:/ }).click();
    const panel = page.getByLabel("Run details panel", { exact: true });
    const timelineTab = panel.getByRole("tab", { name: "Timeline", exact: true });
    await timelineTab.click();
    const timeline = panel.getByRole("list", { name: "Execution timeline", exact: true });
    const entries = timeline.getByRole("button").filter({ hasText: "Notify buyer" });
    await expect(entries).toHaveCount(10);
    const selectedEntry = entries.nth(3);
    await entries.first().click();
    await expect(timelineTab).toHaveAttribute("aria-selected", "true");
    await expect(entries.first()).toHaveAttribute("data-selected", "true");
    await selectedEntry.focus();
    await selectedEntry.press("Enter");
    await expect(selectedEntry).toBeFocused();
    await expect(timelineTab).toHaveAttribute("aria-selected", "true");
    await expect(entries.first()).toHaveAttribute("data-selected", "false");
    await expect(selectedEntry).toHaveAttribute("data-selected", "true");
    await page.screenshot({ path: test.info().outputPath("timeline-selection.png"), fullPage: true });

    await panel.getByRole("tab", { name: "Activity", exact: true }).click();
    const technical = panel.locator(".wf-activity-execution-details");
    await technical.locator("summary").click();
    await expect(technical).toContainText("activity-exec-write-one-4");
    await timelineTab.click();
    await expect(selectedEntry).toHaveAttribute("data-selected", "true");
  });
}
