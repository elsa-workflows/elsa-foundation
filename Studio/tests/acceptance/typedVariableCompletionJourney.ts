import { expect, type Locator, type Page, type Response, type TestInfo } from "@playwright/test";
import { activityDataSupport, attachActivityDataJson, type Capture } from "./activityDataJourneySupport";

const connectionAlias = "Elsa.Activities.Runtime.Core.Models.ConnectionReference";
const declaredNote = "the stored value may differ";

/**
 * A typed variable's declared members complete after `variables.<name>.` in the compact and expanded JavaScript editors
 * (#5677). The host type is the Workbench's Connection reference. An Object variable gets no invented members, hover shows
 * the member with its type and the declared-type note, and the Activity Data chooser still offers the typed variable as a whole value.
 */
export async function typedVariableCompletionJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const tooling: { path: string; request: unknown; status: number; response: unknown }[] = [], pending: Promise<void>[] = [];
  const presses: { prefix: string; presses: number }[] = [];
  const stacking: { what: string; hits: string[]; chain: string[] }[] = [];
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
    await attachActivityDataJson(info, "typed-completion-proof.json", { sourceSha: process.env.UX_SOURCE_SHA, tooling, presses, stacking });
  });

  const typed = { referenceKey: "conn", name: "conn", type: { alias: connectionAlias, collectionKind: "Single" }, storageDriverType: null };
  const untyped = { referenceKey: "plain", name: "plain", type: { alias: "Object", collectionKind: "Single" }, storageDriverType: null };
  const consumer = s.line(); consumer.inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: "" } }];
  const created = await s.seed("typed completion", s.root([consumer]), [], [typed, untyped]);
  // Open on the backend this session is signed in to.
  const backend = new URL(page.url()).searchParams.get("backend");
  await page.goto(`/workflows/definitions?definition=${created.definition.id}${backend ? `&backend=${encodeURIComponent(backend)}` : ""}`);
  await expect(page.locator('.wf-sequence-step[data-id="consumer"]')).toBeAttached();
  await s.select();
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });

  const source = async () => { const stored = (await s.stored(created))?.value; return typeof stored === "string" ? stored : (stored as { source: string }).source; };
  const list = () => page.locator(".cm-tooltip-autocomplete");
  const options = () => list().getByRole("option");
  // Right after autosave refreshes the authoring context, an explicit request can be dropped before it is sent (#5760).
  // Press again, at most twice, and keep the count in the proof.
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
  async function expectConnectionMembers() {
    await expect(list()).toBeVisible();
    await expect(options()).toHaveCount(2);
    for (const name of ["connection", "type"]) {
      const option = options().filter({ has: page.locator(".cm-completionLabel", { hasText: new RegExp(`^${name}$`) }) });
      await expect(option).toHaveCount(1);
      await expect(option.locator(".cm-completionDetail")).toHaveText("String");
    }
    await expect(page.locator(".cm-completionInfo")).toContainText(declaredNote);
    await expectReadable(page.locator(".cm-completionInfo"), "Declared-type note");
    await expectUncovered(page.locator(".cm-completionInfo"), "Declared-type note");
    // The note box belongs to the completion popup; at narrow widths CodeMirror places it over the following options.
    await expectUncovered(list(), "Completion popup");
    // The note never hides an option: each option's centre hits the option itself.
    const hidden = await list().getByRole("option").evaluateAll(items => items.flatMap(item => {
      const rect = item.getBoundingClientRect(), hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
      return hit && item.contains(hit) ? [] : [`${item.textContent} under ${hit?.className}`];
    }));
    expect(hidden, "Every completion option stays visible beside the note").toEqual([]);
  }
  /** Nothing on the page paints over the popup: every sampled point inside it hits the popup itself. */
  async function expectUncovered(box: ReturnType<Page["locator"]>, what: string) {
    const covered = await box.evaluate(element => {
      const rect = element.getBoundingClientRect(), hits: string[] = [];
      const xs = [rect.left + 3, rect.left + rect.width / 4, rect.left + rect.width / 2, rect.right - rect.width / 4, rect.right - 3];
      const ys = [rect.top + 3, rect.top + rect.height / 2, rect.bottom - 3];
      for (const x of xs) for (const y of ys) {
        const hit = document.elementFromPoint(x, y);
        // A point outside the viewport hits nothing, so the user cannot see that part of the popup either.
        if (!hit || !element.contains(hit)) hits.push(`${Math.round(x)},${Math.round(y)} ${hit ? `${hit.tagName.toLowerCase()}.${[...hit.classList].join(".")}` : "off-screen"}`);
      }
      const chain: string[] = [];
      for (let node: Element | null = element; node; node = node.parentElement) {
        const style = getComputedStyle(node);
        const traits = [["z", style.zIndex], ["pos", style.position], ["transform", style.transform], ["isolation", style.isolation],
          ["contain", style.contain], ["opacity", style.opacity], ["filter", style.filter], ["willChange", style.willChange]]
          .filter(([key, value]) => !["auto", "none", "static", "normal", "1"].includes(value) || key === "z" && value !== "auto");
        if (traits.length) chain.push(`${node.tagName.toLowerCase()}.${[...node.classList].join(".")} ${traits.map(t => t.join("=")).join(" ")}`);
      }
      return { hits, chain };
    });
    stacking.push({ what, ...covered });
    expect(covered.hits, `${what} is not covered by other page content`).toEqual([]);
  }
  /** The long dotted alias wraps inside its box, and the box stays inside the viewport. */
  async function expectReadable(box: ReturnType<Page["locator"]>, what: string) {
    const fit = await box.evaluate(element => {
      const rect = element.getBoundingClientRect(), overflows = [element, ...element.querySelectorAll("*")]
        .filter(node => node.scrollWidth > node.clientWidth + 1 && getComputedStyle(node).overflowX !== "auto").length;
      return { left: rect.left, right: rect.right, width: innerWidth, overflows };
    });
    expect(fit.overflows, `${what} has no clipped text`).toBe(0);
    expect(fit.left, `${what} starts inside the viewport`).toBeGreaterThanOrEqual(0);
    // CodeMirror sizes a narrow info box to end at the viewport edge, measured inside the list's 1px border.
    expect(fit.right, `${what} ends inside the viewport`).toBeLessThanOrEqual(fit.width + 1);
  }

  // Compact editor: the typed variable's members, each with its type and the declared-type note.
  await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  const compact = page.getByRole("textbox", { name: "JavaScript expression", exact: true });
  await expect(compact).toBeFocused();
  await page.keyboard.insertText("variables.conn.");
  await expect.poll(source).toBe("variables.conn.");
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await complete("variables.conn.");
  await expectConnectionMembers();
  await capture(page, info, "typed-compact-completion");

  // Applying a member inserts only its name and is saved like typed text.
  await options().filter({ has: page.locator(".cm-completionLabel", { hasText: /^connection$/ }) }).click();
  await expect(list()).toHaveCount(0);
  await expect(compact).toHaveText("variables.conn.connection");
  await expect.poll(source).toBe("variables.conn.connection");
  await expectNoOverscroll(compact, "Applied member");
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await capture(page, info, "typed-member-applied");

  // Hover on the applied member shows its type and the declared-type note.
  const box = await compact.evaluate(content => {
    const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const at = node.textContent!.lastIndexOf("connection");
      if (at < 0) continue;
      const range = document.createRange(); range.setStart(node, at + 2); range.setEnd(node, at + 6);
      const rect = range.getBoundingClientRect(); return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 };
    }
    return null;
  });
  expect(box, "The applied member is rendered").toBeTruthy();
  const hovered = page.waitForResponse(response => isTooling(response) && response.url().endsWith("/hover"));
  await page.mouse.move(box!.x, box!.y);
  expect((await hovered).ok()).toBe(true);
  const hover = page.locator(".cm-tooltip-hover .studio-code-editor-hover");
  await expect(hover).toContainText("connection: String");
  await expect(hover).toContainText(declaredNote);
  await expectReadable(page.locator(".cm-tooltip-hover"), "Hover");
  await expectUncovered(page.locator(".cm-tooltip-hover"), "Hover");
  await capture(page, info, "typed-member-hover");
  await page.mouse.move(0, 0);
  await expect(hover).toHaveCount(0);

  // An Object variable has no declared members, so the explicit request lists nothing.
  await compact.press("ControlOrMeta+End");
  await page.keyboard.insertText(" + variables.plain.");
  await expect.poll(source).toBe("variables.conn.connection + variables.plain.");
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await complete("variables.plain.");
  await page.waitForTimeout(250); // CodeMirror applies a settled answer on its next update.
  await expect(list()).toHaveCount(0);
  await capture(page, info, "untyped-no-members");

  // Expanded editor: the same members for the typed variable.
  await page.getByRole("button", { name: "Open expanded Text editor", exact: true }).click();
  const expanded = page.getByRole("dialog", { name: "Text", exact: true });
  const large = expanded.locator(".cm-content");
  await expect(large).toBeFocused();
  await large.press("ControlOrMeta+End");
  await page.keyboard.insertText(" + variables.conn.");
  const expandedSource = "variables.conn.connection + variables.plain. + variables.conn.";
  await expect.poll(source).toBe(expandedSource);
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await complete(expandedSource);
  await expectConnectionMembers();
  // A long line scrolls inside the editor; the dialog's own header and controls stay in place.
  const sideways = await expanded.evaluate(dialog => [dialog, ...dialog.querySelectorAll("*")]
    .filter(node => node.scrollLeft > 0 && !node.classList.contains("cm-scroller"))
    .map(node => `${node.tagName.toLowerCase()}.${[...node.classList].join(".")} scrollLeft=${node.scrollLeft}`));
  expect(sideways, "Only the editor scrolls sideways").toEqual([]);
  await capture(page, info, "typed-expanded-completion");
  await page.keyboard.press("Escape");
  await expect(list()).toHaveCount(0);

  // The Activity Data chooser offers the typed variable as a whole value: choosing it inserts the variable, no members.
  await page.keyboard.insertText(" + ");
  const beforeInsert = `${expandedSource} + `;
  await expect.poll(source).toBe(beforeInsert);
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await expanded.getByRole("button", { name: "Insert data", exact: true }).press("Enter");
  const chooser = page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const variables = chooser.getByRole("listbox", { name: "Variables", exact: true });
  const variable = variables.getByRole("option", { name: `conn ${connectionAlias}`, exact: true });
  await expect(variable).toBeVisible();
  await expect(variables.getByRole("option", { name: "plain Object", exact: true })).toBeVisible();
  await capture(page, info, "chooser-typed-variable");
  await variable.press("Enter");
  await expect(chooser).toHaveCount(0);
  await expect(large).toBeFocused();
  type Stored = { source: string; parameters: Record<string, { kind: string; variableKey?: string }> };
  const inserted = async () => {
    const value = (await s.stored(created))?.value;
    if (typeof value === "string") return null;
    const definition = value as Stored;
    const alias = Object.entries(definition.parameters ?? {}).find(([, parameter]) => parameter.kind === "Variable" && parameter.variableKey === "conn")?.[0];
    return alias && definition.source === `${beforeInsert}args.${alias}` ? alias : null;
  };
  await expect.poll(inserted, { message: "The whole conn variable is inserted and saved as a parameter" }).not.toBeNull();
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await expect(large).toHaveText(`${beforeInsert}args.${await inserted()}`);
  // The editor scrolls to the inserted reference, so its end is on screen at narrow widths too.
  await expect.poll(() => expanded.locator(".cm-scroller").evaluate(scroller => {
    const lines = scroller.querySelectorAll(".cm-line"), line = lines[lines.length - 1];
    const range = document.createRange(); range.selectNodeContents(line);
    const end = range.getBoundingClientRect().right, visible = scroller.getBoundingClientRect();
    return end > visible.left && end <= visible.right + 1;
  }), { message: "The end of the inserted reference is inside the editor's visible area" }).toBe(true);
  await capture(page, info, "chooser-typed-variable-whole");
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
