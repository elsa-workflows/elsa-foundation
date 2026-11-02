import { expect, test } from "@playwright/test";
import { expectAccessible, signIn } from "./support/journey";

// Opt-in: creates a text Secret and Connection in the supplied Workbench, using the normal Studio UI.
test.skip(!process.env.ELSA_STUDIO_URL, "Set ELSA_STUDIO_URL to an owned Studio connected to a disposable Workbench.");

test("connection validation explains client and real API rejections, then preserves inputs for successful correction", async ({ page }, testInfo) => {
  const name = `connection-validation-${Date.now()}`;
  const secretName = `${name}-key`;
  const secretValue = "private-validation-journey-sentinel";
  await signIn(page);
  await page.goto("/security/secrets");
  await page.getByRole("button", { name: "Create secret", exact: true }).first().click();
  const secret = page.getByRole("dialog", { name: "Create secret", exact: true });
  await secret.getByLabel("Technical name", { exact: true }).fill(secretName);
  await secret.getByLabel("Value", { exact: true }).fill(secretValue);
  await secret.getByRole("button", { name: "Create", exact: true }).click();
  await expect(secret).toHaveCount(0);

  await page.goto("/security/connections");
  await page.getByRole("button", { name: "Create connection", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  const nameInput = dialog.getByLabel("Name", { exact: true });
  const create = dialog.getByRole("button", { name: "Create", exact: true });
  await dialog.getByLabel("Auth scheme", { exact: true }).selectOption("api-key");
  await dialog.getByLabel("Existing Secret for API key", { exact: true }).selectOption(secretName);
  await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
  await nameInput.fill("Backend API");
  await expect(nameInput).toHaveAccessibleDescription(/Spaces are not allowed/);
  await expect(create).toBeDisabled();
  await page.screenshot({ path: testInfo.outputPath("invalid-name-desktop.png") });

  // Bypass client validation on the wire, so the rejection comes from the production Connections API.
  await page.route("**/connections", async route => {
    if (route.request().method() !== "POST") return route.continue();
    await route.continue({ postData: JSON.stringify({ ...route.request().postDataJSON(), name: "Backend API" }) });
  }, { times: 1 });
  await nameInput.fill(name);
  const rejectedResponse = page.waitForResponse(response => response.url().endsWith("/connections") && response.status() === 400);
  await create.click();
  const payload = await (await rejectedResponse).json();
  expect(payload.errors.name[0]).toContain("letters, numbers");
  expect(JSON.stringify(payload)).not.toContain(secretValue);
  await expect(nameInput).toBeFocused();
  await expect(nameInput).toHaveAttribute("aria-invalid", "true");
  await expect(nameInput).toHaveAccessibleDescription(/letters, numbers/);
  await expect(dialog.getByRole("alert")).not.toContainText("Request failed with 400");
  const more = dialog.getByRole("button", { name: /^More options/ });
  await expect(more).toHaveAttribute("aria-expanded", "false");
  await expect(more).not.toContainText("Error");
  await expect(dialog.getByLabel("Existing Secret for API key", { exact: true })).toHaveValue(secretName);
  await expect(dialog.getByLabel("Base URL", { exact: true })).toHaveValue("https://api.example");
  await expect(dialog).not.toContainText(secretValue);
  await expectAccessible(page, ".connections-create-dialog");
  await page.setViewportSize({ width: 320, height: 900 });
  await nameInput.scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath("server-name-error-narrow.png") });
  await nameInput.fill(`${name}-corrected`);
  await create.click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("status")).toContainText(`Connection '${name}-corrected' created.`);
});
