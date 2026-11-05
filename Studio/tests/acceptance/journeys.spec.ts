import { writeFileSync } from "node:fs";
import { expect, test, type Page, type TestInfo } from "@playwright/test";

async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

async function signIn(page: Page) {
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin");
  await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
}

const browserErrors = new WeakMap<Page, string[]>();

test.afterEach(async ({ page }) => {
  expect(browserErrors.get(page), "Uncaught browser errors").toEqual([]);
});

test.beforeEach(async ({ page }, info) => {
  await page.addInitScript(mode => {
    localStorage.removeItem("elsa-studio-theme");
    localStorage.setItem("elsa-studio-workspace-style", "linen");
    localStorage.setItem("elsa-studio-theme-mode", mode);
  }, info.project.use.colorScheme ?? "light");
  const errors: string[] = [];
  browserErrors.set(page, errors);
  page.on("pageerror", error => errors.push(error.message));
  // Keep browser runtime errors actionable without accepting an assertion-only green result.
  await info.attach("source", { body: process.env.UX_SOURCE_SHA ?? "unknown", contentType: "text/plain" });
  await signIn(page);
  await expect(page.getByRole("heading", { name: /^No active workflow definitions in All workflows$/ })).toBeVisible();
  await capture(page, info, "reference");
});

test("collections and connection creation", async ({ page }, info) => {
  const stamp = `${info.project.name}-${Date.now()}`;
  await page.goto("/security/connections");
  await expect(page.getByRole("region", { name: "Connections management", exact: true }).getByRole("heading", { name: "Connections", exact: true })).toBeVisible();
  // A fresh run starts with no connections; later projects use the same disposable store and exercise search recovery.
  const search = page.getByRole("searchbox", { name: /search connections/i });
  await search.fill(`absent-${stamp}`);
  await search.press("Enter");
  await expect(page.getByRole("button", { name: /clear search/i })).toBeVisible();
  await capture(page, info, "no-matches");
  await page.getByRole("button", { name: /clear search/i }).press("Enter");
  await expect(search).toHaveValue("");
  await expect(page.getByRole("table", { name: "Connections", exact: true })).not.toHaveAttribute("aria-busy", "true");
  await capture(page, info, "connections-list");
  if (info.project.name === "desktop-light") {
    await expect(page.getByRole("table", { name: "Connections", exact: true }).getByRole("button", { name: "Create connection", exact: true })).toBeVisible();
  }

  await page.goto("/security/secrets");
  await page.getByRole("button", { name: "Create secret", exact: true }).first().click();
  const secret = page.getByRole("dialog", { name: "Create secret", exact: true });
  await secret.getByLabel("Technical name", { exact: true }).fill(`qa-${stamp}`);
  await secret.getByLabel("Value", { exact: true }).fill("disposable-acceptance-value");
  await secret.getByRole("button", { name: "Create", exact: true }).click();
  await expect(secret).toBeHidden();

  await page.goto("/security/connections");
  const openConnection = page.getByRole("button", { name: "Create connection", exact: true }).first();
  await expect(openConnection).toBeEnabled();
  await openConnection.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  const name = dialog.getByLabel("Name", { exact: true });
  await expect(name).toBeFocused();
  await name.fill("invalid name");
  await dialog.getByLabel("Auth scheme", { exact: true }).selectOption("api-key");
  await dialog.getByLabel("Existing Secret for API key", { exact: true }).selectOption(`qa-${stamp}`);
  await dialog.getByLabel("Base URL", { exact: true }).fill("https://example.invalid");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeDisabled();
  await expect(name).toHaveAttribute("aria-invalid", "true");
  await name.scrollIntoViewIfNeeded();
  await capture(page, info, "invalid-name");
  await name.fill(`qa-${stamp}`);
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("status").filter({ hasText: /^Connection / })).toContainText(`Connection 'qa-${stamp}' created.`);
  const createdRow = page.getByRole("row").filter({ hasText: `qa-${stamp}` });
  await expect(createdRow).toBeVisible();
  const nameLink = createdRow.getByRole("button", { name: `qa-${stamp}`, exact: true });
  // A table can stay inside the viewport while its flexible name column collapses to one character.
  expect((await nameLink.boundingBox())!.width).toBeGreaterThan(100);
  expect((await createdRow.boundingBox())!.height).toBeLessThan(160);
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create connection", exact: true }).first()).toBeFocused();
  await capture(page, info, "completed");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("agent authoring and navigation", async ({ page }, info) => {
  await page.goto("/agents");
  await expect(page.getByTestId("agents-page").getByRole("heading", { name: "Agents", exact: true })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: /^\d[\d,]* agents?$/ })).toBeVisible();
  await capture(page, info, "agents-list");
  const create = page.getByRole("button", { name: "New agent", exact: true });
  await create.press("Enter");
  await expect(page.getByRole("searchbox", { name: "Search agents", exact: true })).toHaveCount(0);
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(create).toBeFocused();
  await create.press("Enter");
  await page.getByRole("textbox", { name: "Name", exact: true }).fill(`Acceptance ${info.project.name} ${Date.now()}`);
  await page.getByRole("textbox", { name: /^Purpose/ }).fill("Proofread a supplied text.");
  await capture(page, info, "agent-create");
  await page.getByRole("button", { name: "Create and edit", exact: true }).click();
  const editor = page.getByTestId("specialist-editor");
  const instructions = editor.getByRole("textbox", { name: /^Instructions/ });
  await expect(instructions).toBeVisible();
  await instructions.fill("Proofread the supplied text and return concise suggestions.");
  await instructions.press("Tab");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved", { timeout: 30_000 });
  await page.reload();
  await expect(instructions).toHaveValue("Proofread the supplied text and return concise suggestions.");
  const advanced = editor.locator("details.agent-specialist-advanced");
  await expect(advanced).not.toHaveAttribute("open");
  await page.getByRole("tab", { name: "Designer", exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "agent-designer");
  await editor.getByRole("combobox", { name: /^Model connection/ }).scrollIntoViewIfNeeded();
  await capture(page, info, "agent-model");
  await advanced.locator("summary").press("Enter");
  await advanced.getByRole("textbox", { name: /^Maximum model calls/ }).scrollIntoViewIfNeeded();
  await capture(page, info, "advanced-settings");
  await advanced.locator("summary").press("Enter");
  const tabs = page.getByRole("tablist", { name: "Activity Definition authoring views" });
  await expect(tabs.getByRole("tab")).toHaveText(["Designer", "Inputs (0)", "Outputs (0)", "Outcomes (1)"]);
  for (const kind of ["input", "output"] as const) {
    await tabs.getByRole("tab", { name: kind === "input" ? "Inputs (0)" : "Outputs (0)", exact: true }).click();
    await expect(page.getByText(`No ${kind}s yet`, { exact: true })).toBeVisible();
    await capture(page, info, `${kind}s`);
    const view = page.locator(".ad-public-interface-view");
    await view.getByRole("button", { name: `Add ${kind}`, exact: true }).click();
    await expect(view.getByRole("textbox", { name: `${kind === "input" ? "Input" : "Output"} name`, exact: true })).toBeFocused();
    await view.getByRole("button", { name: "Cancel", exact: true }).click();
  }
  await tabs.getByRole("tab", { name: "Outcomes (1)", exact: true }).click();
  const outcome = page.getByRole("region", { name: "Done", exact: true });
  await expect(outcome.getByText("Emitted", { exact: true })).toBeVisible();
  await expect(outcome.getByRole("checkbox")).toHaveCount(0);
  await expect(outcome.getByRole("textbox", { name: "Name", exact: true })).toHaveCount(0);
  if ((info.project.use.viewport?.width ?? 0) >= 1440) {
    for (const [label, icon] of [["Agents", "bot"], ["Secrets", "key-round"], ["Connections", "cable"]]) {
      const entry = page.getByRole("link", { name: label, exact: true });
      if (!await entry.isVisible()) await page.getByRole("button", { name: label === "Agents" ? "Build" : "Integrations", exact: true }).click();
      await expect(entry.locator(`svg.lucide-${icon}`)).toBeVisible();
    }
  }
  await outcome.getByText("Emitted", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "completed");
});

test("grouped navigation and moved destinations", async ({ page }, info) => {
  const sidebar = page.locator(".sidebar");
  const navLink = (name: string) => sidebar.getByRole("link", { name, exact: true });
  const desktop = (info.project.use.viewport?.width ?? 0) >= 1025;
  for (const name of ["Build", "Operate", "Integrations"]) await expect(sidebar.getByRole("button", { name, exact: true })).toHaveAttribute("aria-expanded", "true");
  await expect(sidebar.getByRole("button", { name: "More tools" })).toHaveCount(0);
  await navLink("Connections").click();
  await expect(navLink("Connections")).toHaveAttribute("aria-current", "page");
  await capture(page, info, "groups");
  await navLink("Workflows").click();
  await page.getByRole("navigation", { name: "Workflows views" }).getByRole("link", { name: "Executables", exact: true }).click();
  await expect(navLink("Workflows")).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("heading", { name: /Workflow executables/i }).first()).toBeVisible();
  await navLink("Library").click();
  const libraryViews = page.getByRole("navigation", { name: "Library views" });
  await libraryViews.getByRole("link", { name: "Templates", exact: true }).click();
  await expect(navLink("Library")).toHaveAttribute("aria-current", "page");
  await libraryViews.getByRole("link", { name: "Activity definitions", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Library", exact: true })).toBeVisible();
  await capture(page, info, "library");
  const screens = page.getByRole("tablist", { name: "Open screens", exact: true });
  const previousScreenCount = await screens.getByRole("tab").count();
  await page.locator(".studio-library-header").getByRole("link", { name: "Activity availability", exact: true }).click({ modifiers: ["Alt"] });
  await expect(screens.getByRole("tab")).toHaveCount(previousScreenCount + 1);
  await expect(page.locator(".settings-pane").getByRole("heading", { name: "Activity availability", exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Settings sections", exact: true })).toContainText("Workflow authoring");
  await expect(navLink("Settings")).toHaveAttribute("aria-current", "page");
  await capture(page, info, "workflow-authoring-settings");
  // Existing bookmarked routes must resolve to their new Settings surface as well.
  await page.goto("/workflows/runtime-diagnostics");
  await expect(page.locator(".settings-pane").getByRole("heading", { name: "Runtime evidence settings", exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Settings sections", exact: true })).toContainText("Runtime");
  await expect(navLink("Settings")).toHaveAttribute("aria-current", "page");
  await capture(page, info, "runtime-settings");
  await navLink("Diagnostics").click();
  const diagnostics = page.getByRole("navigation", { name: "Diagnostics views" });
  for (const name of ["Console", "Structured logs", "OpenTelemetry"]) {
    await diagnostics.getByRole("link", { name, exact: true }).click();
    await expect(navLink("Diagnostics")).toHaveAttribute("aria-current", "page");
    await expect(diagnostics.getByRole("link", { name, exact: true })).toHaveAttribute("aria-current", "page");
    await expect(page.getByText("This route is unavailable.", { exact: true })).toHaveCount(0);
  }
  await capture(page, info, "diagnostic-views");
  const search = sidebar.getByRole("searchbox", { name: "Search Studio", exact: true });
  await search.fill("templates");
  await expect(navLink("Templates")).toBeVisible();
  await capture(page, info, "search");
  await search.press("Escape");
  if (desktop) {
    await sidebar.getByRole("button", { name: "Build", exact: true }).click();
    await expect(navLink("Agents")).toBeHidden();
    await page.reload();
    await expect(sidebar.getByRole("button", { name: "Build", exact: true })).toHaveAttribute("aria-expanded", "false");
    await sidebar.getByRole("button", { name: "Build", exact: true }).press("Enter");
    await sidebar.getByRole("button", { name: "Collapse navigation", exact: true }).click();
    await expect(navLink("Library")).toBeVisible();
    await sidebar.getByRole("button", { name: "Expand navigation", exact: true }).click();
  }
  await navLink("Connections").click();
  await capture(page, info, "completed");
});
