// Shared Studio readiness for owned-host Playwright drivers.
// Wait on committed chrome (backend switcher + ?backend=), never fixture-only
// data-committed-backend-url or networkidle. Authentication is optional: default
// Studio hosts do not render that select.

/**
 * @param {import('@playwright/test').Page} page
 * @param {import('@playwright/test').Expect} expect
 * @param {{ studioOrigin: string, user?: string, password?: string }} options
 */
export async function signInThroughStudio(page, expect, { studioOrigin, user = "admin", password = "Password123!" }) {
  const username = page.getByLabel("Username", { exact: true });
  const switcher = page.getByRole("button", { name: /Select backend\. Current: / });
  await expect(username.or(switcher)).toBeVisible({ timeout: 30_000 });
  if (await username.isVisible()) {
    await username.fill(user);
    await page.getByLabel("Password", { exact: true }).fill(password);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
  }
  await page.waitForURL(value => value.origin === studioOrigin, { timeout: 30_000 });
  await waitForSelectedBackend(page, expect);
}

/**
 * @param {import('@playwright/test').Page} page
 * @param {import('@playwright/test').Expect} expect
 * @param {string | RegExp} [current]
 */
export async function waitForSelectedBackend(page, expect, current) {
  const name = current
    ? new RegExp(`^Select backend\\. Current: ${typeof current === "string" ? escapeRegExp(current) : current.source}`)
    : /Select backend\. Current: /;
  await expect(page.getByRole("button", { name })).toBeVisible({ timeout: 30_000 });
  await expect(page).toHaveURL(/[?&]backend=/);
}

/**
 * @param {import('@playwright/test').Page} page
 * @param {import('@playwright/test').Expect} expect
 * @param {{ name: string, url: string }} profile
 */
export async function addPersonalBackend(page, expect, { name, url }) {
  await page.goto(new URL("/settings/backends", page.url()).toString());
  await expect(page.getByRole("heading", { name: "Backends", exact: true, level: 2 })).toBeVisible({ timeout: 30_000 });
  await page.getByRole("button", { name: "Add backend", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Add backend", exact: true, level: 2 })).toBeVisible();
  await page.getByLabel("Name", { exact: true }).fill(name);
  await page.getByLabel("Backend API URL", { exact: true }).fill(url);
  const authentication = page.getByLabel("Authentication", { exact: true });
  if (await authentication.isVisible()) {
    // Default Studio has no authenticationProfiles. When a host does render this select,
    // leave Discover on connection — do not selectOption a fixture-only profile id.
    await expect(authentication).toBeEnabled();
  }
  await page.getByRole("button", { name: "Create backend" }).click();
  await expect(page.getByRole("heading", { name, exact: true, level: 2 })).toBeVisible({ timeout: 30_000 });
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
