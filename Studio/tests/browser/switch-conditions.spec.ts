import { expect, test, type Page } from "@playwright/test";

test.setTimeout(60_000);
const owner = (page: Page) => page.locator('.wf-sequence-step[data-id="collection-owner"], .react-flow__node[data-id="collection-owner"], .react-flow__node[data-id="collection-element"]');
const cases = (page: Page) => page.getByRole("region", { name: "Cases", exact: true });
async function navigationAllowed(page: Page) {
  return page.evaluate(() => ({
    studio: window.dispatchEvent(new CustomEvent("elsa:studio-navigation-requested", { cancelable: true, detail: { fromPath: "/workflows/editor", toPath: "/settings/backends" } })),
    unload: window.dispatchEvent(new Event("beforeunload", { cancelable: true }))
  }));
}
async function saved(page: Page) {
  return page.evaluate(() => {
    const value = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find(key => key.startsWith("adaptive-ports:"))!)!);
    return (value.state?.rootActivity ?? value.payload.rootActivity).structure.payload.activities[0];
  });
}

for (const host of ["activity", "workflow"]) for (const composition of ["sequence", "flowchart", "bpmn"]) {
  test(`${host} ${composition}: Boolean conditions use normal syntax, persist and follow case priority`, async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=${composition}&collection-slots=switch-conditions&theme=dark`);
    await page.locator(".wf-sequence-slots, .wf-node-slot-list").getByRole("button", { name: "Add case", exact: true }).click();
    const first = cases(page).getByRole("textbox", { name: "Case 1 label", exact: true });
    await first.fill("Approved"); await first.press("Enter");
    const row = cases(page).locator(".wf-slot-collection-item").first();
    await expect(row).toContainText("Condition");
    await row.getByRole("button", { name: /expression syntax: Literal/i }).click();
    await page.getByRole("option", { name: /JavaScript/ }).click();
    await row.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
    const editor = row.locator(".cm-content").first();
    await editor.fill("1 + 1 === 2");
    await editor.press("Escape");
    await cases(page).getByRole("button", { name: "Add case", exact: true }).click();
    await cases(page).getByRole("textbox", { name: "Case 2 label", exact: true }).fill("Rejected");
    await cases(page).getByRole("textbox", { name: "Case 2 label", exact: true }).press("Enter");
    await cases(page).getByRole("button", { name: "Move case 1 down", exact: true }).click();
    await expect(first).toHaveValue("Rejected");
    await expect(cases(page).getByRole("textbox", { name: "Case 2 label", exact: true })).toHaveValue("Approved");
    await expect(cases(page).locator(".wf-slot-collection-item").nth(1).getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true })).toContainText("1 + 1 === 2");
    if (host === "workflow") {
      expect(await navigationAllowed(page)).toEqual({ studio: false, unload: false });
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect.poll(() => navigationAllowed(page)).toEqual({ studio: true, unload: true });
    }
    await expect.poll(async () => JSON.stringify(await saved(page))).toContain("1 + 1 === 2");
    const before = await saved(page);
    expect(before.structure.payload.cases.map((item: { match: string }) => item.match)).toEqual(["Rejected", "Approved"]);
    expect(before.inputs).toEqual([]);
    expect(before.structure.payload.cases[1].condition.value.expressionType).toBe("JavaScript");
    await page.reload();
    await owner(page).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await expect(cases(page).locator(".wf-slot-collection-item").nth(1).getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true })).toContainText("1 + 1 === 2");
    expect(await saved(page)).toEqual(before);
    const approvedRow = cases(page).locator(".wf-slot-collection-item").nth(1);
    await approvedRow.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
    await approvedRow.locator(".cm-content").first().fill("");
    await approvedRow.locator(".cm-content").first().press("Escape");
    if (host === "workflow") await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect.poll(async () => (await saved(page)).structure.payload.cases[1].condition.value.value).toBe("");
    await page.reload();
    await owner(page).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await expect(cases(page).locator(".wf-slot-collection-item").nth(1).getByRole("button", { name: /Condition expression syntax: JavaScript/ })).toBeVisible();
    expect((await saved(page)).structure.payload.cases[1].caseId).toBe(before.structure.payload.cases[1].caseId);
    expect(errors).toEqual([]);
  });
}


test("upgrades legacy cases explicitly and keeps upgrade atomic in history", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch-upgrade");
  await owner(page).click();
  await page.getByRole("button", { name: "Upgrade to case conditions", exact: true }).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await expect(cases(page).getByRole("textbox", { name: "Case 1 label", exact: true })).toHaveValue("Approved");
  await expect(cases(page)).toContainText("Matches Value");
  await page.getByRole("button", { name: "Undo Activity Graph edit", exact: true }).click();
  await page.getByRole("tab", { name: "Inputs", exact: true }).click();
  await expect(page.getByRole("button", { name: "Upgrade to case conditions", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Redo Activity Graph edit", exact: true }).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await cases(page).getByRole("button", { name: "Use condition", exact: true }).click();
  await expect(cases(page)).toContainText("Condition");
  await expect(cases(page).getByRole("button", { name: "Use condition", exact: true })).toHaveCount(0);
});

for (const composition of ["sequence", "flowchart", "bpmn"]) {
  test(`workflow ${composition}: upgrades to conditions while legacy Switch remains recommended`, async ({ page }, testInfo) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=workflow&composition=${composition}&collection-slots=switch-upgrade`);
    await owner(page).click();
    await expect(page.getByRole("button", { name: "Upgrade to case conditions", exact: true })).toBeVisible();
    if (composition === "flowchart") await page.screenshot({ path: testInfo.outputPath("switch-inputs-upgrade.png") });
    await page.getByRole("button", { name: "Upgrade to case conditions", exact: true }).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await expect(cases(page).getByRole("textbox", { name: "Case 1 label", exact: true })).toHaveValue("Approved");
    await cases(page).getByRole("button", { name: "Use condition", exact: true }).click();
    await expect(cases(page).getByRole("button", { name: /Condition expression syntax: Literal/ })).toBeVisible();
    if (composition === "flowchart") await page.screenshot({ path: testInfo.outputPath("switch-case-condition.png") });
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect.poll(async () => (await saved(page)).activityVersionId).toBe("activity-switch-4.1");
    const upgraded = await saved(page);
    expect(upgraded.structure.schemaVersion).toBe("2.0.0");
    await page.reload();
    await owner(page).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await expect(cases(page).getByRole("button", { name: /Condition expression syntax: Literal/ })).toBeVisible();
    expect(await saved(page)).toEqual(upgraded);
  });
}

test("explains a refused Secret upgrade and preserves the original", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch-upgrade&secret-value");
  await owner(page).click();
  await expect(page.getByRole("button", { name: "Upgrade to case conditions", exact: true })).toBeDisabled();
  await expect(page.getByRole("note").filter({ hasText: "activation-only Secret" })).toBeVisible();
});

test("source-owned case conditions expose no mutation controls", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch-conditions&read-only");
  await owner(page).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await expect(cases(page).getByRole("textbox", { name: "Case 1 label", exact: true })).toHaveAttribute("readonly", "");
  await expect(cases(page).getByRole("button", { name: /Condition expression syntax:/ })).toBeDisabled();
  await expect(cases(page).getByRole("button", { name: /^(Add case|Move case|Remove case|Use condition)/ })).toHaveCount(0);
});

test("keyboard condition editing fits a narrow inspector", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch-conditions&theme=dark");
  const add = page.locator(".wf-sequence-slots").getByRole("button", { name: "Add case", exact: true });
  await add.focus(); await page.keyboard.press("Enter");
  const syntax = cases(page).getByRole("button", { name: /Condition expression syntax:/ });
  await syntax.focus(); await page.keyboard.press("Enter");
  await page.getByRole("option", { name: /JavaScript/ }).click();
  const preview = cases(page).getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true });
  await preview.focus();
  const editor = cases(page).locator(".cm-content").first();
  await editor.fill("true"); await editor.press("Escape");
  await expect.poll(() => cases(page).evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  expect((await cases(page).boundingBox())!.width).toBeLessThanOrEqual(390);
  await page.screenshot({ path: testInfo.outputPath("narrow-case-condition.png") });
});
