import { expect, test, type Page } from "@playwright/test";

const styles = ["quiet", "focus", "open"] as const;
const modes = ["light", "dark", "dim", "high-contrast"] as const;

async function open(page: Page, style: string, scheme: string, mode: string, view = "editor", width = 1440) {
  await page.setViewportSize({ width, height: 960 });
  await page.addInitScript(({ style, scheme, mode }) => {
    localStorage.setItem("elsa-studio-workspace-style", style);
    localStorage.setItem("elsa-studio-color-scheme", scheme);
    localStorage.setItem("elsa-studio-theme-mode", mode);
  }, { style, scheme, mode });
  await page.goto(`/workspace-appearance.html?composition=sequence&view=${view}`);
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", style);
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", scheme);
}

for (const style of styles) for (const scheme of ["classic", "drift"]) for (const mode of modes) {
  test(`${style} with ${scheme} ${mode} keeps the authoring tools usable`, async ({ page }) => {
    await open(page, style, scheme, mode);
    await expect(page.getByRole("list", { name: "Sequence steps" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Review & publish", exact: true })).toBeVisible();
    await page.getByRole("button", { name: /1\. Notify buyer/ }).click();
    await expect(page.getByRole("complementary", { name: "Inspector panel" })).toBeVisible();
    const step = (await page.getByRole("button", { name: /1\. Notify buyer/ }).boundingBox())!;
    const inspector = (await page.locator(".wf-inspector").boundingBox())!;
    expect(step.x + step.width).toBeLessThanOrEqual(inspector.x + 1);
    if (style === "focus") {
      const canvas = (await page.locator(".wf-canvas-shell").boundingBox())!;
      expect(canvas.x + canvas.width).toBeGreaterThan(inspector.x + inspector.width - 1);
      await expect(page.getByRole("separator", { name: "Resize inspector panel" })).toBeVisible();
      const handle = page.getByRole("separator", { name: "Resize inspector panel" });
      const previous = await handle.getAttribute("aria-valuenow");
      await handle.focus();
      await handle.press("ArrowLeft");
      await expect(handle).not.toHaveAttribute("aria-valuenow", previous!);
    }
  });
}

for (const style of styles) {
  test(`${style} retains keyboard focus and high-contrast boundaries in the graph`, async ({ page }) => {
    await open(page, style, "classic", "high-contrast");
    await page.goto("/workspace-appearance.html?composition=flowchart");
    const graphNode = page.locator(".react-flow__node").first();
    await expect(graphNode).toBeVisible();
    await page.keyboard.press("Tab");
    await graphNode.focus();
    await expect(graphNode).toBeFocused();
    await expect.poll(() => graphNode.evaluate(element => element.matches(":focus-visible"))).toBe(true);
    const node = graphNode.locator(".wf-node");
    await expect(node).not.toHaveCSS("border-top-color", "rgba(0, 0, 0, 0)");
    await expect(node).toHaveCSS("outline-style", "solid");
    await expect(node).toHaveCSS("outline-width", "2px");
    const collapse = page.getByRole("button", { name: "Collapse inspector panel", exact: true });
    await collapse.focus();
    await expect(collapse).toHaveCSS("outline-style", "solid");
    await expect(collapse).toHaveCSS("outline-width", "2px");
    await expect(page.getByRole("heading", { level: 1, name: "Workflows", exact: true })).toHaveCount(1);
  });

  test(`${style} adapts its editor at 390px without page overflow`, async ({ page }) => {
    await open(page, style, "classic", "dark", "editor", 390);
    await expect(page.getByRole("list", { name: "Sequence steps" })).toBeVisible();
    const dimensions = await page.evaluate(() => ({ width: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth }));
    expect(dimensions.scroll).toBeLessThanOrEqual(dimensions.width + 1);
    await expect(page.getByRole("button", { name: "Select theme" })).toBeVisible();
  });

  test(`${style} keeps the workflow list aligned and its identities available`, async ({ page }) => {
    await open(page, style, "classic", "light", "list");
    await expect(page.getByRole("table", { name: "Workflow definitions" })).toBeVisible();
    await expect(page.getByRole("link", { name: "First page renewal", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Copy Definition ID", exact: true })).toBeVisible();
  });

  test(`${style} retains the inspected runtime occurrence while switching workspace`, async ({ page }) => {
    await open(page, style, "drift", "dark");
    await page.goto("/workspace-appearance.html?composition=sequence&host=instance&run-canvas=unique");
    await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "1. Notify buyer", exact: true }).click();
    await expect(page.getByRole("tab", { name: "Activity", exact: true })).toHaveAttribute("aria-selected", "true");
    const details = page.getByRole("complementary", { name: "Run details panel", exact: true });
    await expect(details).toContainText("Notify buyer");
    const url = page.url();
    await chooseStyle(page, style === "focus" ? "Quiet Studio" : "Focus Canvas");
    await expect(details).toContainText("Notify buyer");
    await expect(page.getByRole("button", { name: "1. Notify buyer", exact: true })).toHaveAttribute("aria-pressed", "true");
    expect(page.url()).toBe(url);
  });
}

test("appearance controls change one axis at a time with keyboard access", async ({ page }) => {
  await open(page, "focus", "drift", "dark", "appearance");
  const colors = page.getByRole("radiogroup", { name: "Color scheme", exact: true });
  await colors.getByRole("radio", { name: "Meridian", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "focus");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "meridian");
  const quiet = page.getByRole("radiogroup", { name: "Workspace style", exact: true }).getByRole("radio", { name: "Quiet Studio", exact: true });
  await quiet.click();
  await quiet.press("ArrowRight");
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "focus");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "meridian");
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "dark");
});

async function chooseStyle(page: Page, name: string) {
  await page.getByRole("button", { name: "Select theme", exact: true }).click();
  await page.getByRole("menuitemradio", { name, exact: true }).click();
  await page.keyboard.press("Escape");
}

test("Focus clears a hidden navigation filter and preserves an explicit expansion across reload", async ({ page }) => {
  await open(page, "quiet", "classic", "dark");
  await page.getByRole("searchbox", { name: "Search modules" }).fill("Workflows");
  await chooseStyle(page, "Focus Canvas");
  await expect(page.locator(".studio-shell")).toHaveClass(/sidebar-collapsed/);
  await expect(page.getByRole("link", { name: "Home", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Runs", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Expand navigation", exact: true }).click();
  await chooseStyle(page, "Quiet Studio");
  await page.reload();
  await chooseStyle(page, "Focus Canvas");
  await expect(page.locator(".studio-shell")).not.toHaveClass(/sidebar-collapsed/);
});

test("live style and palette changes preserve the selected activity and unsaved property", async ({ page }) => {
  await open(page, "quiet", "classic", "dark");
  await page.getByRole("button", { name: "1. Notify buyer", exact: true }).click();
  await page.getByRole("tab", { name: "Details", exact: true }).click();
  const name = page.getByRole("textbox", { name: /^Display name/ });
  await name.fill("Unsaved customer message");
  for (const style of ["Focus Canvas", "Open Studio", "Quiet Studio"]) {
    await chooseStyle(page, style);
    await expect(name).toHaveValue("Unsaved customer message");
    await expect(page.getByRole("tab", { name: "Details", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByRole("button", { name: "1. Unsaved customer message", exact: true })).toHaveAttribute("aria-pressed", "true");
  }
  await page.getByRole("button", { name: "Select theme", exact: true }).click();
  await page.getByRole("menuitemradio", { name: "Drift", exact: true }).click();
  await expect(page.getByRole("menuitemradio", { name: "Drift", exact: true })).toHaveAttribute("aria-checked", "true");
  await page.keyboard.press("Escape");
  await expect(name).toHaveValue("Unsaved customer message");
});

for (const height of [480, 600]) test(`the appearance menu scrolls every section into pointer reach at ${height}px high`, async ({ page }) => {
  await open(page, "quiet", "classic", "dark");
  await page.setViewportSize({ width: 1024, height });
  await page.getByRole("button", { name: "Select theme", exact: true }).click();
  const menu = page.getByRole("menu");
  await menu.evaluate(element => Promise.all(element.getAnimations().map(animation => animation.finished)));
  const bounds = (await menu.boundingBox())!;
  await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
  await page.mouse.wheel(0, 10000);
  await expect.poll(() => menu.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  const last = menu.locator(".theme-item").last();
  const target = (await last.boundingBox())!;
  expect(target.y).toBeGreaterThanOrEqual(bounds.y);
  expect(target.y + target.height).toBeLessThanOrEqual(bounds.y + bounds.height + 1);
  await expect.poll(() => last.evaluate(element => {
    const box = element.getBoundingClientRect();
    return element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2));
  })).toBe(true);
  await page.mouse.click(target.x + target.width / 2, target.y + target.height / 2);
  await expect(last).toHaveAttribute("aria-checked", "true");
});
