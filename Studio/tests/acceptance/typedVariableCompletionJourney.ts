import { expect, type Page, type Response, type TestInfo } from "@playwright/test";
import { activityDataSupport, attachActivityDataJson, type Capture } from "./activityDataJourneySupport";

const connectionAlias = "Elsa.Activities.Runtime.Core.Models.ConnectionReference";
const declaredNote = "the stored value may differ";

/**
 * A typed variable's declared members complete after `variables.<name>.` in the compact and expanded JavaScript editors
 * (#5677). The host type is the Workbench's Connection reference. An Object variable gets no invented members, hover shows
 * the member with the declared-type note, and the Activity Data chooser still offers the typed variable as a whole value.
 */
export async function typedVariableCompletionJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const tooling: { path: string; request: unknown; status: number; response: unknown }[] = [], pending: Promise<void>[] = [];
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
    await attachActivityDataJson(info, "typed-completion-proof.json", { sourceSha: process.env.UX_SOURCE_SHA, tooling });
  });

  const typed = { referenceKey: "conn", name: "conn", type: { alias: connectionAlias, collectionKind: "Single" }, storageDriverType: null };
  const untyped = { referenceKey: "plain", name: "plain", type: { alias: "Object", collectionKind: "Single" }, storageDriverType: null };
  const consumer = s.line(); consumer.inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: "" } }];
  const created = await s.seed("typed completion", s.root([consumer]), [], [typed, untyped]);
  // Open on the backend this session is signed in to. activityDataSupport.open still links the retired "legacy" profile id.
  const backend = new URL(page.url()).searchParams.get("backend");
  await page.goto(`/workflows/definitions?definition=${created.definition.id}${backend ? `&backend=${encodeURIComponent(backend)}` : ""}`);
  await expect(page.locator('.wf-sequence-step[data-id="consumer"]')).toBeAttached();
  await s.select();
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });

  const source = async () => { const stored = (await s.stored(created))?.value; return typeof stored === "string" ? stored : (stored as { source: string }).source; };
  const list = () => page.locator(".cm-tooltip-autocomplete");
  const options = () => list().getByRole("option");
  async function complete(prefix: string) {
    const answered = page.waitForResponse(response => isTooling(response) && response.url().endsWith("/completions") &&
      JSON.stringify(response.request().postDataJSON()).includes(prefix));
    await page.keyboard.press("Control+Space");
    expect((await answered).ok()).toBe(true);
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
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  await capture(page, info, "typed-member-applied");

  // Hover on the applied member shows the declared-type note.
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
  await expect(hover).toContainText(declaredNote);
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
  await capture(page, info, "typed-expanded-completion");
  await page.keyboard.press("Escape");
  await expect(list()).toHaveCount(0);

  // The Activity Data chooser keeps offering the typed variable as a whole value with no declared members.
  await expanded.getByRole("button", { name: "Insert data", exact: true }).press("Enter");
  const chooser = page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const variables = chooser.getByRole("listbox", { name: "Variables", exact: true });
  const variable = variables.getByRole("option", { name: `conn ${connectionAlias}`, exact: true });
  await expect(variable).toBeVisible();
  await expect(variables.getByRole("option", { name: "plain Object", exact: true })).toBeVisible();
  await capture(page, info, "chooser-typed-variable");
  await variable.press("Enter");
  // Declared members are advisory for expression completion only; the chooser lists none for the variable.
  await expect(chooser.getByRole("listbox", { name: "Members", exact: true }).getByRole("option")).toHaveCount(0);
  await capture(page, info, "chooser-typed-variable-whole");
  for (let attempt = 0; attempt < 3 && await chooser.count(); attempt++) await page.keyboard.press("Escape");
  await expect(chooser).toHaveCount(0);
  await expect.poll(source).toBe(expandedSource);
}
