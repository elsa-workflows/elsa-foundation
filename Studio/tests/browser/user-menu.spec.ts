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
async function mockIdentity(page: Page, options: { displayName?: string; signedIn?: boolean } = {}) {
  const state = { signedIn: options.signedIn ?? true, logoutCalls: [] as string[] };
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
        ? route.fulfill({ json: { status: "authenticated", subject: "user-1", displayName: options.displayName ?? "Alice Admin", tenantId: "default", roles: [], permissions: ["*"], provider: { id: provider, kind: "local" } } })
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

type UserMenuOptions = {
  theme?: "light" | "dark";
  displayName?: string;
  signedIn?: boolean;
  navigation?: "left" | "top" | "theme";
  weaver?: boolean | "dense";
  appearance?: boolean;
  account?: boolean;
  authEnabled?: boolean;
  backend?: "long";
};

async function openShell(page: Page, options: UserMenuOptions = {}) {
  const theme = options.theme ?? "light";
  const state = await mockIdentity(page, options);
  const query = new URLSearchParams({
    mode: "user-menu",
    theme,
    navigation: options.navigation ?? "left",
    weaver: options.weaver === "dense" ? "dense" : options.weaver ? "1" : "0",
    appearance: options.appearance === false ? "off" : "on",
    auth: options.authEnabled === false ? "off" : "on",
    account: options.account === false ? "off" : "on",
    backend: options.backend ?? "local"
  });
  await page.goto(`/dashboard?${query}`);
  await expect(page.locator("html")).toHaveAttribute("data-theme", "classic");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", theme);
  await page.evaluate(() => document.fonts.ready);
  return state;
}

const account = (page: Page, displayName = "Alice Admin") => page.getByRole("button", { name: `Account: ${displayName}` });

async function readShellGeometry(page: Page) {
  return page.evaluate(() => {
    const dimensions = (element: HTMLElement) => {
      const rect = element.getBoundingClientRect();
      return {
        left: rect.left,
        right: rect.right,
        width: rect.width,
        top: rect.top,
        bottom: rect.bottom,
        height: rect.height,
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        scrollLeft: element.scrollLeft
      };
    };
    const frame = document.querySelector<HTMLElement>(".main-frame")!;
    const actions = document.querySelector<HTMLElement>(".topbar-actions")!;
    const content = document.querySelector<HTMLElement>(".content")!;
    const bodyStyle = getComputedStyle(document.body);
    const describe = (selector: string) => {
      const element = document.querySelector<HTMLElement>(selector);
      return element ? dimensions(element) : null;
    };
    const describeAll = (selector: string) => Array.from(document.querySelectorAll<HTMLElement>(selector), dimensions);
    return {
      layout: {
        navigation: document.documentElement.getAttribute("data-nav-mode"),
        appearance: document.documentElement.getAttribute("data-theme-appearance"),
        fonts: document.fonts.status,
        body: { ...dimensions(document.body), margin: bodyStyle.margin, padding: bodyStyle.padding, overflow: bodyStyle.overflow },
        shell: dimensions(document.querySelector<HTMLElement>(".studio-shell")!),
        topbar: dimensions(document.querySelector<HTMLElement>(".topbar")!),
        statusbar: describe(".studio-statusbar"),
        visualViewport: { width: window.visualViewport?.width, offsetLeft: window.visualViewport?.offsetLeft }
      },
      document: { scrollX: window.scrollX, clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth },
      frame: dimensions(frame),
      actions: dimensions(actions),
      content: dimensions(content),
      controls: {
        weaver: describe(".topbar-actions .agent-launcher"),
        github: describe('.topbar-actions a[aria-label="GitHub"]'),
        designer: describe('.topbar-actions a[aria-label="Designer source"]'),
        appearance: describe(".topbar-actions .theme-selector-button"),
        account: describe(".topbar-actions .user-menu-button")
      },
      weaverBadges: describeAll(".topbar-actions .agent-session-indicator > span")
    };
  });
}

async function attachShellGeometry(label: string, geometry: unknown, screenshot: Buffer) {
  await test.info().attach(`${label}-geometry.json`, { body: JSON.stringify(geometry, null, 2), contentType: "application/json" });
  await test.info().attach(`${label}.png`, { body: screenshot, contentType: "image/png" });
}

function expectHeaderToFit(geometry: Awaited<ReturnType<typeof readShellGeometry>>, width: number) {
  const diagnostic = JSON.stringify(geometry, null, 2);
  for (const element of [geometry.document, geometry.layout.shell, geometry.frame, geometry.actions]) {
    expect(element.scrollWidth, diagnostic).toBe(element.clientWidth);
  }
  for (const control of [geometry.frame, geometry.layout.topbar, geometry.layout.statusbar, ...Object.values(geometry.controls)]) {
    if (!control) continue;
    expect(control.left, diagnostic).toBeGreaterThanOrEqual(0);
    expect(control.right, diagnostic).toBeLessThanOrEqual(width);
  }
}

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

test.describe("shell user menu", () => {
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
    await expect(page.locator(".user-menu-button:visible")).toHaveAttribute("aria-expanded", "true");
    await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(page.getByRole("menu")).toHaveCount(0);
    await expect(account(page)).toBeFocused();
  });

  test("keeps left-nav account access visible in the top bar on narrow screens", async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 900 });
    await openShell(page);

    await expect(page.locator(".sidebar-account .user-menu-button")).toBeHidden();
    await expect(account(page)).toBeVisible();
    await account(page).click();
    await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeVisible();
  });

  for (const width of [320, 360]) {
    test(`account action fits and preserves shell position at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await page.emulateMedia({ reducedMotion: "reduce" });
      const displayName = "Administrator";
      await openShell(page, { displayName, navigation: "left", weaver: true });

      const before = await readShellGeometry(page);
      const beforeScreenshot = await page.screenshot();
      const accountBounds = await account(page, displayName).boundingBox();
      const launcherBounds = await page.locator(".agent-launcher").boundingBox();
      expect(accountBounds).not.toBeNull();
      expect(launcherBounds).not.toBeNull();
      const launcher = page.locator(".agent-launcher");
      await expect(launcher).toBeVisible();
      await expect(page.locator(".agent-session-indicator")).toContainText("1 waiting");
      await page.mouse.click(launcherBounds!.x + launcherBounds!.width / 2, launcherBounds!.y + launcherBounds!.height / 2);
      await expect(launcher).toHaveAttribute("aria-expanded", "true");
      await page.mouse.click(launcherBounds!.x + launcherBounds!.width / 2, launcherBounds!.y + launcherBounds!.height / 2);
      await expect(launcher).toHaveAttribute("aria-expanded", "false");
      const afterWeaver = await readShellGeometry(page);
      const afterWeaverScreenshot = await page.screenshot();

      // Use a physical pointer at the button center; locator.click() can itself scroll an offscreen target.
      await page.mouse.click(Math.min(accountBounds!.x + accountBounds!.width / 2, width - 2), accountBounds!.y + accountBounds!.height / 2);
      await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeVisible();
      const afterOpen = await readShellGeometry(page);
      await page.keyboard.press("Escape");
      await expect(page.getByRole("menu")).toHaveCount(0);
      await expect(account(page, displayName)).toBeFocused();
      const afterClose = await readShellGeometry(page);

      await attachShellGeometry(`account-scroll-${width}-before`, before, beforeScreenshot);
      await attachShellGeometry(`account-scroll-${width}-after-weaver`, afterWeaver, afterWeaverScreenshot);
      await attachShellGeometry(`account-scroll-${width}-after-close`, afterClose, await page.screenshot());

      const controls = Object.values(before.controls).filter((control): control is NonNullable<typeof control> => control !== null);
      const measuredOutcome = {
        allDirectControlsWithinViewport: controls.every(control => control.left >= 0 && control.right <= width),
        actionsHaveNoHorizontalOverflow: before.actions.scrollWidth === before.actions.clientWidth,
        frameHasNoHorizontalOverflow: before.frame.scrollWidth === before.frame.clientWidth,
        documentHasNoHorizontalOverflow: before.document.scrollWidth === before.document.clientWidth,
        documentScrollAfterWeaver: afterWeaver.document.scrollX,
        documentScrollAfterOpen: afterOpen.document.scrollX,
        documentScrollAfterEscape: afterClose.document.scrollX,
        frameScrollAfterWeaver: afterWeaver.frame.scrollLeft,
        contentLeftAfterWeaver: afterWeaver.content.left,
        frameScrollAfterOpen: afterOpen.frame.scrollLeft,
        frameScrollAfterEscape: afterClose.frame.scrollLeft,
        contentLeftAfterEscape: afterClose.content.left
      };
      expect(measuredOutcome, JSON.stringify({ before, afterOpen, afterClose }, null, 2)).toEqual({
        allDirectControlsWithinViewport: true,
        actionsHaveNoHorizontalOverflow: true,
        frameHasNoHorizontalOverflow: true,
        documentHasNoHorizontalOverflow: true,
        documentScrollAfterWeaver: before.document.scrollX,
        documentScrollAfterOpen: before.document.scrollX,
        documentScrollAfterEscape: before.document.scrollX,
        frameScrollAfterWeaver: before.frame.scrollLeft,
        contentLeftAfterWeaver: before.content.left,
        frameScrollAfterOpen: before.frame.scrollLeft,
        frameScrollAfterEscape: before.frame.scrollLeft,
        contentLeftAfterEscape: before.content.left
      });
      await expect(page.locator(".theme-mode-toggle")).toBeHidden();
      await expect(page.getByRole("button", { name: "Select theme" })).toBeVisible();
      await expect(page.locator(".topbar-actions .user-menu-name")).toBeHidden();
      await expect(account(page, displayName)).toHaveAttribute("aria-label", `Account: ${displayName}`);

      await launcher.focus();
      await page.keyboard.press("Tab");
      await expect(page.getByRole("link", { name: "GitHub" })).toBeFocused();
      await page.keyboard.press("Tab");
      await expect(page.getByRole("link", { name: "Designer source" })).toBeFocused();
      await page.keyboard.press("Tab");
      await expect(page.getByRole("button", { name: "Select theme" })).toBeFocused();
      await page.keyboard.press("Tab");
      await expect(account(page, displayName)).toBeFocused();

      const themeTrigger = page.getByRole("button", { name: "Select theme" });
      await themeTrigger.focus();
      await page.keyboard.press("Enter");
      const themeMenu = page.locator(".theme-dropdown");
      await expect(themeMenu).toBeVisible();
      for (const mode of ["Light", "Dark", "Dim", "High contrast"]) {
        await expect(themeMenu.getByRole("menuitemradio", { name: mode })).toBeVisible();
      }
      const themeBounds = await themeMenu.boundingBox();
      expect(themeBounds).not.toBeNull();
      expect(themeBounds!.x).toBeGreaterThanOrEqual(0);
      expect(themeBounds!.x + themeBounds!.width).toBeLessThanOrEqual(width);
      for (const navigation of ["Theme default", "Left", "Top"]) {
        await expect(themeMenu.getByRole("menuitemradio", { name: navigation })).toBeVisible();
      }
      await expect(themeMenu.getByRole("menuitemradio", { name: "Light" })).toHaveAttribute("aria-checked", "true");
      await themeMenu.getByRole("menuitemradio", { name: "Dark" }).click();
      await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "dark");
      await expect(themeMenu.getByRole("menuitemradio", { name: "Dark" })).toHaveAttribute("aria-checked", "true");
      for (const navigation of ["Top", "Theme default", "Left"]) {
        await themeMenu.getByRole("menuitemradio", { name: navigation }).click();
        await expect(themeMenu.getByRole("menuitemradio", { name: navigation })).toHaveAttribute("aria-checked", "true");
        await expect(page.locator("html")).toHaveAttribute("data-nav-mode", navigation === "Top" ? "top" : "left");
        await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
        const afterNavigation = await readShellGeometry(page);
        expectHeaderToFit(afterNavigation, width);
        expect(afterNavigation.frame.scrollLeft).toBe(before.frame.scrollLeft);
        expect(afterNavigation.content.left).toBe(before.content.left);
      }
      await themeMenu.getByRole("menuitemradio", { name: "High contrast" }).click();
      await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "high-contrast");
      await expect(themeMenu.getByRole("menuitemradio", { name: "High contrast" })).toHaveAttribute("aria-checked", "true");
      await page.keyboard.press("Escape");
      await expect(page.getByRole("menu")).toHaveCount(0);
      await expect(themeTrigger).toBeFocused();
    });
  }

  for (const width of [640, 641, 1024, 1025]) {
    test(`changes narrow header density at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await openShell(page, { displayName: "Administrator", weaver: true });
      const geometry = await readShellGeometry(page);
      expectHeaderToFit(geometry, width);
      if (width > 640) {
        await expect(page.locator(".theme-mode-toggle")).toBeVisible();
        await expect(page.locator(`${width > 1024 ? ".sidebar-account" : ".topbar-actions"} .user-menu-name`)).toBeVisible();
      } else {
        await expect(page.locator(".theme-mode-toggle")).toBeHidden();
        await expect(page.locator(".topbar-actions .user-menu-name")).toBeHidden();
      }
      const accountBounds = await account(page, "Administrator").boundingBox();
      expect(accountBounds).not.toBeNull();
      expect(accountBounds!.x).toBeGreaterThanOrEqual(0);
      expect(accountBounds!.x + accountBounds!.width).toBeLessThanOrEqual(width);
    });
  }

  test("selects appearance options and signs out with the keyboard at 320px", async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    const state = await openShell(page);
    const themeTrigger = page.getByRole("button", { name: "Select theme" });
    await themeTrigger.focus();
    await page.keyboard.press("Enter");
    const themeMenu = page.locator(".theme-dropdown");
    await expect(themeMenu.getByRole("menuitemradio", { name: "Light", exact: true })).toBeFocused();
    await page.keyboard.press("ArrowDown");
    await expect(themeMenu.getByRole("menuitemradio", { name: "Dark", exact: true })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "dark");

    await page.keyboard.press("Home");
    await expect(themeMenu.getByRole("menuitemradio", { name: "Light", exact: true })).toBeFocused();
    for (const name of ["Dark", "Dim", "High contrast", "Theme default", "Left", "Top"]) {
      await page.keyboard.press("ArrowDown");
      await expect(themeMenu.getByRole("menuitemradio", { name, exact: true })).toBeFocused();
    }
    const top = themeMenu.getByRole("menuitemradio", { name: "Top", exact: true });
    await expect(top).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(top).toHaveAttribute("aria-checked", "true");
    await expect(page.locator("html")).toHaveAttribute("data-nav-mode", "top");
    expectHeaderToFit(await readShellGeometry(page), 320);

    await page.keyboard.press("m");
    await expect(themeMenu.getByRole("menuitem", { name: "Meridian", exact: true })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.locator("html")).toHaveAttribute("data-theme", "meridian");
    await expect(themeMenu).toBeHidden();
    await expect(themeTrigger).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(themeMenu.getByRole("menuitemradio", { name: "Light", exact: true })).toBeFocused();
    await page.keyboard.press("c");
    await expect(themeMenu.getByRole("menuitem", { name: "Classic", exact: true })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.locator("html")).toHaveAttribute("data-theme", "classic");
    await expect(themeTrigger).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(account(page)).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("menuitem", { name: "Sign out", exact: true })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Backend sign-in page" })).toBeVisible();
    expect(state.logoutCalls).toEqual([`/_elsa/identity/logout/${provider}`]);
  });

  for (const width of [320, 360]) {
    test(`keeps top navigation within ${width}px with a long backend endpoint`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await openShell(page, { backend: "long" });
      const before = await readShellGeometry(page);
      await page.getByRole("button", { name: "Select theme" }).click();
      await page.getByRole("menuitemradio", { name: "Top", exact: true }).click();
      await expect(page.locator("html")).toHaveAttribute("data-nav-mode", "top");
      const status = page.locator(".studio-statusbar");
      await expect(status).toContainText("workflow-runtime.internal.example.org:18443");
      await expect(status.locator("span[title]")).toHaveAttribute("title", "Configured backend endpoint; connectivity is not measured");
      const after = await readShellGeometry(page);
      await attachShellGeometry("long-backend-top-navigation", after, await page.screenshot());
      expectHeaderToFit(after, width);
      expect(after.frame.scrollLeft).toBe(before.frame.scrollLeft);
      expect(after.content.left).toBe(before.content.left);
      await page.keyboard.press("Escape");
      await expect(page.getByRole("button", { name: "Select theme" })).toBeFocused();
    });
  }

  test("keeps a dense Weaver status summary and each badge inside the 320px viewport", async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    await openShell(page, { displayName: "Administrator", weaver: "dense" });
    const geometry = await readShellGeometry(page);
    await attachShellGeometry("dense-weaver-320", geometry, await page.screenshot());
    const indicator = page.locator(".agent-session-indicator");
    await expect(indicator).toHaveAttribute("aria-label", "1 active or background Weaver sessions, 2 waiting, 1 blocked, 1 failed, 3 pending proposals, 2 pending artifacts, 1 pending prompts, 1 child sessions");
    expect(geometry.controls.weaver).not.toBeNull();
    expectHeaderToFit(geometry, 320);
    expect(geometry.weaverBadges).toHaveLength(8);
    for (const badge of geometry.weaverBadges) {
      expect(badge.left).toBeGreaterThanOrEqual(0);
      expect(badge.right).toBeLessThanOrEqual(320);
      expect(badge.top).toBeGreaterThanOrEqual(geometry.controls.weaver!.top);
      expect(badge.bottom).toBeLessThanOrEqual(geometry.controls.weaver!.bottom);
    }
  });

  test("keeps remaining controls usable when the shell omits appearance and account actions", async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    await openShell(page, { account: false, appearance: false, weaver: true });
    await expect(page.locator(".user-menu-button")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Select theme" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "GitHub" })).toHaveAttribute("href", "https://github.com/elsa-workflows/elsa-foundation-studio");
    await expect(page.getByRole("link", { name: "Designer source" })).toHaveAttribute("href", "https://github.com/elsa-workflows/elsa-foundation-designer");
    expectHeaderToFit(await readShellGeometry(page), 320);
  });

  test("fits the anonymous shell without an account action", async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    await openShell(page, { authEnabled: false, weaver: true });
    await expect(page.locator(".user-menu-button")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Select theme" })).toBeVisible();
    expectHeaderToFit(await readShellGeometry(page), 320);
  });

  test("preserves the desktop sidebar account and labeled quick controls", async ({ page }) => {
    for (const width of [1280, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      await openShell(page, { displayName: "Administrator", weaver: true });
      await expect(page.locator(".sidebar-account .user-menu-button")).toBeVisible();
      await expect(page.locator(".topbar-actions .user-menu-button")).toBeHidden();
      await expect(page.locator(".theme-mode-toggle")).toBeVisible();
      await expect(page.locator(".sidebar-account .user-menu-name")).toBeVisible();
      const bounds = await page.locator(".sidebar-account .user-menu-button").boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
      expect(bounds!.y).toBeGreaterThanOrEqual(0);
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(900);
      await test.info().attach(`desktop-account-${width}.json`, { body: JSON.stringify(bounds), contentType: "application/json" });
    }
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
      await openShell(page, { theme });
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
