import { writeFileSync } from "node:fs";
import { expect, test, type Browser, type Locator, type Page, type Route, type TestInfo } from "@playwright/test";

// Supplementary composed journeys for grouped navigation (#3863). Each runs once in its own project
// (playwright.config.ts) against hosts tools/ux-acceptance/run.py owns; none creates definitions or other
// store records, so the owned-definition cleanup in journeys.spec.ts has nothing to remove for them.

interface PointerRecord { pointerType: string; x: number; y: number; target: string }
declare global {
  interface Window { __uxPointer?: PointerRecord[] }
}

const admin = { username: "admin", password: "Password123!" };
const pageErrors = new WeakMap<Page, string[]>();

function required(name: string) {
  const value = process.env[name];
  if (!value || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error(`${name} is required from the owned acceptance runner`);
  return value;
}

async function attachJson(info: TestInfo, name: string, value: unknown) {
  const file = info.outputPath(name);
  writeFileSync(file, JSON.stringify(value, null, 2) + "\n");
  await info.attach(name, { path: file, contentType: "application/json" });
}

async function pointerMedia(page: Page) {
  return page.evaluate(() => ({
    coarse: matchMedia("(pointer: coarse)").matches,
    fine: matchMedia("(pointer: fine)").matches,
    hoverNone: matchMedia("(hover: none)").matches,
    maxTouchPoints: navigator.maxTouchPoints,
    touchEvents: "ontouchstart" in window,
    innerWidth,
    innerHeight
  }));
}

async function capture(page: Page, info: TestInfo, name: string) {
  if (info.project.use.hasTouch) expect(await pointerMedia(page), "Coarse pointer emulation stays active").toMatchObject({ coarse: true, fine: false });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const touch = Boolean(info.project.use.hasTouch);
  const width = page.viewportSize()?.width ?? 0;
  if (touch || width <= 1024) {
    // Touch: no two navigation targets may overlap in either axis. An open top-navigation panel or search result
    // surface is an overlay, so its targets are compared with each other rather than with the strip underneath.
    // Fine-pointer narrow layouts keep the ordinary journeys' horizontal-strip rule.
    const overlaps = await page.locator(".sidebar .nav-section a, .sidebar .nav-group-toggle").evaluateAll((nodes, coarse) => {
      const top = document.documentElement.dataset.navMode === "top";
      const targets = nodes.map(node => {
        const overlay = top ? node.closest(".nav-group-panel, .nav-search-results") : null;
        return { text: node.textContent?.trim() ?? "", layer: overlay ? overlay.id || overlay.className : "sidebar", box: node.getBoundingClientRect() };
      }).filter(item => item.box.width > 0 && item.box.height > 0);
      if (!coarse) {
        const strip = targets.sort((a, b) => a.box.left - b.box.left);
        return strip.slice(1).flatMap((item, index) => item.box.left < strip[index].box.right - 1 ? [`${strip[index].text} overlaps ${item.text}`] : []);
      }
      return targets.flatMap((a, index) => targets.slice(index + 1).filter(b => a.layer === b.layer &&
        Math.min(a.box.right, b.box.right) - Math.max(a.box.left, b.box.left) > 1 &&
        Math.min(a.box.bottom, b.box.bottom) - Math.max(a.box.top, b.box.top) > 1).map(b => `${a.text} overlaps ${b.text}`));
    }, touch);
    expect(overlaps, "Navigation targets must not overlap").toEqual([]);
  }
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

async function signIn(page: Page, path: string, username: string, password: string, ready: Locator, touch = false) {
  await page.goto(path);
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill(username);
  await form.getByLabel(/password/i).fill(password);
  const submit = form.getByRole("button", { name: /sign in/i });
  if (touch) await submit.tap();
  else await submit.click();
  await expect(ready).toBeVisible({ timeout: 60_000 });
  await expect(page.locator(".sidebar")).toBeVisible();
}

async function keyboardTo(page: Page, target: Locator, limit = 150) {
  await expect(target).toBeVisible();
  for (let index = 0; index < limit; index++) {
    if (await target.evaluate(node => node === document.activeElement)) return;
    await page.keyboard.press("Tab");
  }
  await expect(target).toBeFocused();
}

async function capabilities(page: Page, backend: string, headers?: Record<string, string>) {
  const response = await page.request.get(`${backend}/capabilities`, { headers });
  const body = await response.json();
  return { status: response.status(), body: body as { capabilities: Array<{ id: string; status?: string | null; links: Array<{ rel: string; href: string; templated?: boolean }> }> } };
}

function relation(document: Awaited<ReturnType<typeof capabilities>>["body"], id: string, rel: string, backend: string) {
  const capability = document.capabilities.find(item => item.id === id && !item.status);
  expect(capability, `${id} is available in the actual capability document`).toBeTruthy();
  const links = capability!.links.filter(link => link.rel === rel && !link.templated);
  expect(links, `${id} advertises exactly one ${rel}`).toHaveLength(1);
  return new URL(links[0].href, `${backend}/`);
}

test.beforeEach(async ({ page }, info) => {
  await page.addInitScript(({ mode }) => {
    localStorage.removeItem("elsa-studio-theme");
    localStorage.setItem("elsa-studio-workspace-style", "linen");
    localStorage.setItem("elsa-studio-theme-mode", mode);
    const log: PointerRecord[] = [];
    Object.defineProperty(window, "__uxPointer", { value: log, configurable: false });
    addEventListener("pointerdown", event => log.push({
      pointerType: event.pointerType, x: Math.round(event.clientX), y: Math.round(event.clientY),
      target: event.target instanceof Element ? event.target.tagName : ""
    }), true);
  }, { mode: info.project.use.colorScheme ?? "light" });
  const errors: string[] = [];
  pageErrors.set(page, errors);
  page.on("pageerror", error => errors.push(error.message));
  await info.attach("source", { body: process.env.UX_SOURCE_SHA ?? "unknown", contentType: "text/plain" });
});

test.afterEach(async ({ page }) => {
  expect(pageErrors.get(page), "Uncaught browser errors").toEqual([]);
});

test("coarse pointer grouped navigation", async ({ page }, info) => {
  const evidence: { targets: unknown[]; taps: unknown[]; dismissal?: unknown } = { targets: [], taps: [] };
  const sidebar = page.locator(".sidebar");
  const group = (name: string) => sidebar.getByRole("button", { name, exact: true });
  const link = (name: string) => sidebar.getByRole("link", { name, exact: true });
  const search = sidebar.getByRole("searchbox", { name: "Search Studio", exact: true });
  const measure = async (target: Locator, label: string, minimum = true) => {
    await target.scrollIntoViewIfNeeded();
    await expect(target).toBeInViewport({ ratio: 1 });
    const data = await target.evaluate(node => {
      const box = node.getBoundingClientRect();
      const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
      const labels = node instanceof HTMLInputElement ? Array.from(node.labels ?? []) : [];
      return {
        text: node.textContent?.trim() ?? "", width: box.width, height: box.height, left: box.left, top: box.top,
        unobscured: Boolean(hit && (hit === node || node.contains(hit) || labels.some(label => label.contains(hit)))),
        coarse: matchMedia("(pointer: coarse)").matches
      };
    });
    evidence.targets.push({ label, minimum, ...data });
    expect(data.coarse, `${label}: coarse pointer media`).toBe(true);
    expect(data.unobscured, `${label}: hit target is not obscured`).toBe(true);
    if (minimum) {
      expect(data.height, `${label}: coarse hit-target height`).toBeGreaterThanOrEqual(44);
      expect(data.width, `${label}: coarse hit-target width`).toBeGreaterThanOrEqual(44);
    }
  };
  const tap = async (target: Locator, label: string, minimum = true) => {
    await measure(target, label, minimum);
    const before = await page.evaluate(() => window.__uxPointer!.length);
    await target.tap();
    const events = await page.evaluate(from => window.__uxPointer!.slice(from), before);
    evidence.taps.push({ label, events });
    expect(events.some(event => event.pointerType === "touch"), `${label}: tapped with a touch pointer`).toBe(true);
  };

  await signIn(page, "/workflows/definitions", admin.username, admin.password,
    page.getByRole("heading", { name: "Workflow definitions", exact: true }), true);
  const device = await pointerMedia(page);
  await attachJson(info, "touch-device.json", { requested: info.project.use, actual: device });
  expect(device).toMatchObject({ coarse: true, fine: false, touchEvents: true, innerWidth: info.project.use.viewport!.width });
  expect(device.maxTouchPoints).toBeGreaterThan(0);

  // Changed controls: group disclosures, primary destinations and search meet the 44px coarse target.
  for (const name of ["Build", "Operate", "Integrations"]) {
    await expect(group(name)).toHaveAttribute("aria-expanded", "true");
    await measure(group(name), `${name} group`);
  }
  await measure(link("Home"), "Home");
  await measure(link("Settings"), "Settings");
  await measure(sidebar.locator("label.sidebar-search"), "Search Studio field");
  await capture(page, info, "touch-groups-open");

  await tap(group("Build"), "Build collapse");
  await expect(group("Build")).toHaveAttribute("aria-expanded", "false");
  await expect(link("Workflows")).toBeHidden();
  await capture(page, info, "touch-group-collapsed");
  await tap(group("Build"), "Build expand");
  await expect(group("Build")).toHaveAttribute("aria-expanded", "true");
  await tap(link("Workflows"), "Workflows");
  const views = page.getByRole("navigation", { name: "Workflows views", exact: true });
  await tap(views.getByRole("link", { name: "Definitions", exact: true }), "Definitions view");
  await tap(views.getByRole("link", { name: "Executables", exact: true }), "Executables view");
  await expect(page).toHaveURL(/\/workflows\/executables(?:\?|$)/);
  await expect(link("Workflows")).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("main").getByRole("heading", { name: "Executables", level: 2, exact: true })).toBeVisible();
  await expect(page.getByRole("table", { name: "Workflow executables", exact: true })
    .or(page.getByRole("heading", { name: "No workflow executables", exact: true }))).toBeVisible();
  await capture(page, info, "touch-local-views");

  await tap(link("Settings"), "Settings entry");
  const sections = page.getByRole("navigation", { name: "Settings sections", exact: true });
  // Settings-shell section links are existing controls; they are tapped and recorded, not held to the new 44px rule.
  await tap(sections.getByRole("link", { name: "Runtime evidence settings", exact: true }), "Runtime evidence settings section", false);
  await expect(page.locator(".settings-pane").getByRole("heading", { name: "Runtime evidence settings", exact: true })).toBeVisible();
  await expect(link("Settings")).toHaveAttribute("aria-current", "page");
  await capture(page, info, "touch-settings-runtime");

  await tap(sections.getByRole("link", { name: "Appearance", exact: true }), "Appearance section", false);
  await tap(page.getByRole("radiogroup", { name: "Navigation", exact: true }).getByRole("radio", { name: "Top", exact: true }), "Top navigation choice", false);
  await expect(page.locator("html")).toHaveAttribute("data-nav-mode", "top");
  await tap(group("Build"), "Top Build open");
  await expect(group("Build")).toHaveAttribute("aria-expanded", "true");
  const buildMenu = sidebar.getByRole("navigation", { name: "Build", exact: true });
  await measure(buildMenu.getByRole("link", { name: "Workflows", exact: true }), "Top Build Workflows destination");
  await capture(page, info, "touch-top-group-open");
  await tap(group("Build"), "Top Build close");
  await expect(group("Build")).toHaveAttribute("aria-expanded", "false");

  await measure(sidebar.locator("label.sidebar-search"), "Top search field");
  await tap(search, "Search Studio input", false);
  await search.fill("templates");
  const results = page.getByRole("navigation", { name: "Search results", exact: true });
  const templates = results.getByRole("link", { name: "Templates", exact: true });
  await measure(templates, "Templates search result");
  await capture(page, info, "touch-top-search");
  await tap(templates, "Templates search result");
  await expect(page.getByRole("navigation", { name: "Library views", exact: true }).getByRole("link", { name: "Templates", exact: true })).toHaveAttribute("aria-current", "page");

  await tap(search, "Search Studio input again", false);
  const stripHeight = await sidebar.evaluate(node => node.getBoundingClientRect().height);
  const absent = "no-such-navigation-coarse-pointer";
  await search.fill(absent);
  const noMatch = results.getByRole("status");
  await expect(noMatch).toHaveText(`No destinations match "${absent}".`);
  await expect(noMatch).toBeInViewport({ ratio: 1 });
  expect(await noMatch.evaluate(node => {
    const range = document.createRange(); range.selectNodeContents(node);
    const surface = node.closest(".nav-search-results")!.getBoundingClientRect();
    return Array.from(range.getClientRects()).every(rect => rect.left >= 0 && rect.right <= innerWidth &&
      rect.top >= 0 && rect.bottom <= innerHeight && rect.left >= surface.left && rect.right <= surface.right &&
      rect.top >= surface.top && rect.bottom <= surface.bottom &&
      node.contains(document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2))) &&
      node.scrollWidth <= node.clientWidth + 1;
  }), "The complete no-match message must wrap inside the visible search surface").toBe(true);
  expect(Math.abs(await sidebar.evaluate(node => node.getBoundingClientRect().height) - stripHeight), "No-match feedback must not resize the strip").toBeLessThanOrEqual(1);
  await capture(page, info, "touch-search-no-match");

  // A tap on inert page content outside the search surface dismisses results and keeps the query.
  const outside = await page.evaluate(() => {
    const main = document.querySelector("main, [role=main]");
    const surface = document.querySelector(".nav-search-results");
    if (!main) return null;
    const box = main.getBoundingClientRect();
    const interactive = "a, button, input, select, textarea, label, summary, [role=button], [role=link], [role=tab], [tabindex]:not([tabindex='-1'])";
    for (let y = Math.max(box.top, 0) + 8; y < Math.min(box.bottom, innerHeight) - 4; y += 12) {
      for (let x = Math.max(box.left, 0) + 8; x < Math.min(box.right, innerWidth) - 4; x += 12) {
        const hit = document.elementFromPoint(x, y);
        if (!hit || !main.contains(hit) || surface?.contains(hit) || hit.closest(interactive)) continue;
        return { x, y, target: hit.tagName };
      }
    }
    return null;
  });
  expect(outside, "An inert point outside the search surface").not.toBeNull();
  const beforeOutside = await page.evaluate(() => window.__uxPointer!.length);
  await page.touchscreen.tap(outside!.x, outside!.y);
  const outsideEvents = await page.evaluate(from => window.__uxPointer!.slice(from), beforeOutside);
  evidence.dismissal = { point: outside, events: outsideEvents };
  expect(outsideEvents.some(event => event.pointerType === "touch")).toBe(true);
  await expect(results).toBeHidden();
  await expect(search).toHaveValue(absent);
  await capture(page, info, "touch-search-outside-dismissed");

  // Keyboard operation recovers after touch use: results, the top menu and its return focus.
  await search.fill("templates");
  await expect(templates).toBeVisible();
  await search.press("Tab");
  await expect(templates).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(results).toBeHidden();
  await expect(search).toBeFocused();
  await expect(search).toHaveValue("templates");
  await search.fill("");
  await keyboardTo(page, group("Build"));
  await page.keyboard.press("Enter");
  await expect(group("Build")).toHaveAttribute("aria-expanded", "true");
  await keyboardTo(page, buildMenu.getByRole("link", { name: "Workflows", exact: true }));
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/\/workflows\/definitions(?:\?|$)/);
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible();
  await expect(group("Build")).toHaveAttribute("aria-expanded", "false");
  await keyboardTo(page, group("Build"));
  await page.keyboard.press("Enter");
  await expect(group("Build")).toHaveAttribute("aria-expanded", "true");
  await page.keyboard.press("Escape");
  await expect(group("Build")).toHaveAttribute("aria-expanded", "false");
  await expect(group("Build")).toBeFocused();
  await capture(page, info, "touch-keyboard-recovery");
  await attachJson(info, "touch-evidence.json", { ...evidence, finalMedia: await pointerMedia(page) });
});

test("restricted principal settings access", async ({ page }, info) => {
  const backend = required("ELSA_WORKBENCH_URL");
  const username = required("UX_RESTRICTED_USER");
  const password = required("UX_RESTRICTED_PASSWORD");
  const subject = required("UX_RESTRICTED_SUBJECT");
  const backendOrigin = new URL(backend).origin;
  let bearer: string | undefined;
  page.on("request", request => {
    const authorization = request.headers().authorization;
    if (authorization?.startsWith("Bearer ") && new URL(request.url()).origin === backendOrigin) bearer = authorization;
  });
  // Record every page request from the start; only the sign-in form POST itself may write.
  const writes: Array<{ method: string; url: string }> = [];
  let signInPosts = 0, observed = 0;
  const record = (request: { method(): string; url(): string }) => {
    const url = new URL(request.url());
    if (request.method() === "POST" && url.pathname === "/_elsa/identity/login" && observed === 0) { signInPosts++; return; }
    if (signInPosts === 0) return;
    observed++;
    if (!["GET", "HEAD", "OPTIONS"].includes(request.method())) writes.push({ method: request.method(), url: `${url.origin}${url.pathname}` });
  };
  page.on("request", record);
  const pane = page.locator(".settings-pane");
  await signIn(page, "/settings/appearance", username, password, pane.getByRole("heading", { name: "Appearance", exact: true }));
  expect(signInPosts, "Exactly one sign-in submission").toBe(1);

  // The actual session the real claims projection produced for the seeded reader.
  const sessionResponse = await page.request.get(`${backend}/_elsa/identity/session`);
  const session = await sessionResponse.json();
  await attachJson(info, "restricted-session.json", { status: sessionResponse.status(), body: session });
  expect(sessionResponse.status()).toBe(200);
  expect(session).toMatchObject({ status: "authenticated", subject });
  expect([...session.permissions].sort()).toEqual(["api-capabilities:read", "workflows/execution-evidence:read", "workflows/runtime:read"]);
  expect(session.roles).toEqual([]);
  // Studio's own bearer for this principal; the token value is never retained in evidence.
  await expect.poll(() => bearer, { message: "Studio called the backend with a bearer token", timeout: 30_000 }).toBeTruthy();
  const headers = { Authorization: bearer! };
  const discovered = await capabilities(page, backend, headers);
  await attachJson(info, "restricted-capabilities.json", discovered);
  expect(discovered.status).toBe(200);
  const catalogUrl = `${backend}/_elsa/identity/capabilities`;
  const catalog = await page.request.get(catalogUrl, { headers, maxRedirects: 0 });
  await attachJson(info, "identity-capabilities.json", { url: catalogUrl, method: "GET", status: catalog.status(), body: await catalog.text(),
    expectation: "identity/providers:read is absent from the actual session, so the protected provider catalog denies it" });
  expect(catalog.status(), "The identity-provider catalog denies the authenticated reader").toBe(403);
  const settingsUrl = relation(discovered.body, "elsa.api.runtime", "runtime-diagnostics", backend);
  const settingsResponse = await page.request.get(settingsUrl.href, { headers });
  const runtimeSettings = await settingsResponse.json();
  await attachJson(info, "runtime-settings.json", { url: settingsUrl.href, status: settingsResponse.status(), body: runtimeSettings });
  expect(settingsResponse.status()).toBe(200);
  expect(runtimeSettings.permissions.canManage).toBe(false);

  const sections = page.getByRole("navigation", { name: "Settings sections", exact: true });
  await expect(sections.getByRole("link", { name: "Appearance", exact: true })).toHaveAttribute("aria-current", "page");
  await expect(sections.getByRole("link", { name: "Features", exact: true })).toHaveCount(0);
  await expect(sections.getByText("Administration", { exact: true })).toHaveCount(0);
  await capture(page, info, "restricted-appearance");
  const denials: Record<string, string> = {};
  for (const [bookmark, name] of [["/settings/feature-management", "restricted-features-settings-denied"], ["/features", "restricted-features-bookmark-denied"]]) {
    await page.goto(bookmark);
    const denied = pane.locator(".empty-state[role=status]").filter({ visible: true });
    await expect(denied).toHaveText("You don't have access to this section.");
    await expect(sections.getByRole("link", { name: "Features", exact: true })).toHaveCount(0);
    denials[bookmark] = (await denied.textContent()) ?? "";
    await capture(page, info, name);
    const appearance = sections.getByRole("link", { name: "Appearance", exact: true });
    await keyboardTo(page, appearance);
    await page.keyboard.press("Enter");
    await expect(pane.getByRole("heading", { name: "Appearance", exact: true })).toBeVisible();
  }
  await keyboardTo(page, sections.getByRole("link", { name: "Runtime evidence settings", exact: true }));
  await page.keyboard.press("Enter");
  await expect(pane.getByRole("heading", { name: "Runtime evidence settings", exact: true })).toBeVisible();
  // Loaded from the real runtime settings response (not the pre-load disabled state), then read-only for this reader.
  await expect(pane.getByText(/^Maximum allowed by Host Policy: /)).toBeVisible();
  await expect(pane.getByText("Current user can view settings but cannot change them.", { exact: true })).toBeVisible();
  const save = pane.getByRole("button", { name: "Save changes", exact: true });
  await expect(save).toBeDisabled();
  for (const role of ["radio", "combobox"] as const) {
    const controls = pane.getByRole(role);
    expect(await controls.count(), `Runtime settings render ${role} level controls`).toBeGreaterThan(0);
    for (const control of await controls.all()) await expect(control).toBeDisabled();
  }
  // Even a forced activation of the disabled Save cannot submit.
  await save.click({ force: true });
  await capture(page, info, "restricted-runtime-readonly");
  const appearance = sections.getByRole("link", { name: "Appearance", exact: true });
  await keyboardTo(page, appearance);
  await page.keyboard.press("Enter");
  await expect(pane.getByRole("heading", { name: "Appearance", exact: true })).toBeVisible();
  await expect(appearance).toHaveAttribute("aria-current", "page");
  await capture(page, info, "restricted-keyboard-recovery");
  page.off("request", record);
  await attachJson(info, "restricted-network.json", { signInPosts, observedRequestsAfterSignIn: observed, writes, denials, expectedWritesAfterSignIn: [] });
  expect(observed, "The browsing after sign-in made real requests").toBeGreaterThan(0);
  expect(writes, "No write request after sign-in").toEqual([]);
});

test("activity availability load failures and recovery", async ({ page }, info) => {
  const backend = required("ELSA_WORKBENCH_URL");
  const pane = page.locator(".settings-pane");
  await signIn(page, "/workflows/definitions", admin.username, admin.password, page.getByRole("heading", { name: "Workflow definitions", exact: true }));
  const discovered = await capabilities(page, backend);
  expect(discovered.status).toBe(200);
  const targets = ["activity-availability", "activity-availability-diagnostics"].map(rel => relation(discovered.body, "elsa.api.activity-design", rel, backend));
  const paths = targets.map(url => url.pathname);
  const origin = new URL(backend).origin;
  const matcher = (url: URL) => url.origin === origin && paths.includes(url.pathname);
  const counts: Record<"held" | "aborted" | "client500", Record<string, number>> = { held: {}, aborted: {}, client500: {} };
  const statuses: Array<{ phase: string; path: string; status: number }> = [];
  let phase = "held";
  page.on("response", response => {
    const url = new URL(response.url());
    if (matcher(url) && response.request().method() === "GET") statuses.push({ phase, path: url.pathname, status: response.status() });
  });
  const settled = (status: number) => Promise.all(paths.map(path => page.waitForResponse(response =>
    new URL(response.url()).origin === origin && new URL(response.url()).pathname === path && response.request().method() === "GET" && response.status() === status)));
  const everyPath = (bucket: Record<string, number>, count: number) => paths.every(path => bucket[path] === count);
  const alert = pane.getByRole("alert");
  const loading = pane.getByText("Loading availability…", { exact: true });
  const retry = pane.getByRole("button", { name: "Retry", exact: true });
  const heading = pane.getByRole("heading", { name: "Activity availability", exact: true });

  // 1. Real GETs held in flight show the loading state, then settle to the real 200 responses.
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const transport = async (route: Route) => {
    expect(route.request().method()).toBe("GET");
    const path = new URL(route.request().url()).pathname;
    const bucket = phase === "held" ? counts.held : counts.aborted;
    bucket[path] = (bucket[path] ?? 0) + 1;
    if (phase === "held") { await gate; await route.continue(); }
    else await route.abort("failed");
  };
  await page.route(matcher, transport);
  try {
    await page.goto("/settings/workflows-activity-availability");
    await expect(loading).toBeVisible();
    await expect.poll(() => everyPath(counts.held, 1), { timeout: 30_000 }).toBe(true);
    await capture(page, info, "availability-loading");
    const real = settled(200);
    release();
    await real;
    await expect(loading).toBeHidden();
    await expect(alert).toHaveCount(0);
    await expect(heading).toBeVisible();
    await capture(page, info, "availability-settled");

    // 2. A transport abort: both automatic attempts fail, and a keyboard Retry recovers to real 200 responses.
    phase = "aborted";
    await page.reload();
    await expect(alert).toContainText("Activity availability could not be loaded.");
    await expect.poll(() => everyPath(counts.aborted, 2), { timeout: 30_000 }).toBe(true);
    await expect(retry).toBeEnabled();
    await capture(page, info, "availability-abort-error");
    await page.unroute(matcher, transport);
    phase = "abort-recovery";
    const recovered = settled(200);
    await keyboardTo(page, retry);
    await page.keyboard.press("Enter");
    await recovered;
    await expect(alert).toHaveCount(0);
    await expect(loading).toBeHidden();
    await expect(heading).toBeVisible();
    await capture(page, info, "availability-abort-recovered");
  } finally {
    release();
    await page.unroute(matcher, transport);
  }

  // 3. An explicit HTTP 500, fulfilled by the client (page.route) rather than a backend fault, then a keyboard
  // Retry that reaches the real server and receives 200.
  phase = "client500";
  const fulfil = async (route: Route) => {
    expect(route.request().method()).toBe("GET");
    const path = new URL(route.request().url()).pathname;
    counts.client500[path] = (counts.client500[path] ?? 0) + 1;
    await route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ title: "Client-fulfilled acceptance 500" }) });
  };
  await page.route(matcher, fulfil);
  try {
    const failed = settled(500);
    await page.reload();
    await failed;
    await expect(alert).toContainText("Activity availability could not be loaded.");
    await expect.poll(() => everyPath(counts.client500, 2), { timeout: 30_000 }).toBe(true);
    await expect(retry).toBeEnabled();
    await capture(page, info, "availability-client-500-error");
  } finally {
    await page.unroute(matcher, fulfil);
  }
  phase = "client500-recovery";
  const recovered = settled(200);
  await keyboardTo(page, retry);
  await page.keyboard.press("Enter");
  await recovered;
  await expect(alert).toHaveCount(0);
  await expect(loading).toBeHidden();
  await expect(heading).toBeVisible();
  await capture(page, info, "availability-client-500-recovered");
  await attachJson(info, "availability-requests.json", { paths, counts: { held: counts.held, aborted: counts.aborted }, statuses: statuses.filter(item => !item.phase.startsWith("client500")),
    seam: "held and aborted owned-client GETs; the server itself was not degraded" });
  await attachJson(info, "client-500-receipt.json", { kind: "client-fulfilled HTTP 500 (page.route fulfill), not a backend outage", paths, fulfilled: counts.client500,
    statuses: statuses.filter(item => item.phase.startsWith("client500")) });
  expect(statuses.filter(item => item.phase === "client500-recovery").map(item => item.status).every(status => status === 200)).toBe(true);
});

async function subsetJourney(page: Page, browser: Browser, info: TestInfo, plane: "design" | "runtime") {
  const backend = required(`UX_${plane.toUpperCase()}_BACKEND_URL`);
  const design = plane === "design";
  const views = page.getByRole("navigation", { name: "Workflows views", exact: true });
  const available = design
    ? { path: "/workflows/definitions", view: "Definitions", heading: page.getByRole("heading", { name: "Workflow definitions", exact: true }) }
    : { path: "/workflows/executables", view: "Executables", heading: page.getByRole("main").getByRole("heading", { name: "Executables", level: 2, exact: true }) };
  const absent = design
    ? { path: "/workflows/executables", view: "Executables", capability: "elsa.api.runtime", settings: "/settings/workflows-runtime-diagnostics", section: "Runtime evidence settings", settingsCapability: "elsa.api.runtime" }
    : { path: "/workflows/definitions", view: "Definitions", capability: "elsa.api.workflow-design", settings: "/settings/workflows-activity-availability", section: "Activity availability", settingsCapability: "elsa.api.activity-design" };
  await signIn(page, available.path, admin.username, admin.password, available.heading);
  const discovered = await capabilities(page, backend);
  await attachJson(info, "subset-capabilities.json", discovered);
  expect(discovered.status).toBe(200);
  const ids = discovered.body.capabilities.map(item => item.id);
  const availableIds = discovered.body.capabilities.filter(item => !item.status).map(item => item.id);
  const mustAdvertise = design ? ["elsa.api.workflow-design", "elsa.api.activity-design"] : ["elsa.api.runtime"];
  const mustNotAdvertise = design ? ["elsa.api.runtime"] : ["elsa.api.workflow-design", "elsa.api.activity-design", "elsa.api.secrets", "elsa.api.connections"];
  for (const id of mustAdvertise) expect(availableIds, `${id} is available in this composition`).toContain(id);
  for (const id of mustNotAdvertise) expect(ids, `${id} is absent from this composition`).not.toContain(id);

  const sidebar = page.locator(".sidebar");
  const workflows = sidebar.getByRole("link", { name: "Workflows", exact: true });
  await workflows.click();
  await expect(page).toHaveURL(url => url.pathname === available.path);
  await expect(views.getByRole("link", { name: available.view, exact: true })).toHaveAttribute("aria-current", "page");
  await expect(views.getByRole("link", { name: absent.view, exact: true })).toHaveCount(0);
  await expect(available.heading).toBeVisible();
  if (!design) await expect(page.getByRole("table", { name: "Workflow executables", exact: true })
    .or(page.getByRole("heading", { name: "No workflow executables", exact: true }))).toBeVisible();
  await capture(page, info, `${plane}-workflows-default`);

  // A bookmark into the absent plane explains the composition instead of failing.
  await page.goto(absent.path);
  // Open-screen tabs keep hidden panels mounted; only the visible screen is the presentation.
  const unavailable = page.locator(".content .empty-state[role=status]").filter({ visible: true });
  await expect(unavailable).toHaveText(`This backend does not support ${absent.capability}.`);
  const viewPresentation = (await unavailable.textContent()) ?? "";
  await capture(page, info, `${plane}-${design ? "runtime" : "design"}-view-unavailable`);
  await page.goto(absent.settings);
  const sections = page.getByRole("navigation", { name: "Settings sections", exact: true });
  await expect(sections.getByRole("link", { name: "Appearance", exact: true })).toBeVisible();
  await expect(sections.getByRole("link", { name: absent.section, exact: true })).toHaveCount(0);
  const settingsNotice = page.locator(".settings-pane .empty-state[role=status]").filter({ visible: true });
  await expect(settingsNotice).toHaveText(new RegExp(`^(You don't have access to this section\\.|This backend does not support ${absent.settingsCapability.replace(/\./g, "\\.")}\\.)$`));
  const settingsPresentation = (await settingsNotice.textContent()) ?? "";
  await capture(page, info, `${plane}-${design ? "runtime" : "design"}-settings-unavailable`);
  // Keyboard recovery returns to the plane this composition does serve.
  await keyboardTo(page, workflows);
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(url => url.pathname === available.path);
  await expect(available.heading).toBeVisible();
  if (design) await capture(page, info, "design-recovery");
  const observation: Record<string, unknown> = { plane, availableCapabilityIds: availableIds, defaultView: available.path, absentView: absent.path,
    absentViewPresentation: viewPresentation, settingsBookmark: absent.settings, settingsPresentation, realBackendComposition: true };
  if (!design) {
    await expect(sidebar.getByRole("button", { name: "Integrations", exact: true })).toHaveCount(0);
    await capture(page, info, "runtime-integrations-absent");
    // Return to the combined composition in a fresh browser context: Integrations comes back with its destinations.
    const combinedStudio = required("ELSA_STUDIO_URL");
    const combinedBackend = required("ELSA_WORKBENCH_URL");
    const context = await browser.newContext({ baseURL: combinedStudio, viewport: info.project.use.viewport, colorScheme: info.project.use.colorScheme });
    try {
      await context.addInitScript(() => {
        localStorage.removeItem("elsa-studio-theme");
        localStorage.setItem("elsa-studio-workspace-style", "linen");
        localStorage.setItem("elsa-studio-theme-mode", "light");
      });
      const combined = await context.newPage();
      const combinedErrors: string[] = [];
      combined.on("pageerror", error => combinedErrors.push(error.message));
      await signIn(combined, "/workflows/definitions", admin.username, admin.password, combined.getByRole("heading", { name: "Workflow definitions", exact: true }));
      const restored = await capabilities(combined, combinedBackend);
      await attachJson(info, "combined-capabilities.json", restored);
      expect(restored.status).toBe(200);
      const restoredIds = restored.body.capabilities.filter(item => !item.status).map(item => item.id);
      for (const id of ["elsa.api.connections", "elsa.api.secrets", "elsa.api.workflow-design", "elsa.api.runtime"]) expect(restoredIds).toContain(id);
      const combinedSidebar = combined.locator(".sidebar");
      const integrations = combinedSidebar.getByRole("button", { name: "Integrations", exact: true });
      await expect(integrations).toBeVisible();
      await combinedSidebar.getByRole("navigation", { name: "Integrations", exact: true }).getByRole("link", { name: "Connections", exact: true }).click();
      await expect(combined.getByRole("table", { name: "Connections", exact: true })).not.toHaveAttribute("aria-busy", "true");
      await capture(combined, info, "combined-integrations-return");
      observation.combinedReturn = { studio: combinedStudio, integrationsVisible: true, pageErrors: combinedErrors };
      expect(combinedErrors, "Uncaught browser errors on the combined return").toEqual([]);
    } finally {
      await context.close();
    }
  }
  await attachJson(info, "subset-observation.json", observation);
}

// The "design" plane returns with a design-only composition once #4160 lets the Workbench boot without the runtime API.
test("runtime-only API composition and combined return", async ({ page, browser }, info) => subsetJourney(page, browser, info, "runtime"));
