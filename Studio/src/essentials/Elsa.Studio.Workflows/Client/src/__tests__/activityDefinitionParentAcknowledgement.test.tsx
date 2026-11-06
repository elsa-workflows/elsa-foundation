import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { authSessionEndedEvent, requestStudioNavigation, StudioHttpError, type StudioActivityDefinitionImplementationEditorProps, type StudioActivityDefinitionImplementationState, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftView, ActivityDraftValidationView } from "../activityDefinitionTypes";
import type { PreparedActivityTestRunRevision } from "../ActivityDefinitionTestRunDialog";
import type { ActivityGraphAuthoringHost } from "../graph-authoring/activityGraphAuthoringHost";
import type { ActivityContractProposalBinding } from "../activityContractProposalBinding";
import type { GraphDocumentAdapter } from "../graph-authoring/graphDocumentAdapter";
import { useGraphAuthoringCanvas } from "../graph-authoring/useGraphAuthoringCanvas";
import { ActivityDefinitionDraftEditor } from "../ActivityDefinitionDraftEditor";

const controls = vi.hoisted(() => ({
  draft: null as ActivityDefinitionDraftView | null,
  replace: vi.fn<(...args: unknown[]) => Promise<ActivityDefinitionDraftView>>(),
  publication: null as ((pending: boolean) => void) | null,
  published: null as (() => boolean | void) | null,
  proposal: null as ((applying: boolean) => void) | null,
  applied: null as ((draft: ActivityDefinitionDraftView) => void) | null,
  flush: null as (() => Promise<{ revision: number; signature: string } | null>) | null,
  beforePublish: null as ((binding: { revision: number; signature: string }) => Promise<boolean>) | null,
  prepareTest: null as ((phase: (value: "saving" | "validating") => void) => Promise<PreparedActivityTestRunRevision | null>) | null,
  validate: vi.fn<(...args: unknown[]) => Promise<ActivityDraftValidationView>>(),
  providerFails: false,
  onProviderLayout: null as (() => void) | null,
  beforeApply: null as ((binding: ActivityContractProposalBinding) => Promise<boolean>) | null
}));
vi.mock("../api/activityDesign", () => ({
  useFullActivityDefinitionDraft: () => ({ data: controls.draft, isPending: false, isFetching: false, isError: false, isFetchedAfterMount: true }),
  useActivityDefinition: () => ({ data: { definition: { displayName: "Parent admission fixture" } } }),
  useActivityAuthoringCapabilities: () => ({ data: { providers: [] }, isPending: false }),
  useActivityContractExpressionDescriptors: () => ({ data: [] }),
  useFullActivityDefinitionVersion: () => ({ data: undefined }),
  replaceActivityDefinitionDraft: (...args: unknown[]) => controls.replace(...args),
  createActivityDefinitionConflictCopy: vi.fn(), validateActivityDefinitionDraft: (...args: unknown[]) => controls.validate(...args)
}));
// Only peripheral UI is stubbed. The actual parent admission/save functions and shared history hook run.
vi.mock("../ActivityDefinitionContractEditor", () => ({ ActivityDefinitionContractEditor: () => null }));
vi.mock("../ActivityDefinitionPublicationReview", () => ({ ActivityDefinitionPublicationReview: (props: { onPublicationPending(pending: boolean): void; onPublished(): boolean | void; flushExactSavedRevision(): Promise<{ revision: number; signature: string } | null>; beforePublish(binding: { revision: number; signature: string }): Promise<boolean> }) => {
  controls.publication = props.onPublicationPending; controls.published = props.onPublished; controls.flush = props.flushExactSavedRevision; controls.beforePublish = props.beforePublish; return null;
} }));
vi.mock("../ActivityDefinitionTestRunDialog", () => ({ ActivityDefinitionTestRunDialog: (props: { prepareExactRevision(phase: (value: "saving" | "validating") => void): Promise<PreparedActivityTestRunRevision | null> }) => {
  controls.prepareTest = props.prepareExactRevision; return <div>Test Run fixture</div>;
} }));
vi.mock("../ActivityDefinitionContractProposalReview", () => ({ ActivityDefinitionContractProposalReview: (props: { onApplyingChange(applying: boolean): void; onApplied(draft: ActivityDefinitionDraftView): void; beforeApply(binding: ActivityContractProposalBinding): Promise<boolean> }) => {
  controls.proposal = props.onApplyingChange; controls.applied = props.onApplied; controls.beforeApply = props.beforeApply; return null;
} }));

type State = StudioActivityDefinitionImplementationState;
type Canvas = ReturnType<typeof useGraphAuthoringCanvas<State>>;
const activity = { nodeId: "root", activityVersionId: "root@1", inputs: [], outputs: [] };
const adapter: GraphDocumentAdapter<State> = {
  resourceKind: "activity-definition-graph", readRoot: () => activity, readLayout: () => [],
  replaceRoot: state => state, replaceLayout: state => state, replaceGraph: state => state
};
const buildModel = () => ({ nodes: [], edges: [] });
const applyModel = (state: State) => state;
const mounted: (() => void)[] = [];
beforeEach(() => {
  controls.draft = draft(); controls.replace.mockReset(); controls.publication = null; controls.published = null; controls.proposal = null; controls.applied = null;
  controls.flush = null; controls.beforePublish = null; controls.prepareTest = null; controls.providerFails = false; controls.validate.mockReset();
  controls.beforeApply = null;
  controls.onProviderLayout = null;
  controls.validate.mockImplementation(async (_context, id, revision) => ({ draftId: String(id), revision: Number(revision), isValid: true, validatedAt: "now", diagnostics: [] }));
  controls.replace.mockImplementation(async (_context, _id, request) => {
    const body = request as { provider: ActivityDefinitionDraftView["provider"]; layout: ActivityDefinitionDraftView["layout"]; activityPresentation: ActivityDefinitionDraftView["activityPresentation"] };
    return { ...controls.draft!, provider: { ...controls.draft!.provider, ...body.provider }, layout: body.layout, activityPresentation: body.activityPresentation, revision: 4 };
  });
});
afterEach(() => { for (const unmount of mounted.splice(0)) unmount(); vi.useRealTimers(); });

function draft(): ActivityDefinitionDraftView {
  return { definitionId: "definition", draftId: "draft", tenantId: "tenant", revision: 3, status: "active", sourceVersionId: null,
    contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] },
    provider: { providerKey: "fixture.graph", schemaVersion: "1", manifestFingerprint: "manifest", payload: { edit: 0 } },
    layout: [], activityPresentation: [], validation: null, createdAt: "2026-11-06T00:00:00Z", updatedAt: "2026-11-06T00:00:00Z" };
}
function renderParent(requested: { definitionId?: string; draftId?: string } = {}) {
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  let props!: StudioActivityDefinitionImplementationEditorProps, canvas!: Canvas;
  function ProviderEditor(value: StudioActivityDefinitionImplementationEditorProps) {
    props = value;
    canvas = useGraphAuthoringCanvas({ document: value.value, adapter, resetKey: value.historyResetKey!, buildModel, applyModel, onChange: value.onChange });
    React.useLayoutEffect(() => { controls.onProviderLayout?.(); }, []);
    if (controls.providerFails) throw new Error("Provider view failed");
    return <output data-provider-state>{JSON.stringify(value.value)}</output>;
  }
  const contribution = { id: "fixture", providerKey: "fixture.graph", providerSchemaVersion: "1", createInitialImplementation: () => ({ payload: {}, layout: [] }), component: ProviderEditor };
  let context = { baseUrl: "test://parent-acknowledgement", http: {} } as unknown as StudioEndpointContext;
  let identity = { subject: "author", tenantId: "tenant" };
  const navigationGuard = vi.fn();
  const render = () => flushSync(() => root.render(<ActivityDefinitionDraftEditor context={context} identity={identity} definitionId={requested.definitionId ?? controls.draft!.definitionId} draftId={requested.draftId ?? controls.draft!.draftId} activityEditors={[contribution]}
    onNavigationGuardChange={navigationGuard} onBack={vi.fn()} onOpenDraft={vi.fn()} onOpenVersion={vi.fn()} onOpenStudioPath={vi.fn()} />));
  render();
  let active = true;
  const unmount = () => { if (!active) return; active = false; flushSync(() => root.unmount()); container.remove(); };
  mounted.push(unmount);
  return { get props() { return props; }, get canvas() { return canvas; }, container, unmount, navigationGuard,
    render(next: ActivityDefinitionDraftView, authority: { baseUrl?: string; subject?: string; tenantId?: string } = {}) {
      controls.draft = next;
      if (authority.baseUrl) context = { ...context, baseUrl: authority.baseUrl };
      identity = { subject: authority.subject ?? identity.subject, tenantId: authority.tenantId ?? identity.tenantId };
      render();
    } };
}
function next(state: State, edit: number): State { return { ...state, payload: { edit } }; }
function save(container: HTMLElement) {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent?.trim() === "Save now");
  if (!button || button.disabled) throw new Error("Save now is unavailable");
  flushSync(() => button.click());
}
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function nativeParticipant(h: ReturnType<typeof renderParent>, options: { edit?: number; failure?: Error; release?: () => void } = {}) {
  const host = h.props.authoringHost!.context as ActivityGraphAuthoringHost;
  const changed = deferred<void>();
  const retainedOnChange = h.props.onChange;
  let accepted = 0;
  const release = vi.fn(options.release);
  const checkpoint = vi.fn(async () => {
    await changed.promise;
    if (options.failure) throw options.failure;
    if (!accepted && options.edit !== undefined) {
      const current = host.readLive().snapshot;
      const applied = flushSync(() => retainedOnChange(next(current, options.edit!), current));
      if (applied !== true) throw new Error("Native admission refused; local work retained");
    }
    const receipt = registration.acknowledge(1, host.readLive().token);
    accepted = 1;
    return { receipt, release };
  });
  const registration = host.getResource().register("retained-native", { readWork: () => ({ pending: accepted !== 1, sequence: 1, acceptedSequence: accepted }), checkpoint });
  return { host, changed, checkpoint, registration, release, notify: () => flushSync(() => host.notifyWorkChanged()) };
}

describe("actual Activity Definition parent acknowledgement", () => {
  it("keeps accepted provider edits undoable/redoable and saves only the live canonical implementation", async () => {
    const h = renderParent(); let accepted: boolean | undefined;
    flushSync(() => { accepted = h.canvas.commitDocument(next(h.props.value, 1)); });
    expect(accepted).toBe(true); expect(h.canvas.canUndo).toBe(true);
    flushSync(() => { accepted = h.canvas.undo(); });
    expect(accepted).toBe(true); expect(h.props.value.payload).toEqual({ edit: 0 });
    flushSync(() => { accepted = h.canvas.redo(); });
    expect(accepted).toBe(true); expect(h.props.value.payload).toEqual({ edit: 1 });
    save(h.container);
    await vi.waitFor(() => expect(controls.replace).toHaveBeenCalledOnce());
    expect(controls.replace).toHaveBeenCalledWith(expect.anything(), "draft", expect.objectContaining({ expectedRevision: 3, provider: expect.objectContaining({ payload: { edit: 1 } }) }));
    await vi.waitFor(() => expect(h.container.textContent).toContain("Saved revision 4"));
    expect(h.canvas.canUndo).toBe(true);
    flushSync(() => expect(h.canvas.undo()).toBe(true));
    expect(h.props.value.payload).toEqual({ edit: 0 });
  });

  it("returns false for a stale baseline from a retained callback and preserves the admitted edit/history", () => {
    const h = renderParent(), original = h.props;
    flushSync(() => expect(h.canvas.commitDocument(next(original.value, 1))).toBe(true));
    flushSync(() => expect(original.onChange(next(original.value, 99), original.value)).toBe(false));
    expect(h.props.value.payload).toEqual({ edit: 1 });
    expect(h.canvas.canUndo).toBe(true);
    flushSync(() => expect(h.canvas.undo()).toBe(true));
    expect(h.props.value.payload).toEqual({ edit: 0 });
  });

  it("revokes the old parent before a replacement provider's layout effect can invoke retained callbacks", () => {
    const h = renderParent(), original = h.props;
    const oldHost = original.authoringHost!.context as ActivityGraphAuthoringHost;
    let accepted: boolean | void = undefined, editable: boolean | undefined;
    controls.onProviderLayout = () => {
      editable = oldHost.canEdit();
      accepted = original.onChange(next(original.value, 99), original.value);
    };
    h.render({ ...draft(), draftId: "replacement" });
    expect(editable).toBe(false);
    expect(accepted).toBe(false);
    expect(h.props.value.payload).toEqual({ edit: 0 });
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("compares explicit implementation fields with normalized presentation regardless of wrapper key order", () => {
    const h = renderParent(), value = h.props.value;
    const expected = { layout: value.layout, activityPresentation: undefined, payload: value.payload };
    flushSync(() => expect(h.props.onChange(next(value, 1), expected)).toBe(true));
    expect(h.props.value.payload).toEqual({ edit: 1 });
  });

  it.each(["publication", "proposal"] as const)("refuses a retained callback immediately when %s holds the parent", held => {
    const h = renderParent();
    flushSync(() => {
      controls[held]!(true);
      expect(h.canvas.commitDocument(next(h.props.value, 1))).toBe(false);
    });
    expect(h.props.value.payload).toEqual({ edit: 0 }); expect(h.canvas.canUndo).toBe(false);
    expect(controls.replace).not.toHaveBeenCalled();
    flushSync(() => controls[held]!(false));
  });

  it("refuses the previous provider callback as soon as canonical provider identity advances", () => {
    const h = renderParent(), original = h.props;
    flushSync(() => {
      controls.applied!({ ...controls.draft!, provider: { ...controls.draft!.provider, providerKey: "another.provider", manifestFingerprint: "new-manifest" } });
      expect(original.onChange(next(original.value, 9), original.value)).toBe(false);
    });
    expect(h.container.textContent).toContain("Implementation editor unavailable");
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("returns false for callbacks retained after parent unmount without scheduling a save", () => {
    vi.useFakeTimers();
    const h = renderParent(), original = h.props; h.unmount();
    expect(original.onChange(next(original.value, 1), original.value)).toBe(false);
    expect(vi.getTimerCount()).toBe(0);
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it.each(["draft", "backend", "subject", "tenant"] as const)("starts a distinct canonical parent when cached %s authority changes without a loading interlude", field => {
    vi.useFakeTimers();
    const h = renderParent(), original = h.props;
    const selected = { ...draft(), draftId: field === "draft" ? "another-draft" : "draft", provider: { ...draft().provider, payload: { edit: 7 } } };
    h.render(selected, field === "backend" ? { baseUrl: "test://another-backend" } : field === "subject" ? { subject: "another-author" } : field === "tenant" ? { tenantId: "another-tenant" } : {});
    expect(h.props.draftId).toBe(selected.draftId);
    expect(h.props.value.payload).toEqual({ edit: 7 });
    expect(original.onChange(next(original.value, 99), original.value)).toBe(false);
    expect(vi.getTimerCount()).toBe(0);
    expect(h.canvas.canUndo).toBe(false);
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("does not mount a provider for a different draft returned under the requested definition", () => {
    controls.draft = { ...draft(), draftId: "unrequested-draft" };
    const h = renderParent({ draftId: "draft" });
    expect(h.container.textContent).toContain("Activity draft unavailable");
    expect(h.container.querySelector("[data-provider-state]")).toBeNull();
  });

  it("surfaces the paused autosave refusal after a real stale revision response", async () => {
    const h = renderParent();
    controls.replace.mockRejectedValueOnce(new StudioHttpError(409, "stale", null, { errorCode: "activity.draft.stale-revision", recovery: { currentRevision: 4 } }));
    flushSync(() => expect(h.canvas.commitDocument(next(h.props.value, 1))).toBe(true));
    save(h.container);
    await vi.waitFor(() => expect(h.container.textContent).toContain("Local work preserved"));
    const current = h.props;
    flushSync(() => expect(current.onChange(next(current.value, 2), current.value)).toBe(false));
    expect(h.props.value.payload).toEqual({ edit: 1 });
    expect(controls.replace).toHaveBeenCalledOnce();
  });

  it("blocks navigation from cached native work before a notification renders, without serializing", () => {
    const h = renderParent(), native = nativeParticipant(h, { edit: 8 });
    expect(h.container.textContent).toContain("Saved revision 3");
    expect(requestStudioNavigation("/activities/draft", "/activities")).toBe(false);
    expect(window.dispatchEvent(new Event("beforeunload", { cancelable: true }))).toBe(false);
    expect(native.checkpoint).not.toHaveBeenCalled();
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("saves the acknowledged native snapshot after a delayed actual-parent checkpoint", async () => {
    const h = renderParent(), native = nativeParticipant(h, { edit: 8 });
    const saving = controls.flush!();
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    expect(controls.replace).not.toHaveBeenCalled();
    native.changed.resolve();
    expect((await saving)?.revision).toBe(4);
    expect(controls.replace).toHaveBeenCalledWith(expect.anything(), "draft", expect.objectContaining({ provider: expect.objectContaining({ payload: { edit: 8 } }) }));
    expect(h.props.value.payload).toEqual({ edit: 8 });
  });

  it("does not report a clean signature as an exact saved revision after native refusal", async () => {
    const h = renderParent(), native = nativeParticipant(h, { failure: new Error("Native serialization refused; work retained") });
    const saving = controls.flush!(); native.changed.resolve();
    expect(await saving).toBeNull();
    expect(controls.replace).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(h.container.textContent).toContain("Native serialization refused"));
    expect(native.host.probe()).toBe(true);
    expect(h.props.value.payload).toEqual({ edit: 0 });
  });

  it("autosaves native work with an otherwise clean signature after the actual 800ms debounce", async () => {
    vi.useFakeTimers();
    const h = renderParent(), native = nativeParticipant(h, { edit: 12 }); native.notify();
    await vi.advanceTimersByTimeAsync(799);
    expect(native.checkpoint).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    expect(controls.replace).not.toHaveBeenCalled();
    native.changed.resolve(); await vi.advanceTimersByTimeAsync(0);
    expect(controls.replace).toHaveBeenCalledOnce();
    expect(h.props.value.payload).toEqual({ edit: 12 });
  });

  it("holds a tab transition until native admission, increments its epoch, and refuses hidden Designer callbacks", async () => {
    const h = renderParent(), retained = h.props, native = nativeParticipant(h, { edit: 7 }), token = native.host.readLive().token;
    const inputs = h.container.querySelector<HTMLButtonElement>('[data-tab-id="inputs"]')!;
    flushSync(() => inputs.click());
    expect(inputs.getAttribute("aria-selected")).toBe("false");
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    native.changed.resolve();
    await vi.waitFor(() => expect(inputs.getAttribute("aria-selected")).toBe("true"));
    expect(native.host.readLive().token.revision).toBe(token.revision + 2); // Actual source acknowledgement + view epoch.
    expect(h.props.readOnly).toBe(true);
    expect(retained.onChange(next(h.props.value, 99), h.props.value)).toBe(false);
    expect(h.props.value.payload).toEqual({ edit: 7 });
  });

  it("retains the Designer view when its pending controller refuses the tab checkpoint", async () => {
    const h = renderParent(), native = nativeParticipant(h, { failure: new Error("Keep this native view") });
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[data-tab-id="inputs"]')!.click());
    native.changed.resolve();
    await vi.waitFor(() => expect(h.container.textContent).toContain("Keep this native view"));
    expect(h.container.querySelector('[data-tab-id="designer"]')!.getAttribute("aria-selected")).toBe("true");
    expect(native.host.probe()).toBe(true);
  });

  it("retains a controller above a failed provider view and still admits its pending source before save", async () => {
    const errors = vi.spyOn(console, "error").mockImplementation(() => undefined);
    try {
      const h = renderParent(), native = nativeParticipant(h, { edit: 14 });
      controls.providerFails = true; h.render(controls.draft!);
      expect(h.container.textContent).toContain("Implementation editor unavailable");
      const saved = controls.flush!(); native.changed.resolve();
      expect((await saved)?.revision).toBe(4);
      expect(controls.replace).toHaveBeenCalledWith(expect.anything(), "draft", expect.objectContaining({ provider: expect.objectContaining({ payload: { edit: 14 } }) }));
    } finally { errors.mockRestore(); }
  });

  it("revokes a delayed native save on the actual auth event without sending its source", async () => {
    const h = renderParent(), native = nativeParticipant(h, { edit: 15 });
    const saved = controls.flush!(); window.dispatchEvent(new Event(authSessionEndedEvent));
    native.changed.resolve();
    expect(await saved).toBeNull();
    expect(controls.replace).not.toHaveBeenCalled();
    expect(native.host.probe()).toBe(true);
    expect(h.props.value.payload).toEqual({ edit: 0 });
  });

  it("does not adopt a saved response after authorization is revoked during HTTP", async () => {
    const h = renderParent(), response = deferred<ActivityDefinitionDraftView>();
    controls.replace.mockReturnValueOnce(response.promise);
    flushSync(() => h.props.onChange(next(h.props.value, 9), h.props.value));
    const saved = controls.flush!(); await vi.waitFor(() => expect(controls.replace).toHaveBeenCalledOnce());
    window.dispatchEvent(new Event(authSessionEndedEvent));
    response.resolve({ ...draft(), revision: 4, provider: { ...draft().provider, payload: { edit: 90 } } });
    expect(await saved).toBeNull();
    expect(h.props.value.payload).toEqual({ edit: 9 });
    expect(h.props.revision).toBe(3);
  });

  it("does not adopt a saved response for another canonical draft", async () => {
    const h = renderParent();
    controls.replace.mockResolvedValueOnce({ ...draft(), draftId: "other-draft", revision: 4 });
    flushSync(() => h.props.onChange(next(h.props.value, 1), h.props.value));
    expect(await controls.flush!()).toBeNull();
    expect(h.props.draftId).toBe("draft"); expect(h.props.value.payload).toEqual({ edit: 1 });
  });

  it("does not rebase a reviewed publication body after delayed native work changes the actual parent", async () => {
    const h = renderParent(), binding = (await controls.flush!())!, native = nativeParticipant(h, { edit: 16 });
    const accepted = controls.beforePublish!(binding);
    expect(h.props.readOnly).toBe(false);
    native.changed.resolve(); expect(await accepted).toBe(false);
    expect(h.props.value.payload).toEqual({ edit: 16 });
    expect(h.props.readOnly).toBe(false);
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("acquires the exact publication lock inside the participant fence and clears only its own failed cleanup", async () => {
    const h = renderParent(), binding = (await controls.flush!())!;
    let refuseCleanup = true;
    const native = nativeParticipant(h, { release: () => {
      const current = native.host.readLive().snapshot;
      expect(h.props.onChange(next(current, 50), current)).toBe(false);
      if (refuseCleanup) throw new Error("Native fence cleanup failed");
    } });
    const accepted = controls.beforePublish!(binding); native.changed.resolve();
    expect(await accepted).toBe(false);
    expect(native.host.probe()).toBe(true);
    expect(h.props.readOnly).toBe(false);
    expect(await controls.beforePublish!(binding)).toBe(false);
    expect(native.release).toHaveBeenCalledOnce();
    refuseCleanup = false;
    // Cleanup itself runs after the publication attempt unlocked. It must not apply the lock again.
    native.release.mockImplementation(() => undefined); native.registration.retryRelease();
    expect(native.release).toHaveBeenCalledTimes(2);
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("prepares Test Run only after pending native work is acknowledged, saved, and validated", async () => {
    const h = renderParent(), native = nativeParticipant(h, { edit: 18 });
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.trim() === "Test Run")!.click());
    await vi.waitFor(() => expect(controls.prepareTest).not.toBeNull());
    const prepared = controls.prepareTest!(vi.fn());
    expect(controls.validate).not.toHaveBeenCalled(); expect(controls.replace).not.toHaveBeenCalled();
    native.changed.resolve();
    expect((await prepared)?.revision).toBe(4);
    expect(controls.validate).toHaveBeenCalledWith(expect.anything(), "draft", 4);
  });

  it("refuses Test Run if newly retained work changes the source after HTTP validation", async () => {
    const h = renderParent(), validation = deferred<ActivityDraftValidationView>();
    controls.validate.mockReturnValueOnce(validation.promise);
    flushSync(() => [...h.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.trim() === "Test Run")!.click());
    await vi.waitFor(() => expect(controls.prepareTest).not.toBeNull());
    const prepared = controls.prepareTest!(vi.fn());
    await vi.waitFor(() => expect(controls.validate).toHaveBeenCalledOnce());
    const native = nativeParticipant(h, { edit: 20 });
    validation.resolve({ draftId: "draft", revision: 3, isValid: true, validatedAt: "now", diagnostics: [] });
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    native.changed.resolve();
    expect(await prepared).toBeNull();
    expect(h.props.value.payload).toEqual({ edit: 20 });
    expect(controls.replace).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(h.container.textContent).toContain("after validation"));
  });

  it("refreshes a hidden clean receipt for Designer→Inputs→Designer and preserves the host identity", async () => {
    const h = renderParent(), native = nativeParticipant(h), initial = native.host.readLive().token;
    native.changed.resolve();
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[data-tab-id="inputs"]')!.click());
    await vi.waitFor(() => expect(h.container.querySelector('[data-tab-id="inputs"]')!.getAttribute("aria-selected")).toBe("true"));
    flushSync(() => h.container.querySelector<HTMLButtonElement>('[data-tab-id="designer"]')!.click());
    await vi.waitFor(() => expect(h.container.querySelector('[data-tab-id="designer"]')!.getAttribute("aria-selected")).toBe("true"));
    expect(native.host.readLive().token.revision).toBe(initial.revision + 2);
    expect(h.props.authoringHost!.context).toBe(native.host);
    expect(h.props.readOnly).toBe(false);
    expect(native.checkpoint).toHaveBeenCalledTimes(2);
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("does not run a retained read-only transition callback after the actual parent unmounts", async () => {
    const h = renderParent(), host = h.props.authoringHost!.context as ActivityGraphAuthoringHost;
    const action = vi.fn(() => undefined); h.unmount();
    await expect(host.transition("view-change", action)).rejects.toThrow("authority");
    expect(action).not.toHaveBeenCalled();
  });

  it("allows explicit Save now to capture clean-signature native work", async () => {
    const h = renderParent(), native = nativeParticipant(h, { edit: 21 }); native.notify();
    save(h.container);
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    expect(controls.replace).not.toHaveBeenCalled(); native.changed.resolve();
    await vi.waitFor(() => expect(controls.replace).toHaveBeenCalledOnce());
    expect(h.props.value.payload).toEqual({ edit: 21 });
  });

  it("locks an unchanged exact review only after the retained participant acknowledges it", async () => {
    const h = renderParent(), binding = (await controls.flush!())!, native = nativeParticipant(h);
    const result = controls.beforePublish!(binding);
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    expect(h.props.readOnly).toBe(false); native.changed.resolve();
    expect(await result).toBe(true);
    await vi.waitFor(() => expect(h.props.readOnly).toBe(true));
    expect(h.props.onChange(next(h.props.value, 99), h.props.value)).toBe(false);
    expect(controls.replace).not.toHaveBeenCalled();
    flushSync(() => controls.publication!(false));
  });

  it("does not authorize publication when logout occurs during fence release after locking", async () => {
    const h = renderParent(), binding = (await controls.flush!())!;
    const native = nativeParticipant(h, { release: () => window.dispatchEvent(new Event(authSessionEndedEvent)) });
    const result = controls.beforePublish!(binding); native.changed.resolve();
    expect(await result).toBe(false);
    expect(controls.replace).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(h.props.readOnly).toBe(true));
    expect(h.props.value.payload).toEqual({ edit: 0 });
  });

  it.each(["replacement", "logout"] as const)("does not adopt a late applied publication or clear another navigation guard after %s", async retirement => {
    const h = renderParent(), binding = (await controls.flush!())!;
    expect(await controls.beforePublish!(binding)).toBe(true);
    const published = controls.published!, receipt = deferred<void>();
    const result = receipt.promise.then(() => published());
    if (retirement === "replacement") {
      h.render({ ...draft(), draftId: "replacement" });
      flushSync(() => h.props.onChange(next(h.props.value, 23), h.props.value));
    } else window.dispatchEvent(new Event(authSessionEndedEvent));
    h.navigationGuard.mockClear();
    receipt.resolve();
    const adopted = await result;
    expect(h.navigationGuard).not.toHaveBeenCalledWith(false);
    expect(adopted).toBe(false);
    expect(h.props.value.payload).toEqual({ edit: retirement === "replacement" ? 23 : 0 });
    expect(h.container.textContent).not.toContain("Published draft");
  });

  it("refuses an exact saved proposal after same-stack native work changes its canonical source", async () => {
    const h = renderParent(), current = draft();
    const binding = { draftId: current.draftId, revision: current.revision, providerKey: current.provider.providerKey, providerSchemaVersion: current.provider.schemaVersion, manifestFingerprint: current.provider.manifestFingerprint };
    const native = nativeParticipant(h, { edit: 25 });
    const applying = controls.beforeApply!(binding);
    await vi.waitFor(() => expect(native.checkpoint).toHaveBeenCalledOnce());
    native.changed.resolve();
    expect(await applying).toBe(false);
    expect(h.props.value.payload).toEqual({ edit: 25 });
    expect(h.props.readOnly).toBe(false);
    expect(controls.replace).not.toHaveBeenCalled();
  });

  it("does not adopt a proposal HTTP response after its authoring authority changes", async () => {
    const h = renderParent(), current = draft();
    const binding = { draftId: current.draftId, revision: current.revision, providerKey: current.provider.providerKey, providerSchemaVersion: current.provider.schemaVersion, manifestFingerprint: current.provider.manifestFingerprint };
    expect(await controls.beforeApply!(binding)).toBe(true);
    window.dispatchEvent(new Event(authSessionEndedEvent));
    flushSync(() => controls.applied!({ ...current, revision: 4, provider: { ...current.provider, payload: { edit: 90 } } }));
    expect(h.props.value.payload).toEqual({ edit: 0 }); expect(h.props.revision).toBe(3);
    flushSync(() => controls.proposal!(false));
  });
});
