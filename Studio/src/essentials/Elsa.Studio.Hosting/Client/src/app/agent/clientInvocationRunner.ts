import type { ElsaStudioModuleApi, StudioAgentClientAcknowledgement, StudioAgentPreparedInvocation } from "../../sdk";

/** In-memory delivery guard; it never turns a missing acknowledgement into a retry of the mutation. */
export function createClientInvocationRunner(api: ElsaStudioModuleApi) {
  const deliveries = new Map<string, { envelope: string; completion: Promise<StudioAgentClientAcknowledgement> }>();
  return {
    run(invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientAcknowledgement> {
      const envelope = stableJson(invocation);
      const previous = deliveries.get(invocation.invocationId);
      if (previous) {
        if (previous.envelope !== envelope) return Promise.reject(new Error("Conflicting client invocation delivery."));
        return previous.completion;
      }
      const completion = execute(invocation, signal);
      deliveries.set(invocation.invocationId, { envelope, completion });
      return completion;
    }
  };

  async function execute(invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientAcknowledgement> {
    if (!invocation.client) throw new Error("Client invocation has no originating binding.");
    const base = { client: invocation.client, approvalDigest: invocation.approvalDigest };
    const refuse = (summary: string): StudioAgentClientAcknowledgement => ({ ...base, outcome: "refused", summary });
    if (invocation.executionLocation !== "clientWorkingState" || invocation.client.clientId !== api.agent.clientId) return refuse("The originating Studio context is unavailable.");
    if (signal?.aborted || Date.parse(invocation.expiresAt) <= Date.now() || !Number.isFinite(Date.parse(invocation.expiresAt))) return refuse("Client invocation was cancelled or expired before dispatch.");
    const contracts = api.agent.toolContracts.list().filter(item => item.id === invocation.toolName && item.moduleId === invocation.moduleId);
    if (contracts.length !== 1) return refuse(contracts.length ? "Duplicate client tool registration." : "Client tool is unavailable.");
    const contract = contracts[0];
    if (contract.executionLocation !== "clientWorkingState" || contract.contractVersion !== invocation.toolContractVersion || !contract.executeClient || (contract.availability && contract.availability.status !== "available")) return refuse("Client tool contract or execution support is unavailable.");
    try {
      return { ...base, ...await contract.executeClient(invocation, signal) };
    } catch (error) {
      // Once dispatched the runner cannot infer whether a module committed before losing its callback.
      return { ...base, outcome: "uncertain", summary: error instanceof Error ? error.message : "The owning resource could not confirm the dispatched change." };
    }
  }
}

function stableJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
  if (value && typeof value === "object") return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${stableJson((value as Record<string, unknown>)[key])}`).join(",")}}`;
  return JSON.stringify(value) ?? "null";
}
