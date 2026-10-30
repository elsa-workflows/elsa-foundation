import { describe, expect, it } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { collectAgentContext } from "../agentContext";

const attachment = { id: "one", source: "Sample", label: "Selected sample", contentType: "sample", sensitivity: "internal" as const, scope: "selection" };
function registry() { return createStudioRegistry({ ...createEndpointContext("https://example.test/"), hostVersion: "1", sdkVersion: "1" }); }
describe("module context collection", () => {
  it("preserves explicit content and typed optional limitations", async () => {
    const api = registry(); api.agent.contextProviders.add({ id: "sample", displayName: "Sample", surfaces: ["*"], sensitivity: "internal", collect: async () => ({ attachments: [], diagnostics: [{ contributorId: "sample", code: "absent", message: "Optional evidence unavailable.", required: false }] }) });
    expect(await collectAgentContext(api, { route: "/arbitrary" }, "explain", undefined, [attachment])).toEqual({ attachments: [attachment], diagnostics: [expect.objectContaining({ required: false })] });
  });
  it("diagnoses duplicate attachments rather than silently picking one", async () => {
    await expect(collectAgentContext(registry(), { route: "/" }, "explain", undefined, [attachment, attachment])).rejects.toThrow("Duplicate context attachment");
  });
  it("propagates unexpected contributor failures", async () => {
    const api = registry(); api.agent.contextProviders.add({ id: "broken", displayName: "Broken", surfaces: ["*"], sensitivity: "internal", collect: async () => { throw new Error("Unexpected failure"); } });
    await expect(collectAgentContext(api, { route: "/" }, "explain")).rejects.toThrow("Unexpected failure");
  });
});
