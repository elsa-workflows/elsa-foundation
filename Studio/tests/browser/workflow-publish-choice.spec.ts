import { expect, test, type Page } from "@playwright/test";

test("publishes directly without opening the review dialog when the target is clear", async ({ page }) => {
  await page.goto("/?mode=workflow-publish-choice");

  await choosePublishMode(page, "Publish");

  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("status")).toContainText("Published to default");
  const calls = await readApiCalls(page);
  expect(calls.filter(call => call.method === "PUT" && call.path.endsWith("/drafts/draft-browser"))).toHaveLength(1);
  expect(calls.filter(call => call.method === "POST" && call.path.endsWith("/promote"))).toHaveLength(1);
  expect(calls.filter(call => call.method === "POST" && call.path === "/publishing/workflows/version-2/publish")).toHaveLength(1);
});

test("Review and Publish opens the real review before saving or publishing", async ({ page }) => {
  await page.goto("/?mode=workflow-publish-choice");

  await page.getByRole("button", { name: "Review and Publish", exact: true }).click();

  const review = page.getByRole("dialog", { name: "Publish workflow" });
  await expect(review).toBeVisible();
  await expect(review).toContainText("Ready to publish");
  const callsBeforePublish = await readApiCalls(page);
  expect(callsBeforePublish.filter(call => call.method === "PUT")).toHaveLength(0);
  expect(callsBeforePublish.filter(call => call.method === "POST" && call.path.endsWith("/promote"))).toHaveLength(0);
  expect(callsBeforePublish.filter(call => call.method === "POST" && call.path.endsWith("/publish"))).toHaveLength(0);

  await review.getByRole("button", { name: "Publish", exact: true }).click();

  const success = page.getByRole("dialog", { name: "Published" });
  await expect(success).toContainText("Version 2.0.0 is active in default.");
  const calls = await readApiCalls(page);
  expect(calls.filter(call => call.method === "PUT" && call.path.endsWith("/drafts/draft-browser"))).toHaveLength(1);
  expect(calls.filter(call => call.method === "POST" && call.path.endsWith("/promote"))).toHaveLength(1);
  expect(calls.filter(call => call.method === "POST" && call.path === "/publishing/workflows/version-2/publish")).toHaveLength(1);
});

test("remembers Publish across reload without starting another publication", async ({ page }) => {
  await page.goto("/?mode=workflow-publish-choice");

  await choosePublishMode(page, "Publish");
  await expect(page.getByRole("status")).toContainText("Published to default");
  await expect(page.getByRole("button", { name: "Publish", exact: true })).toBeVisible();
  const mutationCountBeforeReload = (await readApiCalls(page)).filter(isMutationRequest).length;

  await page.reload();

  await expect(page.getByRole("button", { name: "Publish", exact: true })).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect((await readApiCalls(page)).filter(isMutationRequest)).toHaveLength(0);
  expect(mutationCountBeforeReload).toBeGreaterThan(0);

  const primary = page.getByRole("button", { name: "Publish", exact: true });
  await primary.press("Enter");
  await expect(page.getByRole("status")).toContainText("Published to default");
  await expect(primary).toBeFocused();
});

test("falls back to review when the direct publication preflight finds a trigger conflict", async ({ page }) => {
  await page.goto("/?mode=workflow-publish-choice&outcome=conflict");

  await choosePublishMode(page, "Publish");

  const review = page.getByRole("dialog", { name: "Publish workflow" });
  await expect(review).toBeVisible();
  await expect(review.getByRole("alert")).toContainText("These triggers are already in use");
  await expect(review.getByRole("alert")).toContainText("Conflict with default: http:GET:/orders");
  const calls = await readApiCalls(page);
  expect(calls.filter(call => call.method === "PUT")).toHaveLength(0);
  expect(calls.filter(call => call.method === "POST" && call.path.endsWith("/promote"))).toHaveLength(0);
  expect(calls.filter(call => call.method === "POST" && call.path.endsWith("/publish"))).toHaveLength(0);
});

test("recovers a failed direct activation by retrying the retained promoted version", async ({ page }) => {
  await page.goto("/?mode=workflow-publish-choice&outcome=publish-failure");

  await choosePublishMode(page, "Publish");

  const recovery = page.getByRole("dialog", { name: "Couldn’t publish" });
  await expect(recovery).toContainText("Version 2.0.0 was retained. Channel default was not activated.");
  await recovery.getByRole("button", { name: "Retry publication" }).click();

  await expect(page.getByRole("dialog", { name: "Published" })).toContainText("Version 2.0.0 is active in default.");
  const calls = await readApiCalls(page);
  expect(calls.filter(call => call.method === "PUT" && call.path.endsWith("/drafts/draft-browser"))).toHaveLength(1);
  expect(calls.filter(call => call.method === "POST" && call.path.endsWith("/promote"))).toHaveLength(1);
  expect(calls.filter(call => call.method === "POST" && call.path === "/publishing/workflows/version-2/publish")).toHaveLength(2);
});

async function choosePublishMode(page: Page, label: "Publish" | "Review and Publish") {
  await page.getByRole("button", { name: "Publish options", exact: true }).click();
  await page.getByRole("menu", { name: "Publish action" })
    .getByRole("menuitemradio", { name: label, exact: true })
    .click();
}

async function readApiCalls(page: Page) {
  const text = await page.getByTestId("workflow-publish-api-log").textContent() ?? "";
  return text.split(/\r?\n/).filter(Boolean).map(line => {
    const separator = line.indexOf(" ");
    return { method: line.slice(0, separator), path: line.slice(separator + 1) };
  });
}

function isMutationRequest(call: { method: string; path: string }) {
  return call.method === "PUT" || call.path.endsWith("/promote") || call.path.endsWith("/publish");
}
