import { expect, test } from "@playwright/test";

for (const appearance of ["light", "dark"] as const) {
  for (const viewport of [{ name: "desktop", width: 1440, height: 1000 }, { name: "narrow", width: 390, height: 844 }]) {
    test(`field presentations persist and render consistently in ${appearance} ${viewport.name} mode`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), appearance);
      await page.goto("/form-designer.html");
      await page.getByRole("button", { name: "Create form", exact: true }).click();
      if (viewport.name === "narrow") await page.getByRole("button", { name: "Expand properties panel", exact: true }).click();

      const palette = page.getByRole("complementary", { name: "Field palette", exact: true });
      const properties = page.getByRole("complementary", { name: "Field properties", exact: true });
      await page.getByRole("textbox", { name: "Form ID", exact: true }).fill("presentation-form");
      await page.getByRole("textbox", { name: "Name", exact: true }).fill("Presentation form");
      if (viewport.name === "narrow") await page.getByRole("button", { name: "Expand controls panel", exact: true }).click();
      await palette.getByRole("button", { name: "Yes / no", exact: true }).click();
      if (viewport.name === "narrow") await page.getByRole("button", { name: "Expand properties panel", exact: true }).click();
      await properties.getByRole("textbox", { name: "Label", exact: true }).fill("Accept terms");
      await properties.getByLabel("Display", { exact: true }).selectOption("radio");
      await properties.getByText("Answer labels", { exact: true }).click();
      await properties.getByRole("textbox", { name: "Yes label", exact: true }).fill("I agree");
      await properties.getByRole("textbox", { name: "No label", exact: true }).fill("I decline");
      await properties.getByRole("textbox", { name: "Unanswered label", exact: true }).fill("No response");
      await properties.getByText("Validation", { exact: true }).click();
      await properties.getByRole("checkbox", { name: "Must be checked", exact: true }).check();

      if (viewport.name === "narrow") await page.getByRole("button", { name: "Expand controls panel", exact: true }).click();
      await palette.getByRole("button", { name: "Choice", exact: true }).click();
      if (viewport.name === "narrow") await page.getByRole("button", { name: "Expand properties panel", exact: true }).click();
      await properties.getByRole("textbox", { name: "Label", exact: true }).fill("Channel");
      await properties.getByLabel("Display", { exact: true }).selectOption("radio");
      await properties.getByRole("textbox", { name: "Choices, one per line", exact: true }).fill("Email\nPhone");

      const sourceTab = page.getByRole("tab", { name: "Source", exact: true });
      const previewTab = page.getByRole("tab", { name: "Preview", exact: true });
      await sourceTab.click();
      const source = page.locator("[data-studio-code-editor] .cm-content");
      await expect(source).toContainText('"display": "radio"');
      await expect(source).toContainText('"mustBeTrue": true');
      await expect(source).toContainText('"trueLabel": "I agree"');

      await previewTab.click();
      const preview = page.getByRole("region", { name: "Recipient preview", exact: true });
      const booleanGroup = preview.getByRole("group", { name: "Accept terms" });
      const yes = booleanGroup.getByRole("radio", { name: "I agree" });
      const no = booleanGroup.getByRole("radio", { name: "I decline" });
      await expect(yes).not.toBeChecked();
      await expect(no).not.toBeChecked();
      await no.check();
      await expect(no).toBeChecked();
      await booleanGroup.getByRole("button", { name: "Clear answer", exact: true }).click();
      await expect(no).not.toBeChecked();
      const choiceGroup = preview.getByRole("group", { name: "Channel" });
      await choiceGroup.getByRole("radio", { name: "Phone" }).check();
      await expect(choiceGroup.getByRole("radio", { name: "Phone" })).toBeChecked();
      await page.screenshot({ path: testInfo.outputPath(`field-presentation-${appearance}-${viewport.name}.png`), animations: "disabled" });
      if (viewport.name === "narrow") expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

      await page.getByRole("button", { name: "Save draft", exact: true }).click();
      await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
      const requests = () => page.evaluate(() => (window as unknown as { formRequests: { path: string; body: { form?: { fields?: unknown[] } } }[] }).formRequests);
      await expect.poll(async () => (await requests()).some(request => request.path.endsWith("/draft"))).toBe(true);
      const draft = (await requests()).find(request => request.path.endsWith("/draft"))!;
      expect(draft.body.form?.fields).toEqual(expect.arrayContaining([
        expect.objectContaining({ kind: "boolean", mustBeTrue: true, presentation: { display: "radio", trueLabel: "I agree", falseLabel: "I decline", unansweredLabel: "No response" } }),
        expect.objectContaining({ kind: "choice", choices: ["Email", "Phone"], presentation: { display: "radio" } })
      ]));

      await page.getByRole("button", { name: "Cancel", exact: true }).click();
      const row = page.getByRole("row").filter({ hasText: "presentation-form" });
      await row.getByRole("button", { name: "Presentation form", exact: true }).click();
      await page.getByRole("tab", { name: "Source", exact: true }).click();
      await expect(source).toContainText('"trueLabel": "I agree"');
      await page.getByRole("button", { name: "Publish", exact: true }).click();
      await expect(page.getByText("Published · version 1", { exact: true }).first()).toBeVisible();
      const published = (await requests()).find(request => request.path.endsWith("/publish"))!;
      expect(published.body.form?.fields).toEqual(draft.body.form?.fields);
    });
  }
}