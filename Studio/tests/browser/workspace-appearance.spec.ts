import { expect, test, type Page } from "@playwright/test";
import axe from "axe-core";

const styles = ["linen", "minimal", "quiet", "focus", "open"] as const;
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

for (const style of styles) for (const scheme of ["linen", "classic", "drift"]) for (const mode of modes) {
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
  test(`${style} preserves the semantic fill of danger and primary confirmations`, async ({ page }) => {
    await open(page, style, "classic", "light", "dialogs");
    for (const [action, token] of [["Delete workflow", "--studio-danger"], ["Publish workflow", style === "minimal" ? "--studio-action-primary-bg" : "--studio-accent"]]) {
      await page.getByRole("button", { name: action, exact: true }).click();
      const confirm = page.getByRole("dialog").getByRole("button", { name: "Confirm", exact: true });
      const colors = await confirm.evaluate((element, token) => {
        const probe = document.createElement("span");
        probe.style.backgroundColor = `var(${token})`;
        element.append(probe);
        const expected = getComputedStyle(probe).backgroundColor;
        probe.remove();
        return { actual: getComputedStyle(element).backgroundColor, expected };
      }, token);
      expect(colors.actual).toBe(colors.expected);
      await page.getByRole("dialog").getByRole("button", { name: "Cancel", exact: true }).click();
    }
  });

  test(`${style} keeps publication contrast when palette and base preset differ`, async ({ page }) => {
    await open(page, style, "meridian", "dim", "publication");
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const publish = page.getByRole("dialog").getByRole("button", { name: "Publish", exact: true });
    await expect(publish).toBeVisible();
    await expect(publish).toBeEnabled();
    await publish.evaluate(async element => {
      await document.fonts.ready;
      const surfaces = [element.closest(".wf-dialog"), element.closest(".wf-dialog-backdrop")];
      await Promise.all(surfaces.flatMap(surface => surface?.getAnimations() ?? []).map(animation => animation.finished));
    });
    await expect(page.locator("html")).toHaveAttribute("data-theme", "linen");
    await page.addScriptTag({ content: axe.source });
    const violations = await publish.evaluate(async element => {
      const result = await (window as Window & { axe: { run: (root: Element, options: object) => Promise<{ violations: Array<{ id: string; nodes: Array<{ failureSummary?: string }> }> }> } }).axe.run(element, { runOnly: ["color-contrast"] });
      return result.violations.map(violation => ({ id: violation.id, failures: violation.nodes.map(node => node.failureSummary) }));
    });
    expect(violations).toEqual([]);
  });

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
    await expect(page).toHaveURL(/selected=activity-exec-write-one/);
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

test("independent choices survive a real reload without re-seeding preferences", async ({ page }) => {
  await page.goto("/workspace-appearance.html?view=appearance");
  await page.getByRole("radiogroup", { name: "Workspace style", exact: true }).getByRole("radio", { name: "Focus Canvas", exact: true }).click();
  await page.getByRole("radiogroup", { name: "Color scheme", exact: true }).getByRole("radio", { name: "Drift", exact: true }).click();
  await page.getByRole("region", { name: "Appearance", exact: true }).getByRole("radio", { name: "Dim", exact: true }).click();
  await page.getByRole("radiogroup", { name: "Navigation", exact: true }).getByRole("radio", { name: "Top", exact: true }).click();
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "focus");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "drift");
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "dim");
  await expect(page.locator("html")).toHaveAttribute("data-nav-mode", "top");
});

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
  for (const style of ["Focus Canvas", "Open Studio", "Minimal", "Quiet Studio"]) {
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

for (const style of styles) for (const mode of ["light", "dark"]) {
  test(`${style} owns its presentation independently of the inherited preset in ${mode}`, async ({ page }) => {
    await open(page, style, "meridian", mode, "appearance");
    const presentation = () => page.locator(".breadcrumb").evaluate(element => {
      const root = getComputedStyle(document.documentElement);
      const breadcrumb = getComputedStyle(element);
      return {
        tokens: ["--shadow-sm", "--shadow-md", "--studio-body-size", "--studio-page-title-size", "--studio-title-weight"].map(token => root.getPropertyValue(token)),
        font: breadcrumb.fontFamily, size: breadcrumb.fontSize, transform: breadcrumb.textTransform, spacing: breadcrumb.letterSpacing
      };
    });
    const baseline = await presentation();
    for (const preset of ["signal", "atelier", "elsa-cloud"]) {
      await page.evaluate(preset => localStorage.setItem("elsa-studio-theme", preset), preset);
      await page.reload();
      await expect(page.locator("html")).toHaveAttribute("data-theme", preset);
      expect(await presentation()).toEqual(baseline);
    }
    const borders = await page.locator("html").evaluate(element => {
      const probe = document.createElement("input");
      element.append(probe);
      probe.style.border = "1px solid var(--studio-border)";
      const actual = getComputedStyle(probe).borderTopColor;
      probe.style.borderColor = "var(--border)";
      const expected = getComputedStyle(probe).borderTopColor;
      probe.remove();
      return { actual, expected };
    });
    expect(borders.actual).toBe(borders.expected);
  });
}

for (const style of styles) for (const mode of ["dark", "dim"]) {
  test(`${style} keeps Meridian active navigation text readable in ${mode}`, async ({ page }, testInfo) => {
    await open(page, style, "meridian", mode, "appearance");
    if (style === "focus") await page.getByRole("button", { name: "Expand navigation", exact: true }).click();
    const active = page.locator(".nav-section a.active").first();
    await expect(active).toBeVisible();
    await expect(active).toHaveAttribute("aria-current", "page");
    const hovered = page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Home", exact: true });
    await hovered.hover();
    await hovered.evaluate(async element => {
      await Promise.all(element.getAnimations().map(animation => animation.finished));
    });
    const background = (element: Element) => getComputedStyle(element).backgroundColor;
    expect(await active.evaluate(background)).not.toBe(await hovered.evaluate(background));
    await page.screenshot({ path: testInfo.outputPath("active-and-hover-navigation.png") });
    await page.addScriptTag({ content: axe.source });
    const violations = await active.evaluate(async element => {
      await document.fonts.ready;
      const result = await (window as Window & { axe: { run: (root: Element, options: object) => Promise<{ violations: Array<{ id: string; nodes: Array<{ failureSummary?: string }> }> }> } }).axe.run(element, { runOnly: ["color-contrast"] });
      return result.violations.map(violation => ({ id: violation.id, failures: violation.nodes.map(node => node.failureSummary) }));
    });
    expect(violations).toEqual([]);
  });
}
