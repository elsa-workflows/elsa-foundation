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
