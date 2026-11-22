import { expect, test, type Locator } from "@playwright/test";

for (const palette of ["atelier", "classic"]) for (const theme of ["light", "dark"]) for (const width of [320, 1440]) {
  test(`unavailable organization has explicit keyboard recovery (${palette}, ${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=workflow-library&organization=off&palette=${palette}&theme=${theme}&folderId=finance&view=mine&search=payment&state=all&sort=modified&publicationState=published&columns=tags,location&backend=authoring#results`);
    const recovery = page.getByRole("button", { name: "Clear unavailable organization filters", exact: true });
    await expect(recovery).toBeVisible();
    await expect(page.getByRole("table", { name: "Workflow definitions", exact: true })).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { workflowLibraryRequests: { url: string }[] }).workflowLibraryRequests.filter(request => request.url.includes("/pages")))).toEqual([]);
    await recovery.focus();
    await expect(recovery).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("link", { name: "Collect overdue payment", exact: true })).toBeVisible();
    const url = new URL(page.url());
    expect(url.searchParams.get("search")).toBe("payment");
    expect(url.searchParams.get("state")).toBe("all");
    expect(url.searchParams.get("sort")).toBe("modified");
    expect(url.searchParams.get("publicationState")).toBe("published");
    expect(url.searchParams.get("columns")).toBe("tags,location");
    expect(url.searchParams.get("backend")).toBe("authoring");
    expect(url.hash).toBe("#results");
    await expect(page.getByRole("columnheader", { name: "Tags", exact: true })).toHaveCount(0);
    await expect(page.getByRole("columnheader", { name: "Location", exact: true })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Favorites", exact: true })).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.reload();
    await expect(page.getByRole("link", { name: "Collect overdue payment", exact: true })).toBeVisible();
    await page.goBack();
    await expect(recovery).toBeVisible();
    await expect(page.getByRole("table", { name: "Workflow definitions", exact: true })).toHaveCount(0);
  });
}

for (const theme of ["light", "dark"] as const) for (const width of [390, 1440]) {
  test(`workflow modified timestamps are easy to scan (${theme}, ${width}px)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=workflow-library&theme=${theme}`);
    await expect(page.getByRole("button", { name: width === 390 ? "Folders" : "Manage folders", exact: true })).toBeVisible();
    await expect(page.getByText("Loading library organization…", { exact: true })).toHaveCount(0);
    const row = page.getByRole("row", { name: "Open workflow definition Collect overdue payment", exact: true });
    const modified = row.locator(".wf-definition-modified");
    const timestamp = modified.locator("time");
    await expect(timestamp).toHaveAttribute("datetime", "2026-10-31T12:00:00Z");

    const expected = await page.evaluate(() => {
      const date = new Date("2026-10-31T12:00:00Z");
      return [
        new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(date),
        new Intl.DateTimeFormat(undefined, { timeStyle: "short" }).format(date),
        new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeStyle: "long" }).format(date)
      ];
    });
    await expect(timestamp.locator(".wf-definition-modified-date")).toHaveText(expected[0]);
    await expect(timestamp.locator(".wf-definition-modified-time")).toHaveText(expected[1]);
    await expect(timestamp).toHaveAttribute("title", expected[2]);
    const geometry = await timestamp.evaluate(element => {
      const date = element.querySelector<HTMLElement>(".wf-definition-modified-date")!;
      const time = element.querySelector<HTMLElement>(".wf-definition-modified-time")!;
      const dateBounds = date.getBoundingClientRect();
      const timeBounds = time.getBoundingClientRect();
      return {
        dateBottom: dateBounds.bottom,
        dateWidth: dateBounds.width,
        timeTop: timeBounds.top,
        timeWidth: timeBounds.width,
        cellWidth: element.parentElement!.getBoundingClientRect().width,
        whiteSpace: getComputedStyle(date).whiteSpace
      };
    });
    expect(geometry.dateBottom).toBeLessThanOrEqual(geometry.timeTop + 1);
    expect(geometry.dateWidth).toBeLessThanOrEqual(geometry.cellWidth + 1);
    expect(geometry.timeWidth).toBeLessThanOrEqual(geometry.cellWidth + 1);
    expect(geometry.whiteSpace).toBe("nowrap");

    if (width === 390) {
      const table = page.getByRole("region", { name: "Workflow definitions table" });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect(await table.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
      await table.evaluate(element => { element.scrollLeft = element.scrollWidth; });
      await expect.poll(() => table.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
    }

    await page.screenshot({ path: testInfo.outputPath(`workflow-modified-${theme}-${width}.png`), fullPage: true });
  });
}

test("a workflow keeps its identity while tags, saved views and favorites organize it", async ({ page }) => {
  await page.goto("/?mode=workflow-library&theme=light");
  const payment = page.getByRole("row", { name: "Open workflow definition Collect overdue payment" });
  await expect(payment).toBeVisible();
  await payment.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Collect overdue payment" });
  await chooseTag(dialog, "Critical");
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

  await page.getByRole("link", { name: "Manage views", exact: true }).click();
  await page.getByRole("row").filter({ hasText: "Critical workflows" }).getByRole("button", { name: "Edit" }).click();
  const edit = page.getByRole("dialog", { name: "Edit view" });
  const name = edit.getByRole("textbox", { name: "View name" });
  await expect(name).toBeFocused();
  await name.fill("Priority workflows");
  await edit.getByRole("button", { name: "Save", exact: true }).click();
  await expect(edit).toHaveCount(0);
  await expect(page.getByRole("cell", { name: "Priority workflows", exact: true })).toBeVisible();
});

test("Saved views identify Definitions as their parent", async ({ page }) => {
  await page.goto("/?mode=workflow-library&theme=light");
  await page.getByRole("link", { name: "Manage views", exact: true }).click();

  const breadcrumbs = page.getByRole("navigation", { name: "Breadcrumb", exact: true });
  const definitions = breadcrumbs.getByRole("link", { name: "Definitions", exact: true });
  await expect(definitions).toHaveAttribute("href", /workflows\/definitions/);
  await expect(breadcrumbs.locator('[aria-current="page"]')).toHaveText("Saved views");
  await expect(page.getByRole("button", { name: "Refresh", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Back to workflows", exact: true })).toHaveCount(0);

  await definitions.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible();
});

for (const theme of ["light", "dark"] as const) for (const width of [320, 390, 1440]) {
  test(`fits workflow views and management controls (${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=workflow-library&theme=${theme}`);
    const organization = page.getByRole("region", { name: "Workflow library organization", exact: true });
    const controls = organization.getByRole("group", { name: "Workflow views, folders, and tags", exact: true });
    await expect(controls).toBeVisible();
    const view = controls.getByLabel("Saved workflow view", { exact: true });
    const manageViews = controls.getByRole("link", { name: "Manage views", exact: true });
    const manageFolders = controls.getByRole("button", { name: "Manage folders", exact: true });
    const manageTags = controls.getByRole("link", { name: "Manage tags", exact: true });
    const favorites = organization.getByRole("button", { name: "Favorites", exact: true });
    const filters = organization.getByRole("button", { name: "Filters", exact: true });
    const saveView = controls.getByRole("button", { name: "Save view", exact: true });
    await expect(view).toBeVisible();
    await expect(manageViews).toBeVisible();
    await expect(manageFolders).toBeVisible();
    await expect(manageTags).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

    const compactControls = [favorites, filters, saveView, manageViews, manageFolders, manageTags];
    for (const control of compactControls) {
      await expect(control).toHaveAttribute("data-variant", "ghost");
      await expect(control).toHaveAttribute("data-size", "sm");
      const box = (await control.boundingBox())!;
      expect(box.height).toBeGreaterThanOrEqual(24);
      expect(box.height).toBeLessThanOrEqual(32);
      expect(box.width).toBeGreaterThanOrEqual(24);
    }
    const favoriteBox = (await favorites.boundingBox())!;
    expect(Math.abs(favoriteBox.width - favoriteBox.height)).toBeLessThan(1);

    const centerY = async (control: Locator) => {
      const box = (await control.boundingBox())!;
      return box.y + box.height / 2;
    };
    if (width === 1440) {
      const search = page.getByRole("searchbox", { name: "Search all workflows", exact: true });
      const sort = organization.getByLabel("Sort workflows", { exact: true });
      const searchCenter = await centerY(search);
      for (const control of [sort, view, manageViews, manageFolders, manageTags]) {
        expect(Math.abs((await centerY(control)) - searchCenter)).toBeLessThan(2);
      }
    }

    if (width === 390) {
      const foldersCenter = await centerY(manageFolders);
      for (const control of [manageViews, manageTags]) {
        expect(Math.abs((await centerY(control)) - foldersCenter)).toBeLessThan(2);
      }
    }

    await page.evaluate(() => { document.documentElement.dataset.workspaceStyle = "minimal"; });
    await manageTags.hover();
    expect(await manageTags.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe("rgba(0, 0, 0, 0)");
    await favorites.click();
    await expect(favorites).toHaveAttribute("aria-pressed", "true");
    expect(await favorites.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe("rgba(0, 0, 0, 0)");
    await filters.click();
    await expect(filters).toHaveAttribute("aria-expanded", "true");
    expect(await filters.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe("rgba(0, 0, 0, 0)");

    if (width === 390) {
      await manageFolders.focus();
      await page.keyboard.press("Enter");
      const drawer = page.getByRole("dialog", { name: "Manage workflow folders", exact: true });
      await expect(drawer).toBeVisible();
      await drawer.locator('[data-folder-id="finance"]').click();
      await expect(drawer.getByRole("button", { name: "Rename", exact: true })).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(drawer).toBeHidden();
      await expect(manageFolders).toBeFocused();
      const searchAll = page.getByRole("button", { name: "Search all workflows", exact: true });
      await expect(searchAll).toHaveAttribute("data-variant", "ghost");
      await expect(searchAll).toHaveAttribute("data-size", "sm");
      const searchBox = (await searchAll.boundingBox())!;
      expect(searchBox.height).toBeGreaterThanOrEqual(24);
      expect(searchBox.height).toBeLessThanOrEqual(32);
      await searchAll.hover();
      expect(await searchAll.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe("rgba(0, 0, 0, 0)");
    }
  });
}

test("bulk edits preview explicit identities and keep mixed outcomes visible", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 780 });
  await page.goto("/?mode=workflow-library&bulk=partial&theme=dark");
  await page.getByRole("checkbox", { name: "Select visible workflow definitions" }).check();
  await page.getByRole("button", { name: "Edit tags", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Edit tags on 2 workflows" });
  await chooseTag(dialog, "Critical");
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
  await expect(page.getByRole("button", { name: "Refresh workflow definitions", exact: true })).toBeFocused();
});

test("a stale organization write retains the intended change until an explicit reload", async ({ page }) => {
  await page.goto("/?mode=workflow-library&conflict=tags");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Collect overdue payment" });
  await chooseTag(dialog, "Critical");
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("409");
  await expect(dialog.getByRole("button", { name: "Remove Critical", exact: true })).toBeVisible();
  await dialog.getByRole("button", { name: "Reload tags" }).click();
  await expect(dialog.getByRole("button", { name: "Remove Critical", exact: true })).toHaveCount(0);
});

test("the independent move dialog contains Tab and Shift+Tab", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Move to folder…", exact: true }).click();
  const move = page.getByRole("dialog", { name: "Move workflow definitions", exact: true });
  await expect(move.getByRole("radio", { name: "Unfiled", exact: true })).toBeFocused();
  const first = move.getByRole("button", { name: "Close dialog", exact: true });
  const last = move.getByRole("button", { name: "Move", exact: true });
  await last.focus();
  await page.keyboard.press("Tab");
  await expect(first).toBeFocused();
  await page.keyboard.press("Shift+Tab");
  await expect(last).toBeFocused();
});

for (const theme of ["light", "dark"] as const) {
  for (const width of [390, 1280]) {
    test(`row actions stay available while browsing overflow columns (${theme}, ${width}px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 780 });
      await page.goto(`/?mode=workflow-library&theme=${theme}`);
      await expect(page.getByRole("link", { name: "Manage tags", exact: true })).toBeVisible();
      const table = page.getByRole("region", { name: "Workflow definitions table" });
      await table.evaluate(element => { element.scrollLeft = element.scrollWidth; });
      await expect.poll(() => table.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
      await page.getByRole("button", { name: "Actions for Collect overdue payment", exact: true }).click();
      const organization = page.getByRole("menuitem", { name: "Edit tags…", exact: true });
      await expect(organization).toBeVisible();

      await table.hover();
      await page.mouse.wheel(-2000, 0);
      await expect.poll(() => table.evaluate(element => element.scrollLeft)).toBe(0);
      await expect(organization).toBeVisible();
      await organization.click();
      await expect(page.getByRole("dialog", { name: "Tags for Collect overdue payment", exact: true })).toBeVisible();
    });
  }
}

test("moving from a row closes completely and writes only that workflow", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("checkbox", { name: "Select visible workflow definitions" }).check();
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Move to folder…", exact: true }).click();
  const move = page.getByRole("dialog", { name: "Move workflow definitions", exact: true });
  await expect(page.getByRole("dialog")).toHaveCount(1);
  await expect(move.getByRole("combobox", { name: "Tags", exact: true })).toHaveCount(0);
  await move.getByRole("radio", { name: "Unfiled", exact: true }).check();
  await move.getByRole("button", { name: "Move", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Refresh workflow definitions", exact: true })).toBeFocused();
  const placement = await page.evaluate(() => (window as unknown as { workflowLibraryRequests: { method: string; url: string; body?: unknown }[] }).workflowLibraryRequests.filter(request => request.method === "POST" && request.url.endsWith("/placement")));
  expect(placement).toHaveLength(1);
  expect(placement[0].body).toEqual({ definitionIds: ["collect-payment"], folderId: null });
});

test("Escape cancels a row move without opening a second dialog", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Move to folder…", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Move workflow definitions", exact: true })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("catalog management creates controlled values and retires their stable tag identity", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("link", { name: "Manage tags", exact: true }).click();
  await page.getByRole("button", { name: "Create tag", exact: true }).click();
  const dialog = page.locator(".wf-tag-page");
  await dialog.getByRole("textbox", { name: "Tag name", exact: true }).fill("Region");
  await dialog.getByRole("combobox", { name: "Tag kind" }).selectOption("controlled");
  await dialog.getByRole("combobox", { name: "Tag cardinality" }).selectOption("multiple");
  await dialog.getByRole("button", { name: "Add controlled value" }).click();
  await dialog.getByRole("textbox", { name: "Value name 1" }).fill("Europe");
  await dialog.getByRole("button", { name: "Create tag", exact: true }).click();
  await expect(dialog.getByRole("heading", { name: "Edit workflow tag", exact: true })).toBeVisible();
  await expect(dialog.getByRole("combobox", { name: "Tag kind" })).toBeDisabled();
  const retirement = dialog.getByRole("checkbox", { name: "Retire this tag" });
  await retirement.check();
  const focusedBounds = await retirement.boundingBox();
  await dialog.getByRole("button", { name: "Save tag", exact: true }).click();
  expect(await retirement.boundingBox()).toEqual(focusedBounds);
  await expect(dialog.getByRole("button", { name: "Save tag", exact: true })).toBeEnabled();
  await expect.poll(() => page.evaluate(() => (window as unknown as { workflowLibraryRequests: { method: string }[] }).workflowLibraryRequests.filter(request => request.method === "PUT").length)).toBe(1);
  const writes = await page.evaluate(() => (window as unknown as Window & { workflowLibraryRequests: { method: string; url: string; body?: unknown }[] }).workflowLibraryRequests.filter(request => request.method === "POST" || request.method === "PUT"));
  expect(writes[0].body).toMatchObject({ values: [{ id: "", name: "Europe" }] });
  expect(writes[1].body).toMatchObject({ deprecated: true, revision: "1", values: [{ id: expect.stringMatching(/^value-/), name: "Europe" }] });
});

test("catalog management preserves existing server identities when adding controlled values", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("link", { name: "Manage tags", exact: true }).click();
  await page.getByRole("link", { name: "Owning team", exact: true }).click();
  const dialog = page.locator(".wf-tag-page");
  await dialog.getByRole("button", { name: "Add controlled value" }).click();
  await dialog.getByRole("textbox", { name: "Value name 3" }).fill("Operations");
  await dialog.getByRole("button", { name: "Save tag", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Save tag", exact: true })).toBeEnabled();
  const write = await page.evaluate(() => (window as unknown as { workflowLibraryRequests: { method: string; url: string; body?: unknown }[] }).workflowLibraryRequests.find(request => request.method === "PUT" && request.url.endsWith("/tags/team")));
  expect(write?.body).toMatchObject({ revision: "1", values: [{ id: "value-billing", name: "Billing" }, { id: "value-support", name: "Support" }, { id: "", name: "Operations" }] });
});

test("reader permissions preserve browsing and private views while withholding mutations", async ({ page }) => {
  await page.goto("/?mode=workflow-library&permissions=read");
  await expect(page.getByRole("button", { name: "Save view", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Manage tags", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create folder", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Manage folders", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Collect overdue payment" });
  await expect(dialog.getByRole("combobox", { name: "Tags", exact: true })).toBeDisabled();
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
  await chooseTag(dialog, "Owning team: Billing");
  await chooseTag(dialog, "Owning team: Support");
  await expect(dialog.getByRole("button", { name: "Remove Owning team: Billing", exact: true })).toBeVisible();
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
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Collect overdue payment", exact: true });
  await chooseTag(dialog, "Critical");
  const search = dialog.getByRole("combobox", { name: "Tags", exact: true });
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
  await expect(dialog.getByRole("button", { name: "Remove Critical", exact: true })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Save tags", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(dialog).toBeHidden();
});

test("the tag filter uses the shared outlined field style", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  const tag = page.getByRole("textbox", { name: "Find tags for filters", exact: true });
  const publication = page.getByRole("combobox", { name: "Publication filter", exact: true });
  const classes = await Promise.all([tag, publication].map(field => field.evaluate(element => element.closest(".studio-field")?.className ?? "")));
  expect(classes[0]).not.toBe("");
  expect(classes[0]).toBe(classes[1]);
});

test("Enter submits the tag filter search", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  const field = page.getByRole("textbox", { name: "Find tags for filters", exact: true });
  await field.fill("Critical");
  await field.press("Enter");
  await expect.poll(() => page.evaluate(() => (window as unknown as Window & {
    workflowLibraryRequests: { method: string; url: string }[];
  }).workflowLibraryRequests.some(request => request.method === "GET" && new URL(request.url, location.origin).searchParams.get("search") === "Critical"))).toBe(true);
});

test("a tag search with no matches explains the result and recovers with a new search", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  const panel = page.locator(".wf-library-filter-panel");
  const field = panel.getByRole("textbox", { name: "Find tags for filters", exact: true });
  const find = panel.getByRole("button", { name: "Find tags", exact: true });
  const add = panel.getByRole("button", { name: "Add tag filter", exact: true });
  await field.fill("no-matching-tag-261120");
  await find.click();
  await expect(panel.getByRole("status")).toContainText("No workflow tags match");
  await expect(add).toBeDisabled();

  await field.fill("Critical");
  await find.click();
  await expect(panel.getByRole("status")).toHaveCount(0);
  await expect(add).toBeEnabled();
  await add.click();
  await expect(panel.getByRole("combobox", { name: "Tag filter 1", exact: true })).toHaveValue("critical");
});

async function chooseTag(dialog: Locator, label: string) {
  const input = dialog.getByRole("combobox", { name: "Tags", exact: true });
  await input.fill(label.split(": ")[0]);
  await dialog.getByRole("option", { name: label, exact: true }).click();
}
for (const theme of ["light", "dark"]) for (const width of [390, 1920]) {
  test(`workflow toolbar groups controls and gives versions room (${theme}, ${width}px)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=workflow-library&palette=classic&theme=${theme}`);
    const favorite = page.getByRole("button", { name: "Favorites", exact: true });
    await expect(favorite).toBeVisible();
    await expect(favorite.locator("svg")).toBeVisible();
    await expect(favorite).toHaveText("");
    await expect(favorite).toHaveAttribute("aria-pressed", "false");
    await favorite.click();
    await expect(favorite).toHaveAttribute("aria-pressed", "true");
    await favorite.click();
    await page.getByRole("combobox", { name: "Sort workflows" }).selectOption("name-desc");
    await expect(page).toHaveURL(/sort=name-desc/);
    await page.getByRole("button", { name: "Filters", exact: true }).click();
    await expect(page.getByRole("combobox", { name: "Publication filter" })).toBeVisible();
    await page.getByRole("combobox", { name: "Publication filter" }).selectOption("published");
    await expect(page.getByRole("button", { name: "Filters (1)", exact: true })).toHaveAttribute("aria-expanded", "true");
    await page.getByRole("button", { name: "Clear filters", exact: true }).click();
    await page.getByRole("button", { name: "Filters", exact: true }).click();
    await expect(page.getByRole("button", { name: "Manage folders", exact: true })).toBeVisible();
    if (width > 700) {
      const version = page.getByRole("row", { name: "Open workflow definition Collect overdue payment" }).locator(".wf-definition-version").first();
      expect((await version.boundingBox())!.width).toBeGreaterThanOrEqual(176);
      const number = await version.locator(".wf-definition-version-number").boundingBox();
      const draft = await version.getByText("Draft", { exact: true }).boundingBox();
      expect(Math.abs(number!.y - draft!.y)).toBeLessThan(2);
      expect(draft!.x - number!.x - number!.width).toBeGreaterThanOrEqual(9);
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`workflow-library-${theme}-${width}.png`), fullPage: true });
  });
}

test("bulk delete confirms recoverable deletion and keeps partial failures selected", async ({ page }) => {
  await page.goto("/?mode=workflow-library&palette=classic&theme=light&delete=partial");
  await page.getByRole("checkbox", { name: "Select visible workflow definitions" }).check();
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  const confirmation = page.getByRole("dialog", { name: "Delete selected workflows" });
  await expect(confirmation).toContainText("Delete 2 selected workflow definitions?");
  await expect(confirmation).toContainText("restore");
  await confirmation.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("button", { name: "Delete selected", exact: true })).toBeFocused();
  expect(await page.evaluate(() => (window as unknown as { workflowLibraryRequests: { method: string }[] }).workflowLibraryRequests.filter(request => request.method === "DELETE"))).toHaveLength(0);
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  await confirmation.getByRole("button", { name: "Delete selected", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("1 couldn't be deleted");
  await expect(page.getByRole("alert")).toContainText("welcome: Permission denied");
  await expect(page.getByRole("alert")).toBeFocused();
  await expect(page.getByRole("checkbox", { name: "Select workflow definition Welcome new customer" })).toBeChecked();
  await expect(page.getByRole("row", { name: "Open workflow definition Collect overdue payment" })).toHaveCount(0);
});


test("successful bulk deletion focuses its result when the selection bar disappears", async ({ page }) => {
  await page.goto("/?mode=workflow-library&palette=classic&theme=light");
  await page.getByRole("checkbox", { name: "Select visible workflow definitions" }).check();
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  await page.getByRole("dialog", { name: "Delete selected workflows" }).getByRole("button", { name: "Delete selected", exact: true }).click();
  const result = page.getByRole("status").filter({ hasText: "2 workflow definitions deleted." });
  await expect(result).toBeFocused();
  await expect(page.getByRole("button", { name: "Delete selected", exact: true })).toHaveCount(0);
});


test("bulk deletion from a later page focuses the result after returning to the previous page", async ({ page }) => {
  await page.goto("/?mode=workflow-library&palette=classic&theme=light&paging=delete");
  await page.getByRole("button", { name: "Next page", exact: true }).click();
  await expect(page.getByRole("row", { name: "Open workflow definition Welcome new customer" })).toBeVisible();
  await page.getByRole("checkbox", { name: "Select visible workflow definitions" }).check();
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  await page.getByRole("dialog", { name: "Delete selected workflows" }).getByRole("button", { name: "Delete selected", exact: true }).click();
  await expect(page.getByText("Page 1", { exact: true })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: "1 workflow definition deleted." })).toBeFocused();
});

for (const theme of ["light", "dark"]) for (const width of [320, 1440]) {
  test(`tag collection and edit pages support keyboard navigation (${theme}, ${width}px)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=workflow-library&palette=classic&theme=${theme}`);
    await page.getByRole("link", { name: "Manage tags", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Workflow tags", exact: true })).toBeVisible();
    await expect(page.getByRole("table", { name: "Workflow tags", exact: true })).toBeVisible();
    const listBreadcrumbs = page.getByRole("navigation", { name: "Breadcrumb" });
    await expect(listBreadcrumbs.getByRole("link", { name: "Definitions", exact: true })).toHaveAttribute("href", /workflows\/definitions/);
    await expect(listBreadcrumbs.locator('[aria-current="page"]')).toHaveText("Workflow tags");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`tags-list-${theme}-${width}.png`), fullPage: true });
    const tag = page.getByRole("link", { name: "Owning team", exact: true });
    await tag.focus();
    await page.keyboard.press("Enter");
    await expect(page).toHaveURL(/\/workflows\/tags\/team/);
    await expect(page.getByRole("textbox", { name: "Tag name", exact: true })).toHaveValue("Owning team");
    await expect(page.getByRole("combobox", { name: "Tag to manage", exact: true })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Save tag", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Cancel", exact: true })).toBeVisible();
    const editBreadcrumbs = page.getByRole("navigation", { name: "Breadcrumb" });
    await expect(editBreadcrumbs.getByRole("link", { name: "Definitions", exact: true })).toBeVisible();
    await expect(editBreadcrumbs.getByRole("link", { name: "Workflow tags", exact: true })).toBeVisible();
    await expect(editBreadcrumbs.locator('[aria-current="page"]')).toHaveText("Owning team");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`tags-edit-${theme}-${width}.png`), fullPage: true });
    const tagsLink = editBreadcrumbs.getByRole("link", { name: "Workflow tags", exact: true });
    await tagsLink.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("link", { name: "Owning team", exact: true })).toBeVisible();
    const definitionsLink = page.getByRole("navigation", { name: "Breadcrumb" }).getByRole("link", { name: "Definitions", exact: true });
    await definitionsLink.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible();
  });
}
