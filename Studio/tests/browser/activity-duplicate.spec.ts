import { expect, test } from "@playwright/test";
import axe from "axe-core";

test.use({
  screenshot: "only-on-failure",
  video: "retain-on-failure",
  trace: "retain-on-failure"
});

test("duplicates the selected activity from the context menu with a fresh name", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate&theme=light");

  const original = page.getByRole("button", { name: "Notify buyer", exact: true });
  await expect(original).toBeVisible();
  await original.click({ button: "right" });

  const menu = page.getByRole("menu", { name: "Activity actions" });
  await expect(menu).toBeVisible();
  await expect(menu.getByRole("menuitem", { name: /Duplicate/ })).toBeVisible();
  await menu.getByRole("menuitem", { name: /Duplicate/ }).click();

  await expect(page.getByRole("status")).toHaveText("Duplicated 1 activity.");
  const copy = page.getByRole("button", { name: "Notify buyer (copy)", exact: true });
  await expect(copy).toBeVisible();
  await expect(original).toBeVisible();
  const originalBox = await original.boundingBox();
  const copyBox = await copy.boundingBox();
  expect(copyBox!.x).toBeGreaterThan(originalBox!.x);
  expect(copyBox!.y).toBeGreaterThan(originalBox!.y);
  await expect(copy).toHaveAttribute("aria-pressed", "true");
});

test("copies and pastes with Ctrl/Cmd shortcuts and ignores them in a text field", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate&theme=dark");

  await page.getByRole("button", { name: "Notify buyer", exact: true }).click();
  await page.keyboard.press("Control+c");
  await expect(page.getByRole("status")).toHaveText("Copied 1 activity.");
  await page.keyboard.press("Control+v");
  await expect(page.getByRole("status")).toHaveText("Pasted 1 activity.");
  await expect(page.getByRole("button", { name: "Notify buyer (copy)", exact: true })).toBeVisible();

  await page.getByLabel("Display name").focus();
  await page.keyboard.press("Control+d");
  await expect(page.getByRole("button", { name: "Notify buyer (copy 2)", exact: true })).toHaveCount(0);

  await page.getByRole("button", { name: "Notify buyer", exact: true }).click();
  await page.keyboard.press("Control+d");
  await expect(page.getByRole("button", { name: "Notify buyer (copy 2)", exact: true })).toBeVisible();
});

async function selectNotifyBuyer(page: import("@playwright/test").Page) {
  const node = page.locator(".wf-canvas .react-flow__node[data-id='write-one']");
  await expect(node).toBeVisible({ timeout: 15_000 });
  await node.focus();
  await page.keyboard.press("Enter");
  await expect(node).toHaveAttribute("aria-pressed", "true");
}

function describeActiveElement() {
  const active = document.activeElement;
  return {
    tag: active instanceof HTMLElement ? active.tagName : String(active),
    className: active instanceof HTMLElement ? active.className : "",
    inEditor: active instanceof Element && Boolean(active.closest("[data-studio-code-editor]")),
    ready: Boolean(document.querySelector("[data-studio-code-editor][data-studio-code-editor-ready='true']")),
    surface: document.querySelector("[data-studio-code-editor-surface]")?.getAttribute("data-studio-code-editor-surface") ?? null
  };
}

async function dispatchCanvasShortcut(page: import("@playwright/test").Page, key: "c" | "v" | "d" | "z") {
  const node = page.locator(".wf-canvas .react-flow__node[data-id='write-one']");
  await node.evaluate((element, pressed) => {
    element.dispatchEvent(new KeyboardEvent("keydown", {
      key: pressed,
      code: `Key${pressed.toUpperCase()}`,
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
      composed: true
    }));
  }, key);
}

test("duplicate in the real editor undoes in a single step", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&theme=light");

  await expect(page.locator(".wf-canvas .wf-node").filter({ hasText: "Notify buyer" })).toBeVisible({ timeout: 15_000 });
  const startingCount = await page.locator(".wf-canvas .wf-node").count();

  await selectNotifyBuyer(page);
  await expect(page.getByRole("complementary", { name: /inspector/i })).toContainText("write-one");
  await dispatchCanvasShortcut(page, "d");
  await expect(page.locator(".wf-editor-save-status")).toContainText("Duplicated 1 activity.");
  await expect(page.locator(".wf-canvas .wf-node").filter({ hasText: "Notify buyer (copy)" })).toBeVisible();
  await expect(page.locator(".wf-canvas .wf-node")).toHaveCount(startingCount + 1);

  await dispatchCanvasShortcut(page, "z");
  await expect(page.locator(".wf-canvas .wf-node").filter({ hasText: "Notify buyer (copy)" })).toHaveCount(0);
  await expect(page.locator(".wf-canvas .wf-node")).toHaveCount(startingCount);
});

test("Ctrl+D does nothing while the designer code editor is focused", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&theme=light");

  await expect(page.locator(".wf-canvas .wf-node").filter({ hasText: "Notify buyer" })).toBeVisible({ timeout: 15_000 });
  await selectNotifyBuyer(page);
  await expect(page.getByRole("complementary", { name: /inspector/i })).toContainText("Text");

  const syntaxTrigger = page.getByRole("button", { name: /expression syntax/i }).first();
  if (await syntaxTrigger.isVisible()) {
    const current = await syntaxTrigger.textContent();
    if (!current?.includes("JavaScript")) {
      await syntaxTrigger.click();
      await page.getByRole("option", { name: "JavaScript" }).click();
    }
  }

  const inspector = page.getByRole("complementary", { name: /inspector/i });
  const codeEditor = inspector.locator("[data-studio-code-editor]");
  await expect(codeEditor).toBeVisible({ timeout: 15_000 });
  await codeEditor.click();

  const readyEditor = inspector.locator("[data-studio-code-editor][data-studio-code-editor-ready='true']");
  await expect(readyEditor).toBeVisible({ timeout: 15_000 });
  const settledContent = readyEditor.locator("[data-studio-code-editor-surface='codemirror'] .cm-content");
  await expect(settledContent).toBeVisible({ timeout: 15_000 });
  await settledContent.focus();
  await expect(settledContent).toBeFocused();

  const nodes = page.locator(".wf-canvas .wf-node");
  const startingCount = await nodes.count();
  await page.keyboard.press("ControlOrMeta+d");
  const activeAfterPress = await page.evaluate(describeActiveElement);
  expect(activeAfterPress.inEditor).toBe(true);

  await expect.poll(async () => {
    const duplicated = (await page.locator(".wf-editor-save-status").textContent())?.includes("Duplicated") === true;
    return { duplicated, count: await nodes.count() };
  }).toEqual({ duplicated: false, count: startingCount });
  await expect(page.locator(".wf-canvas .wf-node").filter({ hasText: "Notify buyer (copy)" })).toHaveCount(0);
  await expect(page.locator(".wf-editor-save-status")).not.toContainText("Duplicated");
});

test("activity duplicate menu has no critical or serious automated accessibility violations", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate&theme=light");
  await page.getByRole("button", { name: "Notify buyer", exact: true }).click({ button: "right" });
  await expect(page.getByRole("menu", { name: "Activity actions" })).toBeVisible();

  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (root: Document, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string }> }> };
    }).axe.run(document, { resultTypes: ["violations"] });
    return result.violations.filter(violation =>
      violation.impact === "critical" || violation.impact === "serious"
    );
  });
  expect(violations).toEqual([]);
});

test("Sequence uses authored order, step commands, undo and nested body navigation", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&composition=sequence&theme=dark");
  const steps = page.locator(".wf-sequence-step");
  const ids = () => steps.evaluateAll(elements => elements.map(element => element.getAttribute("data-id")));
  await expect(steps).toHaveCount(3);
  expect(await ids()).toEqual(["write-one", "loop", "write-two"]);
  await page.getByRole("button", { name: "1. Notify buyer", exact: true }).click();
  await page.getByRole("button", { name: "Move Notify buyer down", exact: true }).click();
  expect(await ids()).toEqual(["loop", "write-one", "write-two"]);
  await expect(page.locator('.wf-sequence-step[data-id="write-one"]')).toBeFocused();
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  expect(await ids()).toEqual(["write-one", "loop", "write-two"]);
  await page.getByRole("button", { name: "1. Notify buyer", exact: true }).click();
  await page.getByRole("button", { name: "Duplicate Notify buyer", exact: true }).click();
  await expect(steps).toHaveCount(4);
  await expect(steps.nth(1)).toContainText("Notify buyer (copy)");
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await page.getByRole("button", { name: "Open Body, 1 activity", exact: true }).click();
  await expect(steps).toHaveCount(1);
  await expect(steps).toHaveAttribute("data-id", "nested");
  await page.getByRole("button", { name: "Root", exact: true }).click();
  await expect(steps).toHaveCount(3);
});

test("Sequence insertion works before an existing step without manual connections", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&composition=sequence&theme=light");
  await page.getByRole("button", { name: "Insert step before Notify buyer", exact: true }).click();
  await page.getByRole("searchbox", { name: "Search activities", exact: true }).fill("Write Line");
  await page.getByRole("option", { name: /Write Line/ }).click();
  const steps = page.locator(".wf-sequence-step");
  await expect(steps).toHaveCount(4);
  await expect(steps.nth(0)).toContainText("Write Line");
  await expect(steps.nth(1)).toContainText("Notify buyer");
  await expect(page.locator(".react-flow__edge")).toHaveCount(0);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(steps).toHaveCount(3);
});

test("Sequence drag gestures reorder and insert exactly once at an explicit boundary", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&composition=sequence&theme=dark");
  const steps = page.locator(".wf-sequence-step");
  await expect(steps).toHaveCount(3);
  await page.locator('.wf-sequence-step[data-id="write-one"]').dragTo(
    page.getByRole("button", { name: "Insert step after Write Line", exact: true })
  );
  // Order is authored by the drop boundary, not saved XY.
  await expect.poll(() => steps.evaluateAll(items => items.map(item => item.getAttribute("data-id"))))
    .toEqual(["loop", "write-two", "write-one"]);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await page.getByRole("treeitem", { name: "Write Line, Exact version 1.0.0", exact: true }).dragTo(
    page.getByRole("button", { name: "Insert step between Notify buyer and For Each", exact: true })
  );
  await expect(steps).toHaveCount(4);
  await expect(steps.nth(0)).toHaveAttribute("data-id", "write-one");
  await expect(steps.nth(1)).toHaveAccessibleName("2. Write Line");
  await expect(steps.nth(2)).toHaveAttribute("data-id", "loop");
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(steps).toHaveCount(3);
});
