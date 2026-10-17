import { expect, test, type Page } from "@playwright/test";
import { mkdir, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import axe from "axe-core";

const provider = "aspnetcore-identity";
const loginPage = "<!doctype html><title>Sign in</title><h1>Backend sign-in page</h1>";

/**
 * Answers the backend identity routes the shell's real auth wiring calls, with one local provider whose
 * challenge is the backend login page. Signing out flips the session to anonymous, as the backend does.
 */
async function mockIdentity(page: Page) {
  const state = { signedIn: true, logoutCalls: [] as string[] };
  await page.route("**/_elsa/identity/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname.replace("/_elsa/identity/", "");
    if (path === "bootstrap") {
      return route.fulfill({ json: { ownershipMode: "foundation-owned", providers: [
        { id: provider, kind: "local", displayName: "Elsa", isDefault: true, enabled: true, challenge: { url: "/_elsa/identity/login", method: "GET" } }
      ] } });
    }
    if (path === "session") {
      return state.signedIn
        ? route.fulfill({ json: { status: "authenticated", subject: "user-1", displayName: "Alice Admin", tenantId: "default", roles: [], permissions: ["*"], provider: { id: provider, kind: "local" } } })
        : route.fulfill({ status: 401 });
    }
    if (path === "capabilities") {
      return route.fulfill({ json: { ownershipMode: "foundation-owned", providers: [] } });
    }
    if (path === `logout/${provider}` && route.request().method() === "POST") {
      state.logoutCalls.push(url.pathname);
      state.signedIn = false;
      return route.fulfill({ status: 204 });
    }
    if (path === "login") {
      return route.fulfill({ contentType: "text/html", body: loginPage });
    }
    return route.fulfill({ status: 404 });
  });
  return state;
}

async function openShell(page: Page, theme: "light" | "dark" = "light") {
  const state = await mockIdentity(page);
  await page.goto(`/dashboard?mode=user-menu&theme=${theme}`);
  await expect(page.locator("html")).toHaveAttribute("data-theme", "classic");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", theme);
  return state;
}

const account = (page: Page) => page.getByRole("button", { name: "Account: Alice Admin" });

async function expectNoSeriousAxeViolations(page: Page) {
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (context: object, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: unknown[] }> }> };
    }).axe.run({ include: [[".user-menu"], [".user-menu-content"]] }, { resultTypes: ["violations"] });
    return result.violations.filter(violation => violation.impact === "critical" || violation.impact === "serious");
  });
  await test.info().attach("axe-critical-serious.json", { body: JSON.stringify(violations, null, 2), contentType: "application/json" });
  expect(violations).toEqual([]);
}

test.describe("app-bar user menu", () => {
  test.use({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });

  test("names the signed-in user and offers Sign out", async ({ page }) => {
    await openShell(page);

    await expect(account(page)).toBeVisible();
    await expect(account(page)).toContainText("Alice Admin");
    await account(page).click();

    const menu = page.getByRole("menu");
    await expect(menu).toContainText("Signed in as");
    await expect(menu).toContainText("Alice Admin");
    await expect(menu.getByRole("menuitem", { name: "Sign out" })).toBeVisible();
  });

  test("opens from the keyboard, focuses Sign out and returns focus on Escape", async ({ page }) => {
    await openShell(page);

    await account(page).focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("menu")).toBeVisible();
    // The open menu hides the rest of the page from assistive tech, trigger included, so read it by class.
    await expect(page.locator(".user-menu-button")).toHaveAttribute("aria-expanded", "true");
    await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(page.getByRole("menu")).toHaveCount(0);
    await expect(account(page)).toBeFocused();
  });

  test("signs out through the provider and lands on the backend sign-in page", async ({ page }) => {
    const state = await openShell(page);

    await account(page).click();
    await page.getByRole("menuitem", { name: "Sign out" }).click();

    await expect(page.getByRole("heading", { name: "Backend sign-in page" })).toBeVisible();
    expect(state.logoutCalls).toEqual([`/_elsa/identity/logout/${provider}`]);
    const landed = new URL(page.url());
    expect(landed.pathname).toBe("/_elsa/identity/login");
    expect(new URL(landed.searchParams.get("returnUrl")!, landed.origin).pathname).toBe("/dashboard");
  });

  for (const theme of ["light", "dark"] as const) {
    test(`Classic ${theme}: the open menu has no serious accessibility violations`, async ({ page }) => {
      await openShell(page, theme);
      await account(page).click();
      await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeVisible();

      // Color contrast is part of the axe run, so this is also the legibility check for the theme.
      await expectNoSeriousAxeViolations(page);

      const shot = await page.screenshot({ clip: { x: 760, y: 0, width: 680, height: 240 } });
      await test.info().attach(`user-menu-classic-${theme}.png`, { body: shot, contentType: "image/png" });
      if (process.env.USER_MENU_SCREENSHOT_DIR) {
        const dir = resolve(process.cwd(), process.env.USER_MENU_SCREENSHOT_DIR);
        await mkdir(dir, { recursive: true });
        await writeFile(resolve(dir, `user-menu-classic-${theme}.png`), shot);
      }
    });
  }
});
