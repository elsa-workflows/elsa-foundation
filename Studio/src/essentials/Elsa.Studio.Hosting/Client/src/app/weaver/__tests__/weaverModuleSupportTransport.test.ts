import { describe, expect, it, vi } from "vitest";
import { StudioHttpError, createEndpointContext, type StudioEndpointContext } from "../../../sdk";
import { createWeaverClient } from "../weaverClient";
import { subscribeWeaverSessionStream } from "../weaverStream";
import type { WeaverStreamEvent } from "../weaverTypes";

function context(): StudioEndpointContext {
  return { baseUrl: "https://studio.example/", http: { getJson: vi.fn(), postJson: vi.fn() } } as unknown as StudioEndpointContext;
}
function keys(value: Record<string, unknown>, pascal: boolean): Record<string, unknown> {
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [pascal ? key[0].toUpperCase() + key.slice(1) : key, item]));
}
async function stream(frames: Record<string, unknown>[]): Promise<WeaverStreamEvent[]> {
  const events: WeaverStreamEvent[] = [];
  let finished!: () => void; let failed!: (error: Error) => void;
  const completion = new Promise<void>((resolve, reject) => { finished = resolve; failed = reject; });
  const input = [...frames, { kind: "messageCompleted", messageId: "message" }];
  const fetch = vi.fn(async () => new Response(input.map(frame => `data: ${JSON.stringify(frame)}\n\n`).join(""), { status: 200 }));
  const subscription = subscribeWeaverSessionStream(createEndpointContext("https://studio.example/"), "/stream", event => {
    events.push(event); if (event.type === "message-completed") finished();
  }, failed, { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "message" });
  try { await completion; return events.filter(event => event.type !== "message-completed"); }
  finally { subscription.close(); }
}

const policies = Array.from({ length: 8 }, (_, mask) => ({
  administrative: Boolean(mask & 1), irrecoverable: Boolean(mask & 2), activation: Boolean(mask & 4)
}));
describe("module support transport", () => {
  it.each(policies.flatMap(view => [false, true].map(pascal => ({ view, pascal }))))(
    "reads every independent host class from a complete readonly bootstrap (Pascal=$pascal, $view)", async ({ view, pascal }) => {
      const source = context();
      source.http.getJson = vi.fn(async () => keys({ data: keys({ privileges: keys(view, pascal) }, pascal) }, pascal)) as typeof source.http.getJson;
      expect((await createWeaverClient(source).bootstrap()).privileges).toEqual(view);
      expect(source.http.postJson).not.toHaveBeenCalled();
    });

  it.each([undefined, null, {}, [], { administrative: false }, { administrative: false, irrecoverable: false },
    { administrative: "true", irrecoverable: false, activation: false }, { administrative: false, irrecoverable: 1, activation: false },
    { administrative: false, irrecoverable: false, activation: null }])(
    "does not fabricate disabled or enabled host policy from an absent/malformed projection: %j", async privileges => {
      const source = context(); source.http.getJson = vi.fn(async () => ({ data: { privileges } })) as typeof source.http.getJson;
      expect((await createWeaverClient(source).bootstrap()).privileges).toBeUndefined();
    });

  it("does not substitute session policy or autonomy for host privilege observations", async () => {
    const source = context(); source.http.getJson = vi.fn(async () => ({ data: { policy: {
      defaultAutonomyMode: "fullAuto", privileges: policies[7], allowPrivilegedToolInvocations: true
    } } })) as typeof source.http.getJson;
    expect((await createWeaverClient(source).bootstrap()).privileges).toBeUndefined();
  });

  it("copies each bootstrap observation and sends no host-policy update", async () => {
    const source = context(); const view = { ...policies[0] };
    source.http.getJson = vi.fn(async () => ({ data: { privileges: view } })) as typeof source.http.getJson;
    const client = createWeaverClient(source); const before = await client.bootstrap();
    view.administrative = true; view.activation = true;
    expect((await client.bootstrap()).privileges).toEqual({ administrative: true, irrecoverable: false, activation: true });
    expect(before.privileges).toEqual(policies[0]); expect(source.http.postJson).not.toHaveBeenCalled();
  });

  it.each([true, false, undefined, "true", 1, null, {}, []].flatMap(ownerUnavailable =>
    [false, true].flatMap(pascal => ["http", "wrapped"].map(transport => ({ ownerUnavailable, pascal, transport }))))) (
    "retains strictly boolean owner-unavailable metadata on HTTP error channels: %j", async ({ ownerUnavailable, pascal, transport }) => {
      const source = context();
      const envelope = keys({ error: keys({ code: "module.owner.unavailable", message: "Owner integration unavailable.", statusCode: 503, ownerUnavailable }, pascal),
        data: keys({ proposalId: "proposal", status: "failed", executed: false }, pascal) }, pascal);
      const send = vi.fn(async () => { if (transport === "http") throw new StudioHttpError(503, "Unavailable.", null, envelope); return envelope; });
      source.http.getJson = send as typeof source.http.getJson; source.http.postJson = send as typeof source.http.postJson;
      const client = createWeaverClient(source);
      await expect(client.getSession("session")).rejects.toMatchObject({ name: "WeaverApiError", ownerUnavailable: ownerUnavailable === true });
      await expect(client.executeProposal("proposal", {})).rejects.toMatchObject({ name: "WeaverExecutionError", ownerUnavailable: ownerUnavailable === true,
        terminalOutcome: { approvalStatus: "failed", executed: false } });
    });

  it.each([true, false, undefined, "true", 1, null, {}, []].flatMap(ownerUnavailable => [false, true].map(pascal => ({ ownerUnavailable, pascal }))))(
    "retains owner-unavailable metadata in SSE errors/diagnostics without treating strings as flags: %j", async ({ ownerUnavailable, pascal }) => {
      const diagnostic = keys({ contributorId: "owner", code: "module.owner.unavailable", message: "Owner unavailable.", required: true, ownerUnavailable }, pascal);
      const events = await stream([
        keys({ kind: "contextAdmitted", payload: keys({ attachments: [], diagnostics: [diagnostic] }, pascal) }, pascal),
        keys({ kind: "error", error: keys({ code: "module.owner.unavailable", message: "Owner unavailable.", ownerUnavailable }, pascal) }, pascal)
      ]);
      const flag = ownerUnavailable === true ? { ownerUnavailable: true } : {};
      expect(events).toEqual([
        { type: "context-admitted", attachments: [], diagnostics: [{ contributorId: "owner", code: "module.owner.unavailable", message: "Owner unavailable.", required: true, ...flag }] },
        { type: "error", code: "module.owner.unavailable", message: "Owner unavailable.", ...flag }
      ]);
    });

  it("keeps prepared classes and performed receipt codes opaque across HTTP history, execute and SSE", async () => {
    const source = context();
    const target = { moduleId: "Owner", resourceType: "sample", resourceId: "one", revision: "r1" };
    const prepared = { invocationId: "invocation", target, requiredPrivilegeClasses: ["administrative", "activation"], approvalDigest: "retained-digest" };
    const payload = { typeId: "sample.verdict", schemaVersion: "1", data: { passed: false } };
    const result = { toolCallId: "call", toolName: "sample_test", succeeded: true, content: "Owner reported a negative test verdict.", payload,
      receipt: { invocationId: "invocation", before: target, after: target, scope: "noPersistedEffect", moduleCode: "sample.test.negative", evidence: payload } };
    source.http.getJson = vi.fn(async () => ({ data: [{ id: "proposal", status: "Performed", preparedInvocation: prepared, toolResult: result }] })) as typeof source.http.getJson;
    source.http.postJson = vi.fn(async () => ({ data: { proposalId: "proposal", status: "performed", executed: true, toolResult: result } })) as typeof source.http.postJson;
    const client = createWeaverClient(source); const history = (await client.listProposals("session"))[0];
    expect(history.preparedInvocation?.requiredPrivilegeClasses).toEqual(prepared.requiredPrivilegeClasses);
    expect(history.preparedInvocation?.approvalDigest).toBe("retained-digest"); expect(history.toolResult).toEqual(result);
    expect(await client.executeProposal("proposal", {})).toMatchObject({ approvalStatus: "performed", executed: true, toolResult: result });
    expect(await stream([{ kind: 7, payload: result, resultKind: 2 }])).toEqual([{ type: "module-result", messageId: "message", result }]);
  });
});
