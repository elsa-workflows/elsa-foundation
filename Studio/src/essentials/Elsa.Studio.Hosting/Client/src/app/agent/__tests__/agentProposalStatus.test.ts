import { describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioEndpointContext } from "../../../sdk";
import { createAgentClient } from "../agentClient";

// The existing C# AgentActionProposalStatus contract, including appended Performed = 12.
const statuses = [
  ["Draft", "draft"], ["AwaitingApproval", "awaiting-approval"], ["Approved", "approved"],
  ["Denied", "denied"], ["Edited", "edited"], ["Expired", "expired"], ["Executed", "executed"],
  ["Failed", "failed"], ["Cancelled", "cancelled"], ["Executing", "executing"],
  ["AwaitingClient", "awaiting-client"], ["Uncertain", "uncertain"], ["Performed", "performed"]
] as const;
const wireCases = statuses.flatMap(([pascal, expected], ordinal) =>
  [...new Set<string | number>([ordinal, pascal, pascal[0].toLowerCase() + pascal.slice(1), expected])]
    .map(wire => ({ wire, expected })));

function endpoint(result: Record<string, unknown>): StudioEndpointContext {
  return {
    baseUrl: "https://studio.example/",
    http: {
      getJson: vi.fn(async () => ({ data: [result] })),
      postJson: vi.fn(async () => ({ data: result }))
    }
  } as unknown as StudioEndpointContext;
}

describe("proposal status wire contract", () => {
  it.each(wireCases)("keeps $wire as $expected across history, decisions and execution", async ({ wire, expected }) => {
    const client = createAgentClient(endpoint({ id: "proposal", proposalId: "proposal", status: wire,
      executed: expected === "executed" || expected === "performed" }));
    expect((await client.listProposals("session"))[0].status).toBe(expected);
    expect((await client.approveProposal("proposal", {})).approvalStatus).toBe(expected);
    expect((await client.executeProposal("proposal", {})).approvalStatus).toBe(expected);
  });

  it.each([-1, 13, 1.5, NaN, Infinity, "future-status", "12", "", {}, null, undefined])(
    "does not infer approval or successful execution from unknown status %j and a ran flag", async status => {
      const client = createAgentClient(endpoint({ id: "proposal", proposalId: "proposal", status, executed: true }));
      expect((await client.listProposals("session"))[0].status).toBe("uncertain");
      expect((await client.approveProposal("proposal", {})).approvalStatus).toBe("uncertain");
      expect((await client.executeProposal("proposal", {})).approvalStatus).toBe("uncertain");
    });

  it.each(["executed", "performed"].flatMap(status => [false, undefined].map(executed => ({ status, executed }))))(
    "requires the ran flag with successful status $status ($executed)", async result => {
      const client = createAgentClient(endpoint({ proposalId: "proposal", ...result }));
      expect((await client.executeProposal("proposal", {})).approvalStatus).toBe("uncertain");
    });

  it.each([
    { status: "future-status", executed: false, admitted: false },
    { status: "performed", executed: false, admitted: false },
    { status: "executed", executed: true, admitted: false },
    { status: "awaiting-client", executed: true, admitted: false },
    { status: "awaiting-client", executed: undefined, admitted: false },
    { status: "awaiting-client", executed: false, admitted: true }
  ])("admits a client invocation only with pending-client status and no prior execution: %j", async ({ status, executed, admitted }) => {
    const clientInvocation = { invocationId: "invocation" };
    const client = createAgentClient(endpoint({ proposalId: "proposal", status, executed, clientInvocation }));
    const response = await client.executeProposal("proposal", {});
    expect(response.approvalStatus).toBe(admitted ? "awaiting-client" : "uncertain");
    expect(response.clientInvocation).toEqual(admitted ? clientInvocation : undefined);
  });

  it.each([[3, "denied"], [5, "expired"], [7, "failed"], [8, "cancelled"]] as const)(
    "preserves numeric refusal %s as authoritative terminal %s", async (status, expected) => {
      const envelope = { error: { code: "agent.proposal.refused", message: "No dispatch.", statusCode: 409 },
        data: { proposalId: "proposal", status, executed: false } };
      const context = endpoint({});
      context.http.postJson = vi.fn(async () => { throw new StudioHttpError(409, "No dispatch.", null, envelope); });
      await expect(createAgentClient(context).executeProposal("proposal", {})).rejects.toMatchObject({
        terminalOutcome: { approvalStatus: expected, executed: false }
      });
    });
});
