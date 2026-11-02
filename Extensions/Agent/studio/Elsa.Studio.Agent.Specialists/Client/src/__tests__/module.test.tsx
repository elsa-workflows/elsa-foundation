import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("agent specialists module", () => {
  it("contributes only the review route behind the agent reviews capability", () => {
    const routes = vi.fn();
    const featureAreas = vi.fn();
    register({ routes: { add: routes }, featureAreas: { add: featureAreas }, backend: {} } as unknown as ElsaStudioModuleApi);

    expect(featureAreas).not.toHaveBeenCalled();
    expect(routes).toHaveBeenCalledWith(expect.objectContaining({
      path: "/agents/reviews/:workflowExecutionId/:activityExecutionId",
      requiresBackend: [{ id: "elsa.api.agent-reviews", contractVersion: "1" }]
    }));
  });
});
