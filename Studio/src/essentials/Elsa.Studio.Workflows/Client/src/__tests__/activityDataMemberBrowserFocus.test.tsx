import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ActivityDataMemberBrowser } from "../ActivityDataMemberBrowser";
import type { ActivityDataSelectionHost } from "../activityDataSelection";
import type { ActivityDataContextResult, ActivityDataSourceFacts, ActivityDataSourceQuery, ActivityDataTarget } from "../api/activityDataSelection";

const requestBinding = { expressionType: "WorkflowRequest", value: { memberKey: "payload", path: null } };
const innerBinding = { expressionType: "Variable", value: { referenceKey: "inner-payload", declaringScopeId: "root" } };
const outerBinding = { expressionType: "Variable", value: { referenceKey: "outer-payload", declaringScopeId: "workflow" } };

const sources: ActivityDataSourceFacts[] = [
  { id: "input:payload", displayName: "Request payload", binding: requestBinding },
  { id: "variable:root:inner-payload", displayName: "Payload", binding: innerBinding },
  { id: "variable:workflow:outer-payload", displayName: "Payload", binding: outerBinding }
].map<ActivityDataSourceFacts>(source => ({ ...source, reasonCode: null, availability: "definite", activityResultSource: null,
  type: { alias: "Object", collectionKind: "Single" }, shape: { state: "known", kind: "object", type: null, members: [],
    sourceContractRevision: "contract", profileRevision: "profile", metadataRevision: "metadata", isNullable: false,
    presence: "required", representation: "StructuredValue", isSensitive: false, isCredential: false, reasonCode: null,
    element: null, mapValue: null } }));

const listing: ActivityDataContextResult = {
  state: "supported", code: null, documentRevision: "revision", baseRevision: "base", memberReadsEnabled: true,
  target: { displayName: "Text", constraint: null }, sources, hasMoreSources: false
};

let root: Root | undefined;
let container: HTMLDivElement;
let frames: Map<number, FrameRequestCallback>;
let nextFrame: number;

beforeEach(() => {
  frames = new Map(); nextFrame = 0;
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    const id = ++nextFrame; frames.set(id, callback); return id;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => frames.delete(id));
});

afterEach(() => {
  if (root) flushSync(() => root!.unmount());
  root = undefined; container?.remove(); vi.unstubAllGlobals();
});

function flushFrames() {
  const pending = [...frames.values()]; frames.clear();
  flushSync(() => pending.forEach(callback => callback(performance.now())));
}

function mount() {
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  const requestSources = vi.fn(async (_target: ActivityDataTarget, _signal: AbortSignal, _skip: number, query?: ActivityDataSourceQuery) => ({
    ...listing, sources: sources.filter(source => source.binding === query?.source)
  }));
  const host: ActivityDataSelectionHost = {
    documentRevision: "revision", baseRevision: "base", saving: false, requestSources,
    describeActivity: id => ({ label: id === "root" ? "Sequence" : "Workflow", outputLabels: {} }),
    revealActivity: vi.fn(), findingsFor: () => []
  };
  const onSelect = vi.fn();
  flushSync(() => root!.render(<ActivityDataMemberBrowser host={host} target={{ nodeId: "consumer", inputKey: "text" }}
    result={listing} current refreshing={false} onSelect={onSelect} onRefusal={vi.fn()} onRefresh={vi.fn()} />));
  return { requestSources, onSelect };
}

describe("activity data source focus", () => {
  it("initializes the first available source when the author has not focused a choice", () => {
    const { requestSources, onSelect } = mount();
    flushFrames();
    const first = container.querySelector('[role="group"][aria-label="Workflow inputs"] [role="option"]');
    expect(first?.textContent).toContain("Request payload");
    expect(document.activeElement).toBe(first);
    expect(requestSources).not.toHaveBeenCalled(); expect(onSelect).not.toHaveBeenCalled();
  });

  it("keeps the explicitly focused outer variable across the pending source-list frame and Enter", async () => {
    const { requestSources, onSelect } = mount();
    const outer = [...container.querySelectorAll<HTMLButtonElement>('[role="group"][aria-label="Variables"] [role="option"]')]
      .find(option => option.textContent?.includes("Workflow"))!;
    expect(outer).toBeDefined(); outer.focus();
    // The source list is already interactive while its default focus frame is pending.
    flushFrames();
    expect(document.activeElement).toBe(outer);
    // Send Enter to the actual focused element, so a stolen focus cannot be hidden by dispatching to the desired row.
    flushSync(() => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    await vi.waitFor(() => expect(requestSources).toHaveBeenCalledTimes(1));
    expect(requestSources.mock.calls[0]![3]?.source).toEqual(outerBinding);
    await vi.waitFor(() => expect(container.querySelector(".wf-data-member-heading")?.textContent).toContain("Payload"));
    await vi.waitFor(() => expect(container.querySelector<HTMLButtonElement>(".wf-data-member-use")?.disabled).toBe(false));
    flushFrames();
    expect(document.activeElement?.textContent).toBe("Back");
    expect(onSelect).not.toHaveBeenCalled();
  });
});
