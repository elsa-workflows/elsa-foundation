import { expect, type Locator, type Page, type Response, type TestInfo } from "@playwright/test";
import { activityDataSupport, attachActivityDataJson, type Capture } from "./activityDataJourneySupport";

const customerAlias = "Elsa.Workbench.ActivityDataSelectionProof.Customer";
const declaredNote = "the stored value may differ";

/**
 * A variable declared as a list of a host type completes the item type's members after an index (#5708):
 * `variables.orders[0].` lists the fixture Customer's members under the activity-value profile, with the declared-type
 * note. Insert data still offers the list as a whole value.
 */
export async function typedCollectionCompletionJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const tooling: { path: string; request: unknown; status: number; response: unknown }[] = [], pending: Promise<void>[] = [];
  const presses: { prefix: string; presses: number }[] = [];
  const layout: unknown[] = [];
  const isTooling = (response: Response) => /\/design\/workflows\/expression-tooling\/(completions|hover)$/.test(new URL(response.url()).pathname);
  page.on("response", response => {
    if (!isTooling(response)) return;
    pending.push((async () => {
      tooling.push({ path: new URL(response.url()).pathname, request: response.request().postDataJSON(), status: response.status(),
        response: await response.json().catch(() => null) });
    })());
  });
  registerCleanup(async () => {
    await Promise.all(pending);
    await attachActivityDataJson(info, "typed-collection-proof.json", { sourceSha: process.env.UX_SOURCE_SHA, tooling, presses, layout });
  });

  const orders = { referenceKey: "orders", name: "orders", type: { alias: customerAlias, collectionKind: "List" }, storageDriverType: null };
  const consumer = s.line(); consumer.inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: "" } }];
  const created = await s.seed("typed collection completion", s.root([consumer]), [], [orders]);
  // Open on the backend this session is signed in to.
  const backend = new URL(page.url()).searchParams.get("backend");
  await page.goto(`/workflows/definitions?definition=${created.definition.id}${backend ? `&backend=${encodeURIComponent(backend)}` : ""}`);
  await expect(page.locator('.wf-sequence-step[data-id="consumer"]')).toBeAttached();
  await s.select();
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });

  const source = async () => { const stored = (await s.stored(created))?.value; return typeof stored === "string" ? stored : (stored as { source: string }).source; };
  const settled = async (expected: string) => {
    await expect.poll(source).toBe(expected);
    await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  };
  const list = () => page.locator(".cm-tooltip-autocomplete");
  const option = (label: string) => list().getByRole("option").filter({ has: page.locator(".cm-completionLabel", { hasText: new RegExp(`^${label}$`) }) });
  // Right after autosave an explicit request can be dropped before it is sent (#5760); press again, at most twice.
  async function complete(prefix: string) {
    for (let press = 1; press <= 3; press++) {
      const answered = page.waitForResponse(response => isTooling(response) && response.url().endsWith("/completions") &&
        JSON.stringify(response.request().postDataJSON()).includes(prefix), { timeout: 10_000 }).catch(() => null);
      await page.keyboard.press("Control+Space");
      const response = await answered;
      if (!response) continue;
      expect(response.ok()).toBe(true);
      presses.push({ prefix, presses: press });
      return;
    }
    throw new Error(`Ctrl+Space sent no completion request for ${prefix} after three presses`);
  }

  // Indexing the list completes the item type's members, named as the activity-value profile writes them.
  await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  const editor = page.getByRole("textbox", { name: "JavaScript expression", exact: true });
  await expect(editor).toBeFocused();
  await page.keyboard.insertText("variables.orders[0].");
  await settled("variables.orders[0].");
  // After autosave the compact editor still shows the caret, so a suggestion list can open beside it.
  await expect.poll(() => editor.evaluate(content => {
    const scroller = content.closest(".cm-scroller")!, line = content.querySelector(".cm-line")!;
    const range = document.createRange(); range.selectNodeContents(line);
    const end = range.getBoundingClientRect().right, visible = scroller.getBoundingClientRect();
    return end > visible.left && end <= visible.right + 1;
  }), { message: "The end of the typed path is inside the compact editor's visible area" }).toBe(true);
  await complete("variables.orders[0].");
  await expect(list()).toBeVisible();
  layout.push(await editor.evaluate(content => {
    const rect = (node: Element) => { const r = node.getBoundingClientRect(); return [Math.round(r.left), Math.round(r.top), Math.round(r.right), Math.round(r.bottom)]; };
    const chain: string[] = [];
    for (let node: Element | null = content; node && chain.length < 12; node = node.parentElement) {
      const style = getComputedStyle(node);
      chain.push(`${node.tagName.toLowerCase()}.${[...node.classList].join(".")} rect=${rect(node)} client=${node.clientWidth} scroll=${node.scrollWidth} left=${node.scrollLeft} ox=${style.overflowX} display=${style.display} minw=${style.minWidth}`);
    }
    const popup = document.querySelector(".cm-tooltip-autocomplete"), cursor = document.querySelector(".cm-cursor");
    return { viewport: innerWidth, popup: popup && rect(popup), cursor: cursor && rect(cursor), chain };
  }));
  await expect(list().getByRole("option")).toHaveCount(3);
  for (const name of ["customerId", "display_name", "nickname"]) {
    await expect(option(name)).toHaveCount(1);
    await expect(option(name).locator(".cm-completionDetail")).toHaveText("String");
  }
  await expect(page.locator(".cm-completionInfo")).toContainText(declaredNote);
  await capture(page, info, "collection-item-completion");

  await option("display_name").click();
  await expect(list()).toHaveCount(0);
  await expect(editor).toHaveText("variables.orders[0].display_name");
  await settled("variables.orders[0].display_name");
  await expectNoOverscroll(editor, "Applied item member");
  await capture(page, info, "collection-item-applied");

  // Hover on the item member carries the declared-type note.
  const box = await editor.evaluate(content => {
    const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const at = node.textContent!.lastIndexOf("display_name");
      if (at < 0) continue;
      const range = document.createRange(); range.setStart(node, at + 2); range.setEnd(node, at + 8);
      const rect = range.getBoundingClientRect(); return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 };
    }
    return null;
  });
  expect(box, "The applied member is rendered").toBeTruthy();
  const hovered = page.waitForResponse(response => isTooling(response) && response.url().endsWith("/hover"));
  await page.mouse.move(box!.x, box!.y);
  expect((await hovered).ok()).toBe(true);
  const hover = page.locator(".cm-tooltip-hover .studio-code-editor-hover");
  await expect(hover).toContainText("display_name");
  await expect(hover).toContainText(declaredNote);
  await capture(page, info, "collection-item-hover");
  await page.mouse.move(0, 0);
  await expect(hover).toHaveCount(0);

  // Insert data still offers the list as a whole value: choosing it inserts the variable, with no member view.
  await editor.press("ControlOrMeta+End");
  await page.keyboard.insertText(" + ");
  const beforeInsert = "variables.orders[0].display_name + ";
  await settled(beforeInsert);
  await page.getByRole("button", { name: "Insert data", exact: true }).press("Enter");
  const chooser = page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const variable = chooser.getByRole("listbox", { name: "Variables", exact: true }).getByRole("option").filter({ hasText: /^orders/ });
  await expect(variable).toHaveCount(1);
  await variable.press("Enter");
  await expect(chooser).toHaveCount(0);
  type Stored = { source: string; parameters: Record<string, { kind: string; variableKey?: string }> };
  const inserted = async () => {
    const value = (await s.stored(created))?.value;
    if (typeof value === "string") return null;
    const definition = value as Stored;
    const alias = Object.entries(definition.parameters ?? {}).find(([, parameter]) => parameter.kind === "Variable" && parameter.variableKey === "orders")?.[0];
    return alias && definition.source === `${beforeInsert}args.${alias}` ? alias : null;
  };
  await expect.poll(inserted, { message: "The whole orders list is inserted and saved as a parameter" }).not.toBeNull();
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await capture(page, info, "collection-member-picker");
}

/** A field scrolled sideways shows its text ending near its right edge, not a fragment followed by blank space. */
async function expectNoOverscroll(editor: Locator, what: string) {
  await expect.poll(() => editor.evaluate(content => {
    const scroller = content.closest(".cm-scroller")!, line = content.querySelector(".cm-line")!;
    if (scroller.scrollLeft === 0) return true;
    const range = document.createRange(); range.selectNodeContents(line);
    const end = range.getBoundingClientRect().right, visible = scroller.getBoundingClientRect();
    return end <= visible.right + 1 && end >= visible.right - 24;
  }), { message: `${what}: a scrolled field ends its text at its right edge` }).toBe(true);
}
