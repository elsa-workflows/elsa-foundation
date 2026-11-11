import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { AgentPlayground, parseInputs, pollDelay, readPlaygroundDraftId } from "../AgentPlayground";
import type { PlaygroundDraft, PlaygroundEvidence } from "../playgroundApi";

const cleanup = new Set<() => void>();
afterEach(() => { for (const dispose of cleanup) dispose(); cleanup.clear(); });

function draft(overrides: Partial<PlaygroundDraft> = {}): PlaygroundDraft {
  return {
    draftId: "draft-1", definitionId: "copywriter", revision: 3,
    contract: { inputs: [
      { referenceKey: "brief-key", name: "Brief", type: { alias: "String" }, isRequired: true, isNullable: false },
      { referenceKey: "$agent.options", name: "AgentOptions", type: { alias: "System.Text.Json.JsonElement" }, isRequired: false, isNullable: true }
    ] },
    provider: { providerKey: "elsa.agent", schemaVersion: "2", payload: { allowContinuity: true } },
    ...overrides
  };
}

function evidence(status: string, turn: Partial<NonNullable<NonNullable<PlaygroundEvidence["invocation"]>["turn"]>> | null, overrides: Partial<NonNullable<PlaygroundEvidence["invocation"]>> = {}): PlaygroundEvidence {
  return {
    testRunId: "run-1", draftId: "draft-1", draftRevision: 3, workflowExecutionId: "wf-1", status,
    invocation: {
      workflowExecutionId: "wf-1", activityExecutionId: "act-1",
      agent: { definitionId: "copywriter", definitionVersionId: "activity-test-version-abc", version: "draft-3", templateHash: "t", artifactId: "a", artifactHash: "h", allowContinuity: true },
      model: { providerKey: "anthropic", connectionName: "model", modelId: "claude-fable-5-1" },
      limits: { maximumModelCalls: 8, timeoutMilliseconds: 300000 },
      activityStatus: status === "Completed" ? "Completed" : "Running",
      output: status === "Completed" ? { captureMode: "Payload", value: { Article: "Moss gardens" } } : null,
      turn: turn === null ? null : {
        status: "Accepted", application: "SettlementPending", deadline: "2026-11-03T00:00:00Z", maximumModelCalls: 8,
        modelRequests: [{ id: "r", reservedAt: "2026-11-03T00:00:00Z", maximumOutputTokens: 4096, providerKey: "anthropic", configuredModelId: "claude-fable-5-1" }],
        usage: { observedAt: "2026-11-03T00:00:00Z", usageAvailable: true, inputTokens: 5, outputTokens: 3, totalTokens: 8 },
        conversation: { conversationId: "c".repeat(64), baseRevision: 0, currentRevision: 1, outcomeUnknown: false, turnActive: false },
        ...turn
      },
      ...overrides
    }
  };
}

function fakeContext(responses: { draft?: PlaygroundDraft | Error; drafts?: PlaygroundDraft[]; evidence?: Array<PlaygroundEvidence | Error>; start?: Array<unknown>;
  cancel?: { status: string; reason?: string | null } } = {}) {
  const evidences = [...(responses.evidence ?? [evidence("Completed", {})])];
  const drafts = [...(responses.drafts ?? [])];
  const starts = [...(responses.start ?? [])];
  const getJson = vi.fn(async (path: string) => {
    if (path.startsWith("/design/activities/drafts/")) {
      if (responses.draft instanceof Error) throw responses.draft;
      return (drafts.length > 1 ? drafts.shift() : drafts[0]) ?? responses.draft ?? draft();
    }
    const next = evidences.length > 1 ? evidences.shift() : evidences[0];
    if (next instanceof Error) throw next;
    return next;
  });
  const postJson = vi.fn(async (path: string, _body: unknown) => {
    if (path.endsWith("/cancel")) return { status: "Running", cancellation: responses.cancel ?? { status: "Requested" } };
    const next = starts.shift();
    if (next instanceof Error) throw next;
    return next ?? { testRunId: "run-1", status: "DispatchAccepted" };
  });
  return { context: { http: { getJson, postJson } } as unknown as StudioEndpointContext, getJson, postJson };
}

async function settle() {
  for (let index = 0; index < 10; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

// Waits for a condition across render, effect and timer turns instead of assuming how many turns they take.
async function until(condition: () => boolean) {
  for (let index = 0; index < 200 && !condition(); index++) await new Promise(resolve => setTimeout(resolve, 0));
  await settle();
}

async function render(context: StudioEndpointContext) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<AgentPlayground context={context} draftId="draft-1" pollInterval={0} />));
  await settle();
  cleanup.add(() => { root.unmount(); container.remove(); });
  return container;
}

function type(container: HTMLElement, label: string, value: string) {
  const field = [...container.querySelectorAll("label")].find(x => x.textContent?.startsWith(label))!;
  const textarea = document.getElementById(field.htmlFor) as HTMLTextAreaElement;
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!;
  setter.call(textarea, value);
  textarea.dispatchEvent(new Event("input", { bubbles: true }));
}

async function submit(container: HTMLElement) {
  container.querySelector<HTMLFormElement>("form[aria-label='Run']")!.requestSubmit();
  await settle();
}

const button = (container: HTMLElement, text: string) => [...container.querySelectorAll("button")].find(x => x.textContent?.trim() === text) ?? null;

describe("agent playground", () => {
  it("reads the draft ID from its route", () => {
    expect(readPlaygroundDraftId("/agents/drafts/draft%201/playground")).toBe("draft 1");
    expect(readPlaygroundDraftId("/agents")).toBeNull();
  });

  it("runs the saved revision fresh through the generic Test Run and shows the accepted evidence", async () => {
    const { context, postJson } = fakeContext({ evidence: [evidence("Running", { status: "Dispatching", application: "NotAccepted", usage: null, modelRequests: [] }, { output: null }), evidence("Completed", {})] });
    const container = await render(context);

    expect(container.textContent).toContain("Brief (required)");
    expect(container.textContent).not.toContain("AgentOptions");
    type(container, "Brief", "moss gardens");
    await submit(container);

    expect(postJson).toHaveBeenCalledWith("/publishing/activity-drafts/draft-1/test-runs", expect.objectContaining({
      expectedRevision: 3,
      inputs: { "brief-key": { state: "Present", value: "moss gardens" }, "$agent.options": { state: "Present", value: {} } }
    }));
    expect(JSON.parse(container.querySelector("[data-testid='evidence-output']")!.textContent!)).toEqual({ Article: "Moss gardens" });
    expect(container.querySelector("[data-testid='evidence-usage']")!.textContent).toBe("5 input, 3 output, 8 total tokens");
    expect(container.querySelector("[data-testid='evidence-requests']")!.textContent).toBe("1 of at most 8");
    expect(container.querySelector("[data-testid='evidence-application']")!.textContent).toBe("Accepted and used; settlement is being recorded");
    expect(container.querySelector<HTMLAnchorElement>("[data-testid='playground-invocation-link']")!.getAttribute("href")).toBe("/agents/invocations/wf-1/act-1");
  });

  it("continues an accepted conversation explicitly, with the revision it expects", async () => {
    const { context, postJson } = fakeContext();
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    await submit(container);

    await (async () => { button(container, "Continue this conversation")!.click(); await settle(); })();
    expect((container.querySelector("input[type='radio']:checked")!.parentElement!.textContent)).toContain("Continue conversation at revision 1");
    await submit(container);

    expect(postJson).toHaveBeenLastCalledWith("/publishing/activity-drafts/draft-1/test-runs", expect.objectContaining({
      inputs: expect.objectContaining({ "$agent.options": { state: "Present", value: { conversationId: "c".repeat(64), revision: 1 } } })
    }));
  });

  it("offers no conversation choice when the saved agent does not allow continuity", async () => {
    const container = await render(fakeContext({ draft: draft({ provider: { providerKey: "elsa.agent", schemaVersion: "1", payload: {} },
      contract: { inputs: [{ referenceKey: "brief-key", name: "Brief", type: { alias: "String" }, isRequired: true, isNullable: false }] } }) }).context);

    expect(container.querySelector("fieldset")).toBeNull();
  });

  it("refuses an empty required input without starting a run", async () => {
    const { context, postJson } = fakeContext();
    const container = await render(context);
    await submit(container);

    expect(container.querySelector("[role='alert']")?.textContent).toBe("Enter a value.");
    expect(postJson).not.toHaveBeenCalled();
    type(container, "Brief", "m");
    await settle();
    expect(container.querySelector("[role='alert']")).toBeNull();
  });

  it("says the outcome is unknown instead of claiming a result", async () => {
    const container = await render(fakeContext({ evidence: [evidence("Faulted", { status: "OutcomeUnknown", application: "NotAccepted" }, { output: null })] }).context);
    type(container, "Brief", "moss gardens");
    await submit(container);

    expect(container.textContent).toContain("The model may have received this request, but no result was accepted.");
    expect(container.textContent).toContain("Start a fresh conversation, or continue from the last accepted revision.");
    expect(container.querySelector("[data-testid='evidence-output']")).toBeNull();
    expect(button(container, "Continue this conversation")).toBeNull();
  });

  it("says the result needs the value permission when it was left out for the caller", async () => {
    const container = await render(fakeContext({ evidence: [{ ...evidence("Completed", {}, { output: { captureMode: "Payload", value: null } }), valuesPermitted: false }] }).context);
    type(container, "Brief", "moss gardens");
    await submit(container);

    expect(container.querySelector("[data-testid='evidence-output-withheld']")!.textContent).toContain("permission to inspect captured values");
  });

  it("explains a result the host's capture policy keeps out", async () => {
    const container = await render(fakeContext({ evidence: [evidence("Completed", {}, { output: { captureMode: "DiagnosticSnapshot", value: null } })] }).context);
    type(container, "Brief", "moss gardens");
    await submit(container);

    expect(container.querySelector("[data-testid='evidence-output-withheld']")!.textContent).toContain("DiagnosticSnapshot");
  });

  it("stops a running turn without claiming a sent request was recalled", async () => {
    const running = evidence("Running", { status: "Dispatching", application: "NotAccepted" }, { output: null });
    const { context, postJson } = fakeContext({ evidence: [running, running] });
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    await submit(container);

    button(container, "Stop")!.click();
    await settle();
    expect(postJson).toHaveBeenCalledWith("/publishing/activity-test-runs/run-1/cancel", {});
    expect(container.textContent).toContain("A request already sent to the model is not recalled.");
  });

  it("retries a lost start with the same idempotency key", async () => {
    const lost = Object.assign(new Error("Service unavailable"), { status: 503 });
    const { context, postJson } = fakeContext({ start: [lost] });
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    await submit(container);
    await submit(container);

    const keys = postJson.mock.calls.map(call => (call[1] as { idempotencyKey: string }).idempotencyKey);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
  });

  it("shows a missing draft as not found", async () => {
    const container = await render(fakeContext({ draft: Object.assign(new Error("Not found"), { status: 404 }) }).context);

    expect(container.querySelector("[data-testid='playground-missing']")).not.toBeNull();
  });

  it("continues a chained conversation at its latest accepted revision", async () => {
    const conversation = (revision: number) => ({ conversationId: "c".repeat(64), baseRevision: revision - 1, currentRevision: revision, outcomeUnknown: false, turnActive: false });
    const { context, postJson } = fakeContext({ evidence: [evidence("Completed", { conversation: conversation(1) }), evidence("Completed", { conversation: conversation(2) })] });
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    await submit(container);
    button(container, "Continue this conversation")!.click();
    await settle();
    await submit(container);
    await submit(container);

    const sent = postJson.mock.calls.map(call => (call[1] as { inputs: Record<string, { value: unknown }> }).inputs["$agent.options"].value);
    expect(sent).toEqual([{}, { conversationId: "c".repeat(64), revision: 1 }, { conversationId: "c".repeat(64), revision: 2 }]);
  });

  it("backs off failed status reads and stops refreshing until asked", async () => {
    const failure = Object.assign(new Error("Unavailable"), { status: 503 });
    const { context, getJson } = fakeContext({ evidence: [failure] });
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    const reads = () => getJson.mock.calls.filter(call => String(call[0]).includes("/evidence")).length;
    await submit(container);
    await until(() => container.querySelector("[data-testid='playground-poll-stopped']") !== null);

    expect(reads()).toBe(5);
    // A run that is no longer refreshed does not lock the page.
    expect(button(container, "Run")!.disabled).toBe(false);
    button(container, "Retry")!.click();
    await until(() => reads() === 10 && container.querySelector("[data-testid='playground-poll-stopped']") !== null);
    expect(reads()).toBe(10);
  });

  it("reads immediately, then waits an interval, doubling it after each failure", () => {
    expect([pollDelay(1000, 0, false), pollDelay(1000, 0, true), pollDelay(1000, 1, true), pollDelay(1000, 3, false)]).toEqual([0, 1000, 2000, 8000]);
  });

  it("announces the first status of a run through a live region mounted before it", async () => {
    const container = await render(fakeContext().context);
    const live = container.querySelector("[data-testid='playground-announce']")!;
    expect(live.getAttribute("role")).toBe("status");
    expect(live.textContent).toBe("");

    type(container, "Brief", "moss gardens");
    await submit(container);
    expect(container.querySelector("[data-testid='playground-announce']")).toBe(live);
    expect(live.textContent).toContain("Completed. The model's result was accepted.");
  });

  it("keeps Stop available when the stop was not applied", async () => {
    const running = evidence("Running", { status: "Dispatching", application: "NotAccepted" }, { output: null });
    const { context } = fakeContext({ evidence: [running, running], cancel: { status: "Unavailable", reason: "Runtime Evidence is not available yet." } });
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    await submit(container);
    button(container, "Stop")!.click();
    await settle();

    expect(container.textContent).toContain("Stop was not applied: Runtime Evidence is not available yet.");
    expect(button(container, "Stop")!.disabled).toBe(false);
  });

  it("shows why the generic Test Run refused the start", async () => {
    const { context, getJson } = fakeContext({ start: [{ testRunId: "run-1", status: "ValidationRejected", reason: null,
      failure: { code: "activity.draft.stale-revision", message: "The draft revision is stale.",
        diagnostics: [{ code: "agent.model.unavailable", message: "The selected model connection is unavailable." }] } }] });
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    await submit(container);

    expect(container.querySelector("[data-testid='playground-refusal']")!.textContent).toBe("The draft revision is stale.");
    expect(container.textContent).toContain("activity.draft.stale-revision");
    expect(container.querySelector("[data-testid='playground-diagnostics']")!.textContent).toContain("agent.model.unavailable The selected model connection is unavailable.");
    expect(getJson.mock.calls.some(call => String(call[0]).includes("/evidence"))).toBe(false);
    expect(button(container, "Run")!.disabled).toBe(false);
  });

  it("does not offer to continue an earlier revision's conversation after a reload", async () => {
    const { context } = fakeContext({ drafts: [draft(), draft({ revision: 4 })] });
    const container = await render(context);
    type(container, "Brief", "moss gardens");
    await submit(container);
    expect(button(container, "Continue this conversation")).not.toBeNull();

    button(container, "Reload draft")!.click();
    await settle();
    expect(container.textContent).toContain("Draft revision 4");
    expect(button(container, "Continue this conversation")).toBeNull();
  });

  it("parses typed inputs as JSON and empty optional ones as null", () => {
    const inputs = [
      { referenceKey: "count", name: "Count", type: { alias: "Int32" }, isRequired: false, isNullable: true },
      { referenceKey: "tags", name: "Tags", type: { alias: "Object" }, isRequired: true, isNullable: false }
    ];
    expect(parseInputs(inputs, { tags: "[\"moss\"]" })).toEqual({ values: { count: null, tags: ["moss"] }, errors: {} });
    expect(parseInputs(inputs, { tags: "{" }).errors).toEqual({ tags: "Enter valid JSON." });
    const names = [{ referenceKey: "names", name: "Names", type: { alias: "String", collectionKind: "Array" }, isRequired: true, isNullable: false }];
    expect(parseInputs(names, { names: "[\"moss\",\"fern\"]" }).values).toEqual({ names: ["moss", "fern"] });
  });
});
