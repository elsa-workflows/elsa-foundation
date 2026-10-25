import { expect, test } from "@playwright/test";

test("bulk deletion confirms exact runs, retains failed selection and retries", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html");
  await page.getByRole("checkbox", { name: "Select all workflow runs on this page" }).check();
  await expect(page.getByText("3 workflow runs selected", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  const confirmation = page.getByRole("dialog");
  await expect(confirmation).toContainText("quote-active");
  await expect(confirmation).toContainText("Active runs will be stopped");
  await confirmation.getByRole("button", { name: "Delete", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("1 workflow run could not be deleted");
  await expect(page.getByRole("checkbox", { name: "Select workflow run quote-retry", exact: true })).toBeChecked();
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-completed · quote-definition", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete", exact: true }).click();
  await expect(page.getByText("No workflow runs yet", { exact: true })).toBeVisible();
});

test("cancel sends no deletion and unsupported runtimes have no destructive controls", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html");
  await page.getByRole("button", { name: "Delete workflow run quote-completed", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-completed · quote-definition", exact: true })).toBeVisible();
  await page.goto("/workflow-run-deletion.html?delete=disabled");
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-active · quote-definition", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Delete selected", exact: true })).toHaveCount(0);
  await expect(page.getByRole("checkbox")).toHaveCount(0);
});

test("run deletion communicates danger and disables cleanly while pending", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html?delete=delayed");
  const deleteRun = page.getByRole("button", { name: "Delete workflow run quote-completed" });
  await expect(deleteRun).toBeVisible();
  const initial = await deleteRun.evaluate(button => {
    const style = getComputedStyle(button);
    return { color: style.color, cursor: style.cursor, background: style.backgroundColor };
  });
  expect(initial.cursor).toBe("pointer");
  const danger = await readThemeToken(deleteRun, "--wf-danger", "color");
  expect(initial.color).toBe(danger);

  await deleteRun.hover();
  const hoverBackground = await deleteRun.evaluate(button => getComputedStyle(button).backgroundColor);
  expect(hoverBackground).not.toBe(initial.background);
  expect(hoverBackground).not.toBe("transparent");

  await deleteRun.click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete", exact: true }).click();
  await expect(deleteRun).toBeDisabled();
  const box = await deleteRun.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.click(box!.x + box!.width / 2, box!.y + box!.height / 2);
  await expect.poll(() => page.evaluate(() =>
    (window as Window & { workflowRunDeletionRequests?: string[] }).workflowRunDeletionRequests?.length ?? 0
  )).toBe(1);
  await expect(page.locator("output")).toHaveCount(0);
  await page.mouse.move(0, 0);
  const disabledBackground = await deleteRun.evaluate(button => getComputedStyle(button).backgroundColor);
  await deleteRun.hover();
  const disabled = await deleteRun.evaluate(button => {
    const style = getComputedStyle(button);
    return { color: style.color, cursor: style.cursor, opacity: style.opacity, background: style.backgroundColor };
  });
  expect(disabled.cursor).toBe("not-allowed");
  expect(Number(disabled.opacity)).toBeLessThan(1);
  expect(disabled.color).not.toBe(initial.color);
  expect(disabled.background).toBe(disabledBackground);
  await expect(deleteRun).toBeDisabled();
});

async function readThemeToken(locator: import("@playwright/test").Locator, token: string, property: "color" | "backgroundColor") {
  return locator.evaluate((button, options) => {
    const probe = document.createElement("span");
    probe.style[options.property] = `var(${options.token})`;
    button.append(probe);
    const value = getComputedStyle(probe)[options.property];
    probe.remove();
    return value;
  }, { token, property });
}
