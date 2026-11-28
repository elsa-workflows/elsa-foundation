import { expect, test, type Page } from "@playwright/test";

test.setTimeout(60_000);
const node = (page: Page, id = "collection-owner") => page.locator(`.react-flow__node[data-id="${id}"]`);
const port = (page: Page, name: string) => node(page).locator(`.source[data-handleid="${name}"]`);
const cases = (page: Page) => page.getByRole("region", { name: "Cases", exact: true });
async function selectCases(page: Page) {
  await node(page).locator(".wf-node-content").click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
}
async function save(page: Page, host: string) {
  if (host === "workflow") await page.getByRole("button", { name: "Save", exact: true }).click();
}
async function saved(page: Page) {
  return page.evaluate(() => {
    const value = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find(key => key.startsWith("adaptive-ports:"))!)!);
    return (value.state?.rootActivity ?? value.payload.rootActivity).structure.payload.connections;
  });
}

for (const host of ["workflow", "activity"]) test(`${host}: case wires follow renaming, history, reordering, deletion and reload`, async ({ page }, testInfo) => {
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=flowchart&collection-slots=switch-ports&theme=dark`);
  for (const name of ["Approved", "Rejected", "Default", "Break"]) await expect(port(page, name)).toBeVisible();
  await port(page, "Approved").dragTo(node(page, "write-one").locator(".target"));
  await expect(page.locator(".react-flow__edge")).toHaveCount(1);
  await save(page, host);
  await expect.poll(() => saved(page)).toEqual([expect.objectContaining({ source: { nodeId: "collection-owner", port: "Approved" }, target: { nodeId: "write-one" } })]);
  const original = (await saved(page))[0];

  await selectCases(page);
  const first = cases(page).getByRole("textbox", { name: "Case 1 label", exact: true });
  await first.fill("Accepted"); await first.press("Enter");
  await expect(port(page, "Accepted")).toBeVisible();
  await expect(port(page, "Approved")).toHaveCount(0);
  await save(page, host);
  await expect.poll(() => saved(page)).toEqual([{ ...original, source: { ...original.source, port: "Accepted" } }]);

  await page.getByRole("button", { name: /^Undo(?: Activity Graph edit)?$/ }).click();
  await expect(port(page, "Approved")).toBeVisible();
  await save(page, host);
  await expect.poll(() => saved(page)).toEqual([original]);
  await page.getByRole("button", { name: /^Redo(?: Activity Graph edit)?$/ }).click();
  await expect(port(page, "Accepted")).toBeVisible();
  await selectCases(page);
  await cases(page).getByRole("button", { name: "Move case 1 down", exact: true }).click();
  await save(page, host);
  await expect.poll(() => saved(page)).toEqual([{ ...original, source: { ...original.source, port: "Accepted" } }]);
  await page.reload();
  await expect(port(page, "Accepted")).toBeVisible();
  await expect(page.locator(".react-flow__edge")).toHaveCount(1);
  await selectCases(page);
  await expect(cases(page).getByRole("textbox", { name: "Case 2 label", exact: true })).toHaveValue("Accepted");
  await page.screenshot({ path: testInfo.outputPath(`${host}-switch-case-ports.png`) });

  page.once("dialog", async dialog => { expect(dialog.message()).toContain("outgoing connections"); await dialog.dismiss(); });
  await cases(page).getByRole("button", { name: "Remove case 2", exact: true }).click();
  await expect(port(page, "Accepted")).toBeVisible();
  page.once("dialog", dialog => dialog.accept());
  await cases(page).getByRole("button", { name: "Remove case 2", exact: true }).click();
  await expect(port(page, "Accepted")).toHaveCount(0);
  await expect(page.locator(".react-flow__edge")).toHaveCount(0);
  await save(page, host);
  await expect.poll(() => saved(page)).toEqual([]);
  await page.getByRole("button", { name: /^Undo(?: Activity Graph edit)?$/ }).click();
  await expect(port(page, "Accepted")).toBeVisible();
  await expect(page.locator(".react-flow__edge")).toHaveCount(1);
  await save(page, host);
  await expect.poll(() => saved(page)).toEqual([{ ...original, source: { ...original.source, port: "Accepted" } }]);
  expect(errors).toEqual([]);
});

test("source-owned graphs retain readable case ports without enabling connections", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=flowchart&collection-slots=switch-ports&read-only");
  await expect(port(page, "Approved")).toBeVisible();
  await port(page, "Approved").dragTo(node(page, "write-one").locator(".target"));
  await expect(page.locator(".react-flow__edge")).toHaveCount(0);
  await selectCases(page);
  await expect(cases(page).getByRole("textbox", { name: "Case 1 label", exact: true })).toHaveAttribute("readonly", "");
  await expect(cases(page).getByRole("button", { name: /^(Add case|Move case|Remove case)/ })).toHaveCount(0);
});
