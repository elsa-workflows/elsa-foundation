import { expect, test } from "@playwright/test";
import { builtInThemeDefinitions, cloneThemeDefinition } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";

const directory = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "directory-theme", name: "Directory Theme", source: "custom", enabled: true, published: true };
const managed = { ...directory, id: "managed-theme", name: "Managed Theme" };

for (const themesMode of ["BuiltInsOnly", "DirectoryOnly", "AddToBuiltIns"] as const) {
  test(`${themesMode} controls the actual theme picker`, async ({ page }) => {
    await page.route("**/_elsa/theme-store", route => route.fulfill({ json: {
      themesMode,
      themes: [directory, managed, { ...directory, id: "unpublished-theme", published: false }],
      directoryThemeIds: [directory.id, "unpublished-theme"],
      defaultThemeId: "classic",
      assets: [],
      disabledBuiltInThemeIds: []
    } }));
    await page.goto("/theme-source-modes.html");
    await expect(page.getByTestId("source-mode")).toHaveText(themesMode);
    const expected = themesMode === "BuiltInsOnly" ? builtInThemeDefinitions.map(theme => theme.id)
      : themesMode === "DirectoryOnly" ? [directory.id]
      : [...builtInThemeDefinitions.map(theme => theme.id), directory.id, managed.id];
    await expect(page.getByTestId("selectable-ids")).toHaveText(JSON.stringify(expected));
    await expect(page.getByTestId("current-theme")).toHaveText(themesMode === "DirectoryOnly" ? directory.id : "classic");
    await page.getByRole("button", { name: "Select theme", exact: true }).click();
    await expect(page.getByRole("menuitem", { name: "Directory Theme", exact: true })).toHaveCount(themesMode === "BuiltInsOnly" ? 0 : 1);
    await expect(page.getByRole("menuitem", { name: "Managed Theme", exact: true })).toHaveCount(themesMode === "AddToBuiltIns" ? 1 : 0);
    await expect(page.getByRole("menuitem", { name: "Classic", exact: true })).toHaveCount(themesMode === "DirectoryOnly" ? 0 : 1);
  });
}

test("empty DirectoryOnly explains unavailable appearance selection without losing the other controls", async ({ page }) => {
  await page.route("**/_elsa/theme-store", route => route.fulfill({ json: {
    themesMode: "DirectoryOnly", themes: [], directoryThemeIds: [], defaultThemeId: "", assets: [], disabledBuiltInThemeIds: []
  } }));
  await page.goto("/theme-source-modes.html?view=appearance");
  await expect(page.getByTestId("source-mode")).toHaveText("DirectoryOnly");
  await expect(page.getByTestId("selectable-ids")).toHaveText("[]");
  await expect(page.getByText("No themes are available for selection.")).toBeVisible();
  await expect(page.getByRole("radiogroup", { name: "Theme", exact: true })).toHaveCount(0);
  const appearance = page.getByRole("region", { name: "Appearance" });
  await expect(appearance.getByRole("radiogroup", { name: "Colour mode" })).toBeVisible();
  await expect(appearance.getByRole("radiogroup", { name: "Navigation" })).toBeVisible();
});

test("Theme Builder keeps a real inventory selection after deleting a managed theme with an empty default", async ({ page }) => {
  let present = true;
  const response = () => ({ themesMode: "DirectoryOnly", themes: present ? [managed] : [], directoryThemeIds: [], defaultThemeId: "", assets: [], disabledBuiltInThemeIds: [] });
  await page.route("**/_elsa/theme-store", route => route.fulfill({ json: response() }));
  await page.route("**/_elsa/theme-store/themes/managed-theme", route => {
    expect(route.request().method()).toBe("DELETE");
    present = false;
    return route.fulfill({ json: response() });
  });
  await page.goto("/theme-source-modes.html?view=builder");
  await expect(page.getByTestId("selectable-ids")).toHaveText("[]");
  await page.locator(".theme-builder-list button").filter({ hasText: "Managed Theme" }).click();
  await expect(page.locator(".theme-builder-detail h3")).toHaveText("Managed Theme");
  await page.getByRole("button", { name: "Delete", exact: true }).click();
  await expect(page.getByText("Theme deleted.", { exact: true })).toBeVisible();
  await expect(page.locator(".theme-builder-detail h3")).toHaveText("Classic");
  await expect(page.locator(".theme-builder-list button.active")).toHaveCount(1);
  await expect(page.getByTestId("selectable-ids")).toHaveText("[]");
  await expect(page.getByText(/default not set/)).toBeVisible();
});
