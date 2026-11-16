import { fileURLToPath } from "node:url";
import { rangePositionX } from "../acceptance/rangePosition";
import { expect, test } from "@playwright/test";
import type {} from "./activityPlaybackFixture";

for (const theme of ["light", "dark"] as const) for (const width of [1280, 320]) {
  test(`pending playback cancellation preserves a pointer inspection at ${width}px ${theme}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 }); await page.goto(`/activity-playback.html?theme=${theme}&scenario=pointer`);
    const playback = page.getByRole("region", { name: "Activity playback", exact: true });
    await expect(playback).toContainText("Step 1 of 200");
    await page.evaluate(() => { window.__activityPlaybackFixture.hold = true; });
    await playback.getByRole("button", { name: "Next", exact: true }).click(); await expect(playback).toContainText("Loading step 2");
    const target = page.getByRole("button", { name: "Inspect occurrence below playback", exact: true });
    await target.scrollIntoViewIfNeeded(); const bounds = await target.boundingBox(); expect(bounds).not.toBeNull();
    await page.mouse.move(bounds!.x + bounds!.width / 2, bounds!.y + bounds!.height / 2); await page.mouse.down();
    await expect(playback).toHaveAttribute("aria-busy", "false"); await page.mouse.up();
    await expect(page.getByLabel("Inspected fixture occurrence")).toHaveText("fixture-execution-0");
    await page.evaluate(() => { window.__activityPlaybackFixture.hold = false; window.__activityPlaybackFixture.release("1"); });
    await expect(playback).toContainText("Step 1 of 200");
  });
}

for (const theme of ["light", "dark"] as const) {
  test(`manual pointer inspection keeps its target when playback pauses at 320px ${theme}`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    const clockStart = new Date("2026-11-14T12:00:00Z");
    await page.clock.install({ time: new Date(clockStart.getTime() - 60_000) });
    await page.goto(`/activity-playback.html?theme=${theme}&scenario=pointer`);
    const playback = page.getByRole("region", { name: "Activity playback", exact: true });
    await expect(playback).toContainText("Step 1 of 200"); await page.clock.pauseAt(clockStart);
    await playback.getByRole("button", { name: "Play", exact: true }).click();
    await expect(playback.getByRole("button", { name: "Pause playback", exact: true })).toBeVisible();
    const target = page.getByRole("button", { name: "Inspect occurrence below playback", exact: true });
    await target.scrollIntoViewIfNeeded(); const bounds = await target.boundingBox(); expect(bounds).not.toBeNull();
    await page.mouse.move(bounds!.x + bounds!.width / 2, bounds!.y + bounds!.height / 2); await page.mouse.down();
    await expect(playback.getByRole("button", { name: "Play", exact: true })).toBeVisible(); await page.mouse.up();
    await expect(page.getByLabel("Inspected fixture occurrence")).toHaveText("fixture-execution-0");
    await expect(playback.getByRole("button", { name: "Play", exact: true })).toBeVisible();
    await page.clock.runFor(5000); await expect(playback).toContainText("Step 1 of 200");
  });
}

for (const theme of ["light", "dark"] as const) for (const width of [1280, 320]) {
  test(`playback controls retain focus, keyboard scrubbing and visible bounds at ${width}px ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 }); await page.goto(`/activity-playback.html?theme=${theme}`);
    const playback = page.getByRole("region", { name: "Activity playback", exact: true });
    await expect(playback).toContainText("Step 1 of 200");
    const previous = playback.getByRole("button", { name: "Previous", exact: true }), next = playback.getByRole("button", { name: "Next", exact: true });
    await expect(previous).toBeDisabled(); await next.focus(); await next.press("Enter");
    await expect(playback).toContainText("Step 2 of 200"); await expect(next).toBeFocused();
    const range = playback.getByRole("slider", { name: "Activity position", exact: true });
    await range.focus(); await range.press("End"); await expect(playback).toContainText("Step 200 of 200");
    await expect(next).toBeDisabled(); await expect(playback.getByRole("button", { name: "Replay", exact: true })).toBeEnabled();
    await range.press("Home"); await expect(playback).toContainText("Step 1 of 200"); await expect(range).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`playback-${width}-${theme}.png`), fullPage: true });
  });
}
for (const [speed, interval] of [["0.5", 2000], ["1", 1000], ["2", 500], ["4", 250]] as const) {
  test(`presentation pace ${speed}× advances once per interval and pause retires future ticks`, async ({ page }) => {
    const clockStart = new Date("2026-11-14T12:00:00Z");
    await page.clock.install({ time: new Date(clockStart.getTime() - 60_000) }); await page.goto("/activity-playback.html");
    const playback = page.getByRole("region", { name: "Activity playback", exact: true });
    await expect(playback).toContainText("Step 1 of 200"); await page.clock.pauseAt(clockStart);
    await playback.getByRole("combobox", { name: "Playback speed" }).selectOption(speed);
    await playback.getByRole("button", { name: "Play", exact: true }).click();
    await page.clock.runFor(interval - 1); await expect(playback).toContainText("Step 1 of 200");
    await page.clock.runFor(1); await expect(playback).toContainText("Step 2 of 200");
    await playback.getByRole("button", { name: "Pause playback", exact: true }).click();
    await page.clock.runFor(interval * 4); await expect(playback).toContainText("Step 2 of 200");
  });
}
test("rapid distant seeks confirm the final target without preloading intervening pages", async ({ page }) => {
  await page.goto("/activity-playback.html"); const playback = page.getByRole("region", { name: "Activity playback", exact: true });
  await expect(playback).toContainText("Step 1 of 200");
  await page.evaluate(() => { window.__activityPlaybackFixture.hold = true; window.__activityPlaybackFixture.requests.length = 0; });
  const range = playback.getByRole("slider", { name: "Activity position", exact: true });
  await range.focus(); await range.press("End"); for (let i = 0; i < 9; i++) await range.press("ArrowLeft");
  await expect(playback).toContainText("Loading step 191"); await expect(page.getByLabel("Confirmed fixture occurrence")).toHaveText("fixture-execution-0");
  await page.evaluate(() => { window.__activityPlaybackFixture.hold = false; window.__activityPlaybackFixture.release("190"); });
  await expect(playback).toContainText("Step 191 of 200");
  await page.evaluate(() => { for (let i = 199; i > 190; i--) window.__activityPlaybackFixture.release(String(i)); });
  await expect(page.getByLabel("Confirmed fixture occurrence")).toHaveText("fixture-execution-190");
  const requested = await page.evaluate(() => window.__activityPlaybackFixture.requests.map(request => request.position));
  expect(requested.every(position => Number(position) >= 190)).toBe(true); expect(requested.length).toBeLessThanOrEqual(12);
});
test("failed requested positions retain the cursor and retry stays paused", async ({ page }) => {
  await page.goto("/activity-playback.html"); const playback = page.getByRole("region", { name: "Activity playback", exact: true });
  await expect(playback).toContainText("Step 1 of 200"); await page.evaluate(() => { window.__activityPlaybackFixture.failOnce = true; });
  await playback.getByRole("button", { name: "Next", exact: true }).click();
  await expect(playback).toContainText("previous confirmed position is unchanged"); await expect(page.getByLabel("Confirmed fixture occurrence")).toHaveText("fixture-execution-0");
  await expect(playback).toContainText("Requested step 2"); await expect(playback).not.toContainText("Loading step 2");
  await playback.getByRole("button", { name: "Retry requested activity" }).click(); await expect(playback).toContainText("Step 2 of 200");
  await expect(playback.getByRole("button", { name: "Play", exact: true })).toBeVisible();
});
for (const scenario of ["unknown", "gap", "unavailable", "missing-pin", "one", "empty", "disabled", "uncaptured", "unsupported", "unknown-coverage", "unknown-reason"]) {
  test(`keeps the ${scenario} state distinct without fabricated positions`, async ({ page }) => {
    await page.goto(`/activity-playback.html?scenario=${scenario}`); const playback = page.getByRole("region", { name: "Activity playback", exact: true });
    await expect(playback).toHaveAttribute("aria-busy", "false");
    if (scenario === "unknown") { await expect(playback).toContainText("total unknown"); await expect(playback.getByRole("slider")).toHaveCount(0); }
    if (scenario === "gap") { await expect(playback).toContainText("gap whose boundaries are unknown"); await expect(playback.getByRole("slider")).toHaveCount(0); }
    if (scenario === "unavailable") await expect(playback).toContainText("recording is unavailable");
    if (scenario === "missing-pin") await expect(playback).toContainText("Pinned diagram unavailable");
    if (scenario === "one") { await expect(playback).toContainText("Step 1 of 1"); await expect(playback.getByRole("button", { name: "Next", exact: true })).toBeDisabled(); }
    if (scenario === "empty") await expect(playback).toContainText("No recorded activity occurrences");
    if (scenario === "disabled") await expect(playback).toContainText("Recording was disabled for this admission");
    if (scenario === "uncaptured") await expect(playback).toContainText("No recording was captured for this admission");
    if (scenario === "unsupported") await expect(playback).toContainText("does not support recorded activity playback");
    if (scenario === "unknown-coverage") await expect(playback).toContainText("Recording status is unavailable");
    if (scenario === "unknown-reason") await expect(playback).toContainText("unavailable in this range");
  });
}

for (const theme of ["light", "dark"] as const) {
  test(`native pointer seeking accounts for the actual host input box at 320px ${theme}`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto(`/activity-playback.html?theme=${theme}`);
    await page.addStyleTag({ path: fileURLToPath(new URL("../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css", import.meta.url)) });
    const range = page.getByRole("slider", { name: "Activity position", exact: true });
    const bounds = await range.boundingBox(); expect(bounds).not.toBeNull();
    const index = 50;
    // Exercise the same pointer calculation as the real composed journey.
    await range.click({ position: { x: await rangePositionX(range, index, 200), y: bounds!.height / 2 } });
    expect(Math.abs(Number(await range.inputValue()) - index)).toBeLessThanOrEqual(2);
  });
}
