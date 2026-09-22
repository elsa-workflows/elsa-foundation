import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionContractProposalReview } from "../ActivityDefinitionContractProposalReview";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { ActivityContractProposalView, ActivityDefinitionDraftView } from "../activityDefinitionTypes";

afterEach(() => {
  clearApiCapabilityCache();
  vi.useRealTimers();
  document.body.replaceChildren();
});

describe("Activity Definition contract proposal review", () => {
  it("debounces generation, cancels obsolete exact bindings, and never blocks editor state", async () => {
    vi.useFakeTimers();
    let proposalSignal: AbortSignal | undefined;
    const postJson = vi.fn((_url: string, _body: unknown, init?: RequestInit) => {
      proposalSignal = init?.signal ?? undefined;
      return new Promise((_resolve, reject) => proposalSignal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError"))));
    });
    const harness = renderReview({ context: endpoint(postJson), draft: exactDraft() });

    await vi.advanceTimersByTimeAsync(899);
    expect(postJson).not.toHaveBeenCalled();
    expect(harness.container.textContent).toContain("Waiting briefly for editing to settle");

    await vi.advanceTimersByTimeAsync(1);
    await vi.waitFor(() => expect(postJson).toHaveBeenCalledWith(
      "/design/activities/drafts/draft-1/contract-proposals",
      exactRequest(3),
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    ));
    expect(harness.container.textContent).toContain("It never changes the draft until selected changes are applied");

    harness.render({ draft: exactDraft({ revision: 4, manifestFingerprint: "sha256:implementation-4" }), enabled: false });
    expect(proposalSignal?.aborted).toBe(true);
    await vi.waitFor(() => expect(harness.container.textContent).toContain("Waiting for autosave"));
    expect(harness.onApplyingChange).not.toHaveBeenCalled();
    await harness.unmount();
  });

  it("reviews structured changes individually, defaults destructive changes off, and applies only selected ids", async () => {
    vi.useFakeTimers();
    const applied = exactDraft({ revision: 4, manifestFingerprint: "sha256:implementation-3" });
    applied.contract.inputs.push(input("currency"));
    const postJson = vi.fn(async (url: string) => url.endsWith("/apply") ? applied : proposal());
    const harness = renderReview({ context: endpoint(postJson), draft: exactDraft() });

    await vi.advanceTimersByTimeAsync(900);
    await vi.waitFor(() => expect(harness.container.textContent).toContain("Add input currency"));
    expect(harness.container.textContent).toContain("Remove input order");
    expect(harness.container.textContent).toContain("Unsupported provider proposal change");
    expect(harness.container.textContent).not.toContain("opaque-provider-secret");
    expect(harness.container.textContent).toContain("1 of 3 changes selected");
    expect(harness.container.textContent).toContain("activity.graph.proposal-warning");
    expect(harness.container.textContent).toContain("Configured Expression default");
    expect(harness.container.textContent).not.toContain("private.default.expression");

    const remove = articleByText(harness.container, "Remove input order");
    expect(buttonByText(remove, "Reject").getAttribute("aria-pressed")).toBe("true");
    expect(buttonByText(remove, "Accept").getAttribute("aria-pressed")).toBe("false");
    const unknown = articleByText(harness.container, "Unsupported provider proposal change");
    expect((buttonByText(unknown, "Accept") as HTMLButtonElement).disabled).toBe(true);

    click(buttonByText(harness.container, "Apply selected changes"));
    await vi.waitFor(() => expect(postJson).toHaveBeenLastCalledWith("/design/activities/drafts/draft-1/contract-proposals/apply", {
      ...exactRequest(3),
      proposalFingerprint: "sha256:proposal-3",
      selectedChangeIds: ["input:add:currency"]
    }));
    expect(harness.onApplyingChange.mock.calls.map(call => call[0])).toEqual([true, false]);
    expect(harness.onApplied).toHaveBeenCalledWith(applied);
    await harness.unmount();
  });

  it("supports explicit rejection and preserves the exact local draft when apply fails", async () => {
    vi.useFakeTimers();
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/apply")) throw new TypeError("private transport detail");
      return proposal();
    });
    const draft = exactDraft();
    const harness = renderReview({ context: endpoint(postJson), draft });

    await vi.advanceTimersByTimeAsync(900);
    await vi.waitFor(() => expect(harness.container.textContent).toContain("Add input currency"));
    const addition = articleByText(harness.container, "Add input currency");
    click(buttonByText(addition, "Reject"));
    expect(harness.container.textContent).toContain("0 of 3 changes selected");
    expect((buttonByText(harness.container, "Apply selected changes") as HTMLButtonElement).disabled).toBe(true);
    click(buttonByText(addition, "Accept"));
    click(buttonByText(harness.container, "Apply selected changes"));
    await vi.waitFor(() => expect(harness.container.textContent).toContain("Local contract and implementation content remain unchanged"));
    expect(harness.container.textContent).not.toContain("private transport detail");
    expect(harness.onApplied).not.toHaveBeenCalled();
    expect(draft.contract.inputs).toHaveLength(1);
    expect(draft.provider.payload).toEqual({ local: "content" });
    await harness.unmount();
  });

  it("invalidates a ready proposal when the exact revision or provider fingerprint changes", async () => {
    vi.useFakeTimers();
    const postJson = vi.fn(async (_url: string, body: unknown) => proposal((body as { expectedRevision: number }).expectedRevision));
    const harness = renderReview({ context: endpoint(postJson), draft: exactDraft() });
    await vi.advanceTimersByTimeAsync(900);
    await vi.waitFor(() => expect(harness.container.querySelector("[aria-label='Exact proposal binding']")).not.toBeNull());
    expect(harness.container.textContent).toContain("sha256:implementation-3");

    harness.render({ draft: exactDraft({ revision: 4, manifestFingerprint: "sha256:implementation-4" }), enabled: true });
    await vi.waitFor(() => expect(harness.container.textContent).not.toContain("sha256:implementation-3"));
    expect(harness.container.textContent).toContain("Waiting briefly for editing to settle");
    await vi.advanceTimersByTimeAsync(900);
    await vi.waitFor(() => expect(postJson).toHaveBeenLastCalledWith(
      "/design/activities/drafts/draft-1/contract-proposals",
      exactRequest(4),
      expect.anything()
    ));
    await harness.unmount();
  });
});

function renderReview({ context, draft }: { context: StudioEndpointContext; draft: ActivityDefinitionDraftView }) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const onApplyingChange = vi.fn();
  const onApplied = vi.fn();
  const onFocusDiagnostic = vi.fn(async () => ({ kind: "unsupported" as const, announcement: "Not focused." }));
  const render = ({ draft: next = draft, enabled = true }: { draft?: ActivityDefinitionDraftView; enabled?: boolean } = {}) => {
    flushSync(() => root.render(<ActivityDefinitionContractProposalReview
      context={context}
      draft={next}
      enabled={enabled}
      onApplyingChange={onApplyingChange}
      onApplied={onApplied}
      onFocusDiagnostic={onFocusDiagnostic}
    />));
  };
  render();
  return {
    container,
    render,
    onApplyingChange,
    onApplied,
    async unmount() {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function endpoint(postJson: (url: string, body: unknown, init?: RequestInit) => Promise<unknown>): StudioEndpointContext {
  return {
    baseUrl: `test://proposal-${Math.random()}`,
    http: {
      getJson: vi.fn(async (url: string) => {
        if (url === "/capabilities") return { capabilities: [{
          id: "elsa.api.activity-design",
          contractVersion: "1",
          links: [
            { rel: "activity-draft-contract-proposals", href: "design/activities/drafts/{draftId}/contract-proposals", templated: true },
            { rel: "activity-draft-contract-proposals-apply", href: "design/activities/drafts/{draftId}/contract-proposals/apply", templated: true }
          ]
        }] };
        throw new Error(`Unexpected GET ${url}`);
      }),
      postJson,
      putJson: vi.fn(),
      requestJson: vi.fn(),
      deleteJson: vi.fn(),
      postForm: vi.fn()
    }
  } as unknown as StudioEndpointContext;
}

function exactRequest(revision: number) {
  return {
    expectedRevision: revision,
    expectedProviderKey: "elsa.activity-graph",
    expectedProviderSchemaVersion: "1",
    expectedManifestFingerprint: `sha256:implementation-${revision}`
  };
}

function proposal(revision = 3): ActivityContractProposalView {
  return {
    draftId: "draft-1",
    revision,
    providerKey: "elsa.activity-graph",
    providerSchemaVersion: "1",
    manifestFingerprint: `sha256:implementation-${revision}`,
    proposalFingerprint: `sha256:proposal-${revision}`,
    changes: [{
      changeId: "input:add:currency",
      operation: "Add",
      memberKind: "Input",
      referenceKey: "currency",
      input: input("currency", { default: { syntax: "Expression", value: "private.default.expression" } })
    }, {
      changeId: "input:remove:order",
      operation: "Remove",
      memberKind: "Input",
      referenceKey: "order"
    }, {
      changeId: "future:opaque",
      operation: "MergeProviderPayload",
      memberKind: "FutureMember",
      referenceKey: "future",
      opaquePayload: { value: "opaque-provider-secret" }
    }],
    diagnostics: [{
      code: "activity.graph.proposal-warning",
      severity: "Warning",
      message: "Review inferred graph contract changes.",
      subject: { kind: "ActivityDraft", id: "draft-1", revision },
      location: null,
      remediation: "Accept only intended changes.",
      metadata: {}
    }]
  };
}

function exactDraft(overrides: Partial<{ revision: number; manifestFingerprint: string }> = {}): ActivityDefinitionDraftView {
  const revision = overrides.revision ?? 3;
  return {
    draftId: "draft-1",
    definitionId: "definition-1",
    revision,
    status: "Active",
    contract: { contractSchemaVersion: "1", inputs: [input("order")], outputs: [], outcomes: [] },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "1",
      manifestFingerprint: overrides.manifestFingerprint ?? `sha256:implementation-${revision}`,
      payload: { local: "content" }
    },
    layout: [],
    createdAt: "2026-07-18T09:00:00Z",
    updatedAt: "2026-07-18T09:00:00Z"
  };
}

function input(referenceKey: string, overrides: Record<string, unknown> = {}) {
  return {
    referenceKey,
    name: referenceKey === "order" ? "Order" : "Currency",
    displayName: referenceKey === "order" ? "Order" : "Currency",
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
    durability: "Required",
    ...overrides
  };
}

function buttonByText(container: ParentNode, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.includes(text));
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

function articleByText(container: HTMLElement, text: string) {
  const article = [...container.querySelectorAll("article")].find(candidate => candidate.textContent?.includes(text));
  if (!article) throw new Error(`Article '${text}' not found.`);
  return article;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}
