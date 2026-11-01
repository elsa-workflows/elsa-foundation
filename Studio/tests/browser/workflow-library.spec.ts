import { expect, test } from "@playwright/test";

test("a workflow keeps its identity while tags, saved views and favorites organize it", async ({ page }) => {
  await page.goto("/?mode=workflow-library&theme=light");
  const payment = page.getByRole("row", { name: "Open workflow definition Collect overdue payment" });
  await expect(payment).toBeVisible();
  await payment.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Organize Collect overdue payment" });
  await expect(dialog.getByText("Finance", { exact: true })).toBeVisible();
  await dialog.getByRole("checkbox", { name: "Critical", exact: true }).check();
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(dialog).toBeHidden();

  await page.getByRole("button", { name: "Filters", exact: true }).click();
  await page.getByRole("button", { name: "Add tag filter" }).click();
  await expect(page.getByRole("row", { name: "Open workflow definition Welcome new customer" })).toBeHidden();
  await expect(payment).toBeVisible();
  await page.getByRole("button", { name: "Save view", exact: true }).click();
  const view = page.getByRole("dialog", { name: "Save workflow view" });
  await view.getByRole("textbox", { name: "View name" }).fill("Critical workflows");
  await view.getByRole("checkbox", { name: "Share within this tenant" }).check();
  await view.getByRole("button", { name: "Save as new view" }).click();
  await expect(view).toBeHidden();
  await page.getByRole("combobox", { name: "Saved workflow view" }).selectOption({ label: "Critical workflows · Shared" });
  await expect(page).toHaveURL(/view=view-/);

  await payment.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Add to favorites" }).click();
  await page.getByRole("button", { name: "Favorites", exact: true }).click();
  await expect(payment).toBeVisible();
  const observations = await page.evaluate(() => (window as unknown as Window & { workflowLibraryRequests: { method: string; url: string; body?: unknown }[]; workflowLibraryDefinitions: unknown[] }).workflowLibraryRequests);
  const write = observations.find(request => request.method === "PUT" && request.url.includes("organization/collect-payment"));
  expect(write?.body).toMatchObject({ revision: "1", tags: [{ tagId: "team", valueId: "value-billing" }, { tagId: "critical" }] });
  expect(observations.some(request => /draft|version|publish/.test(request.url))).toBe(false);
});

test("bulk edits preview explicit identities and keep mixed outcomes visible", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 780 });
  await page.goto("/?mode=workflow-library&bulk=partial&theme=dark");
  await page.getByRole("checkbox", { name: "Select visible workflow definitions" }).check();
  await page.getByRole("button", { name: "Edit tags", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Edit tags on 2 workflows" });
  await dialog.getByRole("checkbox", { name: "Critical", exact: true }).check();
  await expect(dialog.getByRole("button", { name: "Apply tag changes" })).toHaveCount(0);
  await dialog.getByRole("button", { name: "Preview changes" }).click();
  await expect(dialog.getByRole("region", { name: "Bulk tag preview" })).toContainText("collect-payment");
  await expect(dialog.getByRole("region", { name: "Bulk tag preview" })).toContainText("welcome");
  await dialog.getByRole("button", { name: "Apply tag changes" }).click();
  await expect(dialog.getByRole("status", { name: "Bulk tag results" })).toContainText("success");
  await expect(dialog.getByRole("status", { name: "Bulk tag results" })).toContainText("conflict");
  const box = await dialog.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0); expect(box!.x + box!.width).toBeLessThanOrEqual(390);
  expect(box!.y).toBeGreaterThanOrEqual(0); expect(box!.y + box!.height).toBeLessThanOrEqual(780);
  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("button", { name: "Refresh workflow definitions" })).toBeFocused();
});

test("a stale organization write retains the intended change until an explicit reload", async ({ page }) => {
  await page.goto("/?mode=workflow-library&conflict=tags");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Organize Collect overdue payment" });
  await dialog.getByRole("checkbox", { name: "Critical", exact: true }).check();
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("409");
  await expect(dialog.getByRole("checkbox", { name: "Critical", exact: true })).toBeChecked();
  await dialog.getByRole("button", { name: "Reload organization" }).click();
  await expect(dialog.getByRole("checkbox", { name: "Critical", exact: true })).not.toBeChecked();
});

test("moving from Organization contains Tab and Shift+Tab in the active move dialog", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  await page.getByRole("button", { name: "Move to folder", exact: true }).click();
  const move = page.getByRole("dialog", { name: "Move workflow definitions", exact: true });
  const first = move.getByRole("radio", { name: "Unfiled", exact: true });
  const last = move.getByRole("button", { name: "Move", exact: true });
  await last.focus();
  await page.keyboard.press("Tab");
  await expect(first).toBeFocused();
  await page.keyboard.press("Shift+Tab");
  await expect(last).toBeFocused();
});

test("Escape cancels the nested move and restores Organization keyboard focus", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const organization = page.getByRole("dialog", { name: "Organize Collect overdue payment", exact: true });
  await organization.getByRole("button", { name: "Move to folder", exact: true }).click();
  const move = page.getByRole("dialog", { name: "Move workflow definitions", exact: true });
  await expect(move).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(move).toBeHidden();
  await expect(organization.getByRole("button", { name: "Move to folder", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(organization).toBeHidden();
  await expect(page.getByRole("button", { name: "Refresh workflow definitions", exact: true })).toBeFocused();
});

test("catalog management creates controlled values and retires their stable tag identity", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Manage tags", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Manage workflow tags" });
  await dialog.getByRole("textbox", { name: "Tag name", exact: true }).fill("Region");
  await dialog.getByRole("combobox", { name: "Tag kind" }).selectOption("controlled");
  await dialog.getByRole("combobox", { name: "Tag cardinality" }).selectOption("multiple");
  await dialog.getByRole("button", { name: "Add controlled value" }).click();
  await dialog.getByRole("textbox", { name: "Value name 1" }).fill("Europe");
  await dialog.getByRole("button", { name: "Create tag", exact: true }).click();
  await expect(dialog.getByRole("status")).toContainText("Saved tag Region");
  await expect(dialog.getByRole("combobox", { name: "Tag kind" })).toBeDisabled();
  await dialog.getByRole("checkbox", { name: "Retire this tag" }).check();
  await dialog.getByRole("button", { name: "Save tag", exact: true }).click();
  await expect(dialog.getByRole("status")).toContainText("Saved tag Region");
  const writes = await page.evaluate(() => (window as unknown as Window & { workflowLibraryRequests: { method: string; url: string; body?: unknown }[] }).workflowLibraryRequests.filter(request => request.method === "POST" || request.method === "PUT"));
  expect(writes[0].body).toMatchObject({ values: [{ id: "", name: "Europe" }] });
  expect(writes[1].body).toMatchObject({ deprecated: true, revision: "1", values: [{ id: expect.stringMatching(/^value-/), name: "Europe" }] });
});

test("catalog management preserves existing server identities when adding controlled values", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Manage tags", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Manage workflow tags" });
  await dialog.getByRole("combobox", { name: "Tag to manage" }).selectOption("team");
  await dialog.getByRole("button", { name: "Add controlled value" }).click();
  await dialog.getByRole("textbox", { name: "Value name 3" }).fill("Operations");
  await dialog.getByRole("button", { name: "Save tag", exact: true }).click();
  await expect(dialog.getByRole("status")).toContainText("Saved tag Owning team");
  const write = await page.evaluate(() => (window as unknown as { workflowLibraryRequests: { method: string; url: string; body?: unknown }[] }).workflowLibraryRequests.find(request => request.method === "PUT" && request.url.endsWith("/tags/team")));
  expect(write?.body).toMatchObject({ revision: "1", values: [{ id: "value-billing", name: "Billing" }, { id: "value-support", name: "Support" }, { id: "", name: "Operations" }] });
});

test("reader permissions preserve browsing and private views while withholding mutations", async ({ page }) => {
  await page.goto("/?mode=workflow-library&permissions=read");
  await expect(page.getByRole("button", { name: "Save view", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Manage tags", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create folder", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Organize Collect overdue payment" });
  await expect(dialog.getByRole("checkbox", { name: "Critical", exact: true })).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Save tags", exact: true })).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Move to folder", exact: true })).toHaveCount(0);
  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Save view", exact: true }).click();
  await expect(page.getByRole("checkbox", { name: "Share within this tenant" })).toBeDisabled();
});

test("bulk removal can clear different values of a single-valued classification across workflows", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("checkbox", { name: "Select visible workflow definitions" }).check();
  await page.getByRole("button", { name: "Edit tags", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Edit tags on 2 workflows" });
  await dialog.getByRole("combobox", { name: "Bulk tag change" }).selectOption("remove");
  await dialog.getByRole("checkbox", { name: "Billing", exact: true }).check();
  await dialog.getByRole("checkbox", { name: "Support", exact: true }).check();
  await expect(dialog.getByRole("checkbox", { name: "Billing", exact: true })).toBeChecked();
  await dialog.getByRole("button", { name: "Preview changes" }).click();
  await expect(dialog.getByRole("region", { name: "Bulk tag preview" })).toContainText("Remove 2 tag assignments");
  await dialog.getByRole("button", { name: "Apply tag changes" }).click();
  await expect(dialog.getByRole("status")).toContainText("success");
});

test("browser Back restores the tagged query's cursor page and page size after toggling favorites", async ({ page }) => {
  const filters = encodeURIComponent(JSON.stringify([{ tagId: "critical", operator: "exists" }]));
  await page.goto(`/?mode=workflow-library&paging=history&tagFilters=${filters}`);
  await expect(page.getByRole("row", { name: "Open workflow definition Tagged workflow page 1", exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "Rows" }).selectOption("25");
  await expect(page.locator(".wf-pagination-summary")).toHaveText("Showing 25 of 75 matching workflows");
  await page.getByRole("button", { name: "Next page", exact: true }).click();
  await expect(page.getByRole("row", { name: "Open workflow definition Tagged workflow page 2", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Next page", exact: true }).click();
  await expect(page.getByRole("row", { name: "Open workflow definition Tagged workflow page 3", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Favorites", exact: true }).click();
  await expect(page.getByRole("row", { name: "Open workflow definition Favorite workflow page 1", exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "Rows" }).selectOption("50");
  await expect(page.locator(".wf-pagination-summary")).toHaveText("Showing 50 of 75 matching workflows");
  await page.goBack();
  await expect(page.getByRole("row", { name: "Open workflow definition Tagged workflow page 3", exact: true })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Rows" })).toHaveValue("25");
  await expect(page.locator(".wf-pagination-summary")).toHaveText("Showing 25 of 75 matching workflows");
  const query = await page.evaluate(() => {
    const requests = (window as unknown as { workflowLibraryRequests: { method: string; url: string }[] }).workflowLibraryRequests;
    const url = requests.filter(request => request.method === "GET" && request.url.startsWith("/fixture/library/pages")).at(-1)!.url;
    return Object.fromEntries(new URL(url, location.origin).searchParams);
  });
  expect(query).toMatchObject({ pageSize: "25", continuationToken: "tag-page-three", tagFilters: JSON.stringify([{ tagId: "critical", operator: "exists" }]) });
  expect(query.favoritesOnly).toBeUndefined();
});

for (const change of ["count-shrinks", "count-grows"] as const) {
  test(`cursor paging follows the returned token when the live result ${change}`, async ({ page }) => {
    await page.goto(`/?mode=workflow-library&paging=${change}`);
    await page.getByRole("combobox", { name: "Rows" }).selectOption("50");
    await expect(page.locator(".wf-pagination-summary")).toHaveText("Showing 50 of 150 matching workflows");
    await page.getByRole("button", { name: "Next page", exact: true }).click();
    await expect(page.getByRole("row", { name: "Open workflow definition Tagged workflow page 2", exact: true })).toBeVisible();
    await expect(page.locator(".wf-page-controls")).not.toContainText("Page 2 of");
    const next = page.getByRole("button", { name: "Next page", exact: true });
    if (change === "count-shrinks") {
      await expect(page.locator(".wf-pagination-summary")).toHaveText("Showing 50 of 100 matching workflows");
      await expect(next).toBeEnabled(); await next.click();
      await expect(page.getByRole("row", { name: "Open workflow definition Tagged workflow page 3", exact: true })).toBeVisible();
      await expect(next).toBeDisabled();
    } else {
      await expect(page.locator(".wf-pagination-summary")).toHaveText("Showing 50 of 200 matching workflows");
      await expect(next).toBeDisabled();
    }
  });
}

test("Enter in tag search searches without saving pending organization changes", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Organize Collect overdue payment", exact: true });
  await dialog.getByRole("checkbox", { name: "Critical", exact: true }).check();
  const search = dialog.getByRole("searchbox", { name: "Find workflow tags", exact: true });
  await search.fill("Owning team");
  await search.press("Enter");
  await expect(dialog).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as unknown as Window & {
    workflowLibraryRequests: { method: string; url: string }[];
  }).workflowLibraryRequests.some(request => request.method === "GET" && new URL(request.url, location.origin).searchParams.get("search") === "Owning team"))).toBe(true);
  const writes = await page.evaluate(() => (window as unknown as Window & {
    workflowLibraryRequests: { method: string; url: string }[];
  }).workflowLibraryRequests.filter(request => request.method === "PUT"));
  expect(writes).toEqual([]);
  await expect(dialog.getByRole("checkbox", { name: "Critical", exact: true })).toBeChecked();
  await expect(dialog.getByRole("button", { name: "Save tags", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(dialog).toBeHidden();
});
