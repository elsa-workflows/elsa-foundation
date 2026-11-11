import { expect, test, type Page, type TestInfo } from "@playwright/test";

async function capture(page: Page, testInfo: TestInfo, name: string) {
  const strip = page.getByRole("tablist", { name: "Open screens", exact: true });
  await expect(strip).toBeVisible();
  await strip.screenshot({ path: testInfo.outputPath(`${name}.png`) });
}

function visible(page: Page) {
  return page.locator(".studio-workspace-panel:not([hidden])");
}

async function openTab(page: Page, path: string) {
  await page.evaluate(target => {
    window.dispatchEvent(new CustomEvent("elsa:studio-workspace-tab-requested", { cancelable: true, detail: { path: target, activate: true } }));
  }, path);
}

test("workspace tabs keep the list title and name new and opened decisions", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 720 });
  await page.goto("/?mode=decision-workspace-tabs&theme=light");
  const tabs = page.getByRole("tablist", { name: "Open screens", exact: true });
  await expect(page.locator("#decisions-title")).toHaveText("Decisions");
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("Decisions");
  await capture(page, testInfo, "decision-tabs-list");

  await openTab(page, "/decisions?new=1");
  await expect(visible(page).getByRole("heading", { name: "New decision", exact: true })).toBeVisible();
  await expect(tabs.getByRole("tab", { name: "Decisions", exact: true })).toBeVisible();
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("New decision");
  await capture(page, testInfo, "decision-tabs-new");

  await visible(page).getByRole("button", { name: "Back to decisions", exact: true }).click();
  await expect(visible(page).locator("#decisions-title")).toHaveText("Decisions");
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("Decisions");
  await visible(page).getByRole("button", { name: "New decision", exact: true }).click();
  await expect(visible(page).getByRole("heading", { name: "New decision", exact: true })).toBeVisible();
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("New decision");

  await visible(page).getByLabel("Name", { exact: true }).fill("Picker policy");
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("Picker policy");
  await visible(page).getByRole("button", { name: "Save decision", exact: true }).click();
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("Picker policy · Draft");
  await capture(page, testInfo, "decision-tabs-saved");

  await openTab(page, "/decisions?decision=approval");
  await expect(visible(page).getByRole("heading", { name: "Approval", exact: true })).toBeVisible();
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("Approval · Draft");
  await visible(page).getByLabel("Name", { exact: true }).fill("Renamed approval");
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("Renamed approval · Draft");
  await capture(page, testInfo, "decision-tabs-renamed");

  await openTab(page, "/decisions?decision=approval&version=v1");
  await expect(visible(page).getByRole("heading", { name: "Approval · Version 1", exact: true })).toBeVisible();
  await expect(tabs.getByRole("tab", { selected: true })).toHaveAccessibleName("Approval · Version 1");
  await capture(page, testInfo, "decision-tabs-published");
});
