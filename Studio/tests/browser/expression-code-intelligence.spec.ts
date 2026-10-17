import { expect, test } from "@playwright/test";

test("the activity inspector preserves a JavaScript session across compact, expanded, and Liquid surfaces", async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 620 });
  await page.goto("/?mode=expression-code-intelligence");

  const syntax = page.getByRole("button", { name: "Path expression syntax" });
  const compactPreview = page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ });
  await expect(compactPreview).toContainText("format");
  await compactPreview.click();

  const compactEditor = page.locator(".studio-code-editor-rich-compact .cm-content");
  await expect(compactEditor).toBeVisible();
  await expect(page.locator(".studio-code-editor-rich-compact .cm-gutters")).toHaveCount(0);

  await page.waitForFunction(() => {
    const readiness = (window as Window & {
      expressionToolingReadiness?: { authoringContextRequests: number; validationRequests: number };
    }).expressionToolingReadiness;
    return (readiness?.authoringContextRequests ?? 0) > 0 &&
      (readiness?.validationRequests ?? 0) > 0;
  });
  await expect(page.locator(".studio-code-editor-diagnostics")).toContainText("BROWSER001");

  await compactEditor.press("Home");
  for (let index = 0; index < 12; index++) await compactEditor.press("ArrowRight");
  await expect(page.locator(".studio-code-editor-signature")).toContainText("formatTotal(value)");

  await compactEditor.press("Control+Space");
  await expect(page.locator(".cm-tooltip-autocomplete")).toContainText("formatTotal");
  await page.keyboard.press("Escape");

  await compactEditor.press("Alt+i");
  await expect(page.getByRole("status", { name: "Hover information" })).toContainText("Formats");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("status", { name: "Hover information" })).toHaveCount(0);

  // With help dismissed, Escape now arms the documented Escape-then-Tab exit.
  await page.keyboard.press("Escape");
  await page.keyboard.press("Tab");
  await expect(syntax).toBeFocused();

  await page.getByRole("button", { name: "Open expanded Path editor" }).click();
  const dialog = page.getByRole("dialog", { name: "Path" });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator(".studio-code-editor-rich-expanded .cm-gutters")).toBeVisible();
  await expect(dialog.locator(".cm-content")).toContainText("format");
  await dialog.getByRole("button", { name: "Close", exact: true }).click();

  await syntax.click();
  await page.getByRole("option", { name: "Liquid", exact: true }).click();
  await expect(page.getByRole("button", { name: /Liquid expression\. Activate to edit\./ })).toContainText("format");
});

test("fifty expression fields remain lightweight until the selected compact editor activates", async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 900 });
  await page.goto("/?mode=expression-code-intelligence&fields=50");

  const previews = page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ });
  await expect(previews).toHaveCount(10);
  await expect(page.getByRole("textbox")).toHaveCount(40);
  await expect(page.locator(".studio-code-editor-rich-compact")).toHaveCount(0);

  const startedAt = Date.now();
  await previews.first().click();
  await expect(page.locator(".studio-code-editor-rich-compact .cm-content")).toBeVisible();
  const activationMilliseconds = Date.now() - startedAt;

  test.info().annotations.push({
    type: "performance",
    description: `50-field compact activation: ${activationMilliseconds} ms`
  });
  expect(activationMilliseconds).toBeLessThan(1_500);
  await expect(page.locator(".studio-code-editor-rich-compact")).toHaveCount(1);
});

test("the compact editor remains operable at a narrow touch viewport", async ({ browser, baseURL }) => {
  const context = await browser.newContext({ hasTouch: true, viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  await page.goto(`${baseURL}/?mode=expression-code-intelligence`);

  const preview = page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ });
  await expect(preview).toBeVisible();
  await preview.tap();
  await expect(page.locator(".studio-code-editor-rich-compact .cm-content")).toBeVisible();
  await expect(page.getByRole("button", { name: "Open expanded Path editor" })).toBeVisible();
  await context.close();
});

test("unavailable IntelliSense keeps syntax-aware editing available", async ({ page }) => {
  await page.goto("/?mode=expression-code-intelligence&tooling=unavailable");

  await page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ }).click();

  const editor = page.locator(".studio-code-editor-rich-compact .cm-content");
  await expect(editor).toBeVisible();
  await expect(editor).toHaveAttribute("contenteditable", "true");
  await expect(page.getByText(
    "JavaScript IntelliSense is unavailable. Syntax highlighting remains active."
  )).toBeVisible();

  const initialSource = await editor.textContent();
  expect(initialSource).toContain("formatTotal(total)");
  await editor.press("End");
  await page.keyboard.insertText(" updated");
  await expect.poll(() => editor.textContent()).toBe(`${initialSource} updated`);
});

for (const width of [390, 900]) {
  test(`keeps syntax controls on the input line when compact feedback grows (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.goto("/?mode=expression-code-intelligence&tooling=unavailable&theme=dark");

    const syntax = page.getByRole("button", { name: "Path expression syntax" });
    const expand = page.getByRole("button", { name: "Open expanded Path editor" });
    for (const language of ["JavaScript", "Liquid"]) {
      if (language === "Liquid") {
        await syntax.click();
        await page.getByRole("option", { name: language, exact: true }).click();
      }
      const preview = page.getByRole("button", { name: `${language} expression. Activate to edit.`, exact: true });
      if (await preview.isVisible()) await preview.click();
      const editor = page.locator(".studio-code-editor-rich-compact .cm-editor");
      await expect(editor).toBeVisible();
      const feedback = page.locator(".studio-code-editor-status");
      await expect(feedback).toContainText(`${language} IntelliSense is unavailable`);
      const editorBox = (await editor.boundingBox())!;
      const feedbackBox = (await feedback.boundingBox())!;
      for (const control of [syntax, expand]) {
        const box = (await control.boundingBox())!;
        expect(box.y).toBeGreaterThanOrEqual(editorBox.y);
        expect(box.y + box.height).toBeLessThanOrEqual(editorBox.y + editorBox.height + 1);
        expect(box.y + box.height).toBeLessThanOrEqual(feedbackBox.y);
      }
      const scrollerBox = (await page.locator(".studio-code-editor-rich-compact .cm-scroller").boundingBox())!;
      expect(scrollerBox.x + scrollerBox.width).toBeLessThanOrEqual((await syntax.boundingBox())!.x);
    }
  });
}
