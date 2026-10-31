import { expect, test, type Page } from "@playwright/test";

test.setTimeout(60_000);

const canvasAdd = (page: Page, item = "case") => page.locator(".wf-sequence-slots, .wf-node-slot-list").getByRole("button", { name: `Add ${item}`, exact: true });
const ownerNode = (page: Page) => page.locator('.wf-sequence-step[data-id="collection-owner"], .react-flow__node[data-id="collection-owner"], .react-flow__node[data-id="collection-element"]');
const cases = (page: Page) => page.getByRole("region", { name: "Cases", exact: true });

async function returnToCases(page: Page) {
  await page.getByRole("button", { name: "Root", exact: true }).click();
  await ownerNode(page).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
}

async function savedOwner(page: Page) {
  return page.evaluate(() => {
    const saved = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find(key => key.startsWith("adaptive-ports:"))!)!);
    return (saved.state?.rootActivity ?? saved.payload.rootActivity).structure.payload.activities[0];
  });
}

test("new Switch exposes visual case authoring", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch&theme=dark");
  await expect(page.locator(".wf-sequence-step")).toContainText("Switch");
  await page.getByRole("button", { name: "Add case", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Case 1 match", exact: true })).toHaveValue("Case 1");
  await cases(page).getByRole("textbox", { name: "Case 1 match", exact: true }).fill("Approved");
  await cases(page).getByRole("textbox", { name: "Case 1 match", exact: true }).press("Enter");
  await expect(page.locator(".wf-sequence-slots").getByRole("button", { name: "Open Approved, Empty", exact: true })).toBeVisible();
});

for (const host of ["activity", "workflow"]) for (const composition of ["sequence", "flowchart", "bpmn"]) {
  test(`${host} ${composition}: cases preserve branch content, history and saved reload`, async ({ page }, testInfo) => {
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=${composition}&collection-slots=switch&theme=${composition === "flowchart" ? "classic" : "dark"}`);
    await canvasAdd(page).click();
    const first = cases(page).getByRole("textbox", { name: "Case 1 match", exact: true });
    await first.fill("Approved"); await first.press("Enter");
    await cases(page).getByRole("button", { name: "Add case", exact: true }).click();
    const second = cases(page).getByRole("textbox", { name: "Case 2 match", exact: true });
    await second.fill("Approved"); await second.press("Enter");
    await expect(cases(page).getByRole("alert")).toContainText("already used");
    await second.fill("Rejected"); await second.press("Enter");
    await expect(cases(page).getByRole("alert")).toHaveCount(0);
    await cases(page).getByRole("button", { name: "Choose Approved activity", exact: true }).click();
    await page.getByRole("option", { name: /Sequence/ }).click();
    await expect(page.getByRole("button", { name: "Switch / Approved", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Add the first step", exact: true }).click();
    await page.getByRole("option", { name: /Write Line/ }).click();
    await expect(page.locator(".wf-sequence-step")).toContainText("Write Line");
    await returnToCases(page);

    await first.fill("[$&]{match}東京"); await first.press("Enter");
    await page.getByRole("button", { name: /^Undo(?: Activity Graph edit)?$/ }).click();
    if (host === "workflow") await ownerNode(page).click();
    await expect(first).toHaveValue("Approved");
    await page.getByRole("button", { name: /^Redo(?: Activity Graph edit)?$/ }).click();
    if (host === "workflow") await ownerNode(page).click();
    await expect(first).toHaveValue("[$&]{match}東京");
    await expect(cases(page).getByRole("button", { name: /Change .* activity/ })).toHaveCount(1);
    await expect(page.locator(".wf-slot-list").getByRole("button", { name: "Choose Default activity", exact: true })).toBeVisible();
    if (host === "workflow") await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect.poll(async () => JSON.stringify(await savedOwner(page))).toContain('"match":"[$&]{match}東京"');
    const beforeReload = await savedOwner(page);
    expect(beforeReload.structure.payload.cases[0].activity.structure.payload.activities).toHaveLength(1);
    await page.reload();
    await ownerNode(page).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await expect(first).toHaveValue("[$&]{match}東京");
    expect(await savedOwner(page)).toEqual(beforeReload);

    page.once("dialog", dialog => dialog.dismiss());
    await cases(page).getByRole("button", { name: "Remove case 1", exact: true }).click();
    await expect(cases(page).getByRole("textbox")).toHaveCount(2);
    page.once("dialog", dialog => dialog.accept());
    await cases(page).getByRole("button", { name: "Remove case 1", exact: true }).click();
    await expect(cases(page).getByRole("textbox")).toHaveCount(1);
    await page.getByRole("button", { name: /^Undo(?: Activity Graph edit)?$/ }).click();
    if (host === "workflow") await ownerNode(page).click();
    await expect(first).toHaveValue("[$&]{match}東京");
    expect(errors).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath(`${host}-${composition}-cases.png`) });
  });
}

test("quoted case names stay distinct from each other and Default in navigation", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch&theme=dark");
  const values = ["", '""', "Default", '"Default"'];
  await canvasAdd(page).click();
  for (const [index, value] of values.entries()) {
    if (index > 0) await cases(page).getByRole("button", { name: "Add case", exact: true }).click();
    const input = cases(page).getByRole("textbox", { name: `Case ${index + 1} match`, exact: true });
    await input.fill(value); await input.press("Enter");
  }
  const labels = await page.locator(".wf-sequence-slots").getByRole("button", { name: /^Open / })
    .evaluateAll(elements => elements.map(element => element.getAttribute("aria-label")));
  expect(new Set(labels).size).toBe(values.length + 1);
  await expect(page.getByRole("button", { name: 'Choose "Default" activity', exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Choose Default activity", exact: true })).toBeVisible();
  await page.getByRole("button", { name: 'Open "Default", Empty', exact: true }).click();
  await expect(page.getByRole("button", { name: 'Switch / "Default"', exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Root", exact: true }).click();
  await page.getByRole("button", { name: "Open Default, Empty", exact: true }).click();
  await expect(page.getByRole("button", { name: "Switch / Default", exact: true })).toBeVisible();
});

test("Parallel's initially empty branches use the same editor", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=parallel&theme=classic");
  await canvasAdd(page, "branch").click();
  const branches = page.getByRole("region", { name: "Branches", exact: true });
  await expect(branches.getByRole("textbox", { name: "Branch 1 name", exact: true })).toHaveValue("Branch 1");
  await branches.getByRole("button", { name: "Remove branch 1", exact: true }).click();
  await expect(branches.getByRole("textbox")).toHaveCount(0);
  await expect(page.getByRole("tab", { name: "Slots", exact: true })).toHaveAttribute("aria-selected", "true");
  await branches.getByRole("button", { name: "Add branch", exact: true }).click();
  await expect(branches.getByRole("textbox")).toHaveCount(1);
});

test("source-owned read-only graph omits collection mutations", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch&read-only&theme=dark");
  await expect(ownerNode(page)).toBeVisible();
  await expect(canvasAdd(page)).toHaveCount(0);
  await ownerNode(page).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await expect(cases(page)).toBeVisible();
  await expect(cases(page).getByRole("button")).toHaveCount(0);
});

for (const theme of ["classic", "dark"]) test(`keyboard case editing fits a narrow inspector (${theme})`, async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`/?mode=activity-duplicate-editor&host=activity&composition=sequence&collection-slots=switch&theme=${theme}`);
  await canvasAdd(page).focus(); await page.keyboard.press("Enter");
  const input = cases(page).getByRole("textbox", { name: "Case 1 match", exact: true });
  await input.focus(); await input.fill(" "); await input.press("Enter");
  await expect(input).toHaveValue(" ");
  await expect(input).toBeFocused();
  const box = await cases(page).boundingBox();
  expect(box!.width).toBeLessThanOrEqual(390);
  expect(await cases(page).evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath(`narrow-${theme}-cases.png`) });
});
