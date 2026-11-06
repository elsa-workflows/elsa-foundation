import { describe, expect, it } from "vitest";
import { JobsClient } from "../jobsApi";
import { invocationDraft, toInvocation, utc } from "../InvocationFields";
import { register } from "../module";
import { fixture } from "./support";
import { permissions } from "../types";
import { payloadDocument } from "../jsonPayload";
import { createStudioRegistry } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/registry";
import { filterAccessibleNavigation } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/navigation/placement";
import { createEndpointContext } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk";

describe("Jobs module transport and forms", () => {
  it("uses advertised collection links and encodes resource identities and opaque cursor query values", async () => {
    const f = fixture(); const client = new JobsClient(f.api);
    await client.read("jobs", ["job/a?other", "attempts"], { limit: 25, after: "opaque+/==", queue: "queue/a" });
    expect(f.get).toHaveBeenCalledWith("/jobs/job%2Fa%3Fother/attempts?limit=25&after=opaque%2B%2F%3D%3D&queue=queue%2Fa");
  });
  it("refuses non-relative capability links before contacting a transport", async () => {
    const f = fixture(); f.api.backendSession!.capabilities!.capabilities[0].links[0].href = "https://foreign.invalid/jobs";
    await expect(new JobsClient(f.api).read("jobs")).rejects.toMatchObject({ status: 501 }); expect(f.get).not.toHaveBeenCalled();
  });
  it("round-trips explicit JSON null and execution policy without adding caller ownership fields", () => {
    const draft = invocationDraft(); draft.jobType = "reports"; draft.inputAlias = "input"; draft.resultAlias = "result"; draft.input = "null";
    const invocation = toInvocation(draft); expect(invocation.input.value).toBeNull(); expect(invocationDraft(invocation).input).toBe("null");
    expect(invocation.policy).toEqual(draft.policy); expect(invocation).not.toHaveProperty("association"); expect(invocation).not.toHaveProperty("scope"); expect(invocation).not.toHaveProperty("actor");
  });
  it.each(["2026-11-06T12:00:00", "2026-02-30T12:00:00Z", "2026-11-06T12:00:00+01:00"])("refuses ambiguous or impossible recurrence instant %s", value => {
    expect(() => utc(value)).toThrow("exact UTC");
  });
  it("preserves seven-digit UTC precision for server-side identity and rejects an oversized JSON envelope", () => {
    expect(utc("2026-11-06T12:00:00.1234567Z")).toBe("2026-11-06T12:00:00.1234567Z");
    const draft = invocationDraft(); draft.jobType = "reports"; draft.inputAlias = "input"; draft.resultAlias = "result"; draft.input = JSON.stringify("x".repeat(1048576));
    expect(() => toInvocation(draft)).toThrow("1 MiB");
  });
  it("registers optional capability-gated routes with one workspace resource for schedule detail and editing", () => {
    const f = fixture(); register(f.api);
    const contribution = f.add.mock.calls[0][0];
    expect(contribution.required).toBe(false); expect(contribution.routes).toHaveLength(12);
    expect(contribution.routes.every((route: { requiresBackend: unknown }) => route.requiresBackend)).toBe(true);
    const detail = contribution.routes.find((route: { id: string }) => route.id === "jobs.schedules.detail");
    const edit = contribution.routes.find((route: { id: string }) => route.id === "jobs.schedules.edit");
    expect(detail.workspace.resourceKey(new URL("https://owned.invalid/jobs/schedules/hourly"))).toBe(edit.workspace.resourceKey(new URL("https://owned.invalid/jobs/schedules/hourly/edit")));
  });
  it.each([permissions.read, permissions.submit, permissions.audit])("keeps Jobs visible through the actual host registry and navigation filter for %s alone", permission => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://owned.invalid/") });
    register(api);
    const visible = filterAccessibleNavigation(api.navigation.list(), { authenticated: true, permissions: [permission], roles: [], canReadAdmin: false });
    expect(visible.map(item => item.id)).toContain("jobs"); expect(api.routes.list()).toHaveLength(12);
  });
  it.each(["9007199254740993", "1e309", "1e-400", "1.0000000000000001", "0.10000000000000001", "-0", "4.9406564584124654e-324"])("refuses numeric input %s before the SDK can alter its value", token => {
    const draft = invocationDraft(); draft.jobType = "reports"; draft.inputAlias = "input"; draft.resultAlias = "result";
    draft.input = `{"nested":[${token}],"numericText":"9007199254740993"}`;
    expect(() => toInvocation(draft)).toThrow("without changing its value");
  });
  it.each(["9007199254740992", "1e20", "0.1", "5e-324", "1.2500e2"])("round-trips representable numeric input %s through JSON request serialization", token => {
    const draft = invocationDraft(); draft.jobType = "reports"; draft.inputAlias = "input"; draft.resultAlias = "result";
    draft.input = `{"number":${token},"numericText":"1e309 and 9007199254740993","escaped":"\\\"1e309\\\""}`;
    const wire = JSON.stringify(toInvocation(draft));
    expect(JSON.parse(wire).input.value).toEqual(JSON.parse(draft.input));
    expect(wire).toContain('"numericText":"1e309 and 9007199254740993"');
  });
  it.each(["input", "result"] as const)("preserves exact retained %s numeric tokens in the export document", kind => {
    const raw = '{"large":9007199254740993,"overflow":1e309,"precise":1.0000000000000001,"zero":-0}';
    const payload = { schema: { alias: "numeric", version: 1 }, value: JSON.parse(raw) };
    const document = payloadDocument(kind === "input" ? { state: "available", input: payload, valueJson: raw } : { state: "available", resultId: "result", payload, valueJson: raw }, kind);
    expect(document).toContain('"value":' + raw); expect(document).not.toContain("9007199254740992"); expect(() => JSON.parse(document)).not.toThrow();
    expect(() => payloadDocument(kind === "input" ? { state: "available", input: payload } : { state: "available", payload }, kind)).toThrow();
  });
});
