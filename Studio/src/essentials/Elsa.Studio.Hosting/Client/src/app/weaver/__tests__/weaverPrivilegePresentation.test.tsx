import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import type { StudioWeaverPreparedInvocation, StudioWeaverResultRendererProps } from "../../../sdk";
import { WeaverProposalReview } from "../WeaverProposalReview";
import type { WeaverActionProposal } from "../weaverTypes";

describe("effective proposal privilege presentation", () => {
  it.each([
    ...[undefined, null, []].flatMap(classes => [3, "Admin", "admin"].map(risk => ({ classes, risk, expected: "Administrative" }))),
    { classes: undefined, risk: "reviewRequired", expected: "None" },
    { classes: null, risk: 2, expected: "None" },
    { classes: [], risk: 0, expected: "None" },
    { classes: [0, 1, 2], risk: "reviewRequired", expected: "Administrative, Irrecoverable, Activation" },
    { classes: ["Activation", "Irrecoverable", "Administrative"], risk: "reviewRequired", expected: "Administrative, Irrecoverable, Activation" },
    { classes: ["activation", "irrecoverable", "activation"], risk: "Admin", expected: "Administrative, Irrecoverable, Activation" },
    { classes: ["irrecoverable"], risk: "reviewRequired", expected: "Irrecoverable" },
    { classes: [2], risk: 3, expected: "Administrative, Activation" },
    { classes: ["future"], risk: "reviewRequired", expected: "Unknown requirement" },
    { classes: [3], risk: "Admin", expected: "Administrative, Unknown requirement" },
    { classes: [false, null], risk: "reviewRequired", expected: "Unknown requirement" },
    { classes: "activation", risk: "reviewRequired", expected: "Unknown requirement" },
    { classes: [], risk: "future", expected: "Unknown requirement" },
    { classes: [], risk: "critical", expected: "Unknown requirement" },
    { classes: ["activation"], risk: 4, expected: "Activation, Unknown requirement" }
  ])("displays $expected from the frozen requirements ($risk / $classes)", ({ classes, risk, expected }) => {
    const prepared = preparation(classes, risk);
    const proposal = Object.freeze({ id: "review", title: "Review operation", summary: "Review the owner's operation.",
      risk: "review-required" as const, revision: "r1", status: "awaiting-approval" as const, preparedInvocation: prepared,
      resourceTarget: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" } });
    const original = JSON.stringify(proposal);
    const approve = vi.fn(); const execute = vi.fn();
    const { container, unmount } = render(<WeaverProposalReview proposals={[proposal]} onApprove={approve} onDeny={() => {}} onExecute={execute} />);
    try {
      expect(container.querySelector('[aria-label="Required host privileges"] dd')?.textContent).toBe(expected);
      expect(JSON.stringify(proposal)).toBe(original);
      expect(prepared.approvalDigest).toBe("unchanged-digest");
      expect(approve).not.toHaveBeenCalled(); expect(execute).not.toHaveBeenCalled();
    } finally { unmount(); }
  });

  it("derives the legacy Admin requirement without a prepared payload", () => {
    const proposal: WeaverActionProposal = Object.freeze({ id: "legacy", title: "Legacy operation", summary: "Review legacy operation.",
      risk: "admin", revision: "r1", status: "awaiting-approval",
      resourceTarget: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" } });
    const { container, unmount } = render(<WeaverProposalReview proposals={[proposal]} onApprove={() => {}} onDeny={() => {}} onExecute={() => {}} />);
    try { expect(container.querySelector('[aria-label="Required host privileges"] dd')?.textContent).toBe("Administrative"); }
    finally { unmount(); }
  });

  it.each([
    { reviewReady: false }, { isLoading: true }, { revision: undefined }, { resourceTarget: undefined }
  ])("does not claim an empty requirement set while review details are incomplete ($reviewReady / $isLoading / $revision)", incomplete => {
    const proposal: WeaverActionProposal = { id: "loading", title: "Weaver proposal", summary: "Review details are loading.",
      risk: "review-required", revision: "r1", status: "awaiting-approval",
      resourceTarget: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" }, ...incomplete };
    const { container, unmount } = render(<WeaverProposalReview proposals={[proposal]} onApprove={() => {}} onDeny={() => {}} onExecute={() => {}} />);
    try { expect(container.querySelector('[aria-label="Required host privileges"] dd')?.textContent).toBe("Not yet available"); }
    finally { unmount(); }
  });

  it("keeps performed terminal with disabled common actions and the owning renderer intact", () => {
    const execute = vi.fn(); const approve = vi.fn(); const deny = vi.fn();
    const proposal: WeaverActionProposal = { id: "performed", title: "Operation performed", summary: "The action ran without persisting a change.",
      risk: "review-required", revision: "r1", status: "performed", preparedInvocation: preparation(["activation"], "reviewRequired"),
      resourceTarget: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" }, resultType: "sample.performed" };
    const renderer = vi.fn((props: StudioWeaverResultRendererProps) => <span data-invocation={props.preparedInvocation?.invocationId}>Owning module result</span>);
    const { container, unmount } = render(<WeaverProposalReview proposals={[proposal]}
      resultRenderers={[{ id: "sample", moduleId: "Sample", displayName: "Sample result", resultTypes: ["sample.performed"], component: renderer }]}
      onApprove={approve} onDeny={deny} onExecute={execute} />);
    try {
      expect(container.querySelector('article')?.getAttribute("data-status")).toBe("performed");
      expect(container.textContent).toContain("Owning module result");
      expect(container.textContent).not.toContain("Persisted resource");
      const buttons = [...container.querySelectorAll<HTMLButtonElement>("footer button")];
      expect(buttons.map(button => button.textContent?.trim())).toEqual(["Approve", "Deny", "Apply"]);
      expect(buttons.every(button => button.disabled)).toBe(true);
      buttons.forEach(button => button.click());
      expect(approve).not.toHaveBeenCalled(); expect(deny).not.toHaveBeenCalled(); expect(execute).not.toHaveBeenCalled();
      expect(renderer.mock.calls[0][0].preparedInvocation).toBe(proposal.preparedInvocation);
    } finally { unmount(); }
  });
});

function preparation(classes: unknown, risk: unknown): StudioWeaverPreparedInvocation {
  return Object.freeze({ invocationId: "invocation", sessionId: "session", turnId: "turn", toolCallId: "call", toolName: "sample",
    toolContractVersion: "1", moduleId: "Sample", actorId: "actor", tenantId: "tenant", target: null, arguments: {},
    approvalDigest: "unchanged-digest", risk, preview: { typeId: "sample.preview", schemaVersion: "1", data: {} }, executionLocation: "server",
    requiresApproval: true, client: null, expiresAt: "2099-01-01T00:00:00Z",
    ...(classes === undefined ? {} : { requiredPrivilegeClasses: Array.isArray(classes) ? Object.freeze([...classes]) : classes })
  }) as StudioWeaverPreparedInvocation;
}

function render(element: React.ReactElement) {
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container); flushSync(() => root.render(element));
  return { container, unmount: () => { flushSync(() => root.unmount()); container.remove(); } };
}
