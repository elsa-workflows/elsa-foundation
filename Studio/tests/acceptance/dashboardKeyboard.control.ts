import { expect, test } from "@playwright/test";
import * as ui from "./dashboardKeyboard";

test("real keyboard driver edits native controls and rejects pointer input", async ({ page }) => {
  await page.setContent('<label>Name<input aria-label="Name"></label><select aria-label="Size"><option disabled value="">Choose</option><option value="medium">Medium</option><option value="wide">Wide</option></select><button>Save</button><output></output>');
  await page.getByRole("button", { name: "Save" }).evaluate(element => element.addEventListener("click", () => { document.querySelector("output")!.textContent = "Saved"; }));
  await ui.startKeyboardEvidence(page);
  await ui.fill(page.getByRole("textbox", { name: "Name" }), "Keyboard question");
  await ui.selectOption(page.getByRole("combobox", { name: "Size" }), "wide");
  await ui.click(page.getByRole("button", { name: "Save" }));
  await expect(page.locator("output")).toHaveText("Saved");
  expect((await ui.keyboardEvidence(page)).pointerEvents).toEqual([]);
  await page.getByRole("button", { name: "Save" }).click();
  await expect(ui.keyboardEvidence(page)).rejects.toThrow(/pointer input/);
});
