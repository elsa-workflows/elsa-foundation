import { describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { decideReview, getReview, readReviewReference } from "../reviewsApi";

function context() {
  const http = { getJson: vi.fn(async (_path: string) => ({})), postJson: vi.fn(async (_path: string, _body: unknown) => ({ status: "Accepted" })) };
  return { context: { http } as unknown as StudioEndpointContext, http };
}

describe("agent reviews api", () => {
  it("reads and decides the exact encoded wait without naming an actor", async () => {
    const { context: ctx, http } = context();
    await getReview(ctx, { workflowExecutionId: "run/1", activityExecutionId: "review 1" });
    await decideReview(ctx, { workflowExecutionId: "run/1", activityExecutionId: "review 1" },
      { contentHash: "hash", decision: "Deny", comment: "  needs work  ", requestId: "review-0001" });

    expect(http.getJson).toHaveBeenCalledWith("/agent/reviews/run%2F1/review%201");
    expect(http.postJson).toHaveBeenCalledWith("/agent/reviews/run%2F1/review%201/decision",
      { contentHash: "hash", decision: "Deny", comment: "needs work", requestId: "review-0001" });
    const body = http.postJson.mock.calls[0][1] as unknown as Record<string, unknown>;
    expect(Object.keys(body)).not.toContain("actorId");
  });

  it("parses the page route and refuses anything else", () => {
    expect(readReviewReference("/agents/reviews/run-1/act-2")).toEqual({ workflowExecutionId: "run-1", activityExecutionId: "act-2" });
    expect(readReviewReference("/studio/agents/reviews/run%201/act-2/")).toEqual({ workflowExecutionId: "run 1", activityExecutionId: "act-2" });
    expect(readReviewReference("/agents/reviews/run-1")).toBeNull();
    expect(readReviewReference("/agents/reviews/run%E0%A4%A/act-2")).toBeNull();
  });
});
