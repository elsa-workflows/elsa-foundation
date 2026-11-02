import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { HumanReviewPage } from "../HumanReviewPage";
import type { HumanReviewView, ReceiptStatus } from "../reviewsApi";

const cleanup = new Set<() => void>();
afterEach(() => { for (const dispose of cleanup) dispose(); cleanup.clear(); });
const reference = { workflowExecutionId: "run-1", activityExecutionId: "review-1" };

function view(overrides: Partial<HumanReviewView> = {}): HumanReviewView {
  return { ...reference, title: "Launch post", content: "Moss gardens reward patience.", notes: "Ready.", contentHash: "hash-1", status: "Pending", ...overrides };
}

function fakeContext(initial: HumanReviewView, receipts: ReceiptStatus[], afterDecision?: HumanReviewView) {
  let current = initial;
  const getJson = vi.fn(async (_path: string) => current);
  const postJson = vi.fn(async (_path: string, _body: unknown) => {
    const status = receipts.shift() ?? "Accepted";
    if (status === "Accepted" && afterDecision) current = afterDecision;
    return { status };
  });
  return { context: { http: { getJson, postJson } } as unknown as StudioEndpointContext, getJson, postJson };
}

async function settle() {
  for (let index = 0; index < 5; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

async function render(context: StudioEndpointContext) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<HumanReviewPage context={context} reference={reference} />));
  await settle();
  cleanup.add(() => { root.unmount(); container.remove(); });
  return container;
}

async function click(element: Element | null) {
  (element as HTMLElement).click();
  await settle();
}

describe("human review page", () => {
  it("shows the exact pending content, notes and both decisions", async () => {
    const container = await render(fakeContext(view(), []).context);

    expect(container.querySelector("[data-testid='review-content']")?.textContent).toBe("Moss gardens reward patience.");
    expect(container.textContent).toContain("Ready.");
    expect(container.querySelector("[data-testid='review-status']")?.textContent).toBe("Awaiting your decision");
    expect([...container.querySelectorAll("button")].map(x => x.textContent?.trim())).toEqual(expect.arrayContaining(["Approve", "Deny"]));
  });

  it("renders structured agent output as formatted JSON", async () => {
    const container = await render(fakeContext(view({ content: { headline: "Moss", tags: ["garden"] } }), []).context);

    expect(JSON.parse(container.querySelector("pre[data-testid='review-content']")!.textContent!)).toEqual({ headline: "Moss", tags: ["garden"] });
  });

  it("approves the pinned content hash with the comment and shows the resolved state", async () => {
    const fake = fakeContext(view(), ["Accepted"], view({ status: "Approved", decidedBy: "ada", decisionComment: "Ship it." }));
    const container = await render(fake.context);
    const textarea = container.querySelector("textarea")!;
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!;
    setter.call(textarea, "Ship it.");
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
    await settle();
    await click([...container.querySelectorAll("button")].find(x => x.textContent?.trim() === "Approve")!);

    expect(fake.postJson).toHaveBeenCalledTimes(1);
    expect(fake.postJson.mock.calls[0][1]).toMatchObject({ contentHash: "hash-1", decision: "Approve", comment: "Ship it." });
    expect(container.querySelector("[data-receipt='Accepted']")).not.toBeNull();
    expect(container.querySelector("[data-testid='review-status']")?.textContent).toBe("Approved");
    expect(container.querySelector("textarea")).toBeNull();
    expect(container.querySelector("[data-testid='review-resolution']")?.textContent).toContain("ada");
  });

  it("retries an unavailable submission with the same request ID", async () => {
    const fake = fakeContext(view(), ["Unavailable", "Accepted"], view({ status: "Rejected" }));
    const container = await render(fake.context);
    const deny = () => [...container.querySelectorAll("button")].find(x => x.textContent?.trim() === "Deny")!;

    await click(deny());
    expect(container.querySelector("[data-receipt='Unavailable']")).not.toBeNull();
    await click(deny());

    const [first, second] = fake.postJson.mock.calls.map(call => (call[1] as unknown as { requestId: string }).requestId);
    expect(second).toBe(first);
    expect(container.querySelector("[data-testid='review-status']")?.textContent).toBe("Rejected");
  });

  it("replays a queued decision with the same request ID", async () => {
    const fake = fakeContext(view(), ["Pending", "Accepted"], view({ status: "Approved" }));
    const container = await render(fake.context);
    const approve = () => [...container.querySelectorAll("button")].find(x => x.textContent?.trim() === "Approve")!;

    await click(approve());
    expect(container.querySelector("[data-receipt='Pending']")).not.toBeNull();
    await click(approve());

    const [first, second] = fake.postJson.mock.calls.map(call => (call[1] as unknown as { requestId: string }).requestId);
    expect(second).toBe(first);
  });

  it("starts fresh when the page moves to another review", async () => {
    const fake = fakeContext(view(), ["Unavailable"]);
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    cleanup.add(() => { root.unmount(); container.remove(); });
    flushSync(() => root.render(<HumanReviewPage context={fake.context} reference={reference} />));
    await settle();
    await click([...container.querySelectorAll("button")].find(x => x.textContent?.trim() === "Deny")!);
    expect(container.querySelector("[data-receipt]")).not.toBeNull();

    const other = { workflowExecutionId: "run-2", activityExecutionId: "review-9" };
    fake.getJson.mockImplementation(async () => view({ ...other, title: "Second post", contentHash: "hash-2" }));
    flushSync(() => root.render(<HumanReviewPage context={fake.context} reference={other} />));
    await settle();

    expect(container.querySelector("[data-receipt]")).toBeNull();
    expect(container.textContent).toContain("Second post");
    expect(fake.getJson.mock.calls.at(-1)?.[0]).toBe("/agent/reviews/run-2/review-9");
    await click([...container.querySelectorAll("button")].find(x => x.textContent?.trim() === "Deny")!);
    const ids = fake.postJson.mock.calls.map(call => (call[1] as unknown as { requestId: string; contentHash: string }));
    expect(ids[1].requestId).not.toBe(ids[0].requestId);
    expect(ids[1].contentHash).toBe("hash-2");
  });

  it("reports conflicts and an earlier resolution without deciding again", async () => {
    const fake = fakeContext(view(), ["AlreadyResolved"], undefined);
    const container = await render(fake.context);
    await click([...container.querySelectorAll("button")].find(x => x.textContent?.trim() === "Approve")!);

    expect(container.querySelector("[data-receipt='AlreadyResolved']")?.textContent).toContain("Another decision");
  });

  it("explains a missing or foreign review", async () => {
    const getJson = vi.fn(async (_path: string): Promise<unknown> => { throw Object.assign(new Error("Not found"), { status: 404 }); });
    const container = await render({ http: { getJson, postJson: vi.fn() } } as unknown as StudioEndpointContext);

    expect(container.textContent).toContain("Review not found");
  });
});
