import { expect, test } from "@playwright/test";

test("resizes the trace list and span details with mouse and keyboard", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/opentelemetry-resize.html");
  await expect(page.getByRole("heading", { name: "Workflow resize fixture" })).toBeVisible();

  const widthHandle = page.getByRole("separator", { name: "Resize trace details panel" });
  const heightHandle = page.getByRole("separator", { name: "Resize span details" });
  await expect(widthHandle).toHaveAttribute("aria-controls", /otel-trace-detail/);
  await expect(heightHandle).toHaveAttribute("aria-controls", /otel-span-inspector/);

  const widthBefore = Number(await widthHandle.getAttribute("aria-valuenow"));
  const widthBounds = await widthHandle.boundingBox();
  expect(widthBounds).not.toBeNull();
  await page.mouse.move(widthBounds!.x + widthBounds!.width / 2, widthBounds!.y + widthBounds!.height / 2);
  await page.mouse.down();
  await page.mouse.move(widthBounds!.x + widthBounds!.width / 2 + 120, widthBounds!.y + widthBounds!.height / 2, { steps: 6 });
  await page.mouse.up();
  const widthAfter = Number(await widthHandle.getAttribute("aria-valuenow"));
  expect(widthAfter).toBeGreaterThan(widthBefore);
  expect(widthAfter).toBeGreaterThanOrEqual(Number(await widthHandle.getAttribute("aria-valuemin")));
  expect(widthAfter).toBeLessThanOrEqual(Number(await widthHandle.getAttribute("aria-valuemax")));
  await widthHandle.focus();
  await page.keyboard.press("End");
  await expect(widthHandle).toHaveAttribute("aria-valuenow", "70");
  await page.keyboard.press("ArrowRight");
  await expect(widthHandle).toHaveAttribute("aria-valuenow", "70");
  await page.keyboard.press("Home");
  await page.keyboard.press("ArrowLeft");
  await expect(widthHandle).toHaveAttribute("aria-valuenow", "30");
  for (let index = 0; index < 8; index++) await page.keyboard.press("ArrowRight");
  await expect(widthHandle).toHaveAttribute("aria-valuenow", "46");

  const heightBefore = Number(await heightHandle.getAttribute("aria-valuenow"));
  const heightBounds = await heightHandle.boundingBox();
  expect(heightBounds).not.toBeNull();
  await page.mouse.move(heightBounds!.x + heightBounds!.width / 2, heightBounds!.y + heightBounds!.height / 2);
  await page.mouse.down();
  await page.mouse.move(heightBounds!.x + heightBounds!.width / 2, heightBounds!.y + heightBounds!.height / 2 + 70, { steps: 6 });
  await page.mouse.up();
  expect(Number(await heightHandle.getAttribute("aria-valuenow"))).toBeGreaterThan(heightBefore);

  await heightHandle.focus();
  await page.keyboard.press("End");
  await expect(heightHandle).toHaveAttribute("aria-valuenow", "75");
  await page.keyboard.press("ArrowDown");
  await expect(heightHandle).toHaveAttribute("aria-valuenow", "75");
  await page.keyboard.press("Home");
  await page.keyboard.press("ArrowUp");
  await expect(heightHandle).toHaveAttribute("aria-valuenow", "25");

  const activitySpan = page.locator(".otel-waterfall-row").filter({ hasText: "WriteLine activity" });
  await activitySpan.click();
  await expect(page.locator(".otel-span-inspector h3")).toHaveText("WriteLine activity");
  await page.screenshot({ path: testInfo.outputPath("opentelemetry-resized.png"), fullPage: true });
});

test("stacks the trace panes at narrow widths without exposing the width separator", async ({ page }) => {
  await page.setViewportSize({ width: 700, height: 900 });
  await page.goto("/opentelemetry-resize.html");

  await expect(page.getByRole("separator", { name: "Resize trace details panel" })).toHaveCount(0);
  await expect(page.getByRole("separator", { name: "Resize span details" })).toBeVisible();
  const list = page.locator(".otel-trace-list");
  const detail = page.locator(".otel-trace-detail");
  expect((await detail.boundingBox())!.y).toBeGreaterThan((await list.boundingBox())!.y);
  expect(await page.locator("html").evaluate(element => element.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(1);
});

test("keeps both resize controls and internal span scrolling in an embedded panel", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 480 });
  await page.goto("/opentelemetry-resize.html?surface=panel");

  await expect(page.getByRole("separator", { name: "Resize trace details panel" })).toBeVisible();
  await expect(page.getByRole("separator", { name: "Resize span details" })).toBeVisible();
  const inspector = page.locator(".otel-span-inspector");
  await expect(inspector).toBeVisible();
  expect(await inspector.evaluate(element => element.scrollHeight - element.clientHeight)).toBeGreaterThan(0);
  await page.locator(".otel-waterfall-row").filter({ hasText: "Checkpoint commit" }).click();
  await expect(inspector.locator("h3")).toHaveText("Checkpoint commit");
});

test("omits the vertical split control when a trace has no span details", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/opentelemetry-resize.html?empty");

  await expect(page.getByRole("separator", { name: "Resize trace details panel" })).toBeVisible();
  await expect(page.getByRole("separator", { name: "Resize span details" })).toHaveCount(0);
  await expect(page.locator(".otel-waterfall")).toBeVisible();
});