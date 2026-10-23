import { expect, test } from "@playwright/test";

test("deferred workflow surfaces announce loading and reveal their content", async ({ page }) => {
  await page.goto("/?theme=light&mode=lazy-boundary");

  const loading = page.getByRole("status");
  await expect(loading).toHaveText("Loading workflow designer…");
  await expect(loading).toHaveAttribute("aria-busy", "true");
  await expect(page.getByRole("region", { name: "Deferred workflow designer" })).toHaveText("Workflow designer ready");
  await expect(loading).toBeHidden();
});
