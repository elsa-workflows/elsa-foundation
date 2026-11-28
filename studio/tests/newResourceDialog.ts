import { expect, type Page } from "@playwright/test";

export async function confirmNewResource(page: Page, kind: "form" | "decision", name: string) {
  const dialog = page.getByRole("dialog", { name: `New ${kind}`, exact: true });
  const primary = dialog.getByRole("button", { name: "Create", exact: true });
  await expect(primary).toBeDisabled();
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
  await primary.click();
  await expect(dialog).toBeHidden();
}
