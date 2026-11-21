import { expect, test } from "@playwright/test";

for (const width of [360, 1440]) for (const theme of ["light", "dark"]) {
  test(`User Tasks collection and details ${width}px ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/user-tasks-ux.html?empty&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No tasks here", exact: true })).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", theme);
    await expect(page.locator("aside")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Next page", exact: true })).toHaveCount(0);
    const region = page.locator(".studio-collection-empty");
    await expect(region).toHaveAttribute("aria-label", "Task inbox");
    await expect(region).toHaveAttribute("role", "status");
    const frame = (await region.boundingBox())!;
    expect(frame.width).toBeGreaterThan(width - 70);
    expect(frame.height).toBeGreaterThanOrEqual(140);
    expect(await region.evaluate(node => getComputedStyle(node).borderTopStyle)).toBe("solid");
    const search = page.getByRole("searchbox", { name: "Search tasks", exact: true });
    expect((await search.boundingBox())!.width).toBeGreaterThan(200);
    await page.screenshot({ path: info.outputPath("empty.png"), fullPage: true });
    await page.goto(`/user-tasks-ux.html?two&theme=${theme}&backend=legacy`);
    const link = page.getByRole("link", { name: /^Review purchase request/ });
    await expect(link).toBeVisible();
    await page.getByRole("searchbox", { name: "Search tasks", exact: true }).fill("missing");
    await expect(page.getByRole("heading", { name: "No matching tasks", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Clear search", exact: true }).last().press("Enter");
    await expect(link).toBeVisible();
    await link.press("Enter");
    const detail = page.getByRole("region", { name: /^Review purchase request/ });
    await expect(detail.getByRole("heading")).toBeFocused();
    expect(new URL(page.url()).searchParams.get("backend")).toBe("legacy");
    await detail.getByRole("textbox", { name: "Note" }).fill("Reviewed with keyboard");
    await expect(detail.getByRole("button", { name: "Approve", exact: true })).toBeEnabled();
    await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveCount(0);
    await expect(page.getByRole("region", { name: "Task inbox", exact: true })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Approve travel request", exact: true })).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("task-detail.png"), fullPage: true });
    page.once("dialog", dialog => dialog.dismiss());
    await detail.getByRole("button", { name: "Back to task list", exact: true }).press("Enter");
    await expect(detail.getByRole("textbox", { name: "Note" })).toHaveValue("Reviewed with keyboard");
    page.once("dialog", dialog => dialog.accept());
    await detail.getByRole("button", { name: "Back to task list", exact: true }).press("Enter");
    await expect(detail).toHaveCount(0); await expect(link).toBeFocused();
    await page.goto(`/user-tasks-ux.html?forms&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No reusable forms yet", exact: true })).toBeVisible();
    await expect(page.locator(".studio-collection-empty")).toBeVisible();
    await expect(page.getByRole("textbox")).toHaveCount(0);
    await page.goto(`/user-tasks-ux.html?forms&populated-forms&theme=${theme}`);
    const formRow = page.getByRole("row").filter({ hasText: "purchase-form-with-a-long-identifier" });
    await expect(formRow).toContainText("Published · v1");
    const edit = formRow.getByRole("button", { name: "Edit", exact: true });
    await expect(edit).toBeVisible();
    expect((await edit.boundingBox())!.height).toBeLessThan(48);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("forms-list.png"), fullPage: true });
    await edit.press("Enter");
    await expect(page.getByRole("textbox", { name: "Form ID", exact: true })).toBeDisabled();
    await expect(page.getByRole("textbox", { name: "Form ID", exact: true })).toHaveValue("purchase-form-with-a-long-identifier");
    if (width <= 600) await page.getByRole("button", { name: "Expand controls panel" }).click();
    await page.getByRole("button", { name: "Text input", exact: true }).click();
    const checkbox = page.getByRole("checkbox", { name: "Required", exact: true });
    expect((await checkbox.boundingBox())!.width).toBeLessThanOrEqual(20);
    expect((await checkbox.boundingBox())!.height).toBeLessThanOrEqual(20);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("form-authoring.png"), fullPage: true });
    await page.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(page.getByRole("button", { name: "Purchase response", exact: true })).toBeFocused();
  });
}

test("User Tasks list errors stay distinct from empty results", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?error");
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page.locator(".studio-collection-empty")).toHaveCount(0);
});

for (const width of [360, 1280]) for (const theme of ["light", "dark"]) {
  test(`empty User Tasks inbox uses the shared panel at ${width}px ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/user-tasks-ux.html?empty&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No tasks here", exact: true })).toBeVisible();
    const empty = page.locator(".studio-collection-empty");
    await expect(empty).toHaveAttribute("aria-label", "Task inbox");
    await expect(empty).toBeVisible();
    expect((await empty.boundingBox())!.width).toBeGreaterThan(width - 64);
    expect((await empty.boundingBox())!.height).toBeGreaterThanOrEqual(140);
    await expect(empty).toHaveCSS("border-top-style", "solid");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("tasks-empty.png"), fullPage: true });
  });
}

for (const width of [360, 1280]) for (const theme of ["light", "dark"]) for (const backend of ["lifecycle", "legacy"]) {
  test(`empty ${backend} Forms collection uses the shared panel at ${width}px ${theme}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const backendQuery = backend === "legacy" ? "&backend=legacy" : "";
    await page.goto(`/user-tasks-ux.html?forms${backendQuery}&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No reusable forms yet", exact: true })).toBeVisible();
    const empty = page.locator(".studio-collection-empty");
    await expect(empty).toBeVisible();
    expect((await empty.boundingBox())!.width).toBeGreaterThan(width - 64);
    expect((await empty.boundingBox())!.height).toBeGreaterThanOrEqual(140);
    await expect(empty).toHaveCSS("border-top-style", "solid");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

for (const width of [360, 1440]) {
  test(`form authoring and versions share one framed workspace (${width}px)`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/user-tasks-ux.html?forms&populated-forms&form-history");
    const row = page.getByRole("row").filter({ hasText: "purchase-form-history" });
    await row.getByRole("button", { name: "Edit", exact: true }).click();
    const workspace = page.getByRole("region", { name: "Form editor", exact: true });
    const frame = (await workspace.boundingBox())!;
    expect(frame.width).toBeGreaterThan(width - 70);
    await expect(workspace.getByRole("tablist")).toHaveCount(1);
    const designerTab = page.getByRole("tab", { name: "Designer", exact: true });
    const sourceTab = page.getByRole("tab", { name: "Source", exact: true });
    const previewTab = page.getByRole("tab", { name: "Preview", exact: true });
    const versionsTab = page.getByRole("tab", { name: "Versions", exact: true });
    await expect(designerTab).toHaveAttribute("aria-selected", "true");
    await expect(workspace.getByRole("tab")).toHaveCount(4);
    if (width <= 600) await page.getByRole("button", { name: "Expand properties panel" }).click();
    await page.getByRole("textbox", { name: "Name", exact: true }).fill("Unsaved designer copy");
    await designerTab.press("End");
    await expect(versionsTab).toHaveAttribute("aria-selected", "true");

    const history = page.getByRole("region", { name: "Published form versions", exact: true });
    const latest = history.getByRole("button", { name: "Select version 2", exact: true });
    await expect(latest).toHaveAttribute("aria-pressed", "true");
    await expect(latest.getByText("Latest", { exact: true })).toBeVisible();
    await expect(history.getByRole("region", { name: "Published form versions preview" }).getByText("Decision note", { exact: true })).toBeVisible();
    await page.screenshot({ path: info.outputPath("published-form-history.png"), fullPage: true });
    await history.getByRole("button", { name: "Load older versions", exact: true }).click();
    const earlier = history.getByRole("button", { name: "Select version 1", exact: true });
    await expect(earlier).toBeVisible();
    await expect(history).not.toContainText("Travel response");
    await earlier.click();
    await expect(page.getByRole("heading", { name: "Published version 1", exact: true })).toBeVisible();
    await expect(page.getByText("Earlier note", { exact: true })).toBeVisible();
    await versionsTab.press("ArrowLeft");
    await expect(previewTab).toHaveAttribute("aria-selected", "true");
    await previewTab.press("ArrowLeft");
    await expect(sourceTab).toHaveAttribute("aria-selected", "true");
    await sourceTab.press("ArrowLeft");
    await expect(designerTab).toHaveAttribute("aria-selected", "true");
    await expect(page.getByRole("textbox", { name: "Name", exact: true })).toHaveValue("Unsaved designer copy");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);

    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Back to forms", exact: true }).click();
    await expect(page.getByText("1 form shown", { exact: true })).toBeVisible();
    await expect(row.getByRole("button", { name: "Unsaved designer copy", exact: true })).toBeFocused();
    await row.getByRole("button", { name: "Edit", exact: true }).click();
    await versionsTab.click();
    await expect(history.getByRole("button", { name: "Select version 2", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Back to forms", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Back to forms", exact: true }).click();
    await expect(row).toContainText("Published · Draft");
    await expect(page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr")).toHaveCount(1);
  });
}

test("unapplied form source stays put when Versions is selected", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?forms&populated-forms&form-history");
  const row = page.getByRole("row").filter({ hasText: "purchase-form-history" });
  await row.getByRole("button", { name: "Edit", exact: true }).click();
  const sourceTab = page.getByRole("tab", { name: "Source", exact: true });
  const versionsTab = page.getByRole("tab", { name: "Versions", exact: true });
  await sourceTab.click();
  const source = page.getByRole("textbox", { name: "Form JSON", exact: true });
  await expect(source).toBeVisible();
  await source.fill("{");
  await expect(page.getByRole("status").filter({ hasText: "Unapplied source edits" })).toBeVisible();
  await versionsTab.click();
  await expect(sourceTab).toHaveAttribute("aria-selected", "true");
  await expect(versionsTab).toHaveAttribute("aria-selected", "false");
  await page.getByRole("button", { name: "Reset", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Unapplied source edits" })).toHaveCount(0);
  await versionsTab.click();
  await expect(versionsTab).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("button", { name: "Select version 2", exact: true })).toBeVisible();
});

test("legacy form pages add history without adding duplicate collection rows", async ({ page }, info) => {
  await page.goto("/user-tasks-ux.html?forms&backend=legacy&legacy-forms");
  const table = page.getByRole("table", { name: "Reusable task forms" });
  await expect(table.locator("tbody tr")).toHaveCount(2);
  await expect(page.getByText("2 forms shown", { exact: true })).toBeVisible();
  const newsletter = table.getByRole("row").filter({ hasText: "newsletter-signup" });
  await newsletter.getByRole("button", { name: "Open form newsletter-signup", exact: true }).click();
  const workspace = page.getByRole("region", { name: "Published form", exact: true });
  const frame = (await workspace.boundingBox())!;
  expect(frame.width).toBeGreaterThan(await page.evaluate(() => innerWidth) - 70);
  expect((await workspace.getByRole("heading", { name: "Published version 2", exact: true }).boundingBox())!.x).toBeGreaterThan(frame.x + 8);
  await expect(workspace.getByRole("tablist")).toHaveCount(1);
  await page.screenshot({ path: info.outputPath("published-form-workspace.png"), fullPage: true });
  await page.getByRole("tab", { name: "Versions", exact: true }).click();
  const history = page.getByRole("region", { name: "Published form versions", exact: true });
  const latest = history.getByRole("button", { name: "Select version 2", exact: true });
  await expect(latest).toHaveAttribute("aria-pressed", "true");
  await expect(latest.getByText("Latest", { exact: true })).toBeVisible();
  await history.getByRole("button", { name: "Load older versions for this form", exact: true }).click();
  await expect(history.getByRole("button", { name: "Select version 1", exact: true })).toBeVisible();
  await expect(history).not.toContainText("Travel details");
  await history.getByRole("button", { name: "Select version 1", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Published version 1", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Back to forms", exact: true }).click();
  await expect(table.locator("tbody tr")).toHaveCount(2);
  await expect(page.getByText("2 forms shown", { exact: true })).toBeVisible();
  await expect(newsletter.getByRole("button", { name: "Open form newsletter-signup", exact: true })).toBeFocused();
});

test("legacy history failure preserves the latest form snapshot", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?forms&backend=legacy&legacy-forms&form-history-error");
  await page.getByRole("row").filter({ hasText: "newsletter-signup" }).getByRole("button", { name: "Open form newsletter-signup", exact: true }).click();
  await page.getByRole("tab", { name: "Versions", exact: true }).click();
  const history = page.getByRole("region", { name: "Published form versions", exact: true });
  await history.getByRole("button", { name: "Load older versions for this form", exact: true }).click();
  await expect(history.getByRole("alert")).toContainText("Older form versions are unavailable");
  await expect(history.getByRole("button", { name: "Select version 2", exact: true })).toBeVisible();
  await expect(history.getByRole("button", { name: "Select version 1", exact: true })).toHaveCount(0);
});

test("read-only form readers can inspect published versions", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?forms&form-history&read-only");
  await page.getByRole("row").filter({ hasText: "purchase-form-history" }).getByRole("button", { name: "View", exact: true }).click();
  await page.getByRole("tab", { name: "Versions", exact: true }).click();
  const history = page.getByRole("region", { name: "Published form versions", exact: true });
  await expect(history.getByRole("button", { name: "Select version 2", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Back to forms", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save draft", exact: true })).toHaveCount(0);
});

test("a failed older-form page leaves the selected published version available", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?forms&populated-forms&form-history&form-history-error");
  await page.getByRole("row").filter({ hasText: "purchase-form-history" }).getByRole("button", { name: "Edit", exact: true }).click();
  await page.getByRole("tab", { name: "Versions", exact: true }).click();
  const history = page.getByRole("region", { name: "Published form versions", exact: true });
  await history.getByRole("button", { name: "Load older versions", exact: true }).click();
  await expect(history.getByRole("alert")).toContainText("Older form versions are unavailable");
  await expect(history.getByRole("button", { name: "Select version 2", exact: true })).toBeVisible();
  await expect(history.getByRole("button", { name: "Select version 1", exact: true })).toHaveCount(0);
});

test("User Tasks distinguish denied and failed requests from an empty inbox", async ({ page }) => {
  for (const [query, title] of [["denied", "Task access unavailable"], ["error", "Tasks unavailable"]]) {
    await page.goto(`/user-tasks-ux.html?${query}`);
    await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "No tasks here", exact: true })).toHaveCount(0);
    await expect(page.locator("aside")).toHaveCount(0);
  }
});

test("a standalone task retains an unconfirmed response and prevents leaving until resolved", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?two&ambiguous&manage&task=purchase");
  const detail = page.getByRole("region", { name: /^Review purchase request/ });
  await detail.getByRole("textbox", { name: /^Note/ }).fill("Keep this exact response");
  await detail.getByRole("button", { name: "Approve", exact: true }).click();
  await expect(detail.getByRole("button", { name: "Retry Approve", exact: true })).toBeVisible();
  await expect(detail.getByRole("button", { name: "Back to task list", exact: true })).toBeDisabled();
  await detail.getByRole("button", { name: "Task actions", exact: true }).click();
  await expect(page.getByRole("menuitem", { name: "Reassign task", exact: true })).toBeDisabled();
  await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Approve travel request", exact: true })).toHaveCount(0);
  await expect(detail.getByRole("textbox", { name: /^Note/ })).toHaveValue("Keep this exact response");
  await detail.getByRole("button", { name: "Retry Approve", exact: true }).click();
  const sent = await page.evaluate(() => (window as unknown as { taskRequests: unknown[] }).taskRequests);
  expect(sent).toHaveLength(2); expect(sent[1]).toEqual(sent[0]);
});

for (const width of [360, 1280]) {
  test(`task history stays out of the response until requested at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/user-tasks-ux.html?task=purchase");
    const detail = page.getByRole("region", { name: /^Review purchase request/ });
    const response = detail.getByRole("region", { name: "Task response", exact: true });
    const note = response.getByRole("textbox", { name: "Note" });
    await note.fill("Keep this response while inspecting history");
    await page.screenshot({ path: info.outputPath("task-workspace.png"), fullPage: true });
    const historyButton = detail.getByRole("button", { name: "History", exact: true });
    await expect(historyButton).toHaveAttribute("aria-expanded", "false");
    await historyButton.click();
    const history = page.getByRole("dialog", { name: "Task history", exact: true });
    await expect(history).toContainText("created");
    await expect(note).toHaveValue("Keep this response while inspecting history");
    await page.screenshot({ path: info.outputPath("task-history.png"), fullPage: true });
    const bounds = (await history.boundingBox())!;
    if (width <= 640) expect(bounds.y + bounds.height).toBeGreaterThanOrEqual(899);
    else expect(bounds.x).toBeGreaterThan(width - 500);
    await page.keyboard.press("Escape");
    await expect(history).toBeHidden();
    await expect(historyButton).toBeFocused();
    await expect(historyButton).toHaveAttribute("aria-expanded", "false");
    await expect(note).toHaveValue("Keep this response while inspecting history");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test("reassignment stays permission-gated and reports errors before closing on success", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?task=purchase");
  await page.getByRole("button", { name: "Task actions" }).click();
  await expect(page.getByRole("menuitem", { name: "Reassign task", exact: true })).toHaveCount(0);

  await page.goto("/user-tasks-ux.html?manage&reassign-error&task=purchase");
  await page.getByRole("button", { name: "Task actions" }).click();
  await page.getByRole("menuitem", { name: "Reassign task", exact: true }).click();
  let dialog = page.getByRole("dialog", { name: "Reassign task", exact: true });
  await dialog.getByRole("textbox", { name: "User ID", exact: true }).fill("bob");
  await dialog.getByRole("button", { name: "Reassign", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("Reassignment failed");
  await expect(dialog).toBeVisible();

  await page.goto("/user-tasks-ux.html?manage&task=purchase");
  await page.getByRole("button", { name: "Task actions" }).click();
  await page.getByRole("menuitem", { name: "Reassign task", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "Reassign task", exact: true });
  await dialog.getByRole("textbox", { name: "User ID", exact: true }).fill("bob");
  await dialog.getByRole("button", { name: "Reassign", exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("region", { name: /^Review purchase request/ })).toBeVisible();
  const sent = await page.evaluate(() => (window as unknown as { taskRequests: Record<string, unknown>[] }).taskRequests);
  expect(sent).toHaveLength(1);
  expect(sent[0]).toMatchObject({ revision: 1, assigneeId: "bob", groupId: null });
});

test("completed task responses remain framed and read-only without response actions", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?completed&task=purchase");
  const detail = page.getByRole("region", { name: /^Review purchase request/ });
  const response = detail.getByRole("region", { name: "Task response", exact: true });
  await expect(response.getByRole("textbox", { name: "Note" })).toHaveValue("Saved response");
  await expect(response.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);
  await expect(detail.getByRole("button", { name: "History", exact: true })).toBeVisible();
});

 test("a failed form publication preserves the shared working copy and list paging", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?forms&form-error");
  await expect(page.getByRole("button", { name: "More forms", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Create form", exact: true }).first().click();
  await page.getByRole("textbox", { name: "Form ID", exact: true }).fill("purchase");
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("Purchase response");
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("This form changed elsewhere");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "More forms", exact: true })).toBeVisible();
});


test("an expired task page restarts its collection with the search preserved", async ({ page }) => {
  await page.goto("/user-tasks-ux.html?cursor-error&after=expired&search=Review");
  await expect(page.getByRole("heading", { name: "Tasks unavailable", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "First page", exact: true }).click();
  await expect(page.getByRole("link", { name: /^Review purchase request/ })).toBeVisible();
  await expect(page.getByRole("searchbox", { name: "Search tasks", exact: true })).toHaveValue("Review");
});
