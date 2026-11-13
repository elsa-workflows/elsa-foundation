import { chromium, expect, type TestInfo, type Page } from "@playwright/test";
import { attachDashboardJson, dashboardBackend, signInDashboardReader, tag, type Capture, type Resource } from "./dashboardJourneySupport";
import * as ui from "./dashboardKeyboard";

type Layout = { source: string; revision?: string; schemaVersion: number; value: { widgets: { id: string; size: string; visible: boolean; options?: Record<string, unknown> }[] } };

/** Current editor policy and unavailable Home contribution retention use only normal authorities. */
export async function dashboardAccessPreservationJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = await dashboardBackend(page.request); const suffix = `access-${Date.now()}`;
  const counter = () => backend.api<{ counts: Record<string, number>; privacyFailures: number }>("/__qualification/dashboard-live/counters");
  const before = await counter(); let question: Resource | undefined;
  const readerBrowser = await chromium.launch();
  const readerContext = await readerBrowser.newContext({ baseURL: process.env.ELSA_STUDIO_URL, viewport: { width: 1440, height: 1000 } });
  async function profile(name: "dashboard-live-reader" | "dashboard-live-editor") {
    const authority = await backend.api<{ revision: string }>("/__qualification/principals/0/authority");
    await backend.api("/__qualification/principals/0/grant-profile", "POST", { profile: name, revision: authority.revision });
  }
  try {
    const category = `Editor ${suffix}`; const now = Date.now();
    const from = new Date(now - 60_000).toISOString(); const to = new Date(now + 60_000).toISOString();
    const record = await backend.api<{ id: string }>("/_acme/processing/records", "POST", { customerCategory: category, status: "processing", startedAt: new Date().toISOString(), completedAt: null, readerIds: ["dashboard-qa-reader"] });
    expect((await backend.api<{ id: string }>(`/_acme/processing/records/${record.id}`)).id).toBe(record.id);
    const catalog = await backend.api<{ items: { id: string; schemaRevision: string }[] }>("/_elsa/studio/dashboard-datasets");
    const source = catalog.items.find(item => item.id === "acme.processing.records"); expect(source).toBeTruthy();
    question = await backend.api<Resource>("/_elsa/studio/questions", "POST", {
      name: `Editor rights ${suffix}`, operationKey: suffix, definition: { query: { schemaVersion: 1, dataset: { id: source!.id, schemaRevision: source!.schemaRevision }, form: "aggregate", time: { field: "startedAt", from, to, timeZone: "UTC" }, groupBy: ["customerCategory"], metrics: [{ alias: "count", metric: "processing.record-count", revision: "v1" }], filters: { field: "customerCategory", operator: "eq", value: { type: "string", literal: category } }, limit: 50 }, visualization: { kind: "table", y: ["count"] }, parameterValues: {} }
    });
    question = await backend.api<Resource>(`/_elsa/studio/questions/${question.id}/audience`, "PUT", { items: [{ kind: "user", id: "dashboard-qa-reader", access: "editor" }] }, tag(question.revision));
    await profile("dashboard-live-editor");
    const reader = await readerContext.newPage(); await signInDashboardReader(reader, info); await ui.startKeyboardEvidence(reader);
    await reader.goto(`/questions/${question.id}`);
    await expect(reader.getByRole("button", { name: "Save question", exact: true })).toBeVisible();
    await expect(reader.getByRole("button", { name: "Share", exact: true })).toHaveCount(0);
    await expect(reader.getByRole("combobox", { name: "Dataset", exact: true })).toBeEnabled();
    await ui.fill(reader.getByLabel("Maximum result rows/groups", { exact: true }), "23");
    const preview = reader.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith(`/questions/${question!.id}/preview`));
    await ui.click(reader.getByRole("button", { name: "Preview question", exact: true }));
    const result = await preview; expect(result.ok()).toBe(true);
    expect(await result.json()).toMatchObject({ outcome: "succeeded", rows: [{ values: { count: { value: "1" } } }] });
    const update = reader.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname.endsWith(`/questions/${question!.id}`));
    await ui.press(reader.getByRole("button", { name: "Save question", exact: true }), "Enter");
    const updated = await update; expect(updated.ok()).toBe(true); const saved = await updated.json() as Resource;
    expect(saved.id).toBe(question.id); expect(saved.revision).not.toBe(question.revision);
    expect(saved.definition).toMatchObject({ query: { limit: 23 } }); question = saved;
    await reader.reload(); await expect(reader.getByLabel("Maximum result rows/groups", { exact: true })).toHaveValue("23");
    await expect(reader.getByRole("button", { name: "Share", exact: true })).toHaveCount(0);
    await capture(reader, info, "editor-authorized-save-reopened");
    await ui.click(reader.getByRole("button", { name: "Copy saved version", exact: true }));
    const dialog = reader.getByRole("dialog", { name: "Save a private copy", exact: true });
    await ui.fill(dialog.getByRole("textbox"), `Private editor copy ${suffix}`);
    const copied = reader.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith(`/questions/${question!.id}/copies`));
    await ui.press(dialog.getByRole("button", { name: "Confirm", exact: true }), "Enter");
    const copyResponse = await copied; expect(copyResponse.ok()).toBe(true); const copy = await copyResponse.json() as Resource;
    expect(copy.id).not.toBe(question.id); expect(copy.ownerId).toBe("dashboard-qa-reader"); expect(copy.definition).toEqual(question.definition);
    await expect(reader.getByRole("button", { name: "Share", exact: true })).toHaveCount(0);
    await capture(reader, info, "editor-private-copy-without-sharing-rights");
    const exchange = await reader.request.get(`${backend.base}/_elsa/identity/token`); expect(exchange.ok()).toBe(true);
    const { accessToken } = await exchange.json() as { accessToken: string };
    const deniedShare = await reader.request.put(`${backend.base}/_elsa/studio/questions/${question.id}/audience`, { data: { items: [] }, headers: { ...tag(question.revision), Authorization: `Bearer ${accessToken}` } });
    expect(deniedShare.status()).toBe(403);
    const deniedDelete = await reader.request.delete(`${backend.base}/_elsa/studio/questions/${question.id}`, { headers: { ...tag(question.revision), Authorization: `Bearer ${accessToken}` } });
    expect(deniedDelete.status()).toBe(403);
    const deletedCopy = await reader.request.delete(`${backend.base}/_elsa/studio/questions/${copy.id}`, { headers: { ...tag(copy.revision), Authorization: `Bearer ${accessToken}` } });
    expect(deletedCopy.ok()).toBe(true);
    await attachDashboardJson(info, "editor-keyboard-proof.json", await ui.keyboardEvidence(reader));
    await profile("dashboard-live-reader");
    const homeRead = page.waitForResponse(response => response.request().method() === "GET" && new URL(response.url()).pathname.endsWith("/_elsa/studio/home-layout"));
    await page.goto("/dashboard"); const homeResponse = await homeRead; expect(homeResponse.ok()).toBe(true);
    const original = await homeResponse.json() as Layout;
    const scope = { "X-Elsa-Studio-Host-Id": homeResponse.request().headers()["x-elsa-studio-host-id"] ?? "default" };
    const widgets = page.locator(".dashboard-grid > .dashboard-widget");
    await expect(widgets.first()).toBeVisible();
    await expect(page.locator('.dashboard-grid > .dashboard-widget[aria-busy="true"]')).toHaveCount(0, { timeout: 30_000 });
    const knownIds = await widgets.evaluateAll(nodes => nodes.map(node => node.getAttribute("data-widget-id")));
    const missing = { id: `uninstalled.${suffix}`, size: "medium", visible: true, options: { retained: "Absent module settings must survive ordinary Home edits" } };
    try {
      const value = { widgets: [...original.value.widgets, missing] };
      await backend.api("/_elsa/studio/home-layout", "PUT", { schemaVersion: 1, value }, { ...scope, ...(original.source === "user" && original.revision ? tag(original.revision) : { "If-None-Match": "*" }) });
      await page.reload(); await expect(widgets.first()).toBeVisible();
      await expect(page.locator(`.dashboard-widget[data-widget-id="${missing.id}"]`)).toHaveCount(0);
      await expect(page.locator('.dashboard-grid > .dashboard-widget[aria-busy="true"]')).toHaveCount(0, { timeout: 30_000 });
      expect(await widgets.evaluateAll(nodes => nodes.map(node => node.getAttribute("data-widget-id")))).toEqual(knownIds);
      await capture(page, info, "home-missing-contribution-known-widgets-retained");
      await ui.click(page.getByRole("button", { name: "Edit home", exact: true }));
      const first = widgets.first(); const firstId = await first.getAttribute("data-widget-id");
      const move = first.getByRole("button", { name: /^Move .+ later$/ });
      const pending = page.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname.endsWith("/_elsa/studio/home-layout"));
      await ui.press(move, "Enter"); expect((await pending).ok()).toBe(true);
      const afterEdit = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope);
      expect(afterEdit.value.widgets.find(item => item.id === missing.id)).toEqual(missing);
      await page.reload(); await expect(widgets.nth(1)).toHaveAttribute("data-widget-id", firstId!);
      const reopened = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope);
      expect(reopened.value.widgets.find(item => item.id === missing.id)).toEqual(missing);
      await capture(page, info, "home-missing-contribution-preserved-after-save-reopen");
    } finally {
      await backend.api("/_elsa/studio/home-layout", "DELETE", undefined, scope);
      if (original.source === "user") await backend.api("/_elsa/studio/home-layout", "PUT", { schemaVersion: original.schemaVersion, value: original.value }, { ...scope, "If-None-Match": "*" });
    }
    const after = await counter(); expect(after.counts["session.send"] ?? 0).toBe(before.counts["session.send"] ?? 0); expect(after.privacyFailures).toBe(0);
    await attachDashboardJson(info, "access-preservation-proof.json", { authorizedEditorSaved: true, editorCopied: true, shareUiAbsent: true, shareApiStatus: deniedShare.status(), originalDeleteStatus: deniedDelete.status(), privateCopyDeletedByOwner: true, missingContributionNotRendered: true, knownHomeWidgetsPreserved: true, missingContributionRetainedAfterUiSaveAndReload: true, sdkBefore: before.counts["session.send"] ?? 0, sdkAfter: after.counts["session.send"] ?? 0 });
  } finally {
    try { await profile("dashboard-live-reader"); }
    finally {
      try {
        if (question) { const current = await backend.api<Resource>(`/_elsa/studio/questions/${question.id}`); await backend.api(`/_elsa/studio/questions/${question.id}`, "DELETE", undefined, tag(current.revision)); }
      } finally { try { await readerContext.close(); } finally { await readerBrowser.close(); } }
    }
  }
}
