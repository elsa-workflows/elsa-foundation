import { expect, type Page } from "@playwright/test";
import { studioPassword, studioUser } from "./journey";

// Real-host readiness: wait for the login form or the committed backend switcher, then for
// ?backend=. Do not wait for networkidle, fixture-only data-committed-backend-url, or an
// Authentication select that default Studio hosts do not render (Codex 7 Nov harness diagnosis on #3650).

const switcherName = /Select backend\. Current: /;

export async function signInToStudio(page: Page, user = studioUser, password = studioPassword) {
  await page.goto("/");
  const username = page.getByLabel("Username", { exact: true });
  const switcher = page.getByRole("button", { name: switcherName });
  await expect(username.or(switcher)).toBeVisible({ timeout: 30_000 });
  if (await username.isVisible()) {
    await username.fill(user);
    await page.getByLabel("Password", { exact: true }).fill(password);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
  }
  const studio = process.env.ELSA_STUDIO_URL;
  if (studio) {
    const origin = new URL(studio).origin;
    await page.waitForURL(url => new URL(url).origin === origin, { timeout: 30_000 });
  }
  await waitForSelectedBackend(page);
}

export async function waitForSelectedBackend(page: Page, current?: string | RegExp) {
  const name = current
    ? new RegExp(`^Select backend\\. Current: ${typeof current === "string" ? escapeRegExp(current) : current.source}`)
    : switcherName;
  await expect(page.getByRole("button", { name })).toBeVisible({ timeout: 30_000 });
  await expect(page).toHaveURL(/[?&]backend=/);
}

export async function addPersonalBackend(page: Page, name: string, url: string) {
  await page.goto("/settings/backends");
  await expect(page.getByRole("heading", { name: "Backends", exact: true, level: 2 })).toBeVisible({ timeout: 30_000 });
  await page.getByRole("button", { name: "Add backend", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Add backend", exact: true, level: 2 })).toBeVisible();
  await page.getByLabel("Name", { exact: true }).fill(name);
  await page.getByLabel("Backend API URL", { exact: true }).fill(url);
  const authentication = page.getByLabel("Authentication", { exact: true });
  if (await authentication.isVisible()) {
    await expect(authentication).toBeEnabled();
  }
  await page.getByRole("button", { name: "Create backend" }).click();
  await expect(page.getByRole("heading", { name, exact: true, level: 2 })).toBeVisible({ timeout: 30_000 });
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
