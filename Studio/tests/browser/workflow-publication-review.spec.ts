import { expect, test, type Page } from "@playwright/test";
import axe from "axe-core";

test("keeps the publication decision calm and actions visible at constrained height", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 620 });
  await page.goto("/?mode=publication-review&theme=dark");

  const dialog = page.getByRole("dialog", { name: "Publish workflow" });
  await expect(dialog.getByText("Update the published workflow with your latest changes.")).toBeVisible();
  await expect(dialog.getByText("Review changes")).toBeVisible();
  await expect(dialog.getByText("Publishing options")).toBeVisible();
  await expect(dialog.getByText("Technical details", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Version 2.0.0", { exact: true })).toBeVisible();
  await expect(dialog.locator(".wf-publication-technical")).not.toHaveAttribute("open");
  await expect(dialog).toContainText("Ready to publish");
  await expect(dialog.getByRole("alert")).toHaveCount(0);
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeVisible();

  const dialogBox = await dialog.boundingBox();
  const footerBox = await dialog.locator(".wf-publication-footer").boundingBox();
  expect(dialogBox).not.toBeNull();
  expect(footerBox).not.toBeNull();
  expect(footerBox!.y + footerBox!.height).toBeLessThanOrEqual(620);
  expect(footerBox!.y + footerBox!.height).toBeLessThanOrEqual(dialogBox!.y + dialogBox!.height + 1);
});

test("creates a named Publication channel and refreshes review automatically", async ({ page }) => {
  await page.goto("/?mode=publication-review");
  const dialog = page.getByRole("dialog", { name: "Publish workflow" });
  await dialog.getByText("Publishing options").click();

  await dialog.getByLabel("Publication channel").selectOption("__create-publication-channel__");
  await dialog.getByLabel("New publication channel").fill("canary");

  await expect(dialog.getByText("Publish your changes to canary.")).toBeVisible();
  await expect(dialog).toContainText("Ready to publish");
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeEnabled();
});

test("offers exact version editing only when the server advertises support", async ({ page }) => {
  await page.goto("/?mode=publication-review");
  const dialog = page.getByRole("dialog", { name: "Publish workflow" });
  await dialog.getByText("Publishing options").click();

  await dialog.getByRole("radio", { name: /Exact version/ }).check();
  await dialog.getByRole("textbox", { name: "Exact semantic version" }).fill("2.3.0-rc.1");

  await expect(dialog).toContainText("2.3.0-rc.1");
  await expect(dialog).toContainText("Ready to publish");
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeEnabled();

  await page.goto("/?mode=publication-review&exact=unsupported");
  const unsupportedDialog = page.getByRole("dialog", { name: "Publish workflow" });
  await unsupportedDialog.getByText("Publishing options").click();
  await expect(unsupportedDialog.getByRole("radio", { name: /Exact version/ })).toHaveCount(0);
});

test("success and retained-promotion recovery keep their actions in the fixed footer", async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 520 });
  await page.goto("/?mode=publication-review&theme=dark");
  await page.getByRole("button", { name: "Publish" }).click();

  const success = page.getByRole("dialog", { name: "Published" });
  await expect(success).toContainText("Version 2.0.0 is active in default.");
  await expect(success.getByRole("button", { name: "Close" })).toBeVisible();
  await expect(success.getByRole("button", { name: "View published workflow" })).toBeVisible();

  await page.goto("/?mode=publication-review&outcome=recovery&theme=dark");
  await page.getByRole("button", { name: "Publish" }).click();
  const recovery = page.getByRole("dialog", { name: "Couldn’t publish" });
  await expect(recovery).toContainText("Version 2.0.0 was retained. Channel default was not activated.");
  await expect(recovery.getByRole("button", { name: "Close" })).toBeVisible();
  await expect(recovery.getByRole("button", { name: "Retry publication" })).toBeVisible();
});


test("shows a conflict descriptor as the primary label and the raw key as muted detail", async ({ page }) => {
  await page.goto("/?mode=publication-review&outcome=conflict");
  const dialog = page.getByRole("dialog", { name: "Publish workflow" });
  const alert = dialog.getByRole("alert");

  await expect(alert).toContainText("These triggers are already in use");
  await expect(alert).toContainText("GET /orders/{id}");
  await expect(alert.locator(".wf-publication-selection")).toHaveText(
    "httpendpoint:sha256:4191f265b8b2d2152d4056ed3d616d32f7c2953e7ae41c4f8b184244b830e4f7");
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeDisabled();
});

test("shows a generic primary label and the raw key as muted detail when no descriptor is present", async ({ page }) => {
  await page.goto("/?mode=publication-review&outcome=conflict&descriptor=0");
  const alert = page.getByRole("dialog", { name: "Publish workflow" }).getByRole("alert");

  await expect(alert).toContainText("Trigger conflict");
  await expect(alert.locator(".wf-publication-selection")).toHaveText(
    "httpendpoint:sha256:4191f265b8b2d2152d4056ed3d616d32f7c2953e7ae41c4f8b184244b830e4f7");
  await expect(alert).not.toContainText("GET /orders/{id}");
});

test("shows trigger counts while keeping exact identifiers in an explicit technical disclosure", async ({ page }) => {
  await page.goto("/?mode=publication-review&theme=dark");
  const dialog = page.getByRole("dialog", { name: "Publish workflow" });
  const technical = dialog.locator(".wf-publication-technical");
  const changes = dialog.locator("details").filter({ has: page.getByText("Review changes", { exact: true }) });
  await changes.locator("summary").click();
  await expect(changes).toContainText("1 added");
  await expect(changes).not.toContainText("sha256:");
  await expect(technical.locator(".wf-publication-claims")).not.toBeVisible();

  await technical.locator("summary").click();
  await expect(technical.locator(".wf-publication-claims")).toContainText("httpendpoint:sha256:");
  await expect(technical.locator(".wf-publication-claims")).toBeVisible();
  await technical.locator("summary").click();
  await expect(technical.locator(".wf-publication-claims")).not.toBeVisible();
  await expect(dialog.getByRole("button", { name: "Publish", exact: true })).toBeEnabled();
});

// Spec 261 FR-515: warnings are listed under their own heading with text and an icon, and Publish stays enabled.
test("lists validation warnings without blocking Publish", async ({ page }) => {
  await page.goto("/?mode=publication-review&warnings=1&theme=light");
  const dialog = page.getByRole("dialog", { name: "Publish workflow" });

  const warnings = dialog.getByRole("region", { name: "Warnings" });
  await expect(warnings).toBeVisible();
  await expect(warnings.getByRole("listitem")).toHaveCount(2);
  await expect(warnings.getByRole("listitem").first()).toContainText("\"Charge card\" is marked safe to repeat");
  await expect(warnings).toContainText("These do not block publishing.");
  await expect(dialog.getByRole("alert")).toHaveCount(0);
  await expect(dialog.getByRole("button", { name: "Publish" })).toBeEnabled();

  await dialog.getByRole("button", { name: "Publish" }).click();
  await expect(page.getByRole("dialog", { name: "Published" })).toContainText("Version 2.0.0 is active in default.");
});

for (const theme of ["light", "dark"]) {
  for (const width of [390, 1200]) {
    test(`the review dialog fits ${width}px in ${theme} with no serious accessibility violations`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await page.goto(`/?mode=publication-review&warnings=1&theme=${theme}`);
      const dialog = page.getByRole("dialog", { name: "Publish workflow" });
      const warnings = dialog.getByRole("region", { name: "Warnings" });
      await expect(warnings).toBeVisible();
      await expect(dialog.locator(".wf-publication-footer").getByRole("button", { name: "Publish" })).toBeVisible();

      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
      const box = await warnings.boundingBox();
      expect(box!.x + box!.width).toBeLessThanOrEqual(width);
      expect(await seriousViolations(page, ".wf-publication-review")).toEqual([]);
    });
  }
}

// The fixture page chrome predates spec 261. Scope the dialog so the footer and contrast are included.
async function seriousViolations(page: Page, selector: string) {
  await page.addScriptTag({ content: axe.source });
  return page.evaluate(async scope => {
    const result = await (window as Window & {
      axe: { run: (root: Element, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: Array<{ target: unknown[] }> }> }> };
    }).axe.run(document.querySelector(scope)!, { resultTypes: ["violations"] });
    return result.violations
      .filter(violation => violation.impact === "critical" || violation.impact === "serious")
      .map(violation => `${violation.id}: ${violation.help} (${violation.nodes.map(node => node.target.join(" ")).join(", ")})`);
  }, selector);
}
