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
  // The heading precedes the initial list/descriptor response; Enter cannot submit while Search is disabled.
  await expect(page.getByRole("button", { name: "Search", exact: true })).toBeEnabled();
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
  await expect(page.locator(".studio-toast").filter({ hasText: "Secret created." })).toBeVisible();
  await expect(page.locator(".secrets-notification")).toHaveCount(0);
  await capture(page, info, "secret-created-toast");

  // Follow the shell navigation so notification history stays in the same mounted host session.
  const moreTools = page.getByRole("button", { name: "More tools", exact: true });
  if (await moreTools.getAttribute("aria-expanded") === "false") await moreTools.press("Enter");
  await page.getByRole("link", { name: "Connections", exact: true }).press("Enter");
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
  await expect(page.locator(".studio-toast").filter({ hasText: `Connection 'qa-${stamp}' created.` })).toBeVisible();
  await expect(page.locator(".connections-notice")).toHaveCount(0);
  const createdRow = page.getByRole("row").filter({ hasText: `qa-${stamp}` });
  await expect(createdRow).toBeVisible();
  const nameLink = createdRow.getByRole("button", { name: `qa-${stamp}`, exact: true });
  // A table can stay inside the viewport while its flexible name column collapses to one character.
  expect((await nameLink.boundingBox())!.width).toBeGreaterThan(100);
  expect((await createdRow.boundingBox())!.height).toBeLessThan(160);
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create connection", exact: true }).first()).toBeFocused();
  await capture(page, info, "completed");
  // A toast must not obscure the primary action or the shell chrome. Keyboard focus alone
  // cannot detect the pointer obstruction caused by the former top-right placement.
  const toastHost = page.locator(".studio-toast-host");
  await expect(toastHost).toBeVisible();
  const assertToastPlacement = async () => expect.poll(() => openConnection.evaluate(button => {
    const action = button.getBoundingClientRect();
    const stack = document.querySelector(".studio-toast-host")!.getBoundingClientRect();
    const dock = document.querySelector(".bottom-panel")!.getBoundingClientRect();
    const header = document.querySelector(".topbar")!.getBoundingClientRect();
    const hit = document.elementFromPoint(action.x + action.width / 2, action.y + action.height / 2);
    return {
      clickable: hit !== null && button.contains(hit),
      overlapsAction: stack.left < action.right && stack.right > action.left && stack.top < action.bottom && stack.bottom > action.top,
      aboveDock: stack.bottom <= dock.top - 10,
      belowHeader: stack.top >= header.bottom,
      withinViewport: stack.left >= 0 && stack.right <= innerWidth && stack.top >= 0
    };
  })).toEqual({ clickable: true, overlapsAction: false, aboveDock: true, belowHeader: true, withinViewport: true });
  await assertToastPlacement();
  await capture(page, info, "primary-action-with-toasts");
  await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
  await assertToastPlacement();
  await expect(page.locator(".studio-toast").filter({ hasText: `Connection 'qa-${stamp}' created.` })).toBeVisible();
  await capture(page, info, "toasts-above-expanded-dock");
  await page.getByRole("button", { name: "Collapse bottom panel", exact: true }).click();
  await assertToastPlacement();
  await openConnection.click();
  await expect(dialog).toBeVisible();
  await expect(name).toBeFocused();
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  const confirmation = page.locator(".studio-toast").filter({ hasText: `Connection 'qa-${stamp}' created.` });
  await expect(page.locator('.studio-toast-live[aria-live="polite"]')).toContainText(`Connection 'qa-${stamp}' created.`);
  await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
  await page.getByRole("button", { name: "Maximize bottom panel", exact: true }).click();
  await expect(confirmation).toBeVisible();
  await expect.poll(() => toastHost.evaluate(host => {
    const stack = host.getBoundingClientRect();
    const header = document.querySelector(".topbar")!.getBoundingClientRect();
    return stack.top >= header.bottom && stack.bottom <= innerHeight && stack.left >= 0 && stack.right <= innerWidth;
  })).toBe(true);
  await capture(page, info, "toasts-over-maximized-dock");
  while (await page.locator(".studio-toast").count()) {
    await page.locator(".studio-toast").first().getByRole("button", { name: "Dismiss notification", exact: true }).press("Enter");
  }
  await expect(confirmation).toHaveCount(0);
  const restoreDock = page.getByRole("button", { name: "Restore bottom panel", exact: true });
  await expect(restoreDock).toBeFocused();
  await restoreDock.press("Enter");
  await page.getByRole("button", { name: "Collapse bottom panel", exact: true }).click();
  await page.getByRole("button", { name: /^Notifications, \d+ unread$/ }).press("Enter");
  const history = page.getByRole("dialog", { name: "Notifications", exact: true });
  await expect(history).toBeVisible();
  await expect(history).toContainText("Secret created.");
  await expect(history).toContainText(`Connection 'qa-${stamp}' created.`);
  await expect(page.locator(".studio-toast")).toHaveCount(0);
  await capture(page, info, "notification-history");
  await history.press("Escape");
  await expect(history).toBeHidden();
  await expect(page.getByRole("button", { name: /^Notifications, \d+ unread$/ })).toBeFocused();
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
      if (!await entry.isVisible()) await page.getByRole("button", { name: /More tools/ }).click();
      await expect(entry.locator(`svg.lucide-${icon}`)).toBeVisible();
    }
  }
  await outcome.getByText("Emitted", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "completed");
});
