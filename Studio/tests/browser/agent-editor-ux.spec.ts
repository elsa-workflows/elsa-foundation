import { expect, test } from "@playwright/test";

for (const width of [390, 1440, 2552]) for (const theme of ["light", "dark"]) {
  test(`agent editing leads with instructions and preserves saved settings at ${width}px ${theme}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 1100 });
    const url = `/?mode=activity-authoring&provider=agent&theme=${theme}`;
    await page.goto(url);
    const editor = page.getByTestId("specialist-editor");
    await expect(editor.getByRole("textbox", { name: /^Instructions/ })).toBeVisible();
    const advanced = editor.locator("details.agent-specialist-advanced");
    await expect(advanced).not.toHaveAttribute("open");
    await expect(editor.getByRole("combobox", { name: /^Model connection/ })).toBeEnabled();
    if (width >= 1440) {
      const [instructions, model] = await editor.locator(".agent-specialist-instructions, .agent-specialist-model").evaluateAll(elements => elements.map(element => { const b = element.getBoundingClientRect(); return { x: b.x, y: b.y, width: b.width }; }));
      expect(model.x).toBeGreaterThan(instructions.x + instructions.width);
      expect(Math.abs(model.y - instructions.y)).toBeLessThan(20);
    }
    await editor.getByRole("textbox", { name: /^Instructions/ }).fill("Proofread the text and return concise suggestions.");
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    await advanced.locator("summary").press("Enter");
    const calls = advanced.getByRole("textbox", { name: /^Maximum model calls/ });
    await calls.fill("2");
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    await page.reload();
    await expect(editor.getByRole("textbox", { name: /^Instructions/ })).toHaveValue("Proofread the text and return concise suggestions.");
    await advanced.locator("summary").click();
    await expect(calls).toHaveValue("2");
    await advanced.locator("summary").click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("agent-designer.png"), fullPage: true });
    await page.getByRole("tab", { name: /^Outcomes \(\d+\)$/ }).click();
    const outcome = page.getByRole("region", { name: "Done", exact: true });
    await expect(outcome.getByText("Emitted", { exact: true })).toBeVisible();
    await expect(outcome.getByRole("checkbox")).toHaveCount(0);
    await expect(outcome.getByRole("button", { name: "Remove outcome" })).toHaveCount(0);
    await expect(outcome.getByRole("textbox", { name: "Name", exact: true })).toHaveCount(0);
    const contract = page.locator(".ad-contract-editor");
    const tabs = page.locator(".ad-authoring-view-tabs");
    expect(Math.abs((await contract.boundingBox())!.x - (await tabs.boundingBox())!.x)).toBeLessThanOrEqual(2);
    await expect(page.locator(".ad-contract-workbench-advanced")).not.toHaveAttribute("open");
    await page.screenshot({ path: testInfo.outputPath("agent-inputs-outputs.png"), fullPage: true });
  });
}

test("rejected informational validation stays truthful and compact", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&provider=agent&invalid");
  const panel = page.locator(".ad-diagnostics-panel");
  await expect(panel.getByRole("heading", { name: "Draft diagnostics", exact: true })).toHaveCount(1);
  await expect(panel.getByLabel("Diagnostic severity counts")).toHaveText("1 info");
  await expect(panel).not.toContainText("0 errors");
  await expect(panel).not.toContainText("Correct the errors");
  await expect(panel).toContainText("did not pass validation");
  await expect(panel.locator(".ad-diagnostic-help")).not.toHaveAttribute("open");
});

test("module-owned icons reach all three main navigation entries", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/?mode=activity-authoring&provider=agent");
  await expect(page.getByTestId("specialist-editor")).toBeVisible();
  for (const [name, icon] of [["Agents", "bot"], ["Secrets", "key-round"], ["Connections", "cable"]]) {
    const entry = page.getByRole("link", { name, exact: true });
    // More tools may start closed; the module icon must survive composition regardless.
    if (!await entry.isVisible()) await page.getByRole("button", { name: /More tools/ }).click();
    await expect(entry.locator(`svg.lucide-${icon}`)).toBeVisible();
  }
});

test("saved invalid limits are revealed and read-only settings remain inspectable", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&provider=agent&invalid-limits");
  const editor = page.getByTestId("specialist-editor");
  const advanced = editor.locator(".agent-specialist-advanced");
  await expect(advanced).toHaveAttribute("open");
  await expect(advanced.getByRole("textbox", { name: /^Maximum model calls/ })).toHaveAttribute("aria-invalid", "true");
  await page.goto("/?mode=activity-authoring&provider=agent&read-only");
  await expect(editor.getByRole("textbox", { name: /^Instructions/ })).toBeDisabled();
  await advanced.locator("summary").click();
  await expect(advanced.getByRole("textbox", { name: /^Maximum model calls/ })).toBeDisabled();
  await expect(advanced.getByRole("textbox", { name: /^Timeout/ })).toBeDisabled();
});


for (const width of [360, 1440]) for (const theme of ["light", "dark"]) {
  test(`blocked publication explains corrections and focuses settings at ${width}px ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-authoring&provider=agent&publication-blocked&theme=${theme}`);
    const editor = page.getByTestId("specialist-editor");
    const trigger = page.locator(".ad-publication-toolbar-trigger");
    await trigger.click();
    const dialog = page.getByRole("dialog", { name: "Review & publish", exact: true });
    const blockers = dialog.locator(".ad-publication-blockers");
    await expect(blockers.getByRole("heading", { name: "Resolve these issues before publishing", exact: true })).toBeVisible();
    await expect(blockers.locator(".ad-publication-diagnostics li")).toHaveCount(2);
    await expect(blockers.getByRole("button", { name: "Edit instructions", exact: true })).toHaveCount(1);
    await expect(dialog.getByRole("button", { name: "Publish 1.0.0", exact: true })).toBeDisabled();
    await expect(dialog.getByText("specialist.manifest.model", { exact: true })).toBeHidden();
    await page.screenshot({ path: info.outputPath("blocked.png"), fullPage: true });
    await blockers.getByRole("button", { name: "Edit instructions", exact: true }).press("Enter");
    const instructions = editor.getByRole("textbox", { name: /^Instructions/ });
    await expect(dialog).toBeHidden();
    await expect(instructions).toBeFocused();
    await instructions.fill("Proofread the supplied text.");
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    await trigger.click();
    await expect(blockers.locator(".ad-publication-diagnostics li")).toHaveCount(1);
    await blockers.getByRole("button", { name: "Choose model connection", exact: true }).click();
    const model = editor.getByRole("combobox", { name: /^Model connection/ });
    await expect(model).toBeFocused();
    await model.selectOption("azure-openai|proofreader");
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    await trigger.click();
    await expect(dialog.getByRole("button", { name: "Publish 1.0.0", exact: true })).toBeEnabled();
    await expect(blockers).toHaveCount(0);
    await page.screenshot({ path: info.outputPath("ready.png"), fullPage: true });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}


for (const width of [360, 1440]) for (const theme of ["light", "dark"]) {
  test(`rejected publication does not reuse earlier readiness at ${width}px ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-authoring&provider=agent&theme=${theme}`);
    await page.locator(".ad-publication-toolbar-trigger").click();
    const dialog = page.getByRole("dialog", { name: "Review & publish", exact: true });
    await expect(dialog.getByRole("button", { name: "Publish 1.0.0", exact: true })).toBeEnabled();
    await dialog.getByRole("button", { name: "Publish 1.0.0", exact: true }).click();
    await expect(dialog.locator(".ad-publication-blockers")).toContainText("Publication did not complete");
    await expect(dialog.getByRole("button", { name: "Publish 1.0.0", exact: true })).toBeDisabled();
    await page.screenshot({ path: info.outputPath("rejected.png"), fullPage: true });
    await dialog.getByText("Technical readiness checks", { exact: true }).click();
    const readiness = dialog.locator(".ad-publication-readiness");
    await expect(readiness.getByRole("heading", { name: "Checks from the earlier review", exact: true })).toBeVisible();
    await expect(readiness).not.toContainText("Ready to publish");
    await readiness.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath("earlier-readiness.png"), fullPage: true });
  });
}
