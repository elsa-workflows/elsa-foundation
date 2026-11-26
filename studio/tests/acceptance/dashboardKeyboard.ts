import { expect, type Locator, type Page } from "@playwright/test";

export const keyboardOnly = process.env.UX_DASHBOARD_KEYBOARD === "1";
type Evidence = { tabs: number; focusedTargets: number; keys: Record<string, number>; pointerEvents: string[] };
const records = new WeakMap<Page, Evidence>();

export async function startKeyboardEvidence(page: Page) {
  if (!keyboardOnly || records.has(page)) return;
  const record: Evidence = { tabs: 0, focusedTargets: 0, keys: {}, pointerEvents: [] }; records.set(page, record);
  const binding = "__dashboardKeyboardPointerEvidence";
  await page.exposeBinding(binding, (_source, kind: string) => { record.pointerEvents.push(kind); });
  const install = (name: string) => {
    for (const kind of ["pointerdown", "mousedown", "wheel"])
      document.addEventListener(kind, () => { void (window as unknown as Record<string, (value: string) => Promise<void>>)[name](kind); }, { capture: true });
  };
  await page.addInitScript(install, binding); await page.evaluate(install, binding);
}

function key(page: Page, name: string) {
  const record = records.get(page); if (record) record.keys[name] = (record.keys[name] ?? 0) + 1;
}

async function tabTo(target: Locator) {
  const page = target.page(); await expect(target).toBeVisible(); await expect(target).toBeEnabled();
  for (let count = 0; count < 400; count++) {
    if (await target.evaluate(element => element === document.activeElement)) {
      const record = records.get(page); if (record) record.focusedTargets++; return;
    }
    key(page, "Tab"); const record = records.get(page); if (record) record.tabs++;
    await page.keyboard.press("Tab");
  }
  throw new Error("The real Tab sequence could not reach the requested control");
}

export async function click(target: Locator) {
  if (!keyboardOnly) return target.click();
  await tabTo(target); key(target.page(), "Enter"); await target.press("Enter");
}

export async function press(target: Locator, name: string) {
  if (!keyboardOnly) return target.press(name);
  if (name === "Escape") { key(target.page(), name); await target.page().keyboard.press(name); return; }
  await tabTo(target); key(target.page(), name); await target.press(name);
}

export async function fill(target: Locator, value: string) {
  if (!keyboardOnly) return target.fill(value);
  await tabTo(target); key(target.page(), "ControlOrMeta+A"); await target.press("ControlOrMeta+A");
  key(target.page(), "text input"); await target.page().keyboard.insertText(value); await expect(target).toHaveValue(value);
}

export async function selectOption(target: Locator, value: string) {
  if (!keyboardOnly) { await target.selectOption(value); return; }
  await tabTo(target);
  const options = await target.locator("option").evaluateAll(nodes => nodes.map(node => ({ value: (node as HTMLOptionElement).value, label: (node as HTMLOptionElement).label, disabled: (node as HTMLOptionElement).disabled })));
  const chosen = options.find(option => !option.disabled && option.value === value);
  if (!chosen) throw new Error("The requested keyboard-selectable option is not declared");
  // Native Chromium select uses platform type-ahead. Arrow navigation may leave its
  // operating-system popup open in headless macOS, without committing a DOM value.
  key(target.page(), "option type-ahead"); await target.page().keyboard.type(chosen.label);
  key(target.page(), "Enter"); await target.press("Enter"); await expect(target).toHaveValue(value);
}

export async function keyboardEvidence(page: Page) {
  const record = records.get(page); if (!record) throw new Error("Keyboard evidence was not started after identity setup");
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => resolve())));
  expect(record.pointerEvents, "Application interactions must not use pointer input").toEqual([]);
  expect(record.tabs).toBeGreaterThan(0); expect(record.focusedTargets).toBeGreaterThan(0); expect(record.keys.Enter ?? 0).toBeGreaterThan(0);
  return { ...record, scope: "Application controls after ordinary identity setup; browser navigation/reload and normal API fixtures are setup or persistence observations", directFocusCalls: 0 };
}
