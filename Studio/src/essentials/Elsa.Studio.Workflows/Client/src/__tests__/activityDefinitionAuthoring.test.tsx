import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  StudioHttpError,
  authSessionStartedEvent,
  StudioWorkspaceTabProvider,
  requestStudioNavigation,
  type StudioActivityDefinitionImplementationEditorProps,
  type StudioActivityDefinitionImplementationEditorContribution,
  type StudioActivityDiagnosticFocusResult,
  type StudioEndpointContext,
  type StudioRuntimeSettings,
  type StudioWorkspaceTabHandle
} from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionsPage } from "../ActivityDefinitionsPage";
import { ActivityDefinitionDraftEditor } from "../ActivityDefinitionDraftEditor";
import { ActivityDefinitionAuthoringTabsHostContext } from "../activityDefinitionAuthoringTabsHost";
import { useNativeBlockAuthority } from "../graph-authoring/nativeBlockAuthority";
import { clearApiCapabilityCache } from "../api/capabilities";
import { activityDesignKeys } from "../api/activityDesign";
import { clearActivityDefinitionRecoveryForIdentity, createActivityDefinitionRecoveryStore } from "../activityDefinitionRecovery";
import { activityDefinitionsObservationEvent, type ActivityDefinitionsObservation } from "../activityDefinitionObservability";
import type { ActivityDefinitionDraftView, ActivityDefinitionManagementView, ActivityDefinitionVersionView } from "../activityDefinitionTypes";
import { isExactVersionAtLeast, safeChangeValue } from "../ActivityDefinitionPublicationReview";

const mountedAuthoringRoots = new Set<() => Promise<void>>();

afterEach(async () => {
  for (const unmount of [...mountedAuthoringRoots]) await unmount();
  clearApiCapabilityCache();
  window.history.replaceState({}, "", "/");
  window.localStorage.clear();
  vi.useRealTimers();
});

describe("Activity Definition authoring", () => {
  it.each(["success", "failure"])("retains a clone %s while the workspace Activity is hidden and settles on reactivation", async outcome => {
    let active = true;
    const workspace: StudioWorkspaceTabHandle = { id: "activity-editor", get active() { return active; }, location: "/workflows/activity-definitions", isActive: () => active, markPendingWork: vi.fn(), setTitle: vi.fn() };
    let resolveClone!: (draft: ActivityDefinitionDraftView) => void;
    let rejectClone!: (error: unknown) => void;
    const clone = { ...fullDraft({ payload: { edit: 0 } }), sourceVersionId: "activity-version-1", activityPresentation: publishedVersion().activityPresentation };
    const postJson = vi.fn().mockImplementationOnce(() => new Promise<ActivityDefinitionDraftView>((resolve, reject) => { resolveClone = resolve; rejectClone = reject; })).mockResolvedValue(clone);
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...clone, ...body as object, revision: 2 }));
    const cleanup = vi.fn();
    const contribution = editingContribution();
    const Editor = contribution.component;
    contribution.component = function ActivityEditor(props) { React.useEffect(() => () => { cleanup(); }, []); return <Editor {...props} />; };
    const onDraftStarted = vi.fn((draft: ActivityDefinitionDraftView) => rendered.rerender({ draftId: draft.draftId, versionId: undefined, allowStartEditing: false }));
    const rendered = renderPage({ workspace, editor: { versionId: "activity-version-1", allowStartEditing: true, onDraftStarted }, contributions: [contribution], postJson, putJson, getJson: publishedEditorGet(clone) });
    await waitForText(rendered.container, "Edit implementation 0");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    active = false;
    rendered.rerender();
    await waitFor(() => expect(cleanup).toHaveBeenCalledTimes(1));
    if (outcome === "success") resolveClone(clone);
    else rejectClone(new StudioHttpError(503, "Unavailable"));
    await waitFor(() => expect(workspace.markPendingWork).toHaveBeenCalled());
    expect(onDraftStarted).not.toHaveBeenCalled();
    expect(putJson).not.toHaveBeenCalled();
    active = true;
    rendered.rerender();
    if (outcome === "failure") {
      await waitForText(rendered.container, "Your changes are still here");
      expect(buttonByText(rendered.container, "Edit implementation 1")).toBeTruthy();
      click(buttonByText(rendered.container, "Retry saving changes"));
    }
    await waitForText(rendered.container, "Saved revision 2");
    expect(postJson).toHaveBeenCalledTimes(outcome === "success" ? 1 : 2);
    expect(onDraftStarted).toHaveBeenCalledTimes(1);
    expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ provider: expect.objectContaining({ payload: { edit: 1 } }) }));
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(true);
  });

  it("reloads fresh server content when returning to a clone after opening another draft", async () => {
    const clone = { ...fullDraft({ payload: { edit: 0 } }), sourceVersionId: "activity-version-1" };
    let server = clone;
    const postJson = vi.fn(async () => clone);
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...clone, ...body as object, revision: 2 }));
    const get = publishedEditorGet();
    const onDraftStarted = vi.fn((draft: ActivityDefinitionDraftView) => rendered.rerender({ draftId: draft.draftId, versionId: undefined, allowStartEditing: false }));
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true, onDraftStarted }, contributions: [editingContribution()], postJson, putJson, getJson: url => {
      if (url === "/design/activities/drafts/activity-draft-1") return Promise.resolve(server);
      if (url === "/design/activities/drafts/activity-draft-2") return Promise.resolve(fullDraft({ draftId: "activity-draft-2", payload: { edit: 4 }, revision: 4 }));
      return get(url);
    } });
    await waitForText(rendered.container, "Edit implementation 0");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Saved revision 2");
    server = { ...clone, revision: 7, provider: { ...clone.provider, payload: { edit: 7 } } };
    rendered.rerender({ draftId: "activity-draft-2" });
    await waitForText(rendered.container, "Saved revision 4");
    rendered.rerender({ draftId: "activity-draft-1" });
    await waitForText(rendered.container, "Saved revision 7");
    expect(buttonByText(rendered.container, "Edit implementation 7")).toBeTruthy();
    expect(postJson).toHaveBeenCalledTimes(1);
  });

  it("validates a published block candidate against its exact version without creating a draft", async () => {
    const clone = { ...fullDraft({ payload: { edit: 0 } }), sourceVersionId: "activity-version-1", activityPresentation: publishedVersion().activityPresentation, layout: [{ nodeId: "root", data: { x: 120, y: 80 } }] };
    const postJson = vi.fn(async (url: string) => {
      if (url === "/design/activities/versions/activity-version-1/candidate-validation") return {
        state: "validated",
        validation: { draftId: "activity-version-1", revision: 0, isValid: true, validatedAt: "2026-07-17T10:00:00Z", diagnostics: [] }
      };
      throw new Error(`Unexpected POST ${url}`);
    });
    const putJson = vi.fn();
    const capture: { current: ReturnType<typeof useNativeBlockAuthority> } = { current: null };
    const contribution = { ...graphContribution(), component: function AuthorityEditor() {
      capture.current = useNativeBlockAuthority();
      return <div>Graph implementation editor</div>;
    } };
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true }, contributions: [contribution], postJson, putJson, getJson: publishedEditorGet(clone) });
    await waitForText(rendered.container, "Graph implementation editor");
    expect(capture.current?.canEdit).toBe(true);
    expect(capture.current?.canApply()).toBe(true);
    const result = await capture.current!.validate({ payload: { edit: 1 }, layout: clone.layout, activityPresentation: clone.activityPresentation });
    expect(result.state).toBe("validated");
    expect(postJson).toHaveBeenCalledWith("/design/activities/versions/activity-version-1/candidate-validation", expect.objectContaining({
      contract: fullDraft().contract,
      provider: expect.objectContaining({ payload: { edit: 1 } }),
      layout: clone.layout,
      activityPresentation: clone.activityPresentation
    }));
    expect(putJson).not.toHaveBeenCalled();
    expect(postJson).toHaveBeenCalledTimes(1);
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(true);
    await rendered.unmount();
  });

  it("opens a full published document without writes or source-draft reads and enables editing when permission arrives", async () => {
    const postJson = vi.fn();
    const putJson = vi.fn();
    const capture = { current: null as StudioActivityDefinitionImplementationEditorProps["onChange"] | null };
    const mounted = vi.fn();
    const contribution = editingContribution(capture);
    const Editor = contribution.component;
    contribution.component = function TrackedEditor(props) { React.useEffect(() => { mounted(); }, []); return <Editor {...props} />; };
    const version = publishedVersion();
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/versions/activity-version-1") return version;
      if (url === "/design/activities/definitions/activity-def-1") return editableDefinition();
      if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
      if (url === "/expressions/descriptors") return { items: [] };
      throw new Error(`Unexpected GET ${url}`);
    });
    const rendered = renderPage({ editor: { versionId: version.versionId, onManage: vi.fn() }, contributions: [contribution], getJson, postJson, putJson });
    await waitForText(rendered.container, "Edit implementation 0");
    const toolbar = rendered.container.querySelector<HTMLElement>(".wf-editor-toolbar");
    expect(toolbar).not.toBeNull();
    expect(toolbar?.querySelector("nav[aria-label='Breadcrumb']")?.textContent).toContain("Activity Definitions");
    expect(toolbar?.querySelector(".wf-editor-title")?.textContent).toContain("Published");
    expect(toolbar?.querySelector(".ad-authoring-toolbar-actions")?.textContent).toContain("Test Run");
    expect(toolbar?.querySelector(".ad-authoring-history")).not.toBeNull();
    expect(toolbar?.querySelector(".ad-authoring-advanced")).not.toBeNull();
    expect(rendered.container.querySelectorAll(".ad-authoring-view-tabs")).toHaveLength(1);
    expect(buttonByText(rendered.container, "Edit implementation 0").disabled).toBe(true);
    expect(rendered.container.querySelector(".ad-draft-context")?.textContent).toContain("Version 1.0.0");
    flushSync(() => capture.current!({ payload: { edit: 1 }, layout: [] }));
    expect(postJson).not.toHaveBeenCalled();
    rendered.rerender({ allowStartEditing: true });
    expect(buttonByText(rendered.container, "Edit implementation 0").disabled).toBe(false);
    click(rendered.container.querySelector("[data-tab-id=outcomes]")!);
    expect(rendered.container.querySelector(".ad-public-interface-view")?.textContent).toContain("Done");
    click(rendered.container.querySelector("[data-tab-id=designer]")!);
    expect(mounted).toHaveBeenCalledTimes(1);
    expect(rendered.container.querySelector<HTMLDetailsElement>(".ad-authoring-advanced:not(.ad-authoring-history)")?.open).toBe(false);
    expect(rendered.container.querySelector(".ad-draft-context")?.textContent).not.toContain("Published draft");
    expect(getJson.mock.calls.some(([url]) => url.includes("/drafts/"))).toBe(false);
    expect(postJson).not.toHaveBeenCalled();
    expect(putJson).not.toHaveBeenCalled();
    expect(window.localStorage.length).toBe(0);
  });

  it("clones once on a real edit, retains edits across the route replacement and preserves undisclosed source layout", async () => {
    let resolveClone!: (draft: ActivityDefinitionDraftView) => void;
    const clone = { ...fullDraft({ payload: { edit: 0 } }), sourceVersionId: "activity-version-1", layout: [{ nodeId: "root", data: { x: 120, y: 80 } }] };
    const postJson = vi.fn(() => new Promise<ActivityDefinitionDraftView>(resolve => { resolveClone = resolve; }));
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...clone, ...body as object, revision: 2 }));
    const capture = { current: null as StudioActivityDefinitionImplementationEditorProps["onChange"] | null };
    const mounted = vi.fn();
    const contribution = editingContribution(capture);
    const Editor = contribution.component;
    contribution.component = function TrackedEditor(props) { React.useEffect(() => { mounted(); }, []); return <Editor {...props} />; };
    const onDraftStarted = vi.fn((draft: ActivityDefinitionDraftView) => {
      expect(rendered.queryClient.getQueryData(activityDesignKeys.fullDefinitionDraft(draft.draftId))).toEqual(draft);
      rendered.rerender({ draftId: draft.draftId, versionId: undefined, allowStartEditing: false });
    });
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true, onManage: vi.fn(), onDraftStarted }, contributions: [contribution], postJson, putJson, getJson: publishedEditorGet(clone) });
    await waitForText(rendered.container, "Edit implementation 0");
    flushSync(() => capture.current!({ payload: { edit: 0 }, layout: [], activityPresentation: publishedVersion().activityPresentation }));
    expect(postJson).not.toHaveBeenCalled();
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    click(buttonByText(rendered.container, "Edit implementation 1"));
    resolveClone(clone);
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    await waitForText(rendered.container, "Saved revision 2");
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions/activity-def-1/drafts", { sourceVersionId: "activity-version-1", presentationLabel: null });
    expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 1, provider: expect.objectContaining({ payload: { edit: 2 } }), layout: clone.layout, activityPresentation: publishedVersion().activityPresentation }));
    expect(buttonByText(rendered.container, "Edit implementation 2")).toBeTruthy();
    expect(mounted).toHaveBeenCalledTimes(1);
    expect(onDraftStarted).toHaveBeenCalledTimes(1);
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(true);
  });

  it("keeps a failed first edit guarded and retries cloning with the latest local content", async () => {
    const clone = { ...fullDraft({ payload: { edit: 0 } }), sourceVersionId: "activity-version-1" };
    const postJson = vi.fn().mockRejectedValueOnce(new StudioHttpError(503, "Unavailable")).mockResolvedValue(clone);
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...clone, ...body as object, revision: 2 }));
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true }, contributions: [editingContribution()], postJson, putJson, getJson: publishedEditorGet(clone) });
    await waitForText(rendered.container, "Edit implementation 0");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Your changes are still here");
    expect(buttonByText(rendered.container, "Edit implementation 1")).toBeTruthy();
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    expect(putJson).not.toHaveBeenCalled();
    expect(window.localStorage.length).toBe(0);
    click(buttonByText(rendered.container, "Retry saving changes"));
    await waitForText(rendered.container, "Saved revision 2");
    expect(postJson).toHaveBeenCalledTimes(2);
    expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ provider: expect.objectContaining({ payload: { edit: 1 } }) }));
  });

  it("keeps an active draft read-only when edit permission is denied, including callback and action gates", async () => {
    const postJson = vi.fn();
    const putJson = vi.fn();
    const capture = { current: null as StudioActivityDefinitionImplementationEditorProps["onChange"] | null };
    const rendered = renderPage({ editor: { draftId: "activity-draft-1", readOnly: true, onManage: vi.fn() }, contributions: [editingContribution(capture)], postJson, putJson, getJson: publishedEditorGet(fullDraft({ payload: { edit: 0 } })) });
    await waitForText(rendered.container, "Edit implementation 0");
    expect(buttonByText(rendered.container, "Edit implementation 0").disabled).toBe(true);
    expect(buttonByText(rendered.container, "Test Run").disabled).toBe(true);
    expect(buttonByText(rendered.container, "Migrate provider").disabled).toBe(true);
    flushSync(() => capture.current!({ payload: { edit: 1 }, layout: [] }));
    click(rendered.container.querySelector("[data-tab-id=outcomes]")!);
    expect([...rendered.container.querySelectorAll<HTMLInputElement>(".ad-public-interface-view input")].every(input => input.disabled)).toBe(true);
    expect(postJson).not.toHaveBeenCalled();
    expect(putJson).not.toHaveBeenCalled();
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(true);
  });

  it("preserves an actual first layout edit and retries failed autosave against the same real draft", async () => {
    const clone = { ...fullDraft({ payload: { edit: 0 } }), sourceVersionId: "activity-version-1", layout: [{ nodeId: "root", data: { x: 120, y: 80 } }] };
    const postJson = vi.fn(async () => clone);
    const putJson = vi.fn().mockRejectedValueOnce(new StudioHttpError(503, "Unavailable")).mockImplementation(async (_url: string, body: unknown) => ({ ...clone, ...body as object, revision: 2 }));
    const capture = { current: null as StudioActivityDefinitionImplementationEditorProps["onChange"] | null };
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true }, contributions: [editingContribution(capture)], postJson, putJson, getJson: publishedEditorGet(clone) });
    await waitForText(rendered.container, "Edit implementation 0");
    const layout = [{ nodeId: "root", data: { x: 300, y: 400 } }];
    flushSync(() => capture.current!({ payload: { edit: 0 }, layout }));
    await waitForText(rendered.container, "Save failed");
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    click(buttonByText(rendered.container, "Save now"));
    await waitForText(rendered.container, "Saved revision 2");
    expect(postJson).toHaveBeenCalledTimes(1);
    expect(putJson).toHaveBeenCalledTimes(2);
    expect(putJson).toHaveBeenLastCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 1, layout }));
  });

  it("starts from the first contract change and retains the selected contract view and member", async () => {
    const clone = { ...fullDraft({ payload: { edit: 0 } }), sourceVersionId: "activity-version-1", layout: [{ nodeId: "root", data: { x: 120, y: 80 } }] };
    const postJson = vi.fn(async () => clone);
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...clone, ...body as object, revision: 2 }));
    const onDraftStarted = vi.fn((draft: ActivityDefinitionDraftView) => rendered.rerender({ draftId: draft.draftId, versionId: undefined, allowStartEditing: false }));
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true, onDraftStarted }, postJson, putJson, getJson: publishedEditorGet(clone) });
    await waitForText(rendered.container, "Graph implementation editor");
    click(rendered.container.querySelector("[data-tab-id=inputs]")!);
    click(buttonByText(rendered.container, "Add input"));
    await waitFor(() => expect(rendered.container.querySelector(".ad-contract-create-form input")).not.toBeNull());
    change(rendered.container.querySelector<HTMLInputElement>(".ad-contract-create-form input")!, "Customer");
    click(buttonByText(rendered.container.querySelector<HTMLElement>(".ad-contract-create-form")!, "Add input"));
    await waitForText(rendered.container, "Saved revision 2");
    expect(rendered.container.querySelector("[data-tab-id=inputs]")?.getAttribute("aria-selected")).toBe("true");
    expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ contract: expect.objectContaining({ inputs: [expect.objectContaining({ name: "Customer" })] }), layout: clone.layout }));
    expect(postJson).toHaveBeenCalledTimes(1);
  });

  it("denies first-edit callbacks for source-owned content even if the caller enables cloning", async () => {
    const postJson = vi.fn();
    const putJson = vi.fn();
    const capture = { current: null as StudioActivityDefinitionImplementationEditorProps["onChange"] | null };
    const get = publishedEditorGet();
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true }, contributions: [editingContribution(capture)], postJson, putJson, getJson: async url => url === "/design/activities/definitions/activity-def-1" ? { ...editableDefinition(), definition: { ...definitionIdentity(), contentAuthority: { kind: "ProviderSource", authorityKey: "source" } } } : get(url) });
    await waitForText(rendered.container, "Edit implementation 0");
    expect(buttonByText(rendered.container, "Edit implementation 0").disabled).toBe(true);
    flushSync(() => capture.current!({ payload: { edit: 1 }, layout: [] }));
    expect(postJson).not.toHaveBeenCalled();
    expect(putJson).not.toHaveBeenCalled();
  });

  it("withholds both shared and scoped cached version payloads until the current mount is authorized", async () => {
    let rejectRead!: (error: unknown) => void;
    const get = publishedEditorGet();
    const rendered = renderPage({
      editor: { versionId: "activity-version-1", allowStartEditing: true },
      seedVersion: { ...publishedVersion(), definition: { ...definitionIdentity(), displayName: "Private cached name" } },
      getJson: url => url === "/design/activities/versions/activity-version-1" ? new Promise((_resolve, reject) => { rejectRead = reject; }) : get(url)
    });
    await waitFor(() => expect(rejectRead).toBeTypeOf("function"));
    expect(rendered.container.textContent).not.toContain("Private cached name");
    expect(rendered.container.textContent).not.toContain("Graph implementation editor");
    rejectRead(new StudioHttpError(403, "Forbidden"));
    await waitForText(rendered.container, "Activity version unavailable");
    expect(rendered.container.textContent).not.toContain("Private cached name");
  });

  it("reverifies a mounted published preview on an auth-session change before revealing its content", async () => {
    let rejectRead!: (error: unknown) => void;
    let reads = 0;
    const get = publishedEditorGet();
    const rendered = renderPage({ editor: { versionId: "activity-version-1", allowStartEditing: true }, getJson: url => {
      if (url === "/design/activities/versions/activity-version-1" && ++reads > 1) return new Promise((_resolve, reject) => { rejectRead = reject; });
      return get(url);
    } });
    await waitForText(rendered.container, "Graph implementation editor");
    flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent)));
    await waitFor(() => expect(rejectRead).toBeTypeOf("function"));
    expect(rendered.container.textContent).not.toContain("Graph implementation editor");
    rejectRead(new StudioHttpError(403, "Forbidden"));
    await waitForText(rendered.container, "Activity version unavailable");
  });

  it("closes History with Escape or a selection, returns focus and routes management progressively", async () => {
    const onManage = vi.fn();
    const rendered = renderPage({ editor: { versionId: "activity-version-1", onManage }, getJson: publishedEditorGet() });
    await waitForText(rendered.container, "Graph implementation editor");
    const moreActions = rendered.container.querySelector<HTMLDetailsElement>(".ad-authoring-more-actions")!;
    const moreActionsSummary = moreActions.querySelector(":scope > summary")!;
    openDetails(moreActions);
    const history = rendered.container.querySelector<HTMLDetailsElement>(".ad-authoring-history")!;
    const historySummary = history.querySelector(":scope > summary")!;
    openDetails(history);
    buttonByText(history, "Drafts").focus();
    pressKey(buttonByText(history, "Drafts"), "Escape");
    expect(history.open).toBe(false);
    expect(document.activeElement).toBe(moreActionsSummary);
    for (const [label, section] of [["Drafts", "drafts"], ["Versions", "versions"], ["Used by", "relationships"], ["Details", "overview"]]) {
      openDetails(moreActions);
      openDetails(history);
      click(buttonByText(history, label));
      expect(onManage).toHaveBeenLastCalledWith(section);
      expect(history.open).toBe(false);
      expect(document.activeElement).toBe(historySummary);
    }
    expect(activityDefinitionBackLink(rendered.container).textContent).toBe("Activity Definitions");
  });

  it("marks local invalid JSON diagnostics as needing attention and reveals them from the header", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      getJson: async url => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/expressions/descriptors") return { items: [] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(rendered.container, "Saved revision 3");
    openDetails(rendered.container.querySelector<HTMLDetailsElement>(".ad-authoring-advanced")!);
    click(buttonByText(rendered.container, "Edit code"));
    await waitFor(() => expect(rendered.container.querySelector(".studio-code-editor textarea")).not.toBeNull());
    change(rendered.container.querySelector<HTMLTextAreaElement>(".studio-code-editor textarea")!, "{ invalid");
    await waitForText(rendered.container, "The JSON buffer is invalid and remains local");
    expect(rendered.container.querySelector(".ad-draft-diagnostics-link")?.classList.contains("needs-attention")).toBe(true);
    const panel = rendered.container.querySelector<HTMLElement>(".ad-diagnostics-panel")!;
    panel.scrollIntoView = vi.fn();
    click(rendered.container.querySelector(".ad-draft-diagnostics-link")!);
    expect(document.activeElement).toBe(panel);
    expect(panel.textContent).toContain("The JSON buffer is invalid and remains local");
    await rendered.unmount();
  });

  it("retains the create form and entered name when its host rerenders after first collection history initialization", async () => {
    const rendered = renderPage({
      getJson: async url => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    click(buttonByText(rendered.container, "New activity definition"));
    await waitForText(rendered.container, "Graph composition");
    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Retain my activity");

    rendered.rerender();

    expect(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")?.value).toBe("Retain my activity");
    expect(buttonByText(rendered.container, "Create")).toBeTruthy();
    await rendered.unmount();
  });

  it("autosaves a presentation-only provider edit with the exact revision and retained implementation", async () => {
    const capture: { current: StudioActivityDefinitionImplementationEditorProps["onChange"] | null } = { current: null };
    const initial = { ...fullDraft({ revision: 3, payload: { edit: 0 } }), activityPresentation: [{ nodeId: "root", displayName: "Original" }] };
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...initial, revision: 4, activityPresentation: (body as ActivityDefinitionDraftView).activityPresentation }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution(capture)],
      putJson,
      getJson: async url => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(rendered.container, "Saved revision 3");
    flushSync(() => capture.current!({ payload: initial.provider.payload, layout: initial.layout, activityPresentation: [{ nodeId: "root", displayName: "Renamed", description: "Context" }] }));
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    click(buttonByText(rendered.container, "Save now"));
    await waitForText(rendered.container, "Saved revision 4");
    expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({
      expectedRevision: 3,
      activityPresentation: [{ nodeId: "root", displayName: "Renamed", description: "Context" }],
      provider: expect.objectContaining({ payload: initial.provider.payload }),
      layout: initial.layout
    }));
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(true);
    await rendered.unmount();
  });

  it("keeps ordinary autosave quiet while retaining the navigation guard", async () => {
    let resolveSave!: (value: ActivityDefinitionDraftView) => void;
    const putJson = vi.fn(() => new Promise<ActivityDefinitionDraftView>(resolve => { resolveSave = resolve; }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async url => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    expect(rendered.container.querySelector(".ad-revision-gate")).toBeNull();
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    click(buttonByText(rendered.container, "Save now"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    expect(rendered.container.querySelector(".ad-revision-gate")).toBeNull();
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    resolveSave(fullDraft({ revision: 4, payload: { edit: 1 } }));
    await waitForText(rendered.container, "Saved revision 4");
    await waitFor(() => expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(true));
    await rendered.unmount();
  });
  it("creates with the sole authorable provider and enters the exact initial draft", async () => {
    const postJson = vi.fn(async (_url: string, _body: unknown) => ({
      definition: definitionIdentity(),
      draft: draftSummary({ providerSchemaVersion: "2" })
    }));
    const rendered = renderPage({
      contributions: [graphContribution(), graphContribution("2")],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities({ graphSchemas: ["1", "2"] });
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ providerSchemaVersion: "2" });
        throw new Error(`Unexpected GET ${url}`);
      },
      postJson
    });

    await waitForText(rendered.container, "No designed activities yet");
    click(buttonByText(rendered.container, "New activity definition"));
    await waitForText(rendered.container, "Graph composition");
    expect(rendered.container.querySelector("select[name='provider']")).toBeNull();
    await waitFor(() => expect(rendered.container.querySelector<HTMLInputElement>("input[name='wf-root-kind'][value='flowchart-v1']")?.checked).toBe(true));

    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Invoice evaluator");
    change(rendered.container.querySelector<HTMLInputElement>("input[name='category']")!, "Finance");
    click(buttonByText(rendered.container, "Create"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson).toHaveBeenCalledWith("/design/activities/definitions", {
      category: "Finance",
      displayName: "Invoice evaluator",
      description: null,
      provider: {
        providerKey: "elsa.activity-graph",
        schemaVersion: "2",
        payload: {
          rootActivity: {
            nodeId: "root",
            activityVersionId: "flowchart-v1",
            inputs: [],
            outputs: [],
            structure: {
              kind: "elsa.flowchart.structure",
              schemaVersion: "1.0.0",
              payload: { activities: [], connections: [], startNodeId: null, nodeMetadata: {}, connectionMetadata: {} }
            }
          },
          variables: [],
          outputMappings: []
        }
      },
      contract: {
        contractSchemaVersion: "1",
        inputs: [],
        outputs: [],
        outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
      },
      layout: []
    });
    await waitFor(() => {
      const route = new URLSearchParams(window.location.search);
      expect(route.get("definition")).toBe("activity-def-1");
      expect(route.get("draft")).toBe("activity-draft-1");
      expect(route.get("section")).toBe("editor");
    });
    await waitForText(rendered.container, "Saved revision 1");
    expect(rendered.container.querySelector(".ad-draft-editor")?.classList.contains("is-graph-designer")).toBe(true);
    const advanced = rendered.container.querySelector<HTMLDetailsElement>(".ad-authoring-advanced")!;
    expect(advanced.open).toBe(false);
    expect(rendered.container.querySelector(".studio-split-button-primary")).not.toBeNull();
    expect(rendered.container.querySelector(".ad-designer-view")?.textContent).toContain("Graph implementation editor");
    expect(rendered.container.querySelectorAll(".ad-authoring-view-tabs")).toHaveLength(1);
    expect(rendered.container.querySelector(".ad-graph-canvas .ad-authoring-view-tabs")).not.toBeNull();
    const inputsTab = rendered.container.querySelector<HTMLButtonElement>("[data-tab-id=inputs]")!;
    inputsTab.focus();
    click(inputsTab);
    expect(rendered.container.querySelector(".ad-graph-canvas .ad-authoring-view-tabs")).not.toBeNull();
    expect(document.activeElement?.getAttribute("data-tab-id")).toBe("inputs");
    expect(rendered.container.querySelector<HTMLElement>(".ad-public-interface-view")?.hidden).toBe(false);
    expect(rendered.container.querySelector(".ad-draft-editor")?.classList.contains("is-graph-designer")).toBe(true);
    expect(advanced.open).toBe(false);
    const designerTab = rendered.container.querySelector<HTMLButtonElement>("[data-tab-id=designer]")!;
    designerTab.focus();
    click(designerTab);
    expect(rendered.container.querySelector(".ad-graph-canvas .ad-authoring-view-tabs")).not.toBeNull();
    expect(document.activeElement?.getAttribute("data-tab-id")).toBe("designer");
    await rendered.unmount();
  }, 10_000);

  it("labels the create dialog fields with accessible names and placeholders", async () => {
    const rendered = renderPage({
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No designed activities yet");
    click(buttonByText(rendered.container, "New activity definition"));
    await waitForText(rendered.container, "Graph composition");

    const displayName = rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!;
    const category = rendered.container.querySelector<HTMLInputElement>("input[name='category']")!;
    const description = rendered.container.querySelector<HTMLTextAreaElement>("textarea[name='description']")!;
    expect(displayName.getAttribute("aria-label")).toBe("Display name");
    expect(displayName.getAttribute("placeholder")).toBeTruthy();
    expect(category.getAttribute("aria-label")).toBe("Category");
    expect(category.getAttribute("placeholder")).toBeTruthy();
    expect(description.labels?.[0]?.textContent?.trim()).toBe("Description");
    expect(description.getAttribute("placeholder")).toBeTruthy();
    const advanced = buttonByText(rendered.container, "Advanced");
    expect(advanced.getAttribute("aria-expanded")).toBe("false");
    expect(rendered.container.querySelector<HTMLInputElement>("input[name='activityTypeKey']")?.closest("[hidden]")).toBeTruthy();
    await rendered.unmount();
  });

  it("explains Activity Type Key rules and blocks an invalid exact key", async () => {
    const rendered = renderPage({
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No designed activities yet");
    click(buttonByText(rendered.container, "New activity definition"));
    await waitForText(rendered.container, "Graph composition");
    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Greeter");
    click(buttonByText(rendered.container, "Advanced"));

    const keyInput = rendered.container.querySelector<HTMLInputElement>("input[name='activityTypeKey']")!;
    expect(rendered.container.textContent).toContain("Starts with elsa.user.");
    expect(rendered.container.textContent).toContain("Matches pattern ^elsa\\.user\\..+$");
    const descriptionIds = keyInput.getAttribute("aria-describedby")?.split(/\s+/) ?? [];
    expect(document.getElementById(descriptionIds[0]!)?.tagName).toBe("UL");
    change(keyInput, "elsa.custom.greeter");

    expect(rendered.container.textContent).toContain("Must start with elsa.user.");
    expect(keyInput.getAttribute("aria-invalid")).toBe("true");
    expect(keyInput.getAttribute("aria-describedby")?.split(/\s+/)).toHaveLength(2);
    expect(document.getElementById(keyInput.getAttribute("aria-describedby")!.split(/\s+/)[1]!)?.getAttribute("role")).toBe("alert");
    expect(buttonByText(rendered.container, "Create").hasAttribute("disabled")).toBe(true);
    await rendered.unmount();
  });

  it("does not preselect among multiple providers and sends an intentional exact key override", async () => {
    const capabilitiesResponse = authoringCapabilities({ graphSchemas: ["1", "2"] });
    capabilitiesResponse.providers.push({
      providerKey: "contoso.script",
      displayName: "Script",
      manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: [] }],
      requiredOutcomes: []
    });
    const postJson = vi.fn(async (_url: string, _body: unknown) => ({ definition: definitionIdentity(), draft: draftSummary() }));
    const rendered = renderPage({
      contributions: [graphContribution("2"), { ...graphContribution(), id: "script", providerKey: "contoso.script" }],
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return capabilitiesResponse;
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ providerSchemaVersion: "2" });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No designed activities yet");
    click(buttonByText(rendered.container, "New activity definition"));
    await waitFor(() => expect(rendered.container.querySelector('[role="dialog"] h2')?.textContent).toBe("New activity definition"));
    await waitFor(() => expect(rendered.container.querySelector("select[name='provider']")).not.toBeNull());
    const provider = rendered.container.querySelector<HTMLSelectElement>("select[name='provider']")!;
    expect(provider.value).toBe("");
    expect([...provider.options].map(option => option.textContent)).toEqual(["Select an implementation type", "Activity Graph", "Script"]);
    change(provider, "elsa.activity-graph|2");
    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Invoice evaluator");
    const advanced = buttonByText(rendered.container, "Advanced");
    expect(advanced.getAttribute("aria-expanded")).toBe("false");
    click(advanced);
    change(rendered.container.querySelector<HTMLInputElement>("input[name='activityTypeKey']")!, "elsa.user.finance.invoice-evaluator");
    const createDefinition = buttonByText(rendered.container, "Create") as HTMLButtonElement;
    await waitFor(() => expect(createDefinition.disabled).toBe(false));
    click(createDefinition);

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/definitions", expect.objectContaining({ activityTypeKey: "elsa.user.finance.invoice-evaluator" })));
    await rendered.unmount();
  }, 15_000);

  it("preselects the sole usable non-graph provider without showing an implementation selector", async () => {
    const response = authoringCapabilities();
    response.providers = [{
      providerKey: "contoso.script",
      displayName: "Script",
      manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: [] }],
      requiredOutcomes: []
    }];
    const rendered = renderPage({
      contributions: [{ ...graphContribution(), id: "script", providerKey: "contoso.script" }],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return response;
        if (url === "/design/activities/catalog") return { activities: [] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No designed activities yet");
    click(buttonByText(rendered.container, "New activity definition"));
    await waitForText(rendered.container, "Display name");
    expect(rendered.container.querySelector<HTMLSelectElement>("select[name='provider']")).toBeNull();
    expect(buttonByText(rendered.container, "Create").hasAttribute("disabled")).toBe(true);
    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Run a script");
    expect(buttonByText(rendered.container, "Create").hasAttribute("disabled")).toBe(false);
    await rendered.unmount();
  });

  it("clears an exact key override when the author returns to generated identity", async () => {
    const postJson = vi.fn(async (_url: string, _body: unknown) => ({ definition: definitionIdentity(), draft: draftSummary() }));
    const rendered = renderPage({
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url.startsWith("/design/activities/definitions?")) return page([]);
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "No designed activities yet");
    click(buttonByText(rendered.container, "New activity definition"));
    await waitForText(rendered.container, "Graph composition");
    await waitFor(() => expect(rendered.container.querySelector<HTMLInputElement>("input[name='wf-root-kind'][value='flowchart-v1']")?.checked).toBe(true));
    change(rendered.container.querySelector<HTMLInputElement>("input[name='displayName']")!, "Generated identity");
    const advanced = buttonByText(rendered.container, "Advanced");
    click(advanced);
    change(rendered.container.querySelector<HTMLInputElement>("input[name='activityTypeKey']")!, "elsa.user.hidden.override");
    click(advanced);
    click(buttonByText(rendered.container, "Create"));

    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    expect(postJson.mock.calls[0]?.[1]).not.toHaveProperty("activityTypeKey");
    await rendered.unmount();
  });

  it("shows an honest unavailable state when no exact provider editor contribution exists", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Implementation editor unavailable");
    expect(rendered.container.textContent).toContain("elsa.activity-graph");
    expect(rendered.container.textContent).toContain("schema 1");
    expect(rendered.container.textContent).not.toContain("JSON editor");
    await rendered.unmount();
  });

  it("contains provider render failures inside the autosave shell", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const observations: ActivityDefinitionsObservation[] = [];
    const observe = (event: Event) => observations.push((event as CustomEvent<ActivityDefinitionsObservation>).detail);
    window.addEventListener(activityDefinitionsObservationEvent, observe);
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [{ ...graphContribution(), component: () => { throw new Error("provider render detail"); } }],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Implementation editor unavailable");
    expect(rendered.container.textContent).toContain("Saved revision 3");
    expect(rendered.container.textContent).not.toContain("provider render detail");
    expect(buttonByText(rendered.container, "Validate saved revision")).toBeTruthy();
    expect(rendered.container.querySelector(".ad-designer-view [role='alert'] button")).toBeNull();
    expect(observations).toContainEqual(expect.objectContaining({ event: "provider-editor", outcome: "unavailable", providerKey: "elsa.activity-graph", providerSchemaVersion: "1" }));
    await rendered.unmount();
    window.removeEventListener(activityDefinitionsObservationEvent, observe);
    consoleError.mockRestore();
  });

  it("offers a draft reload for provider asset failures without trapping other authoring views", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [{ ...graphContribution(), component: () => { throw new Error("Failed to fetch dynamically imported module: /assets/ActivityGraphImplementationEditor.js"); } }],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Implementation editor unavailable");
    const alert = rendered.container.querySelector(".ad-designer-view [role='alert']");
    expect(alert?.textContent).toContain("Studio assets");
    expect(buttonByText(alert as HTMLElement, "Reload draft")).toBeTruthy();
    click(rendered.container.querySelector(".ad-authoring-view-tabs [data-tab-id='inputs']")!);
    expect(rendered.container.querySelector("[data-tab-id='inputs']")?.getAttribute("aria-selected")).toBe("true");
    expect(rendered.container.querySelector(".ad-designer-view [role='alert']")).toBeTruthy();
    expect(rendered.container.textContent).toContain("Saved revision 3");

    await rendered.unmount();
    consoleError.mockRestore();
  });

  it("serializes trailing autosaves against each exact returned revision", async () => {
    let resolveFirst!: (value: unknown) => void;
    const firstSave = new Promise(resolve => { resolveFirst = resolve; });
    const putJson = vi.fn(async (_url: string, body: unknown) => {
      const request = body as { expectedRevision: number; provider: { payload: { edit: number } } };
      if (request.expectedRevision === 3) return firstSave;
      return fullDraft({ revision: 5, payload: request.provider.payload });
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    click(buttonByText(rendered.container, "Edit implementation"));
    resolveFirst(fullDraft({ revision: 4, payload: { edit: 1 } }));

    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(2));
    expect(putJson.mock.calls[0]).toEqual(["/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 3, provider: expect.objectContaining({ payload: { edit: 1 } }) })]);
    expect(putJson.mock.calls[1]).toEqual(["/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 4, provider: expect.objectContaining({ payload: { edit: 2 } }) })]);
    await waitForText(rendered.container, "Saved revision 5");
    await rendered.unmount();
  }, 10_000);

  it("authors capability-backed members and preserves exact default presence plus unknown wire fields", async () => {
    let revision = 3;
    const initial = fullDraft({ revision, payload: { edit: 0 } });
    initial.contract = {
      contractSchemaVersion: "1",
      futureContractField: { retained: true },
      inputs: [{
        referenceKey: "customer-note",
        name: "Customer note",
        displayName: "Customer note",
        description: null,
        category: null,
        order: 0,
        uiHint: null,
        uiSpecifications: { futureUiField: "retained" },
        type: { alias: "String", collectionKind: "Single", futureTypeField: "retained" },
        isRequired: false,
        isNullable: true,
        default: { syntax: "Literal", value: "before", futureDefaultField: "retained" },
        storageDriverKey: "elsa.json",
        durability: "Required",
        futureMemberField: { retained: true }
      }],
      outputs: [],
      outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
    };
    const putJson = vi.fn(async (_url: string, body: unknown) => {
      const request = body as { expectedRevision: number; contract: typeof initial.contract };
      revision = request.expectedRevision + 1;
      return { ...initial, revision, contract: request.contract };
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/authoring-capabilities") {
          const response = authoringCapabilities();
          response.types[0].compatibleStorageDriverKeys.push("elsa.blob");
          response.storageDriverKeys.push("elsa.blob");
          return response;
        }
        if (url === "/expressions/descriptors") return { items: [{ type: "Python", displayName: "Python", editingMode: "text" }] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Customer note");
    showContractView(rendered.container);
    const doneOutcome = selectContractMember(rendered.container, "outcome", "done");
    const doneEmitted = contractMemberRow(rendered.container, "outcome", "done").querySelector<HTMLElement>("[data-contract-field='isEmitted']");
    expect(doneEmitted?.matches("input") ? (doneEmitted as HTMLInputElement).checked : doneEmitted?.querySelector<HTMLInputElement>("input")?.checked).toBe(true);
    expect(doneOutcome.querySelector('input[data-contract-field="name"]')).toBeNull();
    expect(contractMemberRow(rendered.container, "outcome", "done").querySelector(".wf-properties-remove")).toBeNull();
    expect(doneOutcome.querySelector('[data-contract-field="isEmitted"]')).toBeNull();
    const customerNote = selectContractMember(rendered.container, "input", "customer-note");
    expect(controlByAriaLabel<HTMLSelectElement>(rendered.container, "Storage provider").value).toBe("elsa.json");
    const required = contractMemberRow(rendered.container, "input", "customer-note").querySelector<HTMLInputElement>("[data-contract-field='isRequired']")!;
    const nullable = controlByLabel<HTMLInputElement>(rendered.container, "Allow null");
    expect(required.checked).toBe(false);
    expect(nullable.checked).toBe(true);
    expect(nullable.disabled).toBe(false);
    expect(rendered.container.textContent).not.toContain("Remove or change the default value of null before turning this off");
    click(required);
    expect(nullable.checked).toBe(true);

    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({
      contract: expect.objectContaining({
        inputs: [expect.objectContaining({
          isRequired: true,
          isNullable: true,
          default: { syntax: "Literal", value: "before", futureDefaultField: "retained" },
          futureMemberField: { retained: true },
          type: expect.objectContaining({ futureTypeField: "retained" }),
          uiSpecifications: { futureUiField: "retained" }
        })],
        futureContractField: { retained: true }
      })
    })));

    change(controlByLabel<HTMLInputElement>(customerNote, "Value"), "present");
    click(buttonByText(customerNote, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      JSON.stringify((body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default) === JSON.stringify({ syntax: "Literal", value: "present", futureDefaultField: "retained" })
    )).toBe(true));

    change(controlByLabel<HTMLSelectElement>(customerNote, "Default"), "null");
    click(buttonByText(customerNote, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      JSON.stringify((body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default) === JSON.stringify({ syntax: "Literal", value: null, futureDefaultField: "retained" })
    )).toBe(true));
    change(controlByLabel<HTMLSelectElement>(customerNote, "Default"), "literal");
    change(controlByLabel<HTMLInputElement>(customerNote, "Value"), "present again");
    click(buttonByText(customerNote, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      JSON.stringify((body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default) === JSON.stringify({ syntax: "Literal", value: "present again", futureDefaultField: "retained" })
    )).toBe(true));

    change(controlByLabel<HTMLSelectElement>(customerNote, "Default"), "none");
    click(buttonByText(customerNote, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      (body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default === null
    )).toBe(true));

    change(controlByLabel<HTMLSelectElement>(customerNote, "Default"), "null");
    click(buttonByText(customerNote, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) =>
      JSON.stringify((body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default) === JSON.stringify({ syntax: "Literal", value: null })
    )).toBe(true));
    expect(controlByLabel<HTMLInputElement>(customerNote, "Allow null").disabled).toBe(true);
    expect(rendered.container.textContent).toContain("Remove or change the default value of null before turning this off");

    const defaultMode = controlByLabel<HTMLSelectElement>(customerNote, "Default");
    change(defaultMode, "expression");
    expect(defaultMode.value).toBe("expression");
    const expressionSource = controlByLabel<HTMLTextAreaElement>(customerNote, "Expression source");
    expect(expressionSource.value).toBe("");
    change(expressionSource, "customer.note");
    expect(expressionSource.value).toBe("customer.note");
    const applyExpression = buttonByText(customerNote, "Apply default") as HTMLButtonElement;
    expect(applyExpression.disabled).toBe(false);
    click(applyExpression);
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) => {
      const candidate = (body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default;
      return JSON.stringify(candidate) === JSON.stringify({ syntax: "Python", value: "customer.note" });
    })).toBe(true));
    const expressionSave = putJson.mock.calls.find(([, body]) => {
      const candidate = (body as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default;
      return JSON.stringify(candidate) === JSON.stringify({ syntax: "Python", value: "customer.note" });
    });
    const appliedExpression = (expressionSave?.[1] as { contract: { inputs: Array<{ default: unknown }> } }).contract.inputs[0]?.default;
    expect(appliedExpression).toEqual({ syntax: "Python", value: "customer.note" });

    addContractMember(rendered.container, "output", "Result");
    await waitFor(() => expect(contractListButton(rendered.container, "output", "result").getAttribute("aria-expanded")).toBe("true"));
    const output = contractMemberPane(rendered.container, "output", "result");
    click(contractMemberRow(rendered.container, "output", "result").querySelector<HTMLInputElement>("[data-contract-field='isRequired']")!);
    click(controlByLabel<HTMLInputElement>(output, "Allow null"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) => {
      const value = (body as { contract: { outputs: Array<Record<string, unknown>> } }).contract.outputs[0];
      return JSON.stringify(value) === JSON.stringify({
        referenceKey: "result",
        name: "Result",
        displayName: "Result",
        description: null,
        category: null,
        order: 0,
        uiHint: null,
        uiSpecifications: null,
        type: { alias: "String", collectionKind: "Single" },
        isRequired: true,
        isNullable: true,
        storageDriverKey: "elsa.json",
        durability: "Required"
      });
    })).toBe(true));
    await rendered.unmount();
  }, 15_000);

  it("splits contract collections into counted tabs and opens kind-specific creation forms", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.contract.inputs = [{
      referenceKey: "customer-id",
      name: "Customer id",
      displayName: "Friendly customer label",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }, {
      referenceKey: "quantity",
      name: "Quantity",
      displayName: null,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...initial, revision: 4, contract: (body as { contract: unknown }).contract }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    showContractView(rendered.container);
    const list = rendered.container.querySelector<HTMLElement>(".ad-contract-editor .wf-properties-section")!;
    expect([...list.querySelectorAll(".wf-properties-section-head h2")].map(item => item.textContent)).toEqual(["Inputs"]);
    expect([...rendered.container.querySelectorAll(".ad-authoring-view-tabs button")].map(item => item.textContent)).toEqual(["Designer", "Inputs (2)", "Outputs (0)", "Outcomes (1)", "Variables"]);
    const inputAdd = categoryAddButton(rendered.container, "input");
    inputAdd.focus();
    click(inputAdd);
    const inputGroup = inputAdd.closest<HTMLElement>(".ad-contract-editor")!;
    const inputForm = inputGroup.querySelector<HTMLFormElement>("form.ad-contract-create-form")!;
    const inputName = controlByLabel<HTMLInputElement>(inputForm, "Input name");
    await waitFor(() => expect(document.activeElement).toBe(inputName));
    click(buttonByText(inputForm, "Cancel"));
    await waitFor(() => expect(document.activeElement).toBe(inputAdd));

    const customer = contractListButton(rendered.container, "input", "customer-id");
    expect((contractMemberRow(rendered.container, "input", "customer-id").querySelector("input[data-contract-field='name']") as HTMLInputElement).value).toBe("Customer id");
    const customerPane = selectContractMember(rendered.container, "input", "customer-id");
    expect(customerPane.dataset.contractSelected).toBe("true");
    const quantityPane = contractMemberPane(rendered.container, "input", "quantity");
    const donePane = contractMemberPane(rendered.container, "outcome", "done");
    expect(quantityPane.closest<HTMLElement>(".ad-contract-detail-row")?.hidden).toBe(true);
    expect(donePane.hidden).toBe(true);
    expect(rendered.container.querySelectorAll(".ad-contract-detail-row [data-contract-kind]")).toHaveLength(3);

    selectContractMember(rendered.container, "input", "quantity");
    expect(customerPane.hidden).toBe(true);
    expect(quantityPane.dataset.contractSelected).toBe("true");
    expect(contractListButton(rendered.container, "input", "customer-id").getAttribute("aria-expanded")).toBe("false");
    expect(contractListButton(rendered.container, "input", "quantity").getAttribute("aria-expanded")).toBe("true");

    addContractMember(rendered.container, "input", "Approver");
    await waitFor(() => expect(contractListButton(rendered.container, "input", "approver").getAttribute("aria-expanded")).toBe("true"));
    const approverPane = contractMemberPane(rendered.container, "input", "approver");
    expect(approverPane.dataset.contractSelected).toBe("true");
    await waitFor(() => expect(document.activeElement).toBe(contractMemberRow(rendered.container, "input", "approver").querySelector("input[data-contract-field='name']")));

    const outputAdd = categoryAddButton(rendered.container, "output");
    outputAdd.focus();
    click(outputAdd);
    const outputGroup = outputAdd.closest<HTMLElement>(".ad-contract-editor")!;
    const outputForm = outputGroup.querySelector<HTMLFormElement>("form.ad-contract-create-form")!;
    const outputName = controlByLabel<HTMLInputElement>(outputForm, "Output name");
    await waitFor(() => expect(document.activeElement).toBe(outputName));
    flushSync(() => outputName.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await waitFor(() => expect(document.activeElement).toBe(categoryAddButton(rendered.container, "output")));

    addContractMember(rendered.container, "output", "Result");
    await waitFor(() => expect(contractListButton(rendered.container, "output", "result").getAttribute("aria-expanded")).toBe("true"));
    const resultPane = contractMemberPane(rendered.container, "output", "result");
    expect(resultPane.dataset.contractSelected).toBe("true");
    expect(resultPane.querySelector(".ad-member-tabs")).toBeNull();
    expect([...resultPane.querySelectorAll("button[aria-expanded]")].map(summary => summary.textContent)).toEqual(["Presentation", "Advanced"]);
    expect([...resultPane.querySelectorAll<HTMLElement>(".studio-disclosure-fold")].every(details => details.dataset.open === "false")).toBe(true);
    await waitFor(() => expect(document.activeElement).toBe(contractMemberRow(rendered.container, "output", "result").querySelector("input[data-contract-field='name']")));

    addContractMember(rendered.container, "outcome", "Rejected");
    await waitFor(() => expect(contractListButton(rendered.container, "outcome", "rejected").getAttribute("aria-expanded")).toBe("true"));
    expect(contractMemberPane(rendered.container, "outcome", "rejected").dataset.contractSelected).toBe("true");
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) => {
      const contract = (body as { contract: { inputs: Array<{ referenceKey: string }>; outputs: Array<{ referenceKey: string }>; outcomes: Array<{ referenceKey: string }> } }).contract;
      return contract.inputs.some(member => member.referenceKey === "approver") &&
        contract.outputs.some(member => member.referenceKey === "result") &&
        contract.outcomes.some(member => member.referenceKey === "rejected");
    })).toBe(true));
    await rendered.unmount();
  }, 15_000);

  it("blocks contract actions and keeps stored content private when authoring capabilities are forbidden", async () => {
    const putJson = vi.fn();
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/authoring-capabilities") throw new StudioHttpError(403, "forbidden", null);
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Contract options unavailable");
    showContractView(rendered.container);
    expect(rendered.container.textContent).toContain("Existing members remain visible");
    expect(rendered.container.textContent).toContain("Graph implementation editor");
    expect(categoryAddButton(rendered.container, "input").disabled).toBe(true);
    expect([...rendered.container.querySelectorAll("option")].some(option => option.textContent?.includes("System.String"))).toBe(false);
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("keeps unavailable historical aliases inspectable on an exact immutable version", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=versions&version=activity-version-1",
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url.startsWith("/design/activities/definitions/activity-def-1/versions?")) return page([]);
        if (url === "/design/activities/versions/activity-version-1") return {
          definition: definitionIdentity(),
          versionId: "activity-version-1",
          version: "1.0.0",
          lifecycle: "active",
          publishedAt: "2026-07-17T10:00:00Z",
          provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", manifestFingerprint: "sha256:historical" },
          contract: {
            contractSchemaVersion: "1",
            inputs: [{
              referenceKey: "legacy-value",
              name: "Legacy value",
              type: { alias: "Removed.Alias", collectionKind: "Single" },
              isRequired: false,
              isNullable: true,
              default: null,
              storageDriverKey: "historical.driver",
              durability: "Required"
            }],
            outputs: [],
            outcomes: []
          }
        };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Published inputs, outputs and outcomes");
    expect(rendered.container.textContent).toContain("Removed.Alias");
    expect(rendered.container.textContent).toContain("Stored aliases remain visible");
    expect(rendered.container.textContent).not.toContain("Replace type");
    await rendered.unmount();
  });

  it("retains invalid default text, blocks member selection and navigation, then allows navigation after applying it", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: { syntax: "Literal", value: null },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }, {
      referenceKey: "quantity",
      name: "Quantity",
      displayName: "Quantity",
      type: { alias: "Number", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: { syntax: "Literal", value: null },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const putJson = vi.fn(async (_url: string, body: unknown) => ({
      ...initial,
      revision: 4,
      contract: (body as { contract: typeof initial.contract }).contract
    }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/authoring-capabilities") {
          const capabilities = authoringCapabilities();
          capabilities.types.push({
            alias: "Number",
            displayName: "Number",
            category: "Primitives",
            defaultEditor: "number",
            supportedCollectionKinds: ["Single"],
            supportsNull: true,
            supportsDurability: true,
            compatibleStorageDriverKeys: ["elsa.json"]
          });
          return capabilities;
        }
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    showContractView(rendered.container);
    const quantityPane = selectContractMember(rendered.container, "input", "quantity");
    change(controlByLabel<HTMLSelectElement>(quantityPane, "Default"), "literal");
    change(controlByLabel<HTMLInputElement>(quantityPane, "Value"), "-");
    await waitForText(rendered.container, "Enter one finite number. This text is not saved.");
    await waitForText(rendered.container, "Apply or correct the highlighted default value before leaving this editor, testing, or publishing.");
    click(contractListButton(rendered.container, "input", "message"));
    const messagePane = contractMemberPane(rendered.container, "input", "message");
    expect(quantityPane.dataset.contractSelected).toBe("true");
    expect(quantityPane.hidden).toBe(false);
    expect(messagePane.hidden).toBe(true);
    expect(contractListButton(rendered.container, "input", "quantity").getAttribute("aria-expanded")).toBe("true");
    expect(contractListButton(rendered.container, "input", "message").getAttribute("aria-expanded")).toBe("false");
    await waitForText(rendered.container, "Correct the invalid literal text in this member");
    expect(quantityPane.querySelector<HTMLInputElement>("[data-contract-field='default.value']")?.value).toBe("-");
    expect(quantityPane.querySelector("[data-contract-field='default.value']")?.getAttribute("aria-invalid")).toBe("true");
    expect(contractMemberRow(rendered.container, "input", "quantity").querySelector(".ad-contract-default-status")?.textContent).toBe("Correction needed");
    expect(rendered.container.querySelector(".ad-save-state")?.textContent).toContain("Apply or correct the highlighted default value before leaving this editor, testing, or publishing.");
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Save now") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Designer") as HTMLButtonElement).disabled).toBe(true);
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    expect(window.location.pathname).toBe("/workflows/activity-definitions");
    expect(putJson).not.toHaveBeenCalled();
    // Empty collection tabs must not unmount the invalid input buffer or release save guards.
    const outputsTab = rendered.container.querySelector<HTMLButtonElement>(".ad-authoring-view-tabs [data-tab-id='outputs']")!;
    click(outputsTab);
    expect(outputsTab.getAttribute("aria-selected")).toBe("true");
    expect(quantityPane.closest<HTMLElement>(".ad-contract-editor")?.dataset.activeContractKind).toBe("output");
    expect(quantityPane.isConnected).toBe(true);
    expect(quantityPane.closest<HTMLElement>(".ad-contract-detail-row")?.hidden).toBe(true);
    expect(rendered.container.querySelector(".wf-properties-empty-row .wf-muted")?.textContent).toContain("No outputs defined");
    expect((buttonByText(rendered.container, "Save now") as HTMLButtonElement).disabled).toBe(true);
    showContractView(rendered.container);
    expect(quantityPane.hidden).toBe(false);
    expect(controlByLabel<HTMLInputElement>(quantityPane, "Value").value).toBe("-");

    change(controlByLabel<HTMLInputElement>(quantityPane, "Value"), "12");
    await waitFor(() => expect(contractMemberRow(rendered.container, "input", "quantity").querySelector(".ad-contract-default-status")?.textContent).toBe("Apply default"));
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    click(buttonByText(quantityPane, "Apply default"));
    await waitForText(rendered.container, "Saved revision 4");
    expect(putJson).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(true));
    selectContractMember(rendered.container, "input", "message");
    await rendered.unmount();
  });

  it("lets normal selection return from a diagnostic reveal to the member with a pending default", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.contract.inputs = ["message", "quantity", "summary"].map((referenceKey, index) => ({
      referenceKey,
      name: referenceKey,
      displayName: null,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: { syntax: "Literal", value: `saved-${index}` },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }));
    const postJson = vi.fn(async () => ({
      draftId: "activity-draft-1",
      revision: 3,
      isValid: false,
      validatedAt: "2026-07-17T10:01:00Z",
      diagnostics: [{
        code: "activity.contract.input-invalid",
        severity: "Error",
        message: "Review this input.",
        subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
        location: { jsonPointer: "/contract/inputs", referenceKey: "message" },
        remediation: null,
        metadata: {}
      }]
    }));
    const putJson = vi.fn();
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson,
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "Draft diagnostics");
    const diagnostic = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.contract.input-invalid']")!;
    click(diagnostic);
    await waitForText(rendered.container, "Focused the exact provider-neutral contract location");
    expect(contractMemberPane(rendered.container, "input", "message").dataset.contractSelected).toBe("true");

    const quantityPane = selectContractMember(rendered.container, "input", "quantity");
    const defaultValue = controlByLabel<HTMLInputElement>(quantityPane, "Value");
    change(defaultValue, "pending candidate");
    await waitFor(() => expect(contractMemberRow(rendered.container, "input", "quantity").querySelector(".ad-contract-default-status")?.textContent).toBe("Apply default"));
    await waitFor(() => expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false));

    click(diagnostic);
    await waitFor(() => expect(contractMemberPane(rendered.container, "input", "message").dataset.contractSelected).toBe("true"));
    expect(controlByLabel<HTMLInputElement>(quantityPane, "Value").value).toBe("pending candidate");
    selectContractMember(rendered.container, "input", "quantity");
    click(contractListButton(rendered.container, "input", "summary"));
    await waitForText(rendered.container, "Apply or correct this default before selecting another member");
    expect(quantityPane.dataset.contractSelected).toBe("true");
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  }, 10_000);

  it("reviews destructive published-baseline edits before changing only the exact draft", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.sourceVersionId = "activity-version-1";
    initial.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: false,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...initial, revision: 4, contract: (body as { contract: unknown }).contract }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/versions/activity-version-1") return { definition: definitionIdentity(), versionId: "activity-version-1", version: "1.0.0", contract: initial.contract, provider: initial.provider, lifecycle: "active", publishedAt: initial.updatedAt };
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Published baseline");
    showContractView(rendered.container);
    click(contractMemberRow(rendered.container, "input", "message").querySelector<HTMLButtonElement>(".wf-properties-remove")!);
    await waitForText(rendered.container, "Remove published-baseline input");
    expect(putJson).not.toHaveBeenCalled();
    expect(rendered.container.textContent).toContain("Activity Definitions");
    expect(rendered.container.textContent).toContain("None rewritten");
    expect(rendered.container.textContent).toContain("Workflows and occurrences");
    click(buttonByText(rendered.container, "Confirm input removal"));
    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({
      expectedRevision: 3,
      contract: expect.objectContaining({ inputs: [] })
    })));
    await rendered.unmount();
  });

  it("guards existing-member changes conservatively when the exact baseline cannot be loaded", async () => {
    const initial = fullDraft({ revision: 3 });
    initial.sourceVersionId = "missing-version";
    initial.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: false,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const putJson = vi.fn();
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return initial;
        if (url === "/design/activities/versions/missing-version") throw new StudioHttpError(404, "missing");
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Could not load the published version.");
    showContractView(rendered.container);
    click(contractMemberRow(rendered.container, "input", "message").querySelector<HTMLButtonElement>(".wf-properties-remove")!);
    await waitForText(rendered.container, "Remove published-baseline input");
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("autosaves presentation-only copy without impact review but stages a breaking technical name change", async () => {
    let serverDraft = fullDraft({ revision: 3 });
    serverDraft.sourceVersionId = "activity-version-1";
    serverDraft.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: false,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const baselineContract = structuredClone(serverDraft.contract);
    const putJson = vi.fn(async (_url: string, body: unknown) => {
      const request = body as { expectedRevision: number; contract: typeof serverDraft.contract };
      serverDraft = { ...serverDraft, revision: request.expectedRevision + 1, contract: request.contract };
      return serverDraft;
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return serverDraft;
        if (url === "/design/activities/versions/activity-version-1") return { definition: definitionIdentity(), versionId: "activity-version-1", version: "1.0.0", contract: baselineContract, provider: serverDraft.provider, lifecycle: "active", publishedAt: serverDraft.updatedAt };
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Published baseline");
    showContractView(rendered.container);
    click(buttonByText(rendered.container, "Presentation"));
    change(controlByLabel<HTMLInputElement>(rendered.container, "Display name"), "Friendly message");
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    expect(rendered.container.textContent).not.toContain("Review breaking input change");
    await waitForText(rendered.container, "Saved revision 4");

    const technicalName = contractMemberRow(rendered.container, "input", "message").querySelector<HTMLInputElement>("input[data-contract-field='name']")!;
    change(technicalName, "Renamed message");
    pressKey(technicalName, "Enter");
    await waitForText(rendered.container, "Review breaking input change");
    expect(putJson).toHaveBeenCalledTimes(1);
    expect(rendered.container.textContent).toContain("Display name for presentation-only copy");
    click(buttonByText(rendered.container, "Apply input change"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(2));
    expect(putJson.mock.calls[1]?.[1]).toEqual(expect.objectContaining({
      expectedRevision: 4,
      contract: expect.objectContaining({ inputs: [expect.objectContaining({ name: "Renamed message", displayName: "Friendly message" })] })
    }));
    await rendered.unmount();
  }, 12_000);

  it("confirms a published reference-key change and preserves the mounted editor state", async () => {
    let serverDraft = fullDraft({ revision: 3 });
    serverDraft.sourceVersionId = "activity-version-1";
    serverDraft.contract.inputs = [{
      referenceKey: "message",
      name: "Message",
      displayName: "Friendly message",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: false,
      default: { syntax: "Literal", value: "server" },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const baselineContract = structuredClone(serverDraft.contract);
    const putJson = vi.fn(async (_url: string, body: unknown) => {
      const request = body as { expectedRevision: number; contract: typeof serverDraft.contract };
      serverDraft = { ...serverDraft, revision: request.expectedRevision + 1, contract: request.contract };
      return serverDraft;
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return serverDraft;
        if (url === "/design/activities/versions/activity-version-1") return { definition: definitionIdentity(), versionId: "activity-version-1", version: "1.0.0", contract: baselineContract, provider: serverDraft.provider, lifecycle: "active", publishedAt: serverDraft.updatedAt };
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Published baseline");
    showContractView(rendered.container);
    const pane = selectContractMember(rendered.container, "input", "message");
    change(controlByLabel<HTMLInputElement>(pane, "Value"), "candidate");
    click(buttonByText(pane, "Advanced"));
    const key = controlByLabel<HTMLInputElement>(pane, "Stable reference key");
    expect(key.closest<HTMLElement>("[role='tabpanel']")?.hidden).toBe(false);
    change(key, "message-v2");
    click(buttonByText(pane, "Apply key"));
    await waitForText(rendered.container, "Change a published reference key");
    expect(putJson).not.toHaveBeenCalled();
    click(buttonByText(rendered.container, "Change reference key"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    expect(putJson.mock.calls[0]?.[1]).toEqual(expect.objectContaining({
      expectedRevision: 3,
      contract: expect.objectContaining({
        inputs: [expect.objectContaining({
          referenceKey: "message-v2",
          name: "Message",
          default: { syntax: "Literal", value: "server" }
        })]
      })
    }));
    await waitFor(() => expect(contractListButton(rendered.container, "input", "message-v2").getAttribute("aria-expanded")).toBe("true"));
    const renamedPane = contractMemberPane(rendered.container, "input", "message-v2");
    expect(renamedPane.dataset.contractSelected).toBe("true");
    expect(controlByLabel<HTMLInputElement>(renamedPane, "Value").value).toBe("candidate");
    expect((buttonByText(renamedPane, "Apply default") as HTMLButtonElement).disabled).toBe(false);

    click(buttonByText(renamedPane, "Apply default"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(2));
    await waitForText(rendered.container, "Saved revision 5");
    addContractMember(rendered.container, "input", "Message");
    await waitFor(() => expect(contractListButton(rendered.container, "input", "message").getAttribute("aria-expanded")).toBe("true"));

    const newPane = contractMemberPane(rendered.container, "input", "message");
    expect(newPane).not.toBe(renamedPane);
    expect(newPane.hidden).toBe(false);
    expect(renamedPane.hidden).toBe(true);
    expect(newPane.querySelector("[data-contract-field='default.value']")).toBeNull();
    expect(controlByLabel<HTMLSelectElement>(newPane, "Default").value).toBe("none");
    expect(controlByLabel<HTMLInputElement>(renamedPane, "Value").value).toBe("candidate");

    change(controlByLabel<HTMLSelectElement>(newPane, "Default"), "literal");
    change(controlByLabel<HTMLInputElement>(newPane, "Value"), "new candidate");
    click(buttonByText(newPane, "Apply default"));
    await waitFor(() => expect(putJson.mock.calls.some(([, body]) => {
      const inputs = (body as { contract: { inputs: Array<{ referenceKey: string; default: unknown }> } }).contract.inputs;
      return JSON.stringify(inputs.find(input => input.referenceKey === "message-v2")?.default) === JSON.stringify({ syntax: "Literal", value: "candidate" }) &&
        JSON.stringify(inputs.find(input => input.referenceKey === "message")?.default) === JSON.stringify({ syntax: "Literal", value: "new candidate" });
    })).toBe(true));

    selectContractMember(rendered.container, "input", "message-v2");
    expect(renamedPane.hidden).toBe(false);
    expect(newPane.hidden).toBe(true);
    expect(controlByLabel<HTMLInputElement>(renamedPane, "Value").value).toBe("candidate");
    expect(controlByLabel<HTMLInputElement>(newPane, "Value").value).toBe("new candidate");
    selectContractMember(rendered.container, "input", "message");
    expect(newPane.hidden).toBe(false);
    expect(renamedPane.hidden).toBe(true);
    expect(controlByLabel<HTMLInputElement>(newPane, "Value").value).toBe("new candidate");
    expect(controlByLabel<HTMLInputElement>(renamedPane, "Value").value).toBe("candidate");
    await rendered.unmount();
  }, 12_000);

  it("preserves stale local work and creates an explicit parallel recovery draft", async () => {
    const putJson = vi.fn(async () => {
      throw new StudioHttpError(409, "stale", null, {
        errorCode: "activity.draft.stale-revision",
        recovery: { currentRevision: 9, relation: "activity-draft-conflict-copies" }
      });
    });
    const postJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        if (url === "/design/activities/drafts/activity-draft-recovery") return fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: { edit: 1 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation"));
    await waitForText(rendered.container, "Local work preserved");
    expect(rendered.container.textContent).toContain("revision 9");
    expect(rendered.container.textContent).not.toContain("Force overwrite");
    click(buttonByText(rendered.container, "Create parallel recovery draft"));

    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/conflict-copies", expect.objectContaining({
      expectedSourceRevision: 9,
      provider: expect.objectContaining({ payload: { edit: 1 } })
    })));
    await waitFor(() => expect(new URLSearchParams(window.location.search).get("draft")).toBe("activity-draft-recovery"));
    await rendered.unmount();
  }, 10_000);

  it("previews scoped local recovery before applying it and clears it after save", async () => {
    const runtime: StudioRuntimeSettings = {
      identity: { tenantId: "tenant-1", subject: "author-1" },
      activityDefinitions: { localRecovery: { enabled: true, ttlMinutes: 30 } }
    };
    const offline = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new TypeError("offline"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(offline.container, "Saved revision 3");
    click(buttonByText(offline.container, "Edit implementation 0"));
    await waitForText(offline.container, "Offline");
    expect(window.localStorage.length).toBe(1);
    await offline.unmount();

    const putJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ revision: 4, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const recovered = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(recovered.container, "Unsaved local recovery available");
    expect(recovered.container.textContent).toContain("revision 3");
    expect(recovered.container.querySelector(".ad-authoring-identity")?.textContent).toContain("elsa.activity-graph");
    expect(recovered.container.textContent).toContain("never restores it silently");
    expect(recovered.container.textContent).toContain("Review recovered content");
    expect((buttonByText(recovered.container, "Review content before recovery") as HTMLButtonElement).disabled).toBe(true);
    expect(buttonByText(recovered.container, "Edit implementation 0")).toBeTruthy();
    openDetails(recovered.container.querySelector(".ad-recovery-card details")!);
    expect(recovered.container.textContent).toContain('"edit": 1');
    click(buttonByText(recovered.container, "Apply local recovery"));
    await waitForText(recovered.container, "Edit implementation 1");
    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 3, provider: expect.objectContaining({ payload: { edit: 1 } }) })));
    await waitForText(recovered.container, "Saved revision 4");
    expect(window.localStorage.length).toBe(0);
    await recovered.unmount();
  }, 12_000);

  it("reopens a published draft read-only and clears recovery for its immutable revision", async () => {
    const runtime: StudioRuntimeSettings = {
      identity: { tenantId: "tenant-1", subject: "author-1" },
      activityDefinitions: { localRecovery: { enabled: true, ttlMinutes: 30 } }
    };
    createActivityDefinitionRecoveryStore(runtime.activityDefinitions?.localRecovery, runtime.identity)!
      .write(fullDraft({ revision: 3, payload: { edit: 9 } }));
    const putJson = vi.fn();
    const postJson = vi.fn();
    const rendered = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, status: "Published", payload: { edit: 0 } });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url.startsWith("/design/activities/definitions/activity-def-1/drafts?")) return page([]);
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitFor(() => expect(rendered.container.querySelector(".wf-editor-title .wf-chip")?.textContent).toBe("Published"));
    await waitFor(() => expect(window.localStorage.length).toBe(0));
    expect(rendered.container.textContent).not.toContain("Published revision 3 is read-only.");
    expect(rendered.container.textContent).not.toContain("Unsaved local recovery available");
    expect(activityDefinitionBackLink(rendered.container).getAttribute("aria-disabled")).toBeNull();
    expect(rendered.container.querySelector(".ad-publication-toolbar")).toBeNull();
    expect((buttonByText(rendered.container, "Edit implementation 0") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Migrate provider") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Test Run") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Save now") as HTMLButtonElement).disabled).toBe(true);
    expect(rendered.container.querySelector(".wf-publish-actions")).toBeNull();
    expect(rendered.container.querySelector<HTMLInputElement>(".ad-draft-label input")?.disabled).toBe(true);

    const advanced = rendered.container.querySelector<HTMLDetailsElement>(".ad-authoring-advanced")!;
    expect(advanced.open).toBe(false);
    openDetails(advanced);
    click(buttonByText(advanced, "Edit code"));
    await waitFor(() => expect(rendered.container.querySelector("[data-tab-id='code']")).not.toBeNull());
    expect(rendered.container.querySelector<HTMLElement>(".studio-code-editor")?.dataset.readonly).toBe("true");
    expect(putJson).not.toHaveBeenCalled();
    expect(postJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("creates a parallel draft when local recovery is based on an older server revision", async () => {
    const runtime: StudioRuntimeSettings = {
      identity: { tenantId: "tenant-1", subject: "author-1" },
      activityDefinitions: { localRecovery: { enabled: true, ttlMinutes: 30 } }
    };
    createActivityDefinitionRecoveryStore(runtime.activityDefinitions?.localRecovery, runtime.identity)!
      .write(fullDraft({ revision: 3, payload: { edit: 7 } }));
    const putJson = vi.fn();
    const postJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const rendered = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 4, payload: { edit: 8 } });
        if (url === "/design/activities/drafts/activity-draft-recovery") return fullDraft({ draftId: "activity-draft-recovery", revision: 1, payload: { edit: 7 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "server is now at revision 4");
    openDetails(rendered.container.querySelector(".ad-recovery-card details")!);
    click(buttonByText(rendered.container, "Create recovery draft"));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/conflict-copies", expect.objectContaining({
      expectedSourceRevision: 4,
      provider: expect.objectContaining({ payload: { edit: 7 } })
    })));
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("preserves recovery without applying it when the exact provider editor is unavailable", async () => {
    const runtime: StudioRuntimeSettings = {
      identity: { tenantId: "tenant-1", subject: "author-1" },
      activityDefinitions: { localRecovery: { enabled: true } }
    };
    createActivityDefinitionRecoveryStore(runtime.activityDefinitions?.localRecovery, runtime.identity)!
      .write(fullDraft({ revision: 3, payload: { opaque: "local" } }));
    const putJson = vi.fn();
    const postJson = vi.fn();
    const rendered = renderPage({
      runtime,
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { opaque: "server" } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Implementation editor unavailable");
    expect(rendered.container.textContent).toContain("will not apply this opaque state");
    expect(rendered.container.textContent).toContain("Review recovered content");
    expect([...rendered.container.querySelectorAll("button")].some(button => button.textContent?.includes("Apply local recovery") || button.textContent?.includes("Create recovery draft"))).toBe(false);
    expect(buttonByText(rendered.container, "Discard recovery")).toBeTruthy();
    expect(putJson).not.toHaveBeenCalled();
    expect(postJson).not.toHaveBeenCalled();
    await rendered.unmount();
  });

  it("reports a failed save and keeps revision-sensitive actions paused", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new Error("safe test failure"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Save failed");
    expect(rendered.container.textContent).toContain("Revision-sensitive lifecycle actions and navigation are paused");
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(false);
    expect(rendered.container.textContent).not.toContain("safe test failure");
    await rendered.unmount();
  });

  it("makes Save now await the exact current draft revision before reporting completion", async () => {
    let resolveSave!: (value: ActivityDefinitionDraftView) => void;
    const pendingSave = new Promise<ActivityDefinitionDraftView>(resolve => { resolveSave = resolve; });
    const putJson = vi.fn(() => pendingSave);
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      getJson: async url => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    click(buttonByText(rendered.container, "Save now"));
    await waitFor(() => expect(putJson).toHaveBeenCalledTimes(1));
    expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({
      expectedRevision: 3,
      provider: expect.objectContaining({ payload: { edit: 1 } })
    }));
    expect(rendered.container.querySelector<HTMLElement>(".ad-save-state[role='status']")?.textContent).toContain("Saving");
    expect(rendered.container.textContent).not.toContain("Saved revision 4");

    resolveSave(fullDraft({ revision: 4, payload: { edit: 1 } }));
    await waitForText(rendered.container, "Saved revision 4");
    await rendered.unmount();
  });

  it("flushes pending autosave and validates only the exact returned saved revision", async () => {
    const postJson = vi.fn(async (_url: string, body: unknown) => ({ draftId: "activity-draft-1", revision: (body as { expectedRevision: number }).expectedRevision, isValid: true, validatedAt: "2026-07-17T10:01:00Z", diagnostics: [] }));
    const putJson = vi.fn(async (_url: string, body: unknown) => fullDraft({ revision: 4, payload: (body as { provider: { payload: unknown } }).provider.payload }));
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      postJson,
      putJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1", expect.objectContaining({ expectedRevision: 3 })));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/validate", { expectedRevision: 4 }));
    await waitForText(rendered.container, "Revision 4 passed validation");
    await rendered.unmount();
  });

  it("keeps background contract proposals outside autosave and adopts only an explicitly applied exact revision", async () => {
    const source = fullDraft({ revision: 3, payload: { edit: 0 } });
    const currency = {
      referenceKey: "currency",
      name: "Currency",
      displayName: "Currency",
      description: null,
      category: null,
      order: 0,
      uiHint: null,
      uiSpecifications: null,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    };
    const applied = {
      ...source,
      revision: 4,
      contract: { ...source.contract, inputs: [currency] }
    };
    const putJson = vi.fn();
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/contract-proposals")) return {
        draftId: source.draftId,
        revision: source.revision,
        providerKey: source.provider.providerKey,
        providerSchemaVersion: source.provider.schemaVersion,
        manifestFingerprint: source.provider.manifestFingerprint,
        proposalFingerprint: "sha256:proposal-3",
        changes: [{
          changeId: "input:add:currency",
          operation: "Add",
          memberKind: "Input",
          referenceKey: "currency",
          input: currency
        }],
        diagnostics: []
      };
      if (url.endsWith("/contract-proposals/apply")) return applied;
      throw new Error(`Unexpected POST ${url}`);
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return proposalCapabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return source;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/expressions/descriptors") return { items: [] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitFor(() => expect(rendered.container.querySelector(".ad-authoring-view-tabs [data-tab-id='inputs']")).not.toBeNull());
    showContractView(rendered.container);
    openDetails(rendered.container.querySelector<HTMLDetailsElement>(".ad-interface-suggestions")!);
    await waitForText(rendered.container, "Add input Currency");
    expect(putJson).not.toHaveBeenCalled();
    expect(rendered.container.textContent).toContain("1 of 1 changes selected");

    click(buttonByText(rendered.container, "Apply selected changes"));
    await waitFor(() => expect(postJson).toHaveBeenLastCalledWith(
      "/design/activities/drafts/activity-draft-1/contract-proposals/apply",
      {
        expectedRevision: 3,
        expectedProviderKey: "elsa.activity-graph",
        expectedProviderSchemaVersion: "1",
        expectedManifestFingerprint: "sha256:graph",
        proposalFingerprint: "sha256:proposal-3",
        selectedChangeIds: ["input:add:currency"]
      }
    ));
    await waitForText(rendered.container, "Saved revision 4");
    expect(rendered.container.querySelector("[data-contract-kind='input'][data-contract-reference-key='currency']")).not.toBeNull();
    expect(putJson).not.toHaveBeenCalled();
    await rendered.unmount();
  }, 15_000);

  it("renders ordered structured diagnostics with safe severity counts and typed unknown-location handling", async () => {
    const postJson = vi.fn(async () => ({
      draftId: "activity-draft-1",
      revision: 3,
      isValid: false,
      validatedAt: "2026-07-17T10:01:00Z",
      diagnostics: [
        {
          code: "activity.contract.input-invalid",
          severity: "Error",
          message: "Every input requires a reference key.",
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: { jsonPointer: "/contract/inputs", referenceKey: "customer-note" },
          remediation: "Provide a stable reference key.",
          metadata: {}
        },
        {
          code: "activity.future-location",
          severity: "Warning",
          message: "A future provider location needs attention.",
          subject: { kind: "ActivityDraft", id: "hidden-draft-id", revision: 3 },
          location: { providerKey: "future.provider", jsonPointer: "/future/private-location" },
          remediation: "Use a provider editor that supports this location.",
          metadata: {}
        },
        {
          code: "unsafe <private-code>",
          severity: "Info",
          message: "Validation completed with additional context.",
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: null,
          remediation: null,
          metadata: {}
        }
      ]
    }));
    const draft = fullDraft({ revision: 3 });
    draft.contract.inputs = [{
      referenceKey: "customer-note",
      name: "Customer note",
      displayName: "Customer note",
      description: null,
      category: null,
      order: 0,
      uiHint: null,
      uiSpecifications: null,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draft;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/expressions/descriptors") return { items: [] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "Draft diagnostics");
    expect(rendered.container.textContent).toContain("1 error");
    expect(rendered.container.textContent).toContain("1 warning");
    expect(rendered.container.textContent).toContain("1 info");
    expect(rendered.container.textContent).toContain("Provide a stable reference key.");
    const diagnosticsPanel = rendered.container.querySelector<HTMLElement>(".ad-diagnostics-panel")!;
    const diagnosticsDetails = diagnosticsPanel.querySelector("details")!;
    diagnosticsDetails.open = false;
    const scrollToDiagnostics = vi.fn();
    const firstDiagnostic = diagnosticsPanel.querySelector<HTMLElement>(".ad-diagnostic-item[tabindex]")!;
    firstDiagnostic.scrollIntoView = scrollToDiagnostics;
    expect(rendered.container.querySelectorAll(".ad-draft-diagnostics-link")).toHaveLength(1);
    click(rendered.container.querySelector(".ad-draft-diagnostics-link")!);
    expect(diagnosticsDetails.open).toBe(true);
    expect(document.activeElement).toBe(firstDiagnostic);
    expect(scrollToDiagnostics).toHaveBeenCalledWith({ block: "center" });
    const codes = [...rendered.container.querySelectorAll(".ad-diagnostic-code")].map(item => item.textContent);
    expect(codes).toEqual([
      "activity.contract.input-invalid",
      "activity.future-location",
      "activity.validation.issue"
    ]);
    const contractDiagnostic = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.contract.input-invalid']")!;
    click(contractDiagnostic);
    await waitForText(rendered.container, "Focused the exact provider-neutral contract location");
    expect((document.activeElement as HTMLElement)?.hasAttribute("data-contract-reference-key-control")).toBe(true);
    expect(document.activeElement?.closest<HTMLElement>("[role='tabpanel']")?.hidden).toBe(false);
    expect(rendered.container.querySelector(".ad-member-tabs [aria-selected='true']")?.textContent).toBe("Advanced");
    click(buttonByText(rendered.container, "Return to diagnostic"));
    expect(document.activeElement).toBe(contractDiagnostic);
    click(rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.future-location']")!);
    await waitForText(rendered.container, "The exact diagnostic location is unavailable");
    expect(rendered.container.textContent).not.toContain("hidden-draft-id");
    expect(rendered.container.textContent).not.toContain("future.provider");
    await rendered.unmount();
  });

  it("uses the JSON pointer member kind when diagnostic keys overlap across inputs and outputs", async () => {
    const draft = fullDraft({ revision: 3 });
    draft.contract.inputs = [{
      referenceKey: "shared",
      name: "Shared input",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: null,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    draft.contract.outputs = [{
      referenceKey: "shared",
      name: "Shared output",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => ({
        draftId: "activity-draft-1",
        revision: 3,
        isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: [{
          code: "activity.contract.output-invalid",
          severity: "Error",
          message: "Review this output.",
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: { jsonPointer: "/contract/outputs/0/name", referenceKey: "shared" },
          remediation: null,
          metadata: {}
        }]
      })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draft;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "Draft diagnostics");
    click(rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.contract.output-invalid']")!);
    const inputPane = contractMemberPane(rendered.container, "input", "shared");
    const outputPane = contractMemberPane(rendered.container, "output", "shared");
    await waitFor(() => expect(outputPane.dataset.contractSelected).toBe("true"));
    expect(outputPane.hidden).toBe(false);
    expect(inputPane.hidden).toBe(true);
    await waitFor(() => expect((document.activeElement as HTMLElement).dataset.contractField).toBe("name"));
    expect(contractMemberRow(rendered.container, "output", "shared").contains(document.activeElement)).toBe(true);
    await rendered.unmount();
  });

  it("reveals the mapped output before focusing its source when a frame precedes React's deferred render", async () => {
    const draft = fullDraft({ revision: 3 });
    draft.contract.outputs = ["First", "Second"].map(name => ({
      referenceKey: name.toLowerCase(), name,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false, isNullable: true, storageDriverKey: "elsa.json", durability: "Required"
    }));
    draft.provider.payload = { outputMappings: [{ outputReferenceKey: "second", source: null }] };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [graphContribution()],
      getJson: async url => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draft;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        throw new Error(`Unexpected GET ${url}`);
      },
      postJson: async () => ({
        draftId: draft.draftId, revision: draft.revision, isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: [{ code: "activity.graph.output-source", severity: "Error", message: "Choose the output source.",
          subject: { kind: "ActivityDraft", id: draft.draftId, revision: draft.revision },
          location: { providerKey: draft.provider.providerKey, jsonPointer: "/outputMappings/0/source" }, metadata: {} }]
      })
    });
    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.graph.output-source");
    await waitFor(() => expect(rendered.container.querySelector('.ad-output-value-source[data-output-reference-key="second"]')).not.toBeNull());
    const source = rendered.container.querySelector<HTMLElement>('.ad-output-value-source[data-output-reference-key="second"]')!;
    expect(source.closest("[hidden]")).not.toBeNull();
    let hiddenAtFocus: boolean | undefined;
    const originalFocus = HTMLElement.prototype.focus;
    const focus = vi.spyOn(HTMLElement.prototype, "focus").mockImplementation(function (this: HTMLElement, options) {
      if (this === source) hiddenAtFocus = Boolean(this.closest("[hidden]"));
      originalFocus.call(this, options);
    });
    // A frame is allowed to arrive before React commits an async continuation's state update.
    const frame = vi.spyOn(window, "requestAnimationFrame").mockImplementation(callback => {
      queueMicrotask(() => callback(performance.now()));
      return 1;
    });
    try {
      const diagnostic = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.output-source']")!;
      diagnostic.focus();
      click(diagnostic);
      await waitFor(() => expect(hiddenAtFocus).not.toBeUndefined());
      expect(hiddenAtFocus).toBe(false);
      expect(contractMemberPane(rendered.container, "output", "first").hidden).toBe(true);
      expect(contractMemberPane(rendered.container, "output", "second").hidden).toBe(false);
      expect(document.activeElement).toBe(source);
      click(buttonByText(rendered.container, "Return to diagnostic"));
      expect(document.activeElement).toBe(diagnostic);
    } finally {
      frame.mockRestore();
      focus.mockRestore();
      await rendered.unmount();
    }
  });

  it.each([[true, false], [false, false], [false, true]])("keeps empty-contract boundary mappings reachable (existing: %s, lazy: %s)", async (existingMappings, lazyMappings) => {
    const draft = fullDraft({ revision: 3 });
    draft.contract.outputs = [];
    draft.contract.outcomes = [];
    draft.provider.payload = { outcomeMappings: existingMappings ? [{ sourceOutcomeReferenceKey: "Done", boundaryOutcomeReferenceKey: "removed" }] : [] };
    const MappingEditor = () => <section className="ad-graph-outcome-mappings">Outcome mappings</section>;
    let resolveMapping!: (module: { default: typeof MappingEditor }) => void;
    const LazyMappingEditor = React.lazy(() => new Promise<{ default: typeof MappingEditor }>(resolve => { resolveMapping = resolve; }));
    const contribution: StudioActivityDefinitionImplementationEditorContribution = {
      ...graphContribution(),
      publicInterfaceComponent: lazyMappings ? LazyMappingEditor : MappingEditor
    };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [contribution],
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draft;
        throw new Error(`Unexpected GET ${url}`);
      },
      postJson: async () => ({
        draftId: draft.draftId, revision: draft.revision, isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: [{ code: "activity.graph.mapping", severity: "Error", message: "Remove the old mapping.",
          subject: { kind: "ActivityDraft", id: draft.draftId, revision: draft.revision },
          location: { providerKey: draft.provider.providerKey, jsonPointer: "/outcomeMappings/0" }, metadata: {} }]
      })
    });
    await waitForText(rendered.container, "Saved revision 3");
    expect(Boolean(rendered.container.querySelector("[aria-label='Provider boundary mappings']"))).toBe(existingMappings);
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.graph.mapping");
    click(rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.mapping']")!);
    if (lazyMappings) {
      await waitFor(() => expect(document.activeElement?.getAttribute("aria-label")).toBe("Provider boundary mappings"));
      await waitForText(rendered.container, "while its controls load");
      resolveMapping({ default: MappingEditor });
      await waitForText(rendered.container, "Outcome mappings");
      click(rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.mapping']")!);
    }
    await waitFor(() => expect(document.activeElement?.classList.contains("ad-graph-outcome-mappings")).toBe(true));
    expect(rendered.container.querySelector<HTMLElement>("[aria-label='Outcomes']")?.hidden).toBe(false);
    await rendered.unmount();
  });

  it("delegates provider locations through focusDiagnosticLocation and restores keyboard context", async () => {
    const focusDiagnosticLocation = vi.fn(({ editorElement }: { editorElement: HTMLElement }) => {
      editorElement.querySelector<HTMLButtonElement>("[data-provider-focus]")?.focus();
      return { kind: "focused" as const, announcement: "Focused the exact provider control." };
    });
    const contribution: StudioActivityDefinitionImplementationEditorContribution = {
      ...graphContribution(),
      focusDiagnosticLocation,
      component: () => <button type="button" data-provider-focus>Provider target</button>
    };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [contribution],
      postJson: vi.fn(async () => ({
        draftId: "activity-draft-1",
        revision: 3,
        isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: [{
          code: "activity.graph.node-invalid",
          severity: "Error",
          message: "The graph node is invalid.",
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: { providerKey: "elsa.activity-graph", jsonPointer: "/rootActivity/activityVersionId", referenceKey: "done" },
          remediation: "Choose an exact activity version.",
          metadata: {}
        }]
      })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.graph.node-invalid");
    const diagnostic = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.node-invalid']")!;
    click(diagnostic);
    await waitFor(() => expect((document.activeElement as HTMLElement)?.hasAttribute("data-provider-focus")).toBe(true));
    expect(focusDiagnosticLocation).toHaveBeenCalledWith(expect.objectContaining({
      location: expect.objectContaining({ jsonPointer: "/rootActivity/activityVersionId", referenceKey: "done" }),
      subject: expect.objectContaining({ kind: "ActivityDraft" })
    }));
    await waitForText(rendered.container, "Return to diagnostic");
    click(buttonByText(rendered.container, "Return to diagnostic"));
    expect(document.activeElement).toBe(diagnostic);
    focusDiagnosticLocation.mockRejectedValueOnce(new Error("hidden provider focus failure"));
    click(diagnostic);
    await waitForText(rendered.container, "exact diagnostic location is unavailable");
    expect(rendered.container.textContent).not.toContain("hidden provider focus failure");
    expect(rendered.container.textContent).toContain("activity.graph.node-invalid");
    await rendered.unmount();
  });

  it("keeps the latest diagnostic focus context when an older provider request completes late", async () => {
    let resolveFirst!: (result: StudioActivityDiagnosticFocusResult) => void;
    let resolveSecond!: (result: StudioActivityDiagnosticFocusResult) => void;
    const focusDiagnosticLocation = vi.fn()
      .mockImplementationOnce(() => new Promise<StudioActivityDiagnosticFocusResult>(resolve => { resolveFirst = resolve; }))
      .mockImplementationOnce(() => new Promise<StudioActivityDiagnosticFocusResult>(resolve => { resolveSecond = resolve; }));
    const contribution: StudioActivityDefinitionImplementationEditorContribution = {
      ...graphContribution(),
      focusDiagnosticLocation,
      component: () => <button type="button" data-provider-focus>Provider target</button>
    };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [contribution],
      postJson: vi.fn(async () => ({
        draftId: "activity-draft-1",
        revision: 3,
        isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: ["first", "second"].map(referenceKey => ({
          code: `activity.graph.${referenceKey}`,
          severity: "Error" as const,
          message: `Correct the ${referenceKey} graph location.`,
          subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
          location: { providerKey: "elsa.activity-graph", jsonPointer: `/rootActivity/${referenceKey}`, referenceKey },
          remediation: "Use the provider editor.",
          metadata: {}
        }))
      })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.graph.second");
    const first = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.first']")!;
    const second = rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.graph.second']")!;
    click(first);
    await waitFor(() => expect(focusDiagnosticLocation).toHaveBeenCalledTimes(1));
    click(second);
    await waitFor(() => expect(focusDiagnosticLocation).toHaveBeenCalledTimes(2));

    rendered.container.querySelector<HTMLButtonElement>("[data-provider-focus]")?.focus();
    resolveSecond({ kind: "focused", announcement: "Focused the second provider location." });
    await waitForText(rendered.container, "Focused the second provider location.");
    resolveFirst({ kind: "unsupported", announcement: "Stale first request must be ignored." });
    await new Promise(resolve => window.setTimeout(resolve, 0));

    expect(rendered.container.textContent).toContain("Focused the second provider location.");
    expect(rendered.container.textContent).not.toContain("Stale first request must be ignored.");
    click(buttonByText(rendered.container, "Return to diagnostic"));
    expect(document.activeElement).toBe(second);
    await rendered.unmount();
  });

  it("maps typed contract JSON pointers to exact accessible controls before using member fallback", async () => {
    const draft = fullDraft({ revision: 3 });
    draft.contract.inputs = [{
      referenceKey: "customer-note",
      name: "Customer note",
      displayName: "Customer note",
      description: null,
      category: null,
      order: 0,
      uiHint: null,
      uiSpecifications: null,
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: { syntax: "Literal", value: "hello" },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }];
    const pointers = [
      ["activity.contract.type", "/contract/inputs/0/type/alias", "type.alias"],
      ["activity.contract.storage", "/contract/inputs/0/storageDriverKey", "storageDriverKey"],
      ["activity.contract.default", "/contract/inputs/0/default/value", "default.value"],
      ["activity.contract.reference", "/contract/inputs/0/referenceKey", "referenceKey"]
    ] as const;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => ({
        draftId: "activity-draft-1",
        revision: 3,
        isValid: false,
        validatedAt: "2026-07-17T10:01:00Z",
        diagnostics: [
          ...pointers.map(([code, jsonPointer]) => ({
            code,
            severity: "Error" as const,
            message: "Correct this exact contract field.",
            subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
            location: { jsonPointer, referenceKey: "customer-note" },
            remediation: "Use the focused control.",
            metadata: {}
          })),
          {
            code: "activity.contract.emitted",
            severity: "Error",
            message: "The required outcome must remain emitted.",
            subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
            location: { jsonPointer: "/contract/outcomes/0/isEmitted", referenceKey: "done" },
            remediation: "Retain the provider-required outcome.",
            metadata: {}
          }
        ]
      })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draft;
        if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
        if (url === "/expressions/descriptors") return { items: [] };
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "activity.contract.reference");
    for (const [code, , expectedField] of pointers) {
      click(rendered.container.querySelector<HTMLButtonElement>(`[aria-label='Focus ${code}']`)!);
      await waitFor(() => {
        expect((document.activeElement as HTMLElement)?.dataset.contractField).toBe(expectedField);
        expect(buttonByText(rendered.container, "Return to diagnostic")).toBeTruthy();
      });
      click(buttonByText(rendered.container, "Return to diagnostic"));
    }
    click(rendered.container.querySelector<HTMLButtonElement>("[aria-label='Focus activity.contract.emitted']")!);
    await waitFor(() => {
      const focused = document.activeElement as HTMLElement;
      expect(focused.dataset.contractField).toBe("isEmitted");
      expect(focused.getAttribute("aria-label")).toBe("Done emitted");
      expect((focused as HTMLLabelElement).querySelector<HTMLInputElement>("input")?.checked).toBe(true);
    });
    expect(rendered.container.textContent).toContain("exact provider-neutral contract location");
    await rendered.unmount();
  });

  it.each([
    [403, "Draft validation is not authorized"],
    [404, "exact authorized draft could not be confirmed"],
    [503, "Draft validation is unavailable"]
  ])("keeps a %s validation failure privacy-safe", async (status, expected) => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => { throw new StudioHttpError(status, "hidden provider/resource identity", null); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, expected);
    expect(rendered.container.textContent).not.toContain("hidden provider/resource identity");
    await rendered.unmount();
  });

  it("distinguishes a missing validation capability from a transport failure", async () => {
    const unavailableCapabilities = capabilities();
    unavailableCapabilities.capabilities[0]!.links = unavailableCapabilities.capabilities[0]!.links
      .filter(link => link.rel !== "activity-draft-validation");
    const unavailable = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      getJson: async (url: string) => {
        if (url === "/capabilities") return unavailableCapabilities;
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(unavailable.container, "Saved revision 3");
    click(buttonByText(unavailable.container, "Validate saved revision"));
    await waitForText(unavailable.container, "advertised capability could not be confirmed");
    await unavailable.unmount();

    const transport = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => { throw new TypeError("hidden network detail"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });
    await waitForText(transport.container, "Saved revision 3");
    click(buttonByText(transport.container, "Validate saved revision"));
    await waitForText(transport.container, "could not reach the server");
    expect(transport.container.textContent).toContain("was not classified as invalid");
    expect(transport.container.textContent).not.toContain("hidden network detail");
    await transport.unmount();
  });

  it("distinguishes exact-revision validation rejection from transport and Runtime rejection", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async () => {
        throw new StudioHttpError(409, "stale", null, {
          errorCode: "activity.draft.stale-revision",
          recovery: { currentRevision: 4 }
        });
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitForText(rendered.container, "backend rejected validation");
    expect(rendered.container.textContent).toContain("server draft advanced to revision 4");
    expect(rendered.container.textContent).toContain("Runtime rejection is reported by the Test Run experience");
    expect(rendered.container.textContent).not.toContain("could not reach the server");
    await rendered.unmount();
  });

  it("ignores a late validation response after the author advances the draft", async () => {
    let rejectValidation!: (reason: unknown) => void;
    const validation = new Promise((_resolve, reject) => { rejectValidation = reject; });
    const postJson = vi.fn(async () => validation);
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      postJson,
      putJson: vi.fn(async (_url: string, body: unknown) => fullDraft({ revision: 4, payload: (body as { provider: { payload: unknown } }).provider.payload })),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Validate saved revision"));
    await waitFor(() => expect(postJson).toHaveBeenCalledTimes(1));
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Saved revision 4");
    rejectValidation(new StudioHttpError(409, "stale", null, { errorCode: "activity.draft.stale-revision", recovery: { currentRevision: 4 } }));
    await waitFor(() => expect(rendered.container.textContent).not.toContain("Local work preserved"));
    expect(rendered.container.textContent).toContain("Saved revision 4");
    await rendered.unmount();
  });

  it("blocks editor navigation while an unsaved failure is present", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new Error("failed"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Save failed");
    expect(activityDefinitionBackLink(rendered.container).getAttribute("aria-disabled")).toBe("true");
    window.history.pushState({}, "", "/workflows/activity-definitions");
    window.dispatchEvent(new PopStateEvent("popstate"));
    expect(new URLSearchParams(window.location.search).get("section")).toBe("editor");
    expect(requestStudioNavigation(window.location.pathname, "/dashboard")).toBe(false);
    expect(buttonByText(rendered.container, "Discard local changes and return")).toBeTruthy();
    await rendered.unmount();
  });

  it("restores the displayed origin after a dirty editor vetoes Back to another collection entry", async () => {
    const baseUrl = "test://activity-definition-history-veto";
    const displayedCollection = {
      entryId: "history-veto-displayed-origin",
      runtimeScopeId: "test-runtime",
      baseUrl,
      authorityRevision: Number(window.sessionStorage.getItem("elsaActivityDefinitions.authorityRevision") ?? 0),
      searchInput: "invoice",
      search: "invoice",
      providerInput: "",
      providerKey: "",
      authority: "Design" as const,
      pageSize: 25 as const,
      cursorStack: [null],
      cursorIndex: 0
    };
    const destinationCollection = { ...displayedCollection, entryId: "history-veto-destination", searchInput: "different origin", search: "different origin" };
    const stateAtOrigin = { elsaActivityDefinitions: { collection: destinationCollection } };
    const stateAtWorkbench = { elsaActivityDefinitions: { collection: displayedCollection } };
    const stateInEditor = { elsaActivityDefinitions: { collection: displayedCollection } };
    const rendered = renderPage({
      baseUrl,
      historyEntries: [
        { url: "/workflows/activity-definitions", state: stateAtOrigin },
        { url: "/workflows/activity-definitions?definition=activity-def-1", state: stateAtWorkbench },
        { url: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1", state: stateInEditor }
      ],
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new Error("failed"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url.startsWith("/design/activities/definitions?")) return page([managementDefinition()]);
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Save failed");
    const pushState = vi.spyOn(window.history, "pushState");
    window.history.replaceState(stateAtWorkbench, "", "/workflows/activity-definitions?definition=activity-def-1");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitFor(() => expect(pushState).toHaveBeenCalled());
    expect(window.history.state.elsaActivityDefinitions.collection).toEqual(displayedCollection);
    pushState.mockRestore();

    const historyGo = vi.spyOn(window.history, "go");
    click(buttonByText(rendered.container, "Discard local changes and return"));
    await waitFor(() => expect(rendered.container.querySelector(".ad-collection")).not.toBeNull());
    expect(new URLSearchParams(window.location.search).get("section")).toBeNull();
    expect(historyGo).not.toHaveBeenCalled();
    expect(window.location.pathname).toBe("/workflows/activity-definitions");
    expect(window.history.state.elsaActivityDefinitions.collection).toMatchObject({
      entryId: displayedCollection.entryId,
      searchInput: displayedCollection.searchInput,
      search: displayedCollection.search
    });
    expect(rendered.container.querySelector<HTMLInputElement>("input[placeholder='Name, type key, or category']")?.value).toBe("invoice");
    historyGo.mockRestore();
    await rendered.unmount();
  }, 15000);

  it("waits for an authoritative refetch instead of opening a cached draft revision", async () => {
    let resolveDraft!: (value: unknown) => void;
    const draftResponse = new Promise(resolve => { resolveDraft = resolve; });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      seedDraft: fullDraft({ revision: 3, payload: { edit: 0 } }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return draftResponse;
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Loading the exact Activity Definition draft");
    expect(rendered.container.textContent).not.toContain("Saved revision 3");
    resolveDraft(fullDraft({ revision: 4, payload: { edit: 1 } }));
    await waitForText(rendered.container, "Saved revision 4");
    await rendered.unmount();
  });

  it("keeps offline local work mounted across focus and reconnect events", async () => {
    let draftReads = 0;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson: vi.fn(async () => { throw new TypeError("offline"); }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") {
          draftReads += 1;
          return fullDraft({ revision: 3, payload: { edit: 0 } });
        }
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    await waitForText(rendered.container, "Offline");
    window.dispatchEvent(new Event("focus"));
    window.dispatchEvent(new Event("online"));
    await new Promise(resolve => window.setTimeout(resolve, 25));

    expect(draftReads).toBe(1);
    expect(rendered.container.textContent).toContain("Edit implementation 1");
    expect(rendered.container.textContent).toContain("Offline");
    await rendered.unmount();
  });
});

describe("Activity Definition publication", () => {
  it("flushes autosave, binds preflight to the exact revision and head, and publishes a higher exact version once", async () => {
    const putJson = vi.fn(async (_url: string, body: unknown) => fullDraft({
      revision: 4,
      payload: (body as { provider: { payload: unknown } }).provider.payload
    }));
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/publication-preflight")) return publicationPreflight({
        draftRevision: (body as { expectedDraftRevision: number }).expectedDraftRevision,
        definitionHeadVersionId: "version-head",
        hasBaseline: true,
        minimumVersion: "2.0.0"
      });
      if (url.endsWith("/publish")) {
        const request = body as { version: string; idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string };
        return publicationReceipt({
          idempotencyKey: request.idempotencyKey,
          expectedDraftRevision: request.expectedDraftRevision,
          expectedDefinitionHeadVersionId: request.expectedDefinitionHeadVersionId ?? null,
          reviewToken: request.reviewToken,
          requestedVersion: request.version,
          outcome: {
            ...publicationReceipt().outcome!,
            version: request.version
          }
        });
      }
      throw new Error(`Unexpected POST ${url}`);
    });
    const detail: ActivityDefinitionManagementView = managementDefinition();
    detail.definition.headVersionId = "version-head";
    detail.definition.recommendedVersionId = "version-recommended";
    detail.lifecycle.head = { versionId: "version-head", version: "1.4.0", lifecycle: "Active", providerKey: "elsa.activity-graph", providerSchemaVersion: "1" };
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution()],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        if (url === "/design/activities/definitions/activity-def-1") return detail;
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    click(buttonByText(rendered.container, "Review & publish"));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith(
      "/design/activities/drafts/activity-draft-1/publication-preflight",
      { expectedDraftRevision: 4, expectedDefinitionHeadVersionId: "version-head" }
    ));
    await waitForText(rendered.container, "Compared with latest published version");
    expect((controlByLabel<HTMLInputElement>(rendered.container, "Publication version")).value).toBe("2.0.0");
    change(controlByLabel<HTMLInputElement>(rendered.container, "Publication version"), "3.2.1");
    click(buttonByText(rendered.container, "Publish 3.2.1"));
    await waitForText(rendered.container, "Version 3.2.1");
    expect(rendered.container.querySelector(".ad-publication-toolbar")).toBeNull();
    expect(rendered.container.querySelector(".ad-save-state.is-published")).toBeNull();
    expect(rendered.container.querySelector(".wf-editor-title .wf-chip")?.textContent).toBe("Published");
    expect(rendered.container.querySelector(".ad-authoring-more-actions > summary")).toBe(document.activeElement);
    expect(rendered.container.textContent).not.toContain("View publication");
    expect(postJson.mock.calls.filter(([url]) => String(url).endsWith("/publish"))).toHaveLength(1);
    expect(postJson).toHaveBeenCalledWith("/design/activities/drafts/activity-draft-1/publish", expect.objectContaining({
      expectedDraftRevision: 4,
      expectedDefinitionHeadVersionId: "version-head",
      version: "3.2.1",
      reviewToken: "sha256:review"
    }));
    expect(activityDefinitionBackLink(rendered.container).getAttribute("aria-disabled")).toBeNull();
    expect((buttonByText(rendered.container, "Edit implementation 1") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Test Run") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Save now") as HTMLButtonElement).disabled).toBe(true);
    await rendered.unmount();
  });

  it("refreshes the published head and retains only persistent version feedback after publication", async () => {
    let definitionReads = 0;
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/publication-preflight")) return publicationPreflight();
      if (url.endsWith("/publish")) {
        const request = body as { version: string; idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string };
        return publicationReceipt({
          idempotencyKey: request.idempotencyKey,
          expectedDraftRevision: request.expectedDraftRevision,
          expectedDefinitionHeadVersionId: request.expectedDefinitionHeadVersionId ?? null,
          reviewToken: request.reviewToken,
          requestedVersion: request.version
        });
      }
      throw new Error(`Unexpected POST ${url}`);
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") {
          definitionReads += 1;
          return managementDefinition();
        }
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Review & publish"));
    await waitForText(rendered.container, "No latest published version");
    click(buttonByText(rendered.container, "Publish 1.0.0"));

    await waitForText(rendered.container, "Version 1.0.0");
    await waitFor(() => expect(definitionReads).toBeGreaterThan(1));
    expect(rendered.container.querySelector(".ad-publication-toolbar")).toBeNull();
    expect(rendered.container.querySelector(".ad-publication-summary")).toBeNull();
    expect(rendered.container.querySelector(".ad-save-state.is-published")).toBeNull();
    expect(rendered.container.textContent).not.toContain("View publication");
    expect(rendered.container.querySelector(".wf-editor-title .wf-chip")?.textContent).toBe("Published");
    // The definition management view is re-fetched so downstream panels reflect the new version.
    await waitFor(() => expect(postJson.mock.calls.filter(([url]) => String(url).endsWith("/publish"))).toHaveLength(1));
    await rendered.unmount();
  });

  it("locks the reviewed draft through ambiguous publication and keeps it locked until receipt reconciliation", async () => {
    let resolvePublish: ((value: unknown) => void) | null = null;
    let publishRequest: { version: string; idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string } | null = null;
    let receiptCalls = 0;
    const updateCapture: { current: StudioActivityDefinitionImplementationEditorProps["onChange"] | null } = { current: null };
    const putJson = vi.fn(async (_url: string, body: unknown) => fullDraft({
      revision: 4,
      payload: (body as { provider: { payload: unknown } }).provider.payload
    }));
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/publication-preflight")) {
        return publicationPreflight({ draftRevision: (body as { expectedDraftRevision: number }).expectedDraftRevision });
      }
      if (url.endsWith("/publish")) {
        publishRequest = body as typeof publishRequest;
        return await new Promise(resolve => { resolvePublish = resolve; });
      }
      throw new Error(`Unexpected POST ${url}`);
    });
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      contributions: [editingContribution(updateCapture)],
      putJson,
      postJson,
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3, payload: { edit: 0 } });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url.startsWith("/design/activities/publications/")) {
          receiptCalls += 1;
          return publicationReceipt({
            idempotencyKey: publishRequest!.idempotencyKey,
            expectedDraftRevision: publishRequest!.expectedDraftRevision,
            expectedDefinitionHeadVersionId: publishRequest!.expectedDefinitionHeadVersionId ?? null,
            reviewToken: publishRequest!.reviewToken,
            requestedVersion: publishRequest!.version
          });
        }
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Edit implementation 0"));
    click(buttonByText(rendered.container, "Review & publish"));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith(
      "/design/activities/drafts/activity-draft-1/publication-preflight",
      { expectedDraftRevision: 4, expectedDefinitionHeadVersionId: null }
    ));
    await waitForText(rendered.container, "Minimum valid version: 1.0.0");
    click(buttonByText(rendered.container, "Publish 1.0.0"));
    await waitForText(rendered.container, "Publishing atomically…");
    await waitFor(() => expect(publishRequest).not.toBeNull());

    const publishBody = putJson.mock.calls[0][1] as { provider: { payload: { edit: number } } };
    expect(publishBody.provider.payload.edit).toBe(1);
    expect((buttonByText(rendered.container, "Edit implementation 1") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Migrate provider") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Test Run") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Validate saved revision") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Save now") as HTMLButtonElement).disabled).toBe(true);

    flushSync(() => updateCapture.current?.({ payload: { edit: 2 }, layout: [] }));
    expect(buttonByText(rendered.container, "Edit implementation 1")).toBeTruthy();
    expect(putJson).toHaveBeenCalledTimes(1);

    resolvePublish!(publicationReceipt({
      idempotencyKey: publishRequest!.idempotencyKey,
      expectedDraftRevision: publishRequest!.expectedDraftRevision,
      expectedDefinitionHeadVersionId: publishRequest!.expectedDefinitionHeadVersionId ?? null,
      reviewToken: publishRequest!.reviewToken,
      requestedVersion: publishRequest!.version,
      status: "OutcomeUnknown"
    }));
    await waitForText(rendered.container, "not yet authoritative");
    expect((buttonByText(rendered.container, "Edit implementation 1") as HTMLButtonElement).disabled).toBe(true);
    expect((buttonByText(rendered.container, "Test Run") as HTMLButtonElement).disabled).toBe(true);
    expect(controlByLabel<HTMLInputElement>(rendered.container, "Publication version").disabled).toBe(true);
    click(buttonByText(rendered.container, "Reconcile authoritative status"));
    await waitForText(rendered.container, "Version 1.0.0");
    expect(receiptCalls).toBe(1);
    expect(putJson).toHaveBeenCalledTimes(1);
    expect(rendered.container.querySelector(".ad-publication-toolbar")).toBeNull();
    expect(rendered.container.textContent).not.toContain("View publication");
    expect(buttonByText(rendered.container, "Edit implementation 1")).toBeTruthy();
    expect((buttonByText(rendered.container, "Edit implementation 1") as HTMLButtonElement).disabled).toBe(true);
    expect(rendered.container.querySelector(".ad-save-state.is-published")).toBeNull();
    await rendered.unmount();
  });

  it("shows the definition display name in the draft editor header", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    await waitFor(() => expect(rendered.container.querySelector("#activity-draft-title")?.textContent).toBe("Invoice evaluator"));
    await rendered.unmount();
  });

  it("renders first-publication baseline, impact ordering, redaction, and blocking readiness honestly", async () => {
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async (url: string) => {
        if (url.endsWith("/publication-preflight")) return publicationPreflight({
          impactFirstChanges: [
            publicationChange({ changeId: "secret", impact: "Breaking", area: "Provider", kind: "provider.secret-rotated", before: { redacted: true }, after: { kind: "Redacted" } }),
            publicationChange({ changeId: "future", impact: "ProviderFutureImpact", area: "FutureArea", kind: "<future-kind>", message: "A provider-specific change was reported." })
          ],
          provider: { kind: "Provider", key: "elsa.activity-graph", schemaVersion: "1", status: "Unavailable", supportedSchemaVersions: [] },
          isPublishable: false,
          diagnostics: [{
            code: "activity.runtime.consumer-missing",
            severity: "Error",
            message: "Required Runtime consumer is missing.",
            subject: { kind: "ActivityDraft", id: "activity-draft-1", revision: 3 },
            location: null,
            remediation: "Install the required Runtime consumer.",
            metadata: {}
          }]
        });
        throw new Error(`Unexpected POST ${url}`);
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Review & publish"));
    await waitForText(rendered.container, "No published baseline exists");
    expect(rendered.container.textContent).toContain("Protected value redacted");
    expect(rendered.container.textContent).not.toContain("raw-secret");
    expect(rendered.container.textContent).toContain("Provider-specific change");
    expect(rendered.container.textContent).toContain("Other impact · ProviderFutureImpact");
    expect(rendered.container.textContent).toContain("Publication blocked");
    expect((buttonByText(rendered.container, "Publish 1.0.0") as HTMLButtonElement).disabled).toBe(true);
    await rendered.unmount();
  });

  it("discards a stale review without retrying publication writes", async () => {
    let publishCalls = 0;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async (url: string) => {
        if (url.endsWith("/publication-preflight")) return publicationPreflight();
        if (url.endsWith("/publish")) {
          publishCalls += 1;
          throw new StudioHttpError(409, "hidden stale detail", null, { errorCode: "activity.publication.review-stale" });
        }
        throw new Error(`Unexpected POST ${url}`);
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Review & publish"));
    await waitForText(rendered.container, "Minimum valid version: 1.0.0");
    click(buttonByText(rendered.container, "Publish 1.0.0"));
    await waitForText(rendered.container, "reopened preflight without publishing");
    expect(publishCalls).toBe(1);
    expect(rendered.container.textContent).not.toContain("hidden stale detail");
    expect(rendered.container.textContent).not.toContain("Published immutable version");
    await rendered.unmount();
  });

  it("reconciles an ambiguous publish response through the authoritative receipt without resubmitting", async () => {
    let publishCalls = 0;
    let receiptCalls = 0;
    let publishRequest: { version: string; idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string } | null = null;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async (url: string, body: unknown) => {
        if (url.endsWith("/publication-preflight")) return publicationPreflight();
        if (url.endsWith("/publish")) {
          publishCalls += 1;
          publishRequest = body as typeof publishRequest;
          throw new TypeError("ambiguous transport outcome");
        }
        throw new Error(`Unexpected POST ${url}`);
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        if (url.startsWith("/design/activities/publications/")) {
          receiptCalls += 1;
          return publicationReceipt({
            idempotencyKey: publishRequest!.idempotencyKey,
            expectedDraftRevision: publishRequest!.expectedDraftRevision,
            expectedDefinitionHeadVersionId: publishRequest!.expectedDefinitionHeadVersionId ?? null,
            reviewToken: publishRequest!.reviewToken,
            requestedVersion: publishRequest!.version
          });
        }
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Review & publish"));
    await waitForText(rendered.container, "Minimum valid version: 1.0.0");
    click(buttonByText(rendered.container, "Publish 1.0.0"));
    await waitForText(rendered.container, "Version 1.0.0");
    expect(rendered.container.querySelector(".ad-publication-toolbar")).toBeNull();
    expect(rendered.container.textContent).not.toContain("View publication");
    expect(publishCalls).toBe(1);
    expect(receiptCalls).toBe(1);
    await rendered.unmount();
  });

  it.each([
    {
      name: "mismatched receipt envelope",
      receiptOverrides: { requestedVersion: "9.9.9" },
      expectedMessage: "did not match this reviewed operation"
    },
    {
      name: "mismatched applied outcome",
      receiptOverrides: { outcome: { ...publicationReceipt().outcome!, definitionId: "other-definition" } },
      expectedMessage: "did not contain an exact matching outcome"
    },
    {
      name: "unrecognized non-terminal status",
      receiptOverrides: { status: "Preparing" },
      expectedMessage: "not in a recognized terminal state"
    }
  ] satisfies Array<{ name: string; receiptOverrides: Record<string, unknown>; expectedMessage: string }>)("keeps an unresolved $name reconciliation-only without opening a second publication", async ({ receiptOverrides, expectedMessage }) => {
    let preflightCalls = 0;
    let publishCalls = 0;
    const rendered = renderPage({
      path: "/workflows/activity-definitions?definition=activity-def-1&section=editor&draft=activity-draft-1",
      postJson: vi.fn(async (url: string, body: unknown) => {
        if (url.endsWith("/publication-preflight")) {
          preflightCalls += 1;
          return publicationPreflight();
        }
        if (url.endsWith("/publish")) {
          publishCalls += 1;
          const request = body as { idempotencyKey: string; expectedDraftRevision: number; expectedDefinitionHeadVersionId?: string | null; reviewToken: string; version: string };
          return publicationReceipt({
            idempotencyKey: request.idempotencyKey,
            expectedDraftRevision: request.expectedDraftRevision,
            expectedDefinitionHeadVersionId: request.expectedDefinitionHeadVersionId ?? null,
            reviewToken: request.reviewToken,
            requestedVersion: request.version,
            ...receiptOverrides
          });
        }
        throw new Error(`Unexpected POST ${url}`);
      }),
      getJson: async (url: string) => {
        if (url === "/capabilities") return capabilities();
        if (url === "/design/activities/drafts/activity-draft-1") return fullDraft({ revision: 3 });
        if (url === "/design/activities/definitions/activity-def-1") return managementDefinition();
        throw new Error(`Unexpected GET ${url}`);
      }
    });

    await waitForText(rendered.container, "Saved revision 3");
    click(buttonByText(rendered.container, "Review & publish"));
    await waitForText(rendered.container, "Minimum valid version: 1.0.0");
    click(buttonByText(rendered.container, "Publish 1.0.0"));
    await waitForText(rendered.container, expectedMessage);
    const reopen = buttonByText(rendered.container, "Refresh review") as HTMLButtonElement;
    expect(reopen.disabled).toBe(true);
    click(reopen);
    expect(preflightCalls).toBe(1);
    expect(publishCalls).toBe(1);
    await rendered.unmount();
  });

  it("validates exact semantic-version precedence and keeps protected values opaque", () => {
    expect(isExactVersionAtLeast("2.0.0", "2.0.0")).toBe(true);
    expect(isExactVersionAtLeast("7.3.2+build.4", "2.0.0")).toBe(true);
    expect(isExactVersionAtLeast("2.0.0-rc.1", "2.0.0")).toBe(false);
    expect(isExactVersionAtLeast("2.0.0-999999999999999999999999", "2.0.0-1000000000000000000000000")).toBe(false);
    expect(isExactVersionAtLeast("2.0.0-1000000000000000000000000", "2.0.0-999999999999999999999999")).toBe(true);
    expect(isExactVersionAtLeast("02.0.0", "2.0.0")).toBe(false);
    expect(safeChangeValue(publicationChange({ kind: "provider.credential-changed" }), { token: "raw" })).toBe("Protected value withheld");
    expect(safeChangeValue(publicationChange(), { nested: "opaque" })).toBe("Structured value changed");
    expect(safeChangeValue(publicationChange(), { redacted: true, nested: "must-not-render" })).toBe("Protected value redacted");
  });
});

describe("Activity Definition local recovery policy", () => {
  it("is disabled by default and expires within the configured identity scope", () => {
    const draft = { ...fullDraft({ revision: 3, payload: { edit: 1 } }), activityPresentation: [{ nodeId: "root", displayName: "Local name", description: "Local context" }] };
    expect(createActivityDefinitionRecoveryStore(undefined, { tenantId: "tenant-1", subject: "author-1" })).toBeNull();

    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-07-17T10:00:00Z"));
    const author = createActivityDefinitionRecoveryStore({ enabled: true, ttlMinutes: 1 }, { tenantId: "tenant-1", subject: "author-1" })!;
    const otherAuthor = createActivityDefinitionRecoveryStore({ enabled: true, ttlMinutes: 1 }, { tenantId: "tenant-1", subject: "author-2" })!;
    author.write(draft);
    expect(author.read(draft)?.payload).toEqual({ edit: 1 });
    expect(author.read(draft)?.activityPresentation).toEqual(draft.activityPresentation);
    expect(otherAuthor.read(draft)).toBeNull();

    vi.setSystemTime(new Date("2026-07-17T10:01:01Z"));
    expect(author.read(draft)).toBeNull();
    expect(window.localStorage.length).toBe(0);
  });

  it("clears every saved draft for the logging-out identity without crossing into another identity", () => {
    const authorIdentity = { tenantId: "tenant-1", subject: "author-1" };
    const author = createActivityDefinitionRecoveryStore({ enabled: true }, authorIdentity)!;
    const otherAuthor = createActivityDefinitionRecoveryStore({ enabled: true }, { tenantId: "tenant-1", subject: "author-2" })!;
    author.write(fullDraft({ draftId: "draft-a", payload: { edit: 1 } }));
    author.write(fullDraft({ draftId: "draft-b", payload: { edit: 2 } }));
    otherAuthor.write(fullDraft({ draftId: "draft-c", payload: { edit: 3 } }));

    clearActivityDefinitionRecoveryForIdentity(authorIdentity);

    expect(window.localStorage.length).toBe(1);
    expect(otherAuthor.read(fullDraft({ draftId: "draft-c" }))?.payload).toEqual({ edit: 3 });
  });

  it("does not persist recovery without both tenant and user identity", () => {
    expect(createActivityDefinitionRecoveryStore({ enabled: true }, { subject: "author-1" })).toBeNull();
    expect(createActivityDefinitionRecoveryStore({ enabled: true }, { tenantId: "tenant-1" })).toBeNull();
  });
});

function renderPage(options: {
  path?: string;
  getJson(url: string): Promise<unknown>;
  postJson?(url: string, body: unknown): Promise<unknown>;
  putJson?(url: string, body: unknown): Promise<unknown>;
  contributions?: StudioActivityDefinitionImplementationEditorContribution[];
  runtime?: StudioRuntimeSettings;
  seedDraft?: ReturnType<typeof fullDraft>;
  baseUrl?: string;
  historyEntries?: { url: string; state: unknown }[];
  editor?: Partial<React.ComponentProps<typeof ActivityDefinitionDraftEditor>>;
  seedVersion?: ActivityDefinitionVersionView;
  workspace?: StudioWorkspaceTabHandle;
}) {
  const baseUrl = options.baseUrl ?? `test://activity-definition-authoring-${Math.random()}`;
  if (options.historyEntries?.length) {
    const [first, ...rest] = options.historyEntries;
    window.history.replaceState(first.state, "", first.url);
    for (const entry of rest) window.history.pushState(entry.state, "", entry.url);
  } else {
    window.history.replaceState({}, "", options.path ?? "/workflows/activity-definitions");
  }
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const context = {
    baseUrl,
    http: {
      getJson: (url: string) => url === "/design/activities/catalog" ? Promise.resolve(activityCatalog()) : options.getJson(url),
      postJson: options.postJson ?? vi.fn(),
      putJson: options.putJson ?? vi.fn()
    }
  } as unknown as StudioEndpointContext;
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (options.seedDraft) queryClient.setQueryData(activityDesignKeys.fullDefinitionDraft(options.seedDraft.draftId), options.seedDraft);
  if (options.seedVersion) {
    queryClient.setQueryData(activityDesignKeys.fullDefinitionVersion(options.seedVersion.versionId), options.seedVersion);
    queryClient.setQueryData([...activityDesignKeys.fullDefinitionVersion(options.seedVersion.versionId), JSON.stringify([baseUrl, undefined, undefined, 0])], options.seedVersion);
  }
  const contributions = options.contributions ?? [graphContribution()];
  let editor = options.editor;
  const rerender = (next?: Partial<React.ComponentProps<typeof ActivityDefinitionDraftEditor>>) => {
    if (editor && next) editor = { ...editor, ...next };
    const screen = editor ? <ActivityDefinitionDraftEditor context={context} definitionId="activity-def-1" activityEditors={contributions} onNavigationGuardChange={() => {}} onBack={() => {}} onOpenDraft={() => {}} onOpenVersion={() => {}} onOpenStudioPath={() => {}} {...editor} /> : <ActivityDefinitionsPage context={context} activityEditors={() => contributions} runtime={options.runtime} />;
    flushSync(() => root.render(
    <QueryClientProvider client={queryClient}>
      {options.workspace ? <React.Activity mode={options.workspace.active ? "visible" : "hidden"}><StudioWorkspaceTabProvider value={options.workspace}>{screen}</StudioWorkspaceTabProvider></React.Activity> : screen}
    </QueryClientProvider>
    ));
  };
  rerender();
  const unmount = async () => {
    if (!mountedAuthoringRoots.delete(unmount)) return;
    flushSync(() => root.unmount());
    queryClient.clear();
    container.remove();
  };
  mountedAuthoringRoots.add(unmount);
  return {
    container: document.body,
    unmount,
    rerender,
    queryClient
  };
}

function graphContribution(providerSchemaVersion = "1"): StudioActivityDefinitionImplementationEditorContribution {
  return {
    id: "activity-graph",
    providerKey: "elsa.activity-graph",
    providerSchemaVersion,
    createInitialImplementation: () => ({
      payload: {
        rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null },
        variables: [],
        outputMappings: []
      },
      layout: []
    }),
    component: function GraphImplementationFixture() {
      const authoringTabsHostRef = React.useContext(ActivityDefinitionAuthoringTabsHostContext);
      return <main className="wf-canvas-shell ad-graph-canvas">
        <div ref={authoringTabsHostRef?.setTablistHost} className="wf-canvas-tabs ad-authoring-canvas-tabs" />
        <div className="ad-graph-center-content">
          <section id={authoringTabsHostRef?.designerPanelId} className="ad-graph-designer-panel" role="tabpanel" aria-label="Designer" aria-labelledby={authoringTabsHostRef?.designerTabId} hidden={authoringTabsHostRef?.activeView !== "designer"}>Graph implementation editor</section>
          <div ref={authoringTabsHostRef?.setCenterViewHost} className="ad-graph-authoring-view-host" />
        </div>
      </main>;
    }
  };
}

function editingContribution(onChangeCapture?: { current: StudioActivityDefinitionImplementationEditorProps["onChange"] | null }): StudioActivityDefinitionImplementationEditorContribution {
  return {
    ...graphContribution(),
    component: ({ value, onChange, readOnly }: StudioActivityDefinitionImplementationEditorProps) => {
      if (onChangeCapture) onChangeCapture.current = onChange;
      const payload = value.payload as { edit?: number };
      return <button type="button" disabled={readOnly} onClick={() => onChange({ ...value, payload: { edit: (payload.edit ?? 0) + 1 } })}>Edit implementation {payload.edit ?? 0}</button>;
    }
  };
}

function capabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" },
    { rel: "activity-catalog", href: "design/activities/catalog" },
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true },
    { rel: "activity-version-candidate-validation", href: "design/activities/versions/{versionId}/candidate-validation", templated: true },
    { rel: "activity-draft-validation", href: "design/activities/drafts/{draftId}/validate", templated: true },
    { rel: "activity-draft-conflict-copies", href: "design/activities/drafts/{draftId}/conflict-copies", templated: true }
  ] }, {
    id: "elsa.api.expressions",
    contractVersion: "1",
    links: [{ rel: "expression-descriptors", href: "expressions/descriptors" }]
  }, {
    id: "elsa.api.publishing",
    contractVersion: "1",
    links: [
      { rel: "activity-publication-preflight", href: "design/activities/drafts/{draftId}/publication-preflight", templated: true },
      { rel: "activity-publication", href: "design/activities/drafts/{draftId}/publish", templated: true },
      { rel: "activity-publication-receipt", href: "design/activities/publications/{idempotencyKey}", templated: true }
    ]
  }] };
}

function proposalCapabilities() {
  const document = capabilities();
  const activityDesign = document.capabilities.find(capability => capability.id === "elsa.api.activity-design")!;
  activityDesign.links.push(
    { rel: "activity-draft-contract-proposals", href: "design/activities/drafts/{draftId}/contract-proposals", templated: true },
    { rel: "activity-draft-contract-proposals-apply", href: "design/activities/drafts/{draftId}/contract-proposals/apply", templated: true }
  );
  return document;
}

function publicationChange(overrides: Partial<{
  changeId: string;
  area: string;
  kind: string;
  before: unknown;
  after: unknown;
  impact: string;
  message: string;
}> = {}) {
  return {
    changeId: overrides.changeId ?? "change-1",
    area: overrides.area ?? "Contract",
    kind: overrides.kind ?? "input-added",
    subject: { memberKind: "Input", referenceKey: "customer-id", dependencyVersionId: null, occurrenceId: null },
    before: overrides.before,
    after: overrides.after,
    impact: overrides.impact ?? "Additive",
    requiredBump: overrides.impact === "Breaking" ? "Major" : "Minor",
    message: overrides.message ?? "Input customer-id was added."
  };
}

function publicationPreflight(overrides: Record<string, unknown> = {}) {
  return {
    draftId: "activity-draft-1",
    draftRevision: 3,
    definitionId: "activity-def-1",
    definitionHeadVersionId: null,
    hasBaseline: false,
    reviewToken: "sha256:review",
    isPublishable: true,
    minimumVersion: "1.0.0",
    validVersions: ["1.0.0"],
    diff: { compatibility: "Compatible", requiredBump: "Minor", behaviorChanged: true, summary: { breaking: 0, additive: 1, nonBehavioral: 0, warnings: 0 } },
    impactFirstChanges: [publicationChange()],
    dependencies: [],
    provider: { kind: "Provider", key: "elsa.activity-graph", schemaVersion: "1", status: "Available", supportedSchemaVersions: ["1"] },
    storage: [],
    runtime: [{ kind: "Runtime", key: "elsa.activity-graph", schemaVersion: "1", status: "Available", supportedSchemaVersions: ["1"] }],
    diagnostics: [],
    ...overrides
  };
}

function publicationReceipt(overrides: Record<string, unknown> = {}) {
  return {
    idempotencyKey: "operation-1",
    status: "Applied",
    draftId: "activity-draft-1",
    expectedDraftRevision: 3,
    expectedDefinitionHeadVersionId: null,
    reviewToken: "sha256:review",
    requestedVersion: "1.0.0",
    outcome: {
      definitionId: "activity-def-1",
      definitionVersionId: "version-published",
      draftId: "activity-draft-1",
      version: "1.0.0",
      templateId: "template-1",
      templateHash: "sha256:template",
      sourceReferenceId: "source-1",
      publishedAt: "2026-07-17T11:00:00Z"
    },
    errorCode: null,
    diagnostics: [],
    updatedAt: "2026-07-17T11:00:00Z",
    ...overrides
  };
}

function authoringCapabilities({ graphSchemas = ["1"] }: { graphSchemas?: string[] } = {}) {
  return {
    contractSchemaVersions: ["1"],
    activityTypeKeyRules: {
      serverGenerated: true,
      allowsPreCreationOverride: true,
      immutable: true,
      prefix: "elsa.user",
      pattern: "^elsa\\.user\\..+$",
      maximumLength: 160,
      collisionScope: "tenantId + activityTypeKey"
    },
    providers: [{
      providerKey: "elsa.activity-graph",
      displayName: "Activity Graph",
      manifestSchemas: graphSchemas.map(schemaVersion => ({ schemaVersion, isAuthorable: true, migratableFromSchemaVersions: ["1"] })),
      requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }]
    }],
    types: [{
      alias: "String",
      displayName: "Text",
      category: "Primitives",
      defaultEditor: "text",
      supportedCollectionKinds: ["Single"],
      supportsNull: true,
      supportsDurability: true,
      compatibleStorageDriverKeys: ["elsa.json"]
    }],
    storageDriverKeys: ["elsa.json"],
    snapshotFingerprint: "sha256:test"
  };
}

function page<T>(items: T[]) {
  return { items, count: items.length, totalCount: items.length, hasMore: false, continuation: null, snapshot: { snapshotId: "snapshot-1", asOf: "2026-07-17T10:00:00Z" } };
}

function publishedVersion(): ActivityDefinitionVersionView {
  return {
    definition: definitionIdentity(),
    versionId: "activity-version-1",
    version: "1.0.0",
    sourceDraftId: "not-authorized-to-read",
    provider: fullDraft({ payload: { edit: 0 } }).provider,
    contract: fullDraft().contract,
    activityPresentation: [{ nodeId: "root", displayName: "Published root" }],
    lifecycle: "Active",
    publishedAt: "2026-07-17T10:00:00Z"
  };
}

function editableDefinition() {
  return { ...managementDefinition(), actions: [{ action: "create-draft", allowed: true }] };
}

function publishedEditorGet(draft = fullDraft()) {
  return async (url: string) => {
    if (url === "/capabilities") return capabilities();
    if (url === "/design/activities/versions/activity-version-1") return publishedVersion();
    if (url === "/design/activities/definitions/activity-def-1") return editableDefinition();
    if (url === "/design/activities/drafts/activity-draft-1") return draft;
    if (url === "/design/activities/authoring-capabilities") return authoringCapabilities();
    if (url === "/expressions/descriptors") return { items: [] };
    throw new Error(`Unexpected GET ${url}`);
  };
}

function definitionIdentity() {
  return {
    definitionId: "activity-def-1",
    activityTypeKey: "elsa.user.invoice-evaluator.activity-def-1",
    tenantId: "tenant-1",
    category: "Finance",
    displayName: "Invoice evaluator",
    description: null,
    contentAuthority: { kind: "design", authorityKey: "elsa.activity-design", sourceId: null },
    forkedFrom: null,
    headVersionId: null,
    recommendedVersionId: null
  };
}

function draftSummary({ providerSchemaVersion = "1" }: { providerSchemaVersion?: string } = {}) {
  return {
    draftId: "activity-draft-1",
    definitionId: "activity-def-1",
    revision: 1,
    sourceVersionId: null,
    status: "active",
    providerKey: "elsa.activity-graph",
    providerSchemaVersion,
    updatedAt: "2026-07-17T10:00:00Z",
    presentationLabel: null
  };
}

function fullDraft(overrides: Partial<{ draftId: string; revision: number; status: string; payload: unknown; providerSchemaVersion: string }> = {}): ActivityDefinitionDraftView {
  return {
    draftId: overrides.draftId ?? "activity-draft-1",
    definitionId: "activity-def-1",
    tenantId: "tenant-1",
    revision: overrides.revision ?? 1,
    sourceVersionId: null,
    status: overrides.status ?? "active",
    contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: overrides.providerSchemaVersion ?? "1",
      manifestFingerprint: "sha256:graph",
      payload: overrides.payload ?? { rootActivity: { nodeId: "root", activityVersionId: "", inputs: [], outputs: [], structure: null }, variables: [], outputMappings: [] }
    },
    layout: [],
    validation: null,
    createdAt: "2026-07-17T10:00:00Z",
    updatedAt: "2026-07-17T10:00:00Z",
    presentationLabel: null
  };
}

function activityCatalog() {
  return {
    activities: [
      compositionActivity("flowchart-v1", "Elsa.Activities.Flowchart", "Flowchart", "General"),
      compositionActivity("sequence-v1", "Elsa.Activities.Sequence", "Sequence", "Control flow"),
      compositionActivity("bpmn-v1", "Elsa.Activities.BpmnProcess", "BPMN Process", "Control flow"),
      compositionActivity("write-line-v1", "Elsa.Activities.WriteLine", "Write Line", "Utilities")
    ]
  };
}

function compositionActivity(activityVersionId: string, activityTypeKey: string, displayName: string, category: string) {
  return {
    activityVersionId,
    activityTypeKey,
    displayName,
    version: "1.0.0",
    category,
    executionType: "Action",
    inputs: [],
    outputs: [],
    authoringTemplate: { nodeId: "template", activityVersionId, inputs: {}, outputs: {}, structure: null }
  };
}

function managementDefinition() {
  return {
    definition: definitionIdentity(),
    lifecycle: { draftCount: 1, versionCount: 0, head: null, recommendation: null },
    actions: [],
    updatedAt: "2026-07-17T10:00:00Z"
  };
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => (candidate.textContent?.includes(text) || candidate.getAttribute("aria-label") === text))
    ?? (text === "Save now" ? container.querySelector<HTMLButtonElement>("button.wf-save-button[aria-label='Save']") : null);
  if (!button) throw new Error(`Button '${text}' not found. Available buttons: ${[...container.querySelectorAll("button")].map(candidate => `${candidate.textContent?.trim() || candidate.getAttribute("aria-label") || candidate.title}`).filter(Boolean).join(" | ")}`);
  return button;
}

function activityDefinitionBackLink(container: HTMLElement) {
  const link = [...container.querySelectorAll<HTMLAnchorElement>("nav[aria-label='Breadcrumb'] a")]
    .find(candidate => candidate.textContent?.trim().startsWith("Activity Definition"));
  if (!link) throw new Error("Activity Definition breadcrumb link not found.");
  return link;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function change(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) {
  flushSync(() => {
    const prototype = element instanceof HTMLInputElement ? HTMLInputElement.prototype : element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLTextAreaElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function pressKey(element: HTMLElement, key: string) {
  flushSync(() => element.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
}

function controlByLabel<T extends HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(container: HTMLElement, text: string) {
  const label = [...container.querySelectorAll("label")].find(candidate =>
    [...candidate.querySelectorAll(":scope > span")].some(span => span.textContent?.trim() === text)
  );
  const control = label?.control;
  if (!control) throw new Error(`Control labelled '${text}' not found.`);
  return control as T;
}

function controlByAriaLabel<T extends HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(container: HTMLElement, text: string) {
  const control = container.querySelector<T>(`[aria-label="${text}"]`);
  if (!control) throw new Error(`Control labelled '${text}' not found.`);
  return control;
}

function categoryAddButton(container: HTMLElement, kind: "input" | "output" | "outcome") {
  click(container.querySelector<HTMLButtonElement>(`.ad-authoring-view-tabs [data-tab-id="${kind}s"]`)!);
  const button = container.querySelector<HTMLButtonElement>(`.ad-contract-editor .wf-properties-add`);
  if (!button) throw new Error(`Add ${kind} button not found.`);
  return button;
}

function showContractView(container: HTMLElement) {
  const tab = container.querySelector<HTMLButtonElement>(".ad-authoring-view-tabs [data-tab-id='inputs']");
  if (!tab) throw new Error("Inputs tab not found.");
  click(tab);
}

function contractListButton(container: HTMLElement, kind: "input" | "output" | "outcome", referenceKey: string) {
  click(container.querySelector<HTMLButtonElement>(`[data-tab-id="${kind}s"]`)!);
  const button = container.querySelector<HTMLButtonElement>(`.ad-contract-editor button[data-member-kind="${kind}"][data-member-reference-key="${referenceKey}"]`);
  if (!button) throw new Error(`Contract list entry '${kind}:${referenceKey}' not found.`);
  return button;
}

function contractMemberPane(container: HTMLElement, kind: "input" | "output" | "outcome", referenceKey: string) {
  const pane = container.querySelector<HTMLElement>(`.ad-contract-editor section.ad-contract-workbench-member[data-contract-kind="${kind}"][data-contract-reference-key="${referenceKey}"]`);
  if (!pane) throw new Error(`Contract editor pane '${kind}:${referenceKey}' not found.`);
  return pane;
}

function contractMemberRow(container: HTMLElement, kind: "input" | "output" | "outcome", referenceKey: string) {
  const toggle = container.querySelector<HTMLButtonElement>(`.ad-contract-editor button[data-member-kind="${kind}"][data-member-reference-key="${referenceKey}"]`);
  const row = toggle?.closest<HTMLElement>(`tr[data-contract-row-kind="${kind}"]`);
  if (!row) throw new Error(`Contract member row '${kind}:${referenceKey}' not found.`);
  return row;
}

function selectContractMember(container: HTMLElement, kind: "input" | "output" | "outcome", referenceKey: string) {
  const button = contractListButton(container, kind, referenceKey);
  if (button.getAttribute("aria-expanded") !== "true") click(button);
  if (button.getAttribute("aria-expanded") !== "true") throw new Error(`Contract member '${kind}:${referenceKey}' details were not opened.`);
  return contractMemberPane(container, kind, referenceKey);
}

function addContractMember(container: HTMLElement, kind: "input" | "output" | "outcome", name: string) {
  const trigger = categoryAddButton(container, kind);
  const editor = trigger.closest<HTMLElement>(".ad-contract-editor");
  if (!editor) throw new Error(`Contract ${kind} editor not found.`);
  click(trigger);
  const form = editor.querySelector<HTMLFormElement>("form.ad-contract-create-form");
  if (!form) throw new Error(`Contract ${kind} creation form did not open.`);
  const label = `${kind[0]?.toLocaleUpperCase("en-US")}${kind.slice(1)} name`;
  change(controlByLabel<HTMLInputElement>(form, label), name);
  flushSync(() => form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
}

function openDetails(element: Element) {
  flushSync(() => {
    (element as HTMLDetailsElement).open = true;
    element.dispatchEvent(new Event("toggle", { bubbles: true }));
  });
}

async function waitForText(container: HTMLElement, text: string) {
  await waitFor(() => {
    if (!container.textContent?.includes(text)) throw new Error(`Waiting for '${text}'.`);
  });
}

async function waitFor(assertion: () => void) {
  await vi.waitFor(assertion, { timeout: 10_000 });
}
