import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const theme of ["light", "dark"]) {
  for (const width of [390, 1280, 1440]) {
    test(`incident details remain readable at ${width}px in ${theme}`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await page.goto(`/?mode=incident-diagnostics&theme=${theme}`);
      const card = page.locator(".wf-instance-incident");
      await expect(card).toBeVisible();
      const headingColor = await card.locator("strong").evaluate(element => getComputedStyle(element).color);
      await expect(card.locator(".wf-instance-incident-cause")).toHaveCSS("color", headingColor);
      const technical = card.locator(".wf-incident-technical-details");
      await expect(technical).not.toHaveAttribute("open");
      await technical.locator("summary").first().click();
      await expect(card.locator(".wf-incident-message")).toContainText("sha256:");
      await card.getByText("Stack trace", { exact: true }).click();
      for (const content of [card, technical, card.locator(".wf-incident-stacktrace pre")]) {
        expect(await content.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
      }
      const open = card.getByRole("button", { name: "Open incident details", exact: true });
      await open.focus();
      await page.keyboard.press("Enter");
      const dialog = page.getByRole("dialog");
      await expect(dialog).toBeVisible();
      const nextStep = dialog.locator(".wf-incident-next-step");
      await expect(nextStep.getByRole("heading", { name: "Next step" })).toBeVisible();
      await expect(nextStep).toContainText("Current run");
      await expect(nextStep).toContainText("Definition access could not be confirmed here.");
      await expect(nextStep).toContainText("Definition ID");
      expect(await nextStep.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
      expect(await dialog.evaluate(element => {
        const box = element.getBoundingClientRect();
        return box.left >= 0 && box.right <= window.innerWidth && box.top >= 0 && box.bottom <= window.innerHeight;
      })).toBe(true);
      expect(await dialog.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
      expect(await seriousViolations(page)).toEqual([]);
      await page.keyboard.press("Escape");
      await expect(dialog).toBeHidden();
      await expect(open).toBeFocused();
    });
  }
}

test("long incident guidance reflows at 200% effective zoom and keeps keyboard dismissal", async ({ page }) => {
  // A 195 CSS-pixel viewport approximates a 390-pixel laptop viewport at 200% browser zoom.
  await page.setViewportSize({ width: 195, height: 422 });
  await page.goto("/?mode=incident-diagnostics&theme=dark");
  const open = page.getByRole("button", { name: "Open incident details", exact: true });
  await open.focus();
  await page.keyboard.press("Enter");

  const dialog = page.getByRole("dialog");
  const nextStep = dialog.locator(".wf-incident-next-step");
  await expect(nextStep.getByRole("heading", { name: "Next step" })).toBeVisible();
  const overflow = await page.evaluate(() => [...document.querySelectorAll<HTMLElement>("*")]
    .filter(element => element.scrollWidth - element.clientWidth > 1)
    .map(element => `${element.tagName.toLowerCase()}.${String(element.className).replaceAll(" ", ".")}: ${element.scrollWidth} > ${element.clientWidth}`));
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth), overflow.join("; ")).toBeLessThanOrEqual(1);
  expect(await nextStep.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
  expect(await seriousViolations(page)).toEqual([]);
  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
  await expect(open).toBeFocused();
});

async function seriousViolations(page: import("@playwright/test").Page) {
  await page.addScriptTag({ content: axe.source });
  return page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (root: Document, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: Array<{ target: string[]; failureSummary?: string }> }> }> };
    }).axe.run(document, { resultTypes: ["violations"] });
    return result.violations
      .filter(violation => violation.impact === "critical" || violation.impact === "serious")
      .map(violation => `${violation.id}: ${violation.help} (${violation.nodes.flatMap(node => `${node.target.join(" ")}: ${node.failureSummary ?? ""}`).join("; ")})`);
  });
}
