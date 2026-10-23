import { expect, test } from "@playwright/test";

type Profile = "expanded" | "compact";

async function openActivityEditor(page: import("@playwright/test").Page, width: number, profile: Profile = "expanded") {
  await page.setViewportSize({ width, height: 720 });
  await page.goto(`/?mode=studio-scroll-containment&profile=${profile}`);
  const editor = page.locator(`.studio-code-editor[data-profile='${profile}']`);
  await expect(editor).toBeVisible();
  if (profile === "compact") {
    await expect(editor.locator(".studio-code-editor-preview")).toBeVisible();
  } else {
    await expect(editor).toHaveAttribute("data-studio-code-editor-ready", "true");
  }
  await expect(page.locator(".studio-code-editor-escape")).toHaveAttribute("aria-live", "polite");
}

async function flushBrowserFrames(page: import("@playwright/test").Page) {
  await page.evaluate(() => new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()));
  }));
}

async function metrics(page: import("@playwright/test").Page) {
  return page.evaluate(() => {
    const content = document.querySelector<HTMLElement>(".content")!;
    const escape = document.querySelector<HTMLElement>(".studio-code-editor-escape")!;
    const topbar = document.querySelector<HTMLElement>(".topbar")!;
    const footer = document.querySelector<HTMLElement>(".sidebar-footer")!;
    return {
      viewportHeight: window.innerHeight,
      documentHeight: Math.max(document.documentElement.scrollHeight, document.body.scrollHeight),
      windowScrollY: window.scrollY,
      contentClientHeight: content.clientHeight,
      contentScrollHeight: content.scrollHeight,
      contentScrollTop: content.scrollTop,
      escapeTop: escape.getBoundingClientRect().top,
      escapeClip: getComputedStyle(escape).clip,
      topbarTop: topbar.getBoundingClientRect().top,
      footerTop: footer.getBoundingClientRect().top
    };
  });
}

async function exerciseShellScrolling(page: import("@playwright/test").Page, width: number, profile: Profile) {
  await openActivityEditor(page, width, profile);

  const initial = await metrics(page);
  expect(initial.escapeClip).not.toBe("auto");
  // This checks the hidden live region exists below the fold, where it can extend the document
  // when the editor root does not establish its own positioning context.
  expect(initial.escapeTop).toBeGreaterThan(initial.viewportHeight);
  expect(initial.contentScrollHeight).toBeGreaterThan(initial.contentClientHeight);

  const sidebar = page.locator(".sidebar");
  const sidebarBox = (await sidebar.boundingBox())!;
  await page.mouse.move(sidebarBox.x + sidebarBox.width / 2, sidebarBox.y + sidebarBox.height / 2);
  await page.mouse.wheel(0, 900);
  if (width > 640) await expect.poll(() => sidebar.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  else await flushBrowserFrames(page);
  const afterNavigationWheel = await metrics(page);

  if (width > 640) {
    await sidebar.evaluate(element => {
      element.scrollTop = element.scrollHeight;
    });
    await page.mouse.wheel(0, 600);
    await flushBrowserFrames(page);
  }
  const afterNavigationEdge = await metrics(page);

  await page.evaluate(() => {
    document.documentElement.style.scrollBehavior = "auto";
    window.scrollTo(0, 0);
    document.querySelector<HTMLElement>(".content")!.scrollTop = 0;
  });
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0);
  const beforeContentWheel = await metrics(page);
  const headingBox = (await page.getByRole("heading", { name: "Long Activity Definition editor" }).boundingBox())!;
  await page.mouse.move(headingBox.x + headingBox.width / 2, headingBox.y + headingBox.height / 2);
  await page.mouse.wheel(0, 650);
  await expect.poll(() => page.locator(".content").evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  const afterContentWheel = await metrics(page);

  expect(afterNavigationWheel.windowScrollY).toBe(0);
  expect(afterNavigationEdge.windowScrollY).toBe(0);
  expect(afterNavigationEdge.documentHeight).toBeLessThanOrEqual(afterNavigationEdge.viewportHeight);
  expect(afterContentWheel.windowScrollY).toBe(0);
  expect(afterContentWheel.documentHeight).toBeLessThanOrEqual(afterContentWheel.viewportHeight);
  expect(afterContentWheel.contentScrollTop).toBeGreaterThan(0);
  expect(afterContentWheel.topbarTop).toBe(beforeContentWheel.topbarTop);
  if (width > 640) expect(afterContentWheel.footerTop).toBe(beforeContentWheel.footerTop);
}

for (const profile of ["expanded", "compact"] as const) {
  test(`keeps a long ${profile} Activity Definition editor inside the desktop Studio shell`, async ({ page }) => {
    await exerciseShellScrolling(page, 1280, profile);
  });
}

for (const profile of ["expanded", "compact"] as const) {
  for (const width of [390, 320]) {
    test(`keeps long ${profile} Activity Definition scrolling inside the ${width}px Studio shell`, async ({ page }) => {
      await exerciseShellScrolling(page, width, profile);
    });
  }
}

test("does not create document scrolling for a short Activity Definition editor", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto("/?mode=studio-scroll-containment&length=short");
  await expect(page.locator(".studio-code-editor[data-studio-code-editor-ready='true']")).toBeVisible();
  await expect(page.locator(".studio-code-editor-escape")).toHaveAttribute("aria-live", "polite");

  const result = await metrics(page);
  expect(result.documentHeight).toBeLessThanOrEqual(result.viewportHeight);
  expect(result.contentScrollHeight).toBeLessThanOrEqual(result.contentClientHeight);
});
