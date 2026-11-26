import { expect, type Page } from "@playwright/test";

/** These prerequisite journeys own captured nonempty runs on an occurrence-capable backend. */
export async function waitCapturedPlaybackReady(page: Page) {
  const playback = page.getByRole("region", { name: "Activity playback", exact: true });
  await expect(playback.locator(".wf-playback-position strong")).toContainText(/^Step \d+ of \d+/);
  await expect(playback).toHaveAttribute("aria-busy", "false");
}
