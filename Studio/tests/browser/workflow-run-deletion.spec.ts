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
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-completed", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete", exact: true }).click();
  await expect(page.getByText("No workflow runs yet", { exact: true })).toBeVisible();
});

test("cancel sends no deletion and unsupported runtimes have no destructive controls", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html");
  await page.getByRole("button", { name: "Delete workflow run quote-completed", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-completed", exact: true })).toBeVisible();
  await page.goto("/workflow-run-deletion.html?delete=disabled");
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-active", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Delete selected", exact: true })).toHaveCount(0);
  await expect(page.getByRole("checkbox")).toHaveCount(0);
});
