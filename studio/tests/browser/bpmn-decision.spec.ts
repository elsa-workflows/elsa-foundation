import { expect, test, type Page } from "@playwright/test";

const element = (page: Page) => page.locator(".react-flow__node").filter({ has: page.locator(".wf-bpmn-node[data-runtime-node-id='decision']") });
const routing = (page: Page) => page.getByRole("region", { name: "BPMN routing" });
const outcome = (page: Page) => page.locator(".wf-expression-field").filter({ has: page.getByRole("button", { name: /^Outcome expression syntax:/ }) }).locator("input").first();

for (const host of ["workflow", "activity"]) {
  test(`${host} authors decision outcomes and routing together, with undo and persisted reload`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1500, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&bpmn-decision=legacy&theme=dark`);
    await element(page).click();
    await expect(routing(page).getByLabel("Gateway type")).toHaveValue("task");
    await routing(page).getByLabel("Gateway type").selectOption("exclusiveGateway");
    await expect(element(page).locator(".wf-bpmn-gateway-marker")).toHaveText("×");
    await expect(element(page).locator(".wf-bpmn-caption")).toHaveText("BPMN Decision");
    await page.getByRole("button", { name: /^Undo(?: Activity Graph edit)?$/ }).click();
    await expect(element(page).locator(".wf-bpmn-task")).toBeVisible();
    await page.getByRole("button", { name: /^Redo(?: Activity Graph edit)?$/ }).click();
    await expect(element(page).locator(".wf-bpmn-gateway-marker")).toHaveText("×");
    await element(page).click();
    await outcome(page).fill("High");
    await routing(page).getByLabel("Match outcome for flow to High", { exact: true }).fill("High");
    await routing(page).getByLabel("Default flow to Low", { exact: true }).check();
    await expect(routing(page).getByLabel("Match outcome for flow to Low", { exact: true })).toBeDisabled();
    await expect(page.locator(".react-flow__edge[data-id='high-flow']")).toHaveCount(1);
    await routing(page).getByLabel("Gateway type").selectOption("inclusiveGateway");
    await expect(element(page).locator(".wf-bpmn-gateway-marker")).toHaveText("O");
    await page.screenshot({ path: testInfo.outputPath(`${host}-decision.png`) });
    if (host === "workflow") await page.getByRole("button", { name: "Save", exact: true }).click();
    await page.reload();
    await element(page).click();
    await expect(routing(page).getByLabel("Gateway type")).toHaveValue("inclusiveGateway");
    await expect(routing(page).getByLabel("Match outcome for flow to High", { exact: true })).toHaveValue("High");
    await expect(routing(page).getByLabel("Default flow to Low", { exact: true })).toBeChecked();
    await expect(outcome(page)).toHaveValue("High");
  });

  test(`${host} edits and persists a JavaScript Decision expression`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&bpmn-decision=bound&theme=light`);
    await element(page).click();
    await page.getByRole("button", { name: /^Outcome expression syntax:/ }).click();
    await page.getByRole("option", { name: "JavaScript", exact: true }).click();
    const expression = '2000 > 1000 ? "High" : "Low"';
    await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
    await page.locator(".cm-content[contenteditable='true']").fill(expression);
    await routing(page).getByLabel("Match outcome for flow to High", { exact: true }).fill("High");
    if (host === "workflow") await page.getByRole("button", { name: "Save", exact: true }).click();
    await page.reload();
    await element(page).click();
    await expect(page.getByRole("button", { name: "Outcome expression syntax: JavaScript", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /JavaScript expression.*Activate to edit/ })).toContainText(expression);
    await expect(routing(page).getByLabel("Match outcome for flow to High", { exact: true })).toHaveValue("High");
  });

  test(`${host} preserves task loop settings by explaining and disabling gateway conversion`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&bpmn-decision=legacy-loop&theme=dark`);
    await element(page).click();
    await expect(routing(page).getByLabel("Gateway type")).toBeDisabled();
    await expect(routing(page).getByRole("note")).toContainText("Remove this task's loop or multi-instance settings");
    await expect(element(page).locator(".wf-bpmn-task")).toBeVisible();
    await outcome(page).fill("High");
    await expect(outcome(page)).toHaveValue("High");
  });

  test(`${host} attaches a decision to an existing gateway and inserts new decisions as gateways`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&bpmn-decision=unbound&theme=dark`);
    await page.locator(".wf-bpmn-node[data-element-type='exclusiveGateway']").click();
    await page.getByRole("button", { name: "Add decision", exact: true }).click();
    await expect(page.getByRole("button", { name: /^Outcome expression syntax:/ })).toBeVisible();
    await expect(routing(page).getByLabel("Match outcome for flow to High", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add decision", exact: true })).toHaveCount(0);
    await page.getByRole("searchbox", { name: "Search element palette" }).fill("BPMN Decision");
    await page.getByRole("treeitem", { name: /^BPMN Decision,/ }).click();
    await expect(page.locator(".wf-bpmn-node[data-element-type='exclusiveGateway']")).toHaveCount(2);
    await expect(page.locator(".wf-bpmn-node[data-element-type='task']")).toHaveCount(0);
  });

  test(`${host} edits a bound BPMN element name in Details and persists it`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&bpmn-decision=bound&theme=light`);
    await element(page).click();
    await expect(routing(page).getByLabel("Name", { exact: true })).toHaveCount(0);
    await page.getByRole("tab", { name: "Details", exact: true }).click();

    const name = page.getByLabel("BPMN element name", { exact: true });
    await name.fill("Approval gate");
    if (host === "workflow") await page.getByRole("button", { name: "Save", exact: true }).click();
    await page.reload();
    await element(page).click();
    await page.getByRole("tab", { name: "Details", exact: true }).click();
    await expect(page.getByLabel("BPMN element name", { exact: true })).toHaveValue("Approval gate");
    await page.getByLabel("Activity inspector sections").getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(routing(page).getByLabel("Match outcome for flow to High", { exact: true })).toBeVisible();
  });

  test(`${host} keeps standalone BPMN names in routing`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&bpmn-decision=unbound&theme=dark`);
    await page.locator(".wf-bpmn-node[data-element-type='exclusiveGateway']").click();

    await expect(routing(page).getByLabel("Name", { exact: true })).toBeVisible();
    await expect(page.getByRole("tab", { name: "Details", exact: true })).toHaveCount(0);
  });
}

test("workflow summarizes and keyboard-expands a simple task path", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=workflow&composition=bpmn&run-layout&theme=dark");
  await page.locator(".wf-bpmn-node[data-runtime-node-id='write-one']").click();

  const panel = routing(page);
  const disclosure = panel.getByRole("button", { name: /End event.*Any outcome/ });
  await expect(disclosure).toHaveAttribute("aria-expanded", "false");
  await disclosure.focus();
  await page.keyboard.press("Enter");
  await expect(disclosure).toHaveAttribute("aria-expanded", "true");

  const matchOutcome = panel.getByLabel(/^Match outcome/);
  await expect(matchOutcome).toBeVisible();
  await page.keyboard.press("Space");
  await expect(disclosure).toHaveAttribute("aria-expanded", "false");
  await page.keyboard.press("Enter");
  await expect(disclosure).toHaveAttribute("aria-expanded", "true");
  await matchOutcome.fill("Done");
  await matchOutcome.fill("");
  await expect(matchOutcome).toBeFocused();
  await expect(matchOutcome).toBeVisible();
});

test("locked Activity Definition keeps Decision and routing controls read-only", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=bpmn&palette-context&bpmn-decision=bound&read-only&theme=dark");
  await element(page).click();
  await expect(routing(page).getByLabel("Gateway type")).toBeDisabled();
  await expect(routing(page).getByLabel("Name", { exact: true })).toHaveCount(0);
  await expect(routing(page).getByLabel("Match outcome for flow to High", { exact: true })).toBeDisabled();
  await expect(routing(page).getByLabel("Default flow to Low", { exact: true })).toBeDisabled();
  await page.getByRole("tab", { name: "Details", exact: true }).click();
  await expect(page.getByLabel("BPMN element name", { exact: true })).toBeDisabled();
});
