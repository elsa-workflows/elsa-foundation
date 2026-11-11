import { expect, type Page, type TestInfo, type Locator } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
async function section(editor: Locator, name: string) {
  const tab = editor.getByRole("tab", { name: new RegExp(`^${name}(?: \\(\\d+\\))?(?: — needs attention)?$`) });
  await tab.click();
  await expect(tab).toHaveAttribute("aria-selected", "true");
  return tab;
}

/** Uses the normal router, workspace, metadata APIs and disposable source-host persistence. */
export async function tabbedSecretsJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const stamp = `${info.project.name}-${Date.now()}`;
  const name = `TabbedSecret-${stamp}`;
  const value = "disposable-tabbed-acceptance-material";
  const displayName = `Tabbed credentials ${stamp}`;
  const description = "Metadata survives switching sections and reopening the resource.";
  const customScope = "Org.Team-A/Case";
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned Workbench URL").toBeTruthy();
  // Both secrets this journey creates are deleted afterwards, so reused hosts do not accumulate rows.
  for (const owned of [name, `Configuration-${stamp}`]) registerCleanup(async () => {
    const removed = await page.request.delete(`${base}/secrets/${encodeURIComponent(owned)}`, { timeout: 5_000 });
    expect([204, 404], `Cleanup for owned secret ${owned}`).toContain(removed.status());
  });
  await page.goto("/security/secrets");
  const create = page.getByRole("button", { name: "Create secret", exact: true }).first();
  await expect(create).toBeEnabled(); await create.press("Enter");
  const editor = page.getByRole("region", { name: "Create secret", exact: true });
  const technicalName = editor.getByLabel("Technical name", { exact: true });
  await expect(technicalName).toBeFocused();
  await expect(page.getByRole("table", { name: "Secrets", exact: true })).toHaveCount(0);
  await expect(editor.getByRole("button", { name: /^More options/ })).toHaveCount(0);
  await capture(page, info, "secret-section");

  // Create from another section must reveal the first missing field, without a request.
  await section(editor, "Details");
  await editor.getByLabel("Display name", { exact: true }).fill(displayName);
  await editor.getByRole("button", { name: "Create", exact: true }).click();
  await expect(technicalName).toBeFocused();
  await expect(editor.getByRole("alert")).toContainText("Enter a technical name.");
  await capture(page, info, "first-invalid-field");
  await technicalName.fill(` ${name} `); await technicalName.press("Tab");
  await expect(technicalName).toHaveValue(name);

  await section(editor, "Storage & lifetime");
  const store = editor.getByRole("combobox", { name: "Store", exact: true });
  if (await store.count()) await store.selectOption("encrypted");
  await editor.getByRole("combobox", { name: "Scope", exact: true }).selectOption({ label: "Custom…" });
  await editor.getByLabel("Custom scope value", { exact: true }).fill(customScope);
  await expect(editor).toContainText("Scope does not grant access.");
  await editor.getByLabel(/Expires/).fill("2030-01-15T10:30");
  await capture(page, info, "storage-section");
  await editor.getByRole("button", { name: "Create", exact: true }).click();
  const material = editor.getByLabel("Value", { exact: true });
  await expect(material).toBeFocused(); await material.fill(value);
  await expect(material).toHaveAttribute("type", "password");

  const first = await section(editor, "Secret");
  await first.focus(); await page.keyboard.press("ArrowRight");
  const details = editor.getByRole("tab", { name: /^Details/ });
  await expect(details).toBeFocused(); await expect(details).toHaveAttribute("aria-selected", "true");
  await expect(editor.getByLabel("Display name", { exact: true })).toHaveValue(displayName);
  await editor.getByLabel("Description", { exact: true }).fill(description);
  await editor.getByLabel("Tags", { exact: true }).fill("acceptance, tabbed");
  await capture(page, info, "details-section");
  await section(editor, "Secret"); await expect(material).toHaveValue(value);
  await expect(editor.getByRole("tablist")).not.toContainText(value);
  await editor.getByRole("button", { name: "Create", exact: true }).click();
  await expect(editor).toHaveCount(0);
  const row = page.getByRole("row").filter({ hasText: name });
  await expect(row.getByRole("button", { name: `Edit ${name}`, exact: true })).toBeFocused();
  await page.reload(); await expect(row).toBeVisible();
  await capture(page, info, "persisted-list");

  await row.getByRole("link", { name: displayName, exact: true }).click();
  const edit = page.getByRole("region", { name: "Edit secret", exact: true });
  await expect(page.getByRole("table", { name: "Secrets", exact: true })).toHaveCount(0);
  await expect(edit.getByLabel("Description", { exact: true })).toHaveValue(description);
  await expect(edit).toContainText("acceptance, tabbed");
  await section(edit, "Storage & lifetime");
  await expect(edit.getByRole("textbox", { name: "Scope", exact: true })).toHaveValue(customScope);
  await expect(edit.getByRole("textbox", { name: "Scope", exact: true })).toHaveAttribute("readonly", "");
  await section(edit, "Secret"); await expect(edit).toContainText("The existing value stays hidden.");
  await expect(edit.locator("input[type=password]")).toHaveCount(0);
  await expect(edit).not.toContainText(value);
  await section(edit, "Details");
  await edit.getByLabel("Display name", { exact: true }).fill(`${displayName} updated`);
  await edit.getByLabel("Description", { exact: true }).fill("");
  await edit.getByRole("button", { name: "Save changes", exact: true }).click();
  await expect(edit).toHaveCount(0);
  await expect(row.getByRole("button", { name: `Edit ${name}`, exact: true })).toBeFocused();
  await page.reload(); await row.getByRole("button", { name: `Edit ${name}`, exact: true }).click();
  await expect(edit.getByLabel("Display name", { exact: true })).toHaveValue(`${displayName} updated`);
  await expect(edit.getByLabel("Description", { exact: true })).toHaveValue("");
  await capture(page, info, "persisted-metadata");
  const viewport = page.viewportSize()!;
  await page.setViewportSize({ width: 390, height: 844 });
  await capture(page, info, "metadata-390");
  await section(edit, "Storage & lifetime"); await capture(page, info, "storage-390");
  await page.setViewportSize(viewport);
  await edit.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(row.getByRole("button", { name: `Edit ${name}`, exact: true })).toBeFocused();

  // Exercise the other advertised store without resolving or exposing any existing material.
  await create.click(); await technicalName.fill(`Configuration-${stamp}`);
  await section(editor, "Storage & lifetime");
  await editor.getByRole("combobox", { name: "Store", exact: true }).selectOption("configuration");
  await section(editor, "Secret");
  const source = editor.getByLabel("Configuration key", { exact: true });
  await source.fill("Logging:LogLevel:Default");
  await expect(editor.getByLabel("Value", { exact: true })).toHaveCount(0);
  await capture(page, info, "configuration-reference");
  await editor.getByRole("button", { name: "Create", exact: true }).click();
  await expect(editor).toHaveCount(0);
  const configurationRow = page.getByRole("row").filter({ hasText: `Configuration-${stamp}` });
  await configurationRow.getByRole("button", { name: `Edit Configuration-${stamp}`, exact: true }).click();
  await expect(edit).not.toContainText("Logging:LogLevel:Default");
  await section(edit, "Storage & lifetime");
  await expect(edit.locator(".secrets-metadata-grid div").filter({ has: page.locator("dt", { hasText: /^Store$/ }) }).locator("dd")).toHaveText(/Configuration/i);
  await capture(page, info, "configuration-metadata");
  await edit.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(configurationRow.getByRole("button", { name: `Edit Configuration-${stamp}`, exact: true })).toBeFocused();
}
