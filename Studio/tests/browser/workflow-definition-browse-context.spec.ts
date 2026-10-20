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
    await openSecondPage(page, browseUrl.replace("/?", `/?theme=${variant.theme}&`));
    await expect(page.locator(".wf-pagination")).toBeVisible();
    const clockStart = new Date("2026-10-20T12:00:00Z");
    await page.clock.install({ time: clockStart });
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

    await page.getByRole("button", { name: "Refresh", exact: true }).focus();
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
