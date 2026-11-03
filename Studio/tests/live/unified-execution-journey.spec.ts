import { expect, test, type Page } from "@playwright/test";
import { signIn, startTestRun } from "./support/journey";

// Opt in with an owned, saved Sequence containing Event -> Fault. The event must expose the ordinary
// Test Run delivery editor. This starts two harmless Test Runs through the UI; it never publishes,
// intercepts a response, changes capture policy, or invents execution evidence.
const definitionId = process.env.ELSA_UNIFIED_EXECUTION_DEFINITION_ID;
test.use({ contextOptions: { reducedMotion: "reduce" } });
test.skip(!process.env.ELSA_STUDIO_URL || !definitionId,
  "Set ELSA_STUDIO_URL and ELSA_UNIFIED_EXECUTION_DEFINITION_ID to an owned Event -> Fault definition.");

for (const [theme, width] of [["Light", 1440], ["Dark", 390]] as const) {
  test(`both normal live views retain exact evidence with reduced motion (${theme}, ${width}px)`, async ({ page }, testInfo) => {
    test.setTimeout(240_000);
    page.setDefaultTimeout(15_000);
    await page.setViewportSize({ width, height: 1000 });
    await signIn(page);
    await page.goto(`/workflows/definitions?definition=${encodeURIComponent(definitionId!)}`);
    await page.getByRole("button", { name: "Select theme", exact: true }).press("Enter");
    const themeOption = page.getByRole("menuitemradio", { name: theme, exact: true });
    await themeOption.press("Enter");
    await expect(themeOption).toHaveAttribute("aria-checked", "true");
    await themeOption.press("Escape");
    await expect(page.getByRole("button", { name: "1. Event", exact: true })).toBeVisible();
    await startTestRun(page);

    const panel = page.getByRole("complementary", { name: "Inspector panel", exact: true });
    const instanceLink = panel.locator(".wf-runtime-meta > div").filter({ has: page.getByText("Run / Instance", { exact: true }) }).getByRole("button");
    const instanceId = (await instanceLink.innerText()).trim();
    await expect(page.getByRole("region", { name: "Test Run controls", exact: true })).toContainText("Execution Waiting");
    await page.getByRole("button", { name: "1. Event", exact: true }).press("Enter");
    await expect(panel.getByRole("heading", { name: "Event", exact: true })).toBeVisible();
    await panel.locator(".wf-activity-execution-details > summary").press("Enter");
    const executionId = await panel.getByText("Activity Execution ID", { exact: true }).locator("xpath=following-sibling::dd").innerText();

    const instance = await page.context().newPage();
    try {
      await instance.setViewportSize({ width, height: 1000 });
      await instance.goto(`/workflows/instances/${encodeURIComponent(instanceId)}?selected=${encodeURIComponent(executionId.trim())}`);
      const details = instance.getByRole("complementary", { name: "Run details panel", exact: true });
      await expect(details.getByRole("heading", { name: "Event", exact: true })).toBeVisible();
      await expect(details).toContainText("Suspended");
      await expect(details).toContainText("No captured value is available.");
      await expect(instance.getByRole("button", { name: "Pause following", exact: true })).toBeVisible();
      if (width === 390) {
        await instance.getByRole("button", { name: "Back to workflow run canvas", exact: true }).press("Enter");
        await expect(instance.getByRole("button", { name: "1. Event", exact: true })).toBeFocused();
      }

      // Both observers remain active while the existing modeled wait is delivered from the editor.
      await page.getByRole("tab", { name: "Runtime", exact: true }).press("Enter");
      await page.getByRole("region", { name: "Test Run controls", exact: true }).getByRole("textbox").fill(JSON.stringify({
        payload: { safe: "qualification safe payload", password: "QUALIFICATION_PASSWORD_MARKER" }
      }));
      const pulseSamples = Promise.all([page, instance].map(observeNewPulseStyles));
      await page.getByRole("button", { name: "Deliver event", exact: true }).press("Enter");
      await expect(page.getByRole("group", { name: "Attached Test Run", exact: true })).toContainText("Faulted", { timeout: 30_000 });
      await expect(instance.getByRole("region", { name: "Workflow run canvas", exact: true })).toContainText("Faulted", { timeout: 30_000 });

      for (const [index, samples] of (await pulseSamples).entries()) {
        expect(samples.length, `Observed fresh committed cues in ${index === 0 ? "editor" : "instance"}`).toBeGreaterThan(0);
        expect(samples).toEqual(samples.map(() => ({ display: "none", animation: "none" })));
      }
      // Keep the responsive canvas mounted while observing its fresh cues, then reopen exact activity data.
      if (width === 390) await instance.getByRole("button", { name: "1. Event", exact: true }).press("Enter");
      await expect(details).toContainText("Completed");
      await expect(details.getByRole("heading", { name: "Outputs", exact: false })).toBeVisible();
      await expect(instance).toHaveURL(new RegExp(`selected=${encodeURIComponent(executionId.trim())}$`));
      await instance.getByRole("button", { name: "Refresh", exact: true }).press("Enter");
      await expect(details.getByRole("heading", { name: "Event", exact: true })).toBeVisible();
      await expect(details).toContainText("Completed");

      await page.getByRole("button", { name: "1. Event", exact: true }).press(" ");
      for (const evidence of [panel, details]) {
        await expect(evidence).toContainText("qualification safe payload");
        await expect(evidence).toContainText("redacted: sensitive-name");
        await expect(evidence).not.toContainText("QUALIFICATION_PASSWORD_MARKER");
      }

      for (const view of [page, instance]) {
        expect(await view.evaluate(() => matchMedia("(prefers-reduced-motion: reduce)").matches)).toBe(true);
        await expect(view.locator(".wf-execution-node-pulse:visible, .wf-execution-edge-pulse:visible, .wf-execution-sequence-pulse:visible")).toHaveCount(0);
        expect(await view.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
      }
      await testInfo.attach("normal editor", { body: await page.screenshot(), contentType: "image/png" });
      await testInfo.attach("normal instance", { body: await instance.screenshot(), contentType: "image/png" });
      await testInfo.attach("execution identity", { body: JSON.stringify({ definitionId, instanceId, executionId: executionId.trim(), theme, width, reducedMotion: true }), contentType: "application/json" });

      // End the ordinary backend session once, after the final layout, and check both entry points.
      if (width === 390) {
        await page.getByRole("button", { name: /^Account:/ }).press("Enter");
        const logoutCompleted = page.waitForResponse(response =>
          response.request().method() === "POST" && response.url().includes("/_elsa/identity/logout/"), { timeout: 120_000 });
        await page.getByRole("menuitem", { name: "Sign out", exact: true }).press("Enter");
        expect((await logoutCompleted).status()).toBe(204);
        await expect(page.getByRole("group", { name: "Attached Test Run", exact: true })).toHaveCount(0);
        await expect(page.locator("input[type=password]")).toBeVisible({ timeout: 30_000 });
        await instance.getByRole("button", { name: "Refresh", exact: true }).press("Enter");
        await expect(instance.getByText("Execution observation unavailable", { exact: true })).toBeVisible();
        await expect(instance.getByText("Sign in again to observe this execution.", { exact: true })).toBeVisible();
        await expect(instance.getByRole("complementary", { name: "Run details panel", exact: true })).toHaveCount(0);
        await expect(instance.getByRole("region", { name: "Workflow run canvas", exact: true })).toHaveCount(0);
        await expect(instance.getByText("qualification safe payload", { exact: true })).toHaveCount(0);
        await signIn(page);
        await page.goto(`/workflows/definitions?definition=${encodeURIComponent(definitionId!)}`);
        await expect(page.getByRole("button", { name: "1. Event", exact: true })).toBeVisible();
        await expect(page.getByRole("group", { name: "Previous Test Run", exact: true })).toHaveCount(0);
      }
    } finally {
      await instance.close();
    }
  });
}

/** Observe actual fresh cue elements during delivery; an empty canvas after their lifetime is insufficient proof. */
function observeNewPulseStyles(page: Page) {
  return page.evaluate(() => new Promise<Array<{ display: string; animation: string }>>(resolve => {
    const selector = ".wf-execution-node-pulse, .wf-execution-edge-pulse, .wf-execution-sequence-pulse";
    const seen = new WeakSet<Element>(document.querySelectorAll(selector));
    const samples: Array<{ display: string; animation: string }> = [];
    const observer = new MutationObserver(() => {
      for (const pulse of document.querySelectorAll(selector)) {
        if (seen.has(pulse)) continue;
        seen.add(pulse);
        const style = getComputedStyle(pulse);
        samples.push({ display: style.display, animation: style.animationName });
      }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
    setTimeout(() => { observer.disconnect(); resolve(samples); }, 4000);
  }));
}
