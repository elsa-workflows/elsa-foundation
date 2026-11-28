import { expect, test, type Locator } from "@playwright/test";

async function expectCompletedPill(badge: Locator) {
  await expect(badge).toHaveText("Completed");
  const appearance = await badge.evaluate(element => {
    const styles = getComputedStyle(element);
    const probe = document.createElement("span");
    probe.style.color = "var(--studio-success-fg)";
    document.body.append(probe);
    const successColor = getComputedStyle(probe).color;
    probe.remove();
    return {
      border: styles.borderTopColor,
      background: styles.backgroundColor,
      color: styles.color,
      padding: [styles.paddingLeft, styles.paddingRight],
      successColor
    };
  });

  expect(appearance.border).not.toBe("rgba(0, 0, 0, 0)");
  expect(appearance.background).not.toBe("rgba(0, 0, 0, 0)");
  expect(appearance.color).toBe(appearance.successColor);
  expect(appearance.padding).toEqual(["8px", "8px"]);
}

for (const theme of ["light", "dark"]) {
  test(`keeps completed status pills clear in the Minimal workspace (${theme})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.addInitScript(() => {
      localStorage.setItem("elsa-studio-workspace-style", "minimal");
    });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&execution-motion=causal&palette-theme=classic&appearance=${theme}&theme=${theme}`);
    await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");

    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();

    const summary = page.getByRole("region", { name: "Test run summary", exact: true });
    const summaryBadge = summary.locator(".wf-status-badge");
    await expect(summaryBadge).toHaveAttribute("data-status", "running");
    await expect(summaryBadge).toHaveText("Running");
    await expect(summaryBadge).toHaveCSS("background-color", "rgba(0, 0, 0, 0)");

    await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
    await expect(summaryBadge).toHaveAttribute("data-status", "completed");
    await expectCompletedPill(summaryBadge);

    const canvasBadge = page.locator('.wf-node-runtime-strip .wf-status-badge[data-status="completed"]').first();
    await expect(canvasBadge).toBeVisible();
    await expectCompletedPill(canvasBadge);
    await testInfo.attach(`minimal-completed-status-${theme}.png`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: "image/png"
    });
  });
}