import { expect, test } from "@playwright/test";

const browseUrl = "/?mode=workflow-definition-browse&folderId=folder-operations&state=all&search=renewal";

async function recordedPageRequests(page: import("@playwright/test").Page) {
  return page.evaluate(() =>
    (window as Window & { workflowDefinitionPageRequests?: Record<string, string>[] })
      .workflowDefinitionPageRequests ?? []);
}

async function expectRestoredPageRequest(page: import("@playwright/test").Page, requestCountBeforeOpen: number) {
  await expect.poll(
    async () => (await recordedPageRequests(page)).slice(requestCountBeforeOpen),
    { message: "return should request the saved browse page with its observed API cursor" }
  ).toEqual(expect.arrayContaining([expect.objectContaining({
    pageSize: "25",
    continuationToken: "opaque-cursor/next+002",
    searchTerm: "renewal",
    folderId: "folder-operations",
    state: "all"
  })]));
}

async function openSecondPage(page: import("@playwright/test").Page, url = browseUrl) {
  await page.goto(url);
  await expect(page.getByRole("textbox", { name: "Search selected folder" })).toHaveValue("renewal");
  await expect(page.getByText("First page renewal", { exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Folder breadcrumb" }).getByRole("button", { name: "Operations" })).toBeVisible();
  await page.getByLabel("Rows").selectOption("25");
  await expect.poll(async () => (await recordedPageRequests(page)).some(request => request.pageSize === "25")).toBe(true);
  await page.getByRole("button", { name: "Next page" }).click();
  await expect(page.getByText("Page 2", { exact: false })).toBeVisible();
  await expect(page.getByText("Second page renewal", { exact: true })).toBeVisible();
}

async function expectPriorPageIsDisplayOnly(page: import("@playwright/test").Page) {
  await expect(page.getByText("Second page renewal", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Second page renewal" })).toHaveCount(0);
  await expect(page.getByLabel("Select workflow definition Second page renewal")).toHaveCount(0);
  await expect(page.locator(".wf-pagination")).toHaveCount(0);
}

async function recordedSearchRequests(page: import("@playwright/test").Page, searchTerm: string) {
  return (await recordedPageRequests(page)).filter(request => request.searchTerm === searchTerm);
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    window.addEventListener("popstate", () => {
      const parameters = new URLSearchParams(window.location.search);
      const historyEvents = (window as Window & { workflowDefinitionHistoryEvents?: unknown[] })
        .workflowDefinitionHistoryEvents ?? [];
      historyEvents.push({
        definitionId: parameters.get("definition"),
        search: parameters.get("search"),
        folderId: parameters.get("folderId"),
        state: parameters.get("state"),
        editorMounted: Boolean(document.querySelector("[data-studio-workflow-editor]"))
      });
      (window as Window & { workflowDefinitionHistoryEvents?: unknown[] }).workflowDefinitionHistoryEvents = historyEvents;
    });
  });
});

test("narrow keyboard users preserve workflow browse context through global search, history, and reload", async ({ page }) => {
  await page.setViewportSize({ width: 430, height: 760 });
  await page.goto("/?theme=light&mode=folder-restructure&folderId=folder-operations&state=all&search=workflow");

  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search selected folder" })).toHaveValue("workflow");
  await expect(page.getByRole("button", { name: "All states" })).toHaveAttribute("aria-pressed", "true");

  await page.getByRole("button", { name: "Search all workflows" }).click();
  await expect(page.getByText("All workflow", { exact: true })).toBeVisible();
  await expect(page).not.toHaveURL(/folderId=/);
  await expect(page).toHaveURL(/state=all/);
  await expect(page).toHaveURL(/search=workflow/);

  await page.getByRole("button", { name: "Identifiers for All workflow", exact: true }).click();
  const resultBreadcrumb = page.getByRole("navigation", { name: "Folder for All workflow" });
  const operations = resultBreadcrumb.getByRole("button", { name: "Operations" });
  await operations.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page).toHaveURL(/folderId=folder-operations/);
  await expect(page).not.toHaveURL(/definition=/);

  await page.goBack();
  await expect(page.getByText("All workflow", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search all workflows" })).toHaveValue("workflow");
  await page.goBack();
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search selected folder" })).toHaveValue("workflow");

  await page.goForward();
  await expect(page.getByText("All workflow", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search all workflows" })).toHaveValue("workflow");
  await page.goBack();
  await expect(page.getByText("Operations workflow")).toBeVisible();

  const requests = await page.evaluate(() =>
    (window as Window & { browseRequests?: string[] }).browseRequests ?? []);
  expect(requests.some(url => url.includes("folderId=folder-operations") && url.includes("state=all") && url.includes("searchTerm=workflow"))).toBe(true);
  expect(requests.some(url => !url.includes("folderId=") && url.includes("state=all") && url.includes("searchTerm=workflow"))).toBe(true);

  await page.reload();
  await expect(page.getByText("Operations workflow")).toBeVisible();
  await expect(page.getByRole("button", { name: "Folders" })).toBeVisible();
});

test("editor Back restores the later workflow page with its observed opaque cursor", async ({ page }) => {
  await openSecondPage(page);
  const requestCountBeforeOpen = (await recordedPageRequests(page)).length;
  await page.getByRole("link", { name: "Second page renewal" }).click();
  await expect(page.getByRole("button", { name: "Definitions", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Definitions", exact: true }).click();
  await expect(page.getByRole("button", { name: "Next page" })).toBeVisible();

  await expectRestoredPageRequest(page, requestCountBeforeOpen);
  await expect(page.getByText("Second page renewal", { exact: true })).toBeVisible();
  const historyEvents = await page.evaluate(() =>
    (window as Window & { workflowDefinitionHistoryEvents?: { definitionId: string | null; editorMounted: boolean }[] })
      .workflowDefinitionHistoryEvents ?? []);
  expect(historyEvents).toContainEqual({
    definitionId: "workflow-page-two",
    search: "renewal",
    folderId: "folder-operations",
    state: "all",
    editorMounted: false
  });
  expect(historyEvents).toContainEqual({
    definitionId: null,
    search: "renewal",
    folderId: "folder-operations",
    state: "all",
    editorMounted: true
  });
});

for (const width of [320, 390]) {
  test(`workflow definition identifiers and copy controls fit the Workbench viewport at ${width}px`, async ({ page }) => {
    await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.setViewportSize({ width, height: 844 });
    await page.goto(`${browseUrl}&long-identities=1&workbench-shell=1`);
    await page.getByRole("button", { name: "Next page" }).click();
    const row = page.getByRole("row", { name: /Second page renewal/ });
    await expect(row.locator(".wf-definition-name")).toHaveText("Second page renewal");
    await expect(page.getByRole("columnheader", { name: "Definition ID", exact: true })).toBeAttached();

    const tracks = await row.evaluate(element => {
      const table = element.closest<HTMLElement>(".wf-definition-grid")!;
      const box = (item: Element) => { const r = item.getBoundingClientRect(); return { left: r.left, width: r.width }; };
      return {
        header: [...table.querySelectorAll("[role=columnheader]")].map(box),
        cells: [...element.querySelectorAll("[role=cell]")].map(box)
      };
    });
    expect(tracks.header).toHaveLength(8);
    expect(tracks.cells).toHaveLength(8);
    for (let index = 0; index < tracks.header.length; index++) {
      expect(Math.abs(tracks.header[index].left - tracks.cells[index].left)).toBeLessThanOrEqual(1);
      expect(Math.abs(tracks.header[index].width - tracks.cells[index].width)).toBeLessThanOrEqual(1);
    }

    const primaryCopy = row.getByRole("button", { name: "Copy Definition ID" });
    await primaryCopy.focus();
    await page.keyboard.press("Enter");
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("workflow-page-two");
    expect(new URL(page.url()).searchParams.has("definition")).toBe(false);

    const trigger = row.getByRole("button", { name: "Identifiers for Second page renewal", exact: true });
    await trigger.focus();
    await page.keyboard.press("Enter");
    const details = page.getByRole("dialog", { name: "Workflow definition identifiers for Second page renewal", exact: true });
    await expect(details).toBeVisible();
    await expect(details.getByRole("button", { name: "Close identifiers", exact: true })).toBeFocused();
    const geometry = await details.evaluate(element => {
      const panel = element.closest(".wf-definition-identifiers-popover")!.getBoundingClientRect();
      return { left: panel.left, right: panel.right, fields: [...element.querySelectorAll(".studio-copyable-identifier")].map(field => {
        const label = field.querySelector(".studio-copyable-identifier__label")!.getBoundingClientRect();
        const value = field.querySelector(".studio-copyable-identifier__value")!.getBoundingClientRect();
        const copy = field.querySelector("button")!.getBoundingClientRect();
        return { left: label.left, right: copy.right, labelBottom: label.bottom, valueTop: value.top };
      }) };
    });
    expect(geometry.left).toBeGreaterThanOrEqual(7);
    expect(geometry.right).toBeLessThanOrEqual(width - 7);
    expect(geometry.fields).toHaveLength(4);
    for (const field of geometry.fields) {
      expect(field.left).toBeGreaterThanOrEqual(geometry.left);
      expect(field.right).toBeLessThanOrEqual(geometry.right);
      expect(field.labelBottom).toBeLessThan(field.valueTop);
    }
    await details.getByRole("button", { name: "Copy Draft ID", exact: true }).focus();
    await page.keyboard.press("Space");
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(`draft-${"immutable-identifier-".repeat(7)}`);
    expect(new URL(page.url()).searchParams.has("definition")).toBe(false);
    await page.keyboard.press("Escape");
    await expect(details).toHaveCount(0);
    await expect(trigger).toBeFocused();
    await trigger.click();
    await expect(details).toBeVisible();
    await page.getByRole("heading", { name: "Workflow definitions", exact: true }).click();
    await expect(details).toHaveCount(0);
  });
}

test("Workflow Definitions groups its create and refresh actions above the collection filters", async ({ page }) => {
  await page.goto(browseUrl);
  const heading = page.getByRole("heading", { name: "Workflow definitions", level: 1 });
  await expect(heading).toBeVisible();
  const header = page.locator("header").filter({ has: heading });
  const actions = header.getByRole("group", { name: "Workflow definition page actions" });
  await expect(header.getByText("Create and organize workflow definitions")).toBeVisible();
  await expect(actions.getByRole("button", { name: "Create workflow" })).toBeVisible();
  await expect(actions.getByRole("button", { name: "Refresh workflow definitions" })).toBeVisible();
  const search = page.getByRole("textbox", { name: "Search selected folder" });
  await expect(page.getByRole("group", { name: "Definition state" })).toBeVisible();
  const headingBounds = await heading.boundingBox();
  const searchBounds = await search.boundingBox();
  expect(headingBounds).not.toBeNull();
  expect(searchBounds).not.toBeNull();
  expect(headingBounds!.y).toBeLessThan(searchBounds!.y);

  await actions.getByRole("button", { name: "Create workflow" }).click();
  await expect(page.getByRole("dialog", { name: "Create workflow" })).toBeVisible();
});

test("Activity and Workflow definition pages use the same desktop and narrow content inset", async ({ page }) => {
  await page.route("**/capabilities", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(activityCapabilities())
  }));
  await page.route(/\/design\/activities\/definitions\?.*/, route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(emptyActivityCollection("inset-fixture"))
  }));

  for (const { width, expectedInset } of [{ width: 1280, expectedInset: 18 }, { width: 390, expectedInset: 12 }]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/?mode=activity-definitions&theme=light");
    const activityInset = await page.getByRole("heading", { name: "Activity Definitions" }).evaluate(heading => {
      const page = heading.closest("main")!;
      return heading.getBoundingClientRect().left - page.getBoundingClientRect().left;
    });
    await expect(page.getByRole("heading", { name: "No designed activities yet" })).toBeVisible();

    await page.goto(`${browseUrl}&theme=light`);
    const workflowInset = await page.getByRole("heading", { name: "Workflow definitions", level: 1 }).evaluate(heading => {
      const page = heading.closest(".wf-page")!;
      return heading.getBoundingClientRect().left - page.getBoundingClientRect().left;
    });

    expect(activityInset).toBe(expectedInset);
    expect(workflowInset).toBe(expectedInset);
    expect(workflowInset).toBe(activityInset);
  }
});

test("definition header actions use the shared button recipe in the custom material fixture and remain actionable", async ({ page }) => {
  let activityReads = 0;
  let holdActivityRefresh = false;
  let releaseActivityRefresh: (() => void) | undefined;
  await page.route("**/capabilities", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(activityCapabilities())
  }));
  await page.route(/\/design\/activities\/definitions\?.*/, async route => {
    activityReads += 1;
    if (holdActivityRefresh && activityReads > 1) {
      await new Promise<void>(resolve => { releaseActivityRefresh = resolve; });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(emptyActivityCollection("actions-fixture"))
    });
  });

  await page.goto("/?mode=activity-definitions&theme=material");
  await expect(page.getByRole("heading", { name: "No designed activities yet" })).toBeVisible();
  const activityHeader = page.locator("header").filter({ has: page.getByRole("heading", { name: "Activity Definitions" }) });
  const activityCreate = activityHeader.getByRole("button", { name: "Create activity" });
  await expectMaterialPrimaryRecipe(activityCreate);
  await activityCreate.click();
  await expect(page.getByRole("dialog", { name: "Create Activity Definition" })).toBeVisible();
  await page.getByRole("button", { name: "Close" }).click();
  const activityReadsBeforeRefresh = activityReads;
  holdActivityRefresh = true;
  const activityRefresh = activityHeader.getByRole("button", { name: "Refresh" });
  await activityRefresh.click();
  await expect(activityRefresh).toBeDisabled();
  const disabledStyle = await activityRefresh.evaluate(element => ({
    cursor: getComputedStyle(element).cursor
  }));
  expect(disabledStyle.cursor).toBe("not-allowed");
  await expect.poll(async () => Number(await activityRefresh.evaluate(element => getComputedStyle(element).opacity))).toBeLessThanOrEqual(0.55);
  releaseActivityRefresh?.();
  await expect(activityRefresh).toBeEnabled();
  await expect.poll(() => activityReads).toBeGreaterThan(activityReadsBeforeRefresh);

  await page.goto(`${browseUrl}&theme=material`);
  const workflowCreate = page.getByRole("button", { name: "Create workflow" });
  await expectMaterialPrimaryRecipe(workflowCreate);
  await workflowCreate.click();
  const workflowCreateDialog = page.getByRole("dialog", { name: "Create workflow" });
  await expect(workflowCreateDialog).toBeVisible();
  await workflowCreateDialog.getByRole("button", { name: "Cancel" }).click();
  const requestsBeforeRefresh = (await recordedPageRequests(page)).length;
  await page.getByRole("button", { name: "Refresh workflow definitions" }).click();
  await expect.poll(async () => (await recordedPageRequests(page)).length).toBeGreaterThan(requestsBeforeRefresh);
});

async function expectMaterialPrimaryRecipe(button: import("@playwright/test").Locator) {
  const colors = await button.evaluate(element => ({
    actual: getComputedStyle(element).backgroundColor,
    material: getComputedStyle(document.documentElement).getPropertyValue("--studio-material-send-bg").trim(),
    accent: getComputedStyle(document.documentElement).getPropertyValue("--studio-accent").trim()
  }));
  expect(colors.material).toBe("rgb(31, 87, 123)");
  expect(colors.actual).toBe(colors.material);
  expect(colors.actual).not.toBe(colors.accent);
}

function activityCapabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true }
  ] }] };
}

function emptyActivityCollection(snapshotId: string) {
  return { items: [], count: 0, totalCount: 0, hasMore: false, continuation: null, snapshot: { snapshotId, asOf: "2026-10-20T00:00:00Z" } };
}

test("browser Back restores the later workflow page with its observed opaque cursor", async ({ page }) => {
  await openSecondPage(page);
  const requestCountBeforeOpen = (await recordedPageRequests(page)).length;
  await page.getByRole("link", { name: "Second page renewal" }).click();
  await expect(page.getByRole("button", { name: "Definitions", exact: true })).toBeVisible();
  await page.goBack();
  await expect(page).not.toHaveURL(/definition=/);

  await expectRestoredPageRequest(page, requestCountBeforeOpen);
  await expect(page.getByText("Second page renewal", { exact: true })).toBeVisible();
});

test("a full reload keeps URL filters and starts with the default page and page size", async ({ page }) => {
  await openSecondPage(page);
  await page.reload();

  await expect(page.getByText("First page renewal", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Search selected folder" })).toHaveValue("renewal");
  await expect(page.getByRole("button", { name: "All states" })).toHaveAttribute("aria-pressed", "true");
  await expect(page).toHaveURL(/folderId=folder-operations/);
  await expect(page).toHaveURL(/state=all/);
  await expect(page).toHaveURL(/search=renewal/);
  await expect(page.getByLabel("Rows")).toHaveValue("10");

  const requests = await recordedPageRequests(page);
  expect(requests.some(request => request.pageSize === "10" && !request.continuationToken &&
    request.searchTerm === "renewal" && request.folderId === "folder-operations" && request.state === "all")).toBe(true);
});

const searchStatusVariants = [
  { name: "light laptop", width: 1280, height: 800, theme: "light" },
  { name: "dark narrow", width: 430, height: 760, theme: "dark" }
];

for (const variant of searchStatusVariants) {
  test(`search refresh keeps prior results truthful through failure, retry, and empty success (${variant.name})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: variant.width, height: variant.height });
    const clockStart = new Date("2026-10-20T12:00:00Z");
    // Install before application timers and pause at a future target, independent of protocol latency.
    await page.clock.install({ time: new Date(clockStart.getTime() - 60_000) });
    await openSecondPage(page, browseUrl.replace("/?", `/?theme=${variant.theme}&`));
    await expect(page.locator(".wf-pagination")).toBeVisible();
    await page.clock.pauseAt(clockStart);
    await page.evaluate(() => {
      (window as Window & { workflowBrowseTestControl?: { deferNext(searchTerm: string): void } })
        .workflowBrowseTestControl?.deferNext("latest");
    });

    const search = page.getByRole("textbox", { name: "Search selected folder" });
    await search.fill("latest");
    const staleStatus = page.getByRole("status").filter({ hasText: "Showing previous results" });
    await expect(staleStatus).toContainText("Showing previous results for “renewal” on page 2");
    await expect(staleStatus).toContainText("“latest” waits to refresh");
    await expectPriorPageIsDisplayOnly(page);
    await page.screenshot({ path: testInfo.outputPath(`${variant.name.replaceAll(" ", "-")}-deferred.png`), fullPage: true });

    await page.getByRole("button", { name: "Refresh workflow definitions" }).focus();
    await page.keyboard.press("Enter");
    await expect.poll(async () => (await recordedSearchRequests(page, "latest")).length)
      .toBe(1);
    const deferredRequest = (await recordedSearchRequests(page, "latest"))[0];
    expect(deferredRequest).toMatchObject({
      pageSize: "25",
      searchTerm: "latest",
      folderId: "folder-operations",
      state: "all"
    });
    expect(deferredRequest.continuationToken).toBeUndefined();
    await expect(staleStatus).toContainText("“latest” refreshes");
    await expectPriorPageIsDisplayOnly(page);

    await page.evaluate(() => {
      (window as Window & { workflowBrowseTestControl?: { rejectPending(searchTerm: string, message: string): void } })
        .workflowBrowseTestControl?.rejectPending("latest", "The latest search could not reach the workflow service.");
    });
    const failure = page.getByRole("alert");
    await expect(failure).toContainText("Couldn't load workflow definitions for “latest”");
    await expect(failure).toContainText("The latest search could not reach the workflow service.");
    await expect(staleStatus).toContainText("The latest search “latest” failed");
    await expect(page.getByText("No workflows match “latest”", { exact: true })).toHaveCount(0);
    await expectPriorPageIsDisplayOnly(page);
    await page.screenshot({ path: testInfo.outputPath(`${variant.name.replaceAll(" ", "-")}-failure.png`), fullPage: true });

    await page.getByRole("button", { name: "Retry current search" }).click();
    await expect.poll(async () => (await recordedSearchRequests(page, "latest")).length)
      .toBe(2);
    await expect(page.getByText("Latest workflow result", { exact: true })).toBeVisible();
    await expect(page.getByText("Second page renewal", { exact: true })).toHaveCount(0);
    const retryRequest = (await recordedSearchRequests(page, "latest"))[1];
    expect(retryRequest).toMatchObject({
      pageSize: "25",
      searchTerm: "latest",
      folderId: "folder-operations",
      state: "all"
    });
    expect(retryRequest.continuationToken).toBeUndefined();

    await search.fill("empty");
    await page.clock.runFor(250);
    await expect.poll(async () => (await recordedSearchRequests(page, "empty")).length).toBe(1);
    await expect(page.getByRole("heading", { name: "No workflows match “empty”" })).toBeVisible();
    await expect(page.getByText("Latest workflow result", { exact: true })).toHaveCount(0);
    await expect(page.getByRole("alert")).toHaveCount(0);
    await search.fill("");
    await expect(page).not.toHaveURL(/(?:[?&])search=/);
    await expect(page.getByRole("status").filter({ hasText: "The previous search “empty” on page 1 returned no workflows" })).toBeVisible();
    await page.clock.runFor(250);
    await expect(page.getByText("First page renewal", { exact: true })).toBeVisible();
    const clearedRequest = (await recordedPageRequests(page)).at(-1);
    expect(clearedRequest?.searchTerm).toBeUndefined();
    expect(clearedRequest?.continuationToken).toBeUndefined();
  });
}
