import { expect, test, type Page } from "@playwright/test";

async function open(page: Page, scenario = "default") {
  await page.goto(`/agent-review.html?scenario=${scenario}`);
  await page.evaluate(() => sessionStorage.clear());
  await page.reload();
}

test("approves the exact pending content with the keyboard and stays resolved after reload", async ({ page }) => {
  await open(page);
  await expect(page.getByRole("heading", { name: "Launch announcement" })).toBeVisible();
  await expect(page.getByTestId("review-content")).toHaveText("Moss gardens reward patience.");
  await expect(page.getByText("Proofreader: ready to publish.")).toBeVisible();
  await expect(page.getByTestId("review-status")).toHaveText("Awaiting your decision");

  await page.getByLabel("Comment (optional)").focus();
  await page.keyboard.type("Ship it.");
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Approve" })).toBeFocused();
  await page.keyboard.press("Enter");

  await expect(page.getByRole("status")).toHaveAttribute("data-receipt", "Accepted");
  await expect(page.getByTestId("review-status")).toHaveText("Approved");
  await expect(page.getByRole("button", { name: "Approve" })).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId("review-status")).toHaveText("Approved");
  await expect(page.getByTestId("review-resolution")).toContainText("Ship it.");
  expect(await page.evaluate(() => (window as unknown as { fixtureRequests: () => unknown[] }).fixtureRequests().length)).toBe(1);
});

test("denies with the keyboard and never offers approval afterwards", async ({ page }) => {
  await open(page);
  await page.getByRole("button", { name: "Approve" }).focus();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Deny" })).toBeFocused();
  await page.keyboard.press("Enter");

  await expect(page.getByTestId("review-status")).toHaveText("Rejected");
  await expect(page.getByRole("button", { name: "Approve" })).toHaveCount(0);
});

test("shows structured agent output as formatted JSON", async ({ page }) => {
  await open(page, "json");
  const content = page.getByTestId("review-content");
  await expect(content).toContainText("\"headline\": \"Moss gardens\"");
  await expect(content).toContainText("\"tags\": [");
});

test("retries an unavailable submission with the same request ID", async ({ page }) => {
  await open(page, "unavailable");
  await page.getByRole("button", { name: "Approve" }).click();
  await expect(page.getByRole("status")).toHaveAttribute("data-receipt", "Unavailable");
  await page.getByRole("button", { name: "Approve" }).click();
  await expect(page.getByTestId("review-status")).toHaveText("Approved");

  const requests = await page.evaluate(() => (window as unknown as { fixtureRequests: () => { requestId: string }[] }).fixtureRequests());
  expect(requests).toHaveLength(2);
  expect(requests[1].requestId).toBe(requests[0].requestId);
});

test("reports a conflict and an already resolved review without deciding", async ({ page }) => {
  await open(page, "conflict");
  await page.getByRole("button", { name: "Deny" }).click();
  await expect(page.getByRole("status")).toHaveAttribute("data-receipt", "Conflict");
  await expect(page.getByTestId("review-status")).toHaveText("Awaiting your decision");

  await open(page, "resolved");
  await expect(page.getByTestId("review-status")).toHaveText("Approved");
  await expect(page.getByTestId("review-resolution")).toContainText("grace");
});

test("explains a missing review", async ({ page }) => {
  await open(page, "missing");
  await expect(page.getByTestId("review-missing")).toBeVisible();
});

test("fits a narrow viewport without horizontal scrolling", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 800 });
  await open(page, "json");
  await expect(page.getByTestId("review-content")).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  await expect(page.getByRole("button", { name: "Approve" })).toBeVisible();
});
